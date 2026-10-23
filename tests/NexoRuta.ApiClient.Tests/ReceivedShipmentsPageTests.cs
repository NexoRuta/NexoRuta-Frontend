using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task ReceivedShipmentsSearchAcceptsAnExplicitlyEmptyStateFilter()
    {
        var shipmentId = Guid.Parse("01a1649c-352e-78dc-ab28-aa0d23a830dc");
        var api = new FakeApi
        {
            EnviosResponse = [CreateShipment(1) with { Id = shipmentId }]
        };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        using var login = await LoginAsync(client, "admin@example.test");

        using var response = await client.GetAsync($"/?Buscar={shipmentId:D}&Estado=&Pagina=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(shipmentId.ToString("D"), html);
        Assert.Single(Regex.Matches(html, "class=\"nr-shipment-row\""));
        Assert.Contains("Todos los estados", html);
    }

    [Fact]
    public async Task ReceivedShipmentsTreatsWhitespaceFiltersAsEmptyAndClampsInvalidPages()
    {
        var api = new FakeApi { EnviosResponse = Enumerable.Range(1, 12).Select(CreateShipment).ToArray() };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        using var login = await LoginAsync(client, "admin@example.test");

        using var whitespace = await client.GetAsync("/?Buscar=%20%20&Estado=%20&Pagina=1");
        var whitespaceHtml = await whitespace.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, whitespace.StatusCode);
        Assert.Equal(10, Regex.Matches(whitespaceHtml, "class=\"nr-shipment-row\"").Count);
        Assert.DoesNotContain("Limpiar filtros", whitespaceHtml);

        using var malformedPage = await client.GetAsync("/?Pagina=no-es-un-numero");
        var malformedHtml = await malformedPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, malformedPage.StatusCode);
        Assert.Equal(10, Regex.Matches(malformedHtml, "class=\"nr-shipment-row\"").Count);

        using var outOfRangePage = await client.GetAsync("/?Pagina=2147483647");
        var outOfRangeHtml = await outOfRangePage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, outOfRangePage.StatusCode);
        Assert.Equal(2, Regex.Matches(outOfRangeHtml, "class=\"nr-shipment-row\"").Count);
        Assert.Contains("00000000-0000-0000-0000-000000000012", outOfRangeHtml);
    }

    [Fact]
    public async Task ReceivedShipmentsSearchSkipsOptionalNamesMissingFromAnApiRecord()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var json = JsonSerializer.Serialize(CreateShipment(1), options)
            .Replace("\"comercioNombre\":\"Comercio Este\"", "\"comercioNombre\":null", StringComparison.Ordinal);
        var shipment = JsonSerializer.Deserialize<EnvioResponse>(json, options)
            ?? throw new InvalidOperationException("The synthetic shipment fixture must deserialize.");
        var api = new FakeApi { EnviosResponse = [shipment] };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        using var login = await LoginAsync(client, "admin@example.test");

        using var response = await client.GetAsync("/?Buscar=NoExiste&Estado=&Pagina=1");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("No encontramos envíos con esos filtros", html);
        Assert.DoesNotContain("class=\"nr-shipment-row\"", html);
    }

    [Fact]
    public async Task ReceivedShipmentsFilterAndPaginateTheAuthorizedResponse()
    {
        var api = new FakeApi { EnviosResponse = Enumerable.Range(1, 12).Select(CreateShipment).ToArray() };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        using var login = await LoginAsync(client, "admin@example.test");

        using var firstPage = await client.GetAsync("/");
        var firstHtml = await firstPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        Assert.Contains("Mostrando 1–10 de 12 envíos", firstHtml);
        Assert.Equal(10, Regex.Matches(firstHtml, "class=\"nr-shipment-row\"").Count);
        Assert.Contains("Actualizar", firstHtml);
        Assert.Contains("name=\"Pagina\" value=\"1\"", firstHtml);
        Assert.Contains("Buscar envíos", firstHtml);
        Assert.Contains("Código, comercio o destinatario", firstHtml);
        Assert.Contains("href=\"/?Pagina=2\"", firstHtml);
        Assert.Contains("Comercio Centro", firstHtml);
        Assert.Contains("aria-label=\"Copiar identificador completo del envío", firstHtml);
        Assert.Contains("role=\"status\" aria-live=\"polite\"", firstHtml);
        Assert.Contains("id=\"shipment-detail-dialog\"", firstHtml);
        Assert.Contains("data-bs-toggle=\"offcanvas\"", firstHtml);
        Assert.Contains("aria-labelledby=\"shipment-detail-title\"", firstHtml);
        Assert.Contains("id=\"shipment-detail-template-00000000-0000-0000-0000-000000000002\"", firstHtml);
        Assert.DoesNotContain("nr-shipment-details", firstHtml);
        Assert.Contains("SINT-003", firstHtml);
        Assert.Contains("3 bultos", firstHtml);
        Assert.Contains("Destinataria sint", firstHtml);

        using var secondPage = await client.GetAsync("/?Pagina=2");
        var secondHtml = await secondPage.Content.ReadAsStringAsync();
        Assert.Contains("Mostrando 11–12 de 12 envíos", secondHtml);
        Assert.Equal(2, Regex.Matches(secondHtml, "class=\"nr-shipment-row\"").Count);
        Assert.Contains("href=\"/?Pagina=1\"", secondHtml);

        using var filtered = await client.GetAsync("/?Buscar=Centro&Estado=Entregado");
        var filteredHtml = await filtered.Content.ReadAsStringAsync();
        Assert.Contains("Mostrando 1–2 de 2 envíos", filteredHtml);
        Assert.Contains("Comercio Centro", filteredHtml);
        Assert.Contains("Destinatario Centro", filteredHtml);
        Assert.DoesNotContain("Destinatario 04", filteredHtml);
        Assert.Contains("Todos los estados", filteredHtml);
        Assert.Contains("name=\"Buscar\" value=\"Centro\"", filteredHtml);

        using var codeSearch = await client.GetAsync("/?Buscar=00000000-0000-0000-0000-000000000002");
        var codeHtml = await codeSearch.Content.ReadAsStringAsync();
        Assert.Contains("Mostrando 1–1 de 1 envíos", codeHtml);
        Assert.Contains("Comercio Centro", codeHtml);

        Assert.Contains("administracion@example.test", filteredHtml);
        Assert.Contains("00000000-0000-0000-0000-000000000201", filteredHtml);
        Assert.Contains("30", filteredHtml);
        Assert.Contains("20", filteredHtml);
        Assert.Contains("10 cm", filteredHtml);

        var shipmentListCalls = api.Calls.Where(call => call.Path == "/api/envios").ToArray();
        Assert.Equal(4, shipmentListCalls.Length);
        Assert.All(shipmentListCalls, call =>
        {
            Assert.Equal("jwt-admin", call.Token);
            Assert.Equal(string.Empty, call.Query);
        });
    }

    [Fact]
    public async Task ReceivedShipmentsDistinguishEmptyResultsFromApiFailure()
    {
        var emptyApi = new FakeApi { EnviosResponse = [] };
        using (var factory = new BackofficeFactory(emptyApi))
        using (var client = Client(factory))
        {
            using var login = await LoginAsync(client, "admin@example.test");
            using var empty = await client.GetAsync("/");
            var html = await empty.Content.ReadAsStringAsync();
            Assert.Contains("Todavía no hay envíos", html);
            Assert.DoesNotContain("No encontramos envíos con esos filtros", html);
        }

        var filteredApi = new FakeApi { EnviosResponse = [CreateShipment(1)] };
        using (var factory = new BackofficeFactory(filteredApi))
        using (var client = Client(factory))
        {
            using var login = await LoginAsync(client, "admin@example.test");
            using var noMatches = await client.GetAsync("/?Buscar=NoExiste");
            var html = await noMatches.Content.ReadAsStringAsync();
            Assert.Contains("No encontramos envíos con esos filtros", html);
            Assert.DoesNotContain("Todavía no hay envíos", html);
            Assert.Equal(1, html.Split("Limpiar filtros", StringSplitOptions.None).Length - 1);
        }

        var unavailableApi = new FakeApi { ShipmentsStatusOverride = HttpStatusCode.ServiceUnavailable };
        using var unavailableFactory = new BackofficeFactory(unavailableApi);
        using var unavailableClient = Client(unavailableFactory);
        using var unavailableLogin = await LoginAsync(unavailableClient, "admin@example.test");
        using var unavailable = await unavailableClient.GetAsync("/?Buscar=Centro&Estado=Entregado");
        var unavailableHtml = await unavailable.Content.ReadAsStringAsync();
        Assert.Contains("No se pudo consultar el servicio", unavailableHtml);
        Assert.Contains("Reintentar", unavailableHtml);
        Assert.DoesNotContain("Todavía no hay envíos", unavailableHtml);
        Assert.Contains("name=\"Buscar\" value=\"Centro\"", unavailableHtml);
        Assert.Contains("name=\"Estado\"", unavailableHtml);
        Assert.Matches("<option value=\"Entregado\"[^>]*selected", unavailableHtml);
        Assert.Contains("Buscar=Centro", unavailableHtml);
        Assert.Contains("Estado=Entregado", unavailableHtml);
    }

    private static EnvioResponse CreateShipment(int number)
    {
        var id = Guid.Parse($"00000000-0000-0000-0000-{number:D12}");
        var commerceName = number == 2 ? "Comercio Centro" : "Comercio Este";
        var recipientName = number == 1
            ? "Destinataria sintética con nombre extenso para comprobar el ajuste del detalle"
            : number == 6 ? "Destinatario Centro" : $"Destinatario {number:D2}";
        var address = number == 1
            ? "Avenida de ejemplo con una dirección deliberadamente larga para verificar que el texto se parta sin desbordar el panel, Montevideo"
            : $"Av. Ejemplo {number}, Montevideo";
        var packages = number == 1
            ? new[]
            {
                new BultoResponse("SINT-001", 650, 24, 18, 12),
                new BultoResponse("SINT-002", 430, 20, 15, 10),
                new BultoResponse("SINT-003", 275, 16, 12, 8)
            }
            : [new BultoResponse($"B-{number:D3}", 1000 + number * 100, 30, 20, 10)];
        return new EnvioResponse(
            id,
            Guid.Parse("00000000-0000-0000-0000-000000000101"),
            Guid.Parse($"00000000-0000-0000-0000-{(1000 + number):D12}"),
            Guid.Parse("00000000-0000-0000-0000-000000000201"),
            "administracion@example.test",
            "Operador de prueba",
            commerceName,
            recipientName,
            address,
            number % 2 == 0 ? "Entregado" : "EnTransito",
            1,
            packages);
    }
}

public sealed partial class BackofficeSessionAndPagesTests
{
    private sealed partial class FakeApi
    {
        public IReadOnlyList<EnvioResponse>? EnviosResponse { get; init; }
        public HttpStatusCode? ShipmentsStatusOverride { get; init; }
    }
}
