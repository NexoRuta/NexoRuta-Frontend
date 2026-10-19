using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class TarifasApiClientTests
{
    [Fact]
    public async Task DraftReplacementUsesTypedRouteEnumsExactDecimalsAndNoTenantInput()
    {
        var zoneId = Guid.NewGuid();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal("/api/configuracion/tarifas/borrador/tarifas", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("jwt", request.Headers.Authorization.Parameter);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal(0, body.RootElement.GetProperty("versionConcurrencia").GetInt64());
            Assert.False(body.RootElement.TryGetProperty("operadorId", out _));
            var rate = body.RootElement.GetProperty("tarifas")[0];
            Assert.Equal("Estandar", rate.GetProperty("modalidad").GetString());
            Assert.Equal(zoneId, rate.GetProperty("zonaId").GetGuid());
            Assert.Equal(25.500000m, rate.GetProperty("precioPorKilogramoUyu").GetDecimal());
            Assert.Equal("RecargoPorcentual", rate.GetProperty("ajustes")[0].GetProperty("tipo").GetString());
            return Json(HttpStatusCode.OK, """{"versionTarifarioId":"11111111-1111-4111-8111-111111111111","estado":"Borrador","numeroVersionPublicada":null,"versionConcurrencia":1,"moneda":"UYU","creadaUtc":"2026-10-19T00:00:00+00:00","publicadaUtc":null,"tarifas":[]}""");
        })) { BaseAddress = new Uri("https://api.example.test/") };

        var result = await new TarifasApiClient(http).ReemplazarTarifasBorradorAsync(new(0,
        [
            new(zoneId, ModalidadTarifaria.Estandar, 120m, 25.500000m, 900m,
            [new("Peak", TipoAjusteTarifario.RecargoPorcentual, null, 15m)])
        ]), "jwt");

        Assert.Equal(1, result.VersionConcurrencia);
        Assert.Equal(EstadoVersionTarifario.Borrador, result.Estado);
    }

    [Fact]
    public async Task HistoryAndSimulationUseTheirRoutesAndPreserveDecimalResponseValues()
    {
        var requests = new Queue<(HttpMethod Method, string Path)>();
        using var http = new HttpClient(new Handler((request, _) =>
        {
            requests.Enqueue((request.Method, request.RequestUri!.PathAndQuery));
            if (request.RequestUri.AbsolutePath == "/api/configuracion/tarifas/publicaciones")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"items":[],"pagina":2,"tamanoPagina":10,"total":0}"""));
            if (request.RequestUri.AbsolutePath.StartsWith("/api/configuracion/tarifas/publicaciones/", StringComparison.Ordinal))
                return Task.FromResult(Json(HttpStatusCode.OK, """{"versionTarifarioId":"11111111-1111-4111-8111-111111111111","estado":"Publicada","numeroVersionPublicada":2,"versionConcurrencia":4,"moneda":"UYU","creadaUtc":"2026-10-19T00:00:00+00:00","publicadaUtc":"2026-10-19T00:01:00+00:00","tarifas":[]}"""));
            return Task.FromResult(Json(HttpStatusCode.OK, """{"origen":"Publicacion","versionTarifarioId":"11111111-1111-4111-8111-111111111111","numeroVersionPublicada":2,"versionConcurrencia":4,"zonaId":"22222222-2222-4222-8222-222222222222","modalidad":"Urgente","moneda":"UYU","pesoTotalKg":3.5,"volumenTotalM3":0.034,"importeBaseUyu":120,"componentePesoUyu":89.25,"componenteVolumenUyu":30.6,"ajustes":[],"subtotalUyu":239.85,"recargosUyu":47.9775,"bonificacionesUyu":21.9925,"totalPrevioPisoUyu":265.835,"pisoCeroAplicado":false,"totalFinalUyu":265.84}"""));
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var client = new TarifasApiClient(http);

        var page = await client.ListarPublicacionesAsync("jwt", 2, 10);
        Assert.Equal(0, page.Total);
        var versionId = Guid.NewGuid();
        _ = await client.ObtenerPublicacionAsync(versionId, "jwt");
        var result = await client.SimularAsync(new(Guid.NewGuid(), Guid.NewGuid(), ModalidadTarifaria.Urgente,
            [new(1500m, 40m, 30m, 20m)]), "jwt");

        Assert.Equal((HttpMethod.Get, "/api/configuracion/tarifas/publicaciones?pagina=2&tamanoPagina=10"), requests.Dequeue());
        Assert.Equal((HttpMethod.Get, $"/api/configuracion/tarifas/publicaciones/{versionId}"), requests.Dequeue());
        Assert.Equal((HttpMethod.Post, "/api/configuracion/tarifas/simulaciones"), requests.Dequeue());
        Assert.Equal(265.835m, result.TotalPrevioPisoUyu);
        Assert.Equal(265.84m, result.TotalFinalUyu);
        Assert.Equal(0.034m, result.VolumenTotalM3);
        Assert.Equal(OrigenSimulacionTarifario.Publicacion, result.Origen);
    }

    [Fact]
    public async Task DraftAndPublishMethodsShareTypedApiClientAndPropagateConflictStatus()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/configuracion/tarifas")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"configurado":false,"publicacionVigente":null,"borrador":null}"""));
            if (path == "/api/configuracion/tarifas/borrador" && request.Method == HttpMethod.Post)
                return Task.FromResult(Json(HttpStatusCode.Created, """{"versionTarifarioId":"11111111-1111-4111-8111-111111111111","estado":"Borrador","numeroVersionPublicada":null,"versionConcurrencia":0,"moneda":"UYU","creadaUtc":"2026-10-19T00:00:00+00:00","publicadaUtc":null,"tarifas":[]}"""));
            if (path == "/api/configuracion/tarifas/borrador")
                return Task.FromResult(Json(HttpStatusCode.OK, """{"versionTarifarioId":"11111111-1111-4111-8111-111111111111","estado":"Borrador","numeroVersionPublicada":null,"versionConcurrencia":0,"moneda":"UYU","creadaUtc":"2026-10-19T00:00:00+00:00","publicadaUtc":null,"tarifas":[]}"""));
            if (path == "/api/configuracion/tarifas/borrador/publicar")
                return Task.FromResult(Json(HttpStatusCode.Conflict, """{"code":"tariff.draft_conflict","message":"El borrador cambió."}"""));
            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var client = new TarifasApiClient(http);

        Assert.False((await client.ObtenerEstadoAsync("jwt")).Configurado);
        Assert.Equal(EstadoVersionTarifario.Borrador, (await client.CrearBorradorAsync("jwt")).Estado);
        Assert.Equal(EstadoVersionTarifario.Borrador, (await client.ObtenerBorradorAsync("jwt")).Estado);
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() =>
            client.PublicarBorradorAsync(new(0), "jwt"));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Contains("borrador cambió", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
