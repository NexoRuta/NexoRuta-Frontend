using System.Net;
using System.Text;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.ApiClient.Tests;

public sealed class SpecializedApiClientTests
{
    private const string ApiToken = "session-jwt";

    [Theory]
    [InlineData("usuarios", "api/usuarios/actual")]
    [InlineData("operadores", "api/operadores")]
    [InlineData("listar", "api/envios")]
    [InlineData("crear", "api/envios")]
    public async Task Clients_PreserveEndpointsBearerAndHttpErrors(string operation, string path)
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(new Uri(new Uri("http://api.test/"), path), request.RequestUri);
            Assert.Equal(operation == "crear" ? HttpMethod.Post : HttpMethod.Get, request.Method);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(ApiToken, request.Headers.Authorization.Parameter);
            Assert.False(request.Headers.Contains("X-NexoRuta-Acceso"));
            return Task.FromResult(Response(HttpStatusCode.Forbidden, "{\"detail\":\"Access denied\"}"));
        });
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => Invoke(operation, http));
        Assert.Equal(HttpStatusCode.Forbidden, error.StatusCode);
        Assert.Equal("Access denied", error.Message);
    }

    public static TheoryData<string, string, string> InvalidResponses => new()
    {
        { "usuarios", "null", "La API devolvió una respuesta vacía." },
        { "usuarios", "not-json", "La API devolvió una respuesta con un formato inesperado." },
        { "usuarios", "", "La API devolvió una respuesta con un formato inesperado." },
        { "operadores", "null", "La API devolvió una respuesta vacía." },
        { "operadores", "not-json", "La API devolvió una respuesta con un formato inesperado." },
        { "operadores", "", "La API devolvió una respuesta con un formato inesperado." },
        { "listar", "null", "La API devolvió una respuesta vacía." },
        { "listar", "not-json", "La API devolvió una respuesta con un formato inesperado." },
        { "listar", "", "La API devolvió una respuesta con un formato inesperado." },
        { "crear", "null", "La API devolvió una respuesta vacía." },
        { "crear", "not-json", "La API devolvió una respuesta con un formato inesperado." },
        { "crear", "", "La API devolvió una respuesta con un formato inesperado." },
    };

    [Theory]
    [MemberData(nameof(InvalidResponses))]
    public async Task Clients_PreserveNullMalformedAndEmptyResponseErrors(string operation, string body, string message)
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(Response(HttpStatusCode.OK, body)));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => Invoke(operation, http));
        Assert.Equal(message, error.Message);
        Assert.Equal(HttpStatusCode.OK, error.StatusCode);
    }

    private static Task Invoke(string operation, HttpClient http) => operation switch
    {
        "usuarios" => new UsuariosApiClient(http).ObtenerUsuarioActualAsync(ApiToken),
        "operadores" => new OperadoresApiClient(http).ListarOperadoresAsync(ApiToken),
        "listar" => new EnviosApiClient(http).ListarEnviosAsync(ApiToken),
        "crear" => new EnviosApiClient(http).CrearEnvioAsync(new CrearEnvioRequest(), ApiToken),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new TestHttpHandler(send)) { BaseAddress = new Uri("http://api.test/") };

    private static HttpResponseMessage Response(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class TestHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
