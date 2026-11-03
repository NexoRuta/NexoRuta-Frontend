using System.Net;
namespace NexoRuta.ApiClient.Tests;
public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task DayListDoesNotLoadGlobalFleetOrShipments()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/rutas?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Finalizadas", html); Assert.Contains("Canceladas", html);
        Assert.DoesNotContain("Salida física:", html);
        Assert.DoesNotContain(api.Calls, c => c.Path == "/api/envios" || c.Path == "/api/reparto/flota" || c.Path.Contains("/estimaciones"));
    }
    [Theory]
    [InlineData("/deposito", "Depósito")]
    [InlineData("/flota", "Flota")]
    public async Task OperationalTasksHaveOwnAddress(string path, string title)
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()); Assert.Contains(title, html);
    }
    [Fact]
    public async Task RequestedMissingRouteNeverDisplaysAnotherRoute()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync($"/rutas/{Guid.NewGuid()}?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()); Assert.Contains("Ruta no disponible", html);
    }
    [Fact]
    public async Task DepotReceiptPreservesTaskSearchAndExactVersionInPrg()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "Admitido" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/deposito?fecha=2026-10-20");
        using var response = await client.PostAsync("/deposito?handler=Recibir", Form(token,
            ("Fecha", "2026-10-20"), ("Vista", "deposito"), ("BuscarRecepcion", "Ana"),
            ("FiltroRecepcion", "Devoluciones"), ("Pagina", "2"),
            ("envioId", EnvioPrueba.ToString()), ("version", "8")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.StartsWith("/deposito?", location);
        Assert.Contains("buscarRecepcion=Ana", location);
        Assert.Contains("filtroRecepcion=Devoluciones", location);
        Assert.Contains("pagina=2", location);
        Assert.Equal(8, Assert.Single(api.RepartoPayloads).Body.GetProperty("version").GetInt64());
    }
    [Theory]
    [InlineData("/deposito")]
    [InlineData("/flota")]
    [InlineData("/rutas/00000000-0000-0000-0000-000000000701")]
    public async Task TaskPagesKeepRouteManagementPermission(string path)
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/acceso-denegado", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(api.Calls, c => c.Path == "/api/reparto/flota" || c.Path == "/api/rutas");
    }


    [Theory]
    [InlineData("invalid")]
    [InlineData("0001-01-01")]
    [InlineData("9999-12-31")]
    public async Task DateErrorsAndBoundariesKeepNavigationRecoverable(string date)
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/rutas?fecha=" + date);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        if (date == "invalid") Assert.Contains("fecha", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("nr-date-picker d-flex", html);
    }

    [Fact]
    public async Task DepotDefersPhysicalReceiptToExplicitShipmentContext()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "Admitido" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var list = await client.GetStringAsync("/deposito?fecha=2026-10-20");
        Assert.DoesNotContain("handler=Recibir", list);
        var selected = await client.GetStringAsync($"/deposito?fecha=2026-10-20&envioSeleccionadoId={EnvioPrueba}");
        Assert.Contains("handler=Recibir", selected);
        Assert.Contains(EnvioPrueba.ToString(), selected);
        Assert.Contains("físicamente en depósito", WebUtility.HtmlDecode(selected));
    }

    [Fact]
    public async Task DepotFilterValuesMatchTheirContractAndExcludeOtherCustodies()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var list = WebUtility.HtmlDecode(await client.GetStringAsync("/deposito?fecha=2026-10-20"));
        Assert.Contains("value=\"Primera recepción\"", list);
        Assert.Contains("value=\"En depósito\"", list);
        var first = await client.GetStringAsync("/deposito?fecha=2026-10-20&filtroRecepcion=Primera%20recepci%C3%B3n");
        Assert.DoesNotContain("class=\"nr-receipt-card\"", first);
        var stored = await client.GetStringAsync("/deposito?fecha=2026-10-20&filtroRecepcion=En%20dep%C3%B3sito");
        Assert.Contains("class=\"nr-receipt-card\"", stored);
        Assert.Contains("No hay recepciones pendientes", first);
    }

    [Fact]
    public async Task DepotReassignmentKeepsTargetRouteDateEditableAndShipmentContext()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "AsignadoRuta" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var html = await client.GetStringAsync($"/deposito?fecha=2026-10-21&envioSeleccionadoId={EnvioPrueba}&buscarRecepcion=Ana");
        Assert.Contains("id=\"fecha-reasignacion\"", html);
        Assert.Contains("2026-10-21", html);
        Assert.Contains("name=\"envioSeleccionadoId\" value=\"" + EnvioPrueba, html);
        Assert.Contains("name=\"buscarRecepcion\" value=\"Ana\"", html);
        Assert.Contains("Consultar rutas", html);
    }
}
