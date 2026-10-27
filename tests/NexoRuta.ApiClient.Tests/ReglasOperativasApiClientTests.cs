using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.ReglasOperativas.Clients;
using NexoRuta.ApiClient.Features.ReglasOperativas.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class ReglasOperativasApiClientTests
{
    [Fact]
    public async Task ClientUsesTypedRoutesBearerNullsStringEnumsAndExpectedConcurrency()
    {
        var calls = new Queue<(HttpMethod Method, string Path)>();
        using var http = new HttpClient(new Handler(async (request, ct) =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal("jwt", request.Headers.Authorization.Parameter);
            calls.Enqueue((request.Method, request.RequestUri!.PathAndQuery));
            var path = request.RequestUri.AbsolutePath;
            if (path == "/api/configuracion/reglas-operativas" )
                return Json(HttpStatusCode.OK, """{"configurado":false,"publicacionVigente":null,"borrador":null}""");
            if (path.EndsWith("/borrador/publicar", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Equal(0, body.RootElement.GetProperty("versionConcurrencia").GetInt64());
                return Version(HttpStatusCode.OK, "Publicada", 1, 1);
            }
            if (path.EndsWith("/publicaciones", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, """{"items":[],"pagina":2,"tamanoPagina":10,"total":0}""");
            if (path.Contains("/publicaciones/", StringComparison.Ordinal))
                return Version(HttpStatusCode.OK, "Publicada", 1, 1);
            if (request.Method == HttpMethod.Put)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var root = body.RootElement;
                Assert.Equal(0, root.GetProperty("versionConcurrencia").GetInt64());
                Assert.Equal(JsonValueKind.Null, root.GetProperty("maxIntentosEntrega").ValueKind);
                Assert.Equal(JsonValueKind.Null, root.GetProperty("politicaDevolucionAlAgotarIntentos").ValueKind);
                Assert.Equal(60, root.GetProperty("plazoValidacionPruebaMinutos").GetInt32());
                Assert.Equal("Fotografia", root.GetProperty("requisitosPrueba")[0].GetString());
                Assert.Equal("RECEPTOR_AUSENTE", root.GetProperty("motivos")[0].GetProperty("codigo").GetString());
                Assert.False(root.TryGetProperty("operadorId", out _));
                return Json(HttpStatusCode.OK, """{"versionReglasOperativasId":"11111111-1111-4111-8111-111111111111","estado":"Borrador","numeroVersionPublicada":null,"versionConcurrencia":1,"maxIntentosEntrega":null,"intervaloEntreIntentosMinutos":null,"politicaDevolucionAlAgotarIntentos":null,"plazoEstandarMinutos":null,"plazoUrgenteMinutos":null,"requisitosPrueba":["Fotografia"],"motivos":[{"codigo":"RECEPTOR_AUSENTE","descripcion":"No se encontró receptor","disponible":true}],"creadaUtc":"2026-10-19T00:00:00Z","publicadaUtc":null}""");
            }
            if (request.Method == HttpMethod.Post)
                return Version(HttpStatusCode.Created, "Borrador", null, 0);
            if (path.EndsWith("/borrador", StringComparison.Ordinal))
                return Json(HttpStatusCode.OK, """{"versionReglasOperativasId":"11111111-1111-4111-8111-111111111111","estado":"Borrador","numeroVersionPublicada":null,"versionConcurrencia":0,"maxIntentosEntrega":null,"intervaloEntreIntentosMinutos":null,"politicaDevolucionAlAgotarIntentos":null,"plazoEstandarMinutos":null,"plazoUrgenteMinutos":null,"requisitosPrueba":[],"motivos":[],"creadaUtc":"2026-10-19T00:00:00Z","publicadaUtc":null}""");
            throw new InvalidOperationException($"Unexpected request: {request.Method} {path}");
        })) { BaseAddress = new Uri("https://api.example.test/") };
        var client = new ReglasOperativasApiClient(http);

        Assert.False((await client.ObtenerEstadoAsync("jwt")).Configurado);
        Assert.Equal(EstadoVersionReglasOperativas.Borrador, (await client.CrearBorradorAsync("jwt")).Estado);
        var draft = await client.ObtenerBorradorAsync("jwt");
        Assert.Null(draft.MaxIntentosEntrega);
        var updated = await client.ReemplazarBorradorAsync(new(0, null, null, null, null, null, 60,
            [TipoPruebaEntrega.Fotografia], [new("RECEPTOR_AUSENTE", "No se encontró receptor", true)]), "jwt");
        Assert.Equal(1, updated.VersionConcurrencia);
        Assert.Null(updated.NumeroVersionPublicada);
        Assert.Equal("RECEPTOR_AUSENTE", updated.Motivos[0].Codigo);
        Assert.Equal(EstadoVersionReglasOperativas.Publicada,
            (await client.PublicarBorradorAsync(new(0), "jwt")).Estado);
        Assert.Equal(0, (await client.ListarPublicacionesAsync("jwt", 2, 10)).Total);
        var versionId = Guid.NewGuid();
        Assert.Equal(1, (await client.ObtenerPublicacionAsync(versionId, "jwt")).NumeroVersionPublicada);

        Assert.Equal((HttpMethod.Get, "/api/configuracion/reglas-operativas"), calls.Dequeue());
        Assert.Equal((HttpMethod.Post, "/api/configuracion/reglas-operativas/borrador"), calls.Dequeue());
        Assert.Equal((HttpMethod.Get, "/api/configuracion/reglas-operativas/borrador"), calls.Dequeue());
        Assert.Equal((HttpMethod.Put, "/api/configuracion/reglas-operativas/borrador"), calls.Dequeue());
        Assert.Equal((HttpMethod.Post, "/api/configuracion/reglas-operativas/borrador/publicar"), calls.Dequeue());
        Assert.Equal((HttpMethod.Get, "/api/configuracion/reglas-operativas/publicaciones?pagina=2&tamanoPagina=10"), calls.Dequeue());
        Assert.Equal((HttpMethod.Get, $"/api/configuracion/reglas-operativas/publicaciones/{versionId}"), calls.Dequeue());
    }

    [Fact]
    public async Task ClientPreservesApiConflictErrorsAndCancellation()
    {
        using var source = new CancellationTokenSource();
        using var http = new HttpClient(new Handler((request, ct) =>
        {
            Assert.True(ct.CanBeCanceled);
            return Task.FromResult(Json(HttpStatusCode.Conflict,
                """{"code":"operational_rules.draft_conflict","message":"El borrador cambió."}"""));
        })) { BaseAddress = new Uri("https://api.example.test/") };

        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() =>
            new ReglasOperativasApiClient(http).PublicarBorradorAsync(new(0), "jwt", source.Token));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Contains("borrador cambió", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static HttpResponseMessage Version(HttpStatusCode status, string state, int? number, long concurrency)
        => Json(status, $$"""{"versionReglasOperativasId":"11111111-1111-4111-8111-111111111111","estado":"{{state}}","numeroVersionPublicada":{{(number is null ? "null" : number.Value)}},"versionConcurrencia":{{concurrency}},"maxIntentosEntrega":2,"intervaloEntreIntentosMinutos":60,"politicaDevolucionAlAgotarIntentos":"DecisionOperativa","plazoEstandarMinutos":1440,"plazoUrgenteMinutos":360,"requisitosPrueba":["Fotografia"],"motivos":[{"codigo":"RECEPTOR_AUSENTE","descripcion":"No se encontró receptor","disponible":true}],"creadaUtc":"2026-10-19T00:00:00Z","publicadaUtc":null}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed class Handler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
