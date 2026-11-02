using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;

namespace NexoRuta.ApiClient.Tests;

public sealed class ImportacionEnviosApiClientTests
{
    private const string Token = "commerce-jwt";
    private static readonly Guid OperatorId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private const string Completed = """
        {"estado":"Completada","resumen":{"creados":1,"existentes":0,"duplicadosEnArchivo":0,"conflictos":0,"invalidos":0,"noProcesados":0},
        "filas":[{"registro":1,"referenciaExterna":"PO-1","estado":"Creado","envioId":"20000000-0000-0000-0000-000000000002","mismoQueRegistro":null,"errores":[]}]}
        """;

    [Theory]
    [InlineData(HttpStatusCode.OK, "Completada", "Creado")]
    [InlineData(HttpStatusCode.Conflict, "RechazadaAtomica", "Conflicto")]
    [InlineData(HttpStatusCode.UnprocessableEntity, "RechazadaAtomica", "Invalido")]
    public async Task ImportarAsync_StreamsMultipartAndPreservesRowResultsForExpectedStatuses(
        HttpStatusCode status, string expectedImportState, string rowState)
    {
        var streamCount = 0;
        using var http = CreateHttpClient(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/envios/importaciones", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(Token, request.Headers.Authorization.Parameter);
            Assert.Equal("multipart/form-data", request.Content!.Headers.ContentType!.MediaType);
            var multipart = await request.Content.ReadAsStringAsync(cancellationToken);
            Assert.Contains("name=archivo", multipart, StringComparison.Ordinal);
            Assert.Contains("filename=orders.csv", multipart, StringComparison.Ordinal);
            Assert.Contains("name=operadorId", multipart, StringComparison.Ordinal);
            Assert.Contains(OperatorId.ToString("D"), multipart, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("ref-1,Ada,ada@example.test", multipart, StringComparison.Ordinal);
            return JsonResponse(status, ResponseBody(rowState));
        });

        var result = await new EnviosApiClient(http).ImportarAsync(
            "orders.csv", 64, OperatorId,
            _ =>
            {
                streamCount++;
                return ValueTask.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes("ref-1,Ada,ada@example.test")));
            },
            Token);

        Assert.Equal(status, result.StatusCode);
        Assert.Equal(expectedImportState, result.Response.Estado);
        Assert.Equal(rowState, Assert.Single(result.Response.Filas).Estado);
        Assert.Equal(1, streamCount);
    }

    [Fact]
    public async Task ImportarAsync_CreatesANewStreamOnEveryManualRetryAndAllowsExactFileLimit()
    {
        var streams = new List<TrackingStream>();
        var uploadsCompleted = 0;
        using var http = CreateHttpClient(async (request, cancellationToken) =>
        {
            await request.Content!.CopyToAsync(Stream.Null, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, Completed);
        });
        var client = new EnviosApiClient(http);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            var attemptIndex = attempt;
            _ = await client.ImportarAsync("orders.csv", 5 * 1024 * 1024, OperatorId,
                _ =>
                {
                    var stream = new TrackingStream(Encoding.UTF8.GetBytes($"file-{attemptIndex}"));
                    streams.Add(stream);
                    return ValueTask.FromResult<Stream>(stream);
                }, Token, () =>
                {
                    uploadsCompleted++;
                    return Task.CompletedTask;
                });
        }

        Assert.Equal(2, streams.Count);
        Assert.Equal(2, uploadsCompleted);
        Assert.NotSame(streams[0], streams[1]);
        Assert.All(streams, stream => Assert.True(stream.WasDisposed));
    }

    [Fact]
    public async Task ImportarAsync_RejectsOversizedFileBeforeOpeningStreamOrSending()
    {
        var sent = false;
        using var http = CreateHttpClient((_, _) =>
        {
            sent = true;
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, Completed));
        });

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => new EnviosApiClient(http).ImportarAsync(
            "oversized.csv", 5 * 1024 * 1024 + 1, OperatorId,
            _ => throw new InvalidOperationException("The stream should not be opened."), Token));
        Assert.False(sent);
    }

    [Fact]
    public async Task ImportarAsync_MapsFileLevelErrorAndPreservesFunctionalHttpStatus()
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.RequestEntityTooLarge,
            "{\"tipo\":\"Archivo\",\"codigo\":\"FileTooLarge\",\"mensaje\":\"El archivo supera el límite de 5 MiB.\"}")));

        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ImportarAsync(
            "orders.csv", 64, OperatorId,
            _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])), Token));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, error.StatusCode);
        Assert.Equal("El archivo supera el límite de 5 MiB.", error.Message);
    }

    [Fact]
    public async Task ImportarAsync_RejectsIncompleteStructuredResponse()
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK,
            "{\"estado\":\"Completada\"}")));

        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ImportarAsync(
            "orders.csv", 10, OperatorId, _ => ValueTask.FromResult<Stream>(new MemoryStream([1, 2, 3])), Token));

        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
        Assert.Contains("formato inesperado", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ListarParaImportacionAsync_UsesCreateScopedCatalogAndReadsInactiveFlag()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/operadores/importacion-envios", request.RequestUri!.AbsolutePath);
            Assert.Equal(Token, request.Headers.Authorization!.Parameter);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK,
                "[{\"operadorId\":\"10000000-0000-0000-0000-000000000001\",\"nombre\":\"Operador histórico\",\"activo\":false}]"));
        });

        var operatorInfo = Assert.Single(await new OperadoresApiClient(http).ListarParaImportacionAsync(Token));
        Assert.Equal(OperatorId, operatorInfo.OperadorId);
        Assert.Equal("Operador histórico", operatorInfo.Nombre);
        Assert.False(operatorInfo.Activo);
    }

    private static string ResponseBody(string rowState)
        => rowState == "Creado" ? Completed : $$"""
            {"estado":"RechazadaAtomica","resumen":{"creados":0,"existentes":0,"duplicadosEnArchivo":0,"conflictos":{{(rowState == "Conflicto" ? 1 : 0)}},"invalidos":{{(rowState == "Invalido" ? 1 : 0)}},"noProcesados":0},
            "filas":[{"registro":1,"referenciaExterna":"PO-1","estado":"{{rowState}}","envioId":null,"mismoQueRegistro":null,"errores":[]}]}
            """;

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new TestHttpHandler(send)) { BaseAddress = new Uri("http://api.test/") };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class TestHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }

    private sealed class TrackingStream(byte[] content) : MemoryStream(content)
    {
        public bool WasDisposed { get; private set; }
        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            base.Dispose(disposing);
        }
    }
}
