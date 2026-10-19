using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class ZonasCoberturaApiClientTests
{
    [Fact]
    public async Task ListarAsync_UsesFeatureRouteBearerAndTypedPage()
    {
        using var http = new HttpClient(new Handler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/configuracion/zonas?pagina=2&tamanoPagina=10&estado=Activa", request.RequestUri!.PathAndQuery);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("jwt", request.Headers.Authorization.Parameter);
            return Task.FromResult(Json(HttpStatusCode.OK, """{"items":[],"pagina":2,"tamanoPagina":10,"total":0}"""));
        })) { BaseAddress = new Uri("https://api.example.test/") };

        var result = await new ZonasCoberturaApiClient(http).ListarAsync("jwt", "Activa", 2, 10);

        Assert.Empty(result.Items);
        Assert.Equal(2, result.Pagina);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public async Task CrearAsync_SendsGeoJsonAsObjectAndDoesNotAcceptOwnerFields()
    {
        using var coordinates = JsonDocument.Parse("[[[-56,-34],[-55,-34],[-55,-33],[-56,-33],[-56,-34]]]");
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/configuracion/zonas", request.RequestUri!.AbsolutePath);
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("Polygon", body.RootElement.GetProperty("geometria").GetProperty("type").GetString());
            Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("geometria").GetProperty("coordinates").ValueKind);
            Assert.False(body.RootElement.TryGetProperty("operadorId", out _));
            return Json(HttpStatusCode.Created, """{"id":"11111111-1111-4111-8111-111111111111","nombre":"Centro","estado":"Inactiva","geometria":{"type":"MultiPolygon","coordinates":[]},"zonaHoraria":"America/Montevideo","franjas":[],"version":0,"revisionActual":1,"origenGeometria":"Dibujada","codigoDepartamento":null,"versionFuente":null,"creadaUtc":"2026-10-19T00:00:00+00:00","actualizadaUtc":"2026-10-19T00:00:00+00:00"}""");
        })) { BaseAddress = new Uri("https://api.example.test/") };

        var result = await new ZonasCoberturaApiClient(http).CrearAsync(new("Centro",
            new GeoJsonGeometryDto("Polygon", coordinates.RootElement.Clone())), "jwt");

        Assert.Equal("MultiPolygon", result.Geometria.Type);
        Assert.Equal(0, result.Version);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
