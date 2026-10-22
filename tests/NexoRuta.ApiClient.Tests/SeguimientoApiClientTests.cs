using System.Net;
using System.Text;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Seguimiento.Clients;

namespace NexoRuta.ApiClient.Tests;

public sealed class SeguimientoApiClientTests
{
    private static readonly string Token = new('a', 64);

    [Fact]
    public async Task TokenValido_DevuelveEstado()
    {
        using var http = CrearCliente(
            HttpStatusCode.OK,
            """{"estado":"Admitido"}""");

        var resultado = await new SeguimientoApiClient(http)
            .ConsultarAsync(Token);

        Assert.NotNull(resultado);
        Assert.Equal("Admitido", resultado.Estado);
    }

    [Fact]
    public async Task TokenInexistente_DevuelveNull()
    {
        using var http = CrearCliente(
            HttpStatusCode.NotFound, "{}");

        var resultado = await new SeguimientoApiClient(http)
            .ConsultarAsync(Token);

        Assert.Null(resultado);
    }

    [Fact]
    public async Task LimiteExcedido_Propaga429()
    {
        using var http = CrearCliente(
            HttpStatusCode.TooManyRequests, "{}");

        var error = await Assert.ThrowsAsync<NexoRutaApiException>(
            () => new SeguimientoApiClient(http)
                .ConsultarAsync(Token));

        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
    }

    private static HttpClient CrearCliente(
        HttpStatusCode estado,
        string respuesta)
    {
        return new HttpClient(
            new RespuestaSimuladaHandler(estado, respuesta))
        {
            BaseAddress = new Uri("http://api.test/")
        };
    }

    private sealed class RespuestaSimuladaHandler(
        HttpStatusCode estado,
        string respuesta) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                $"/api/public/seguimiento/{Token}",
                request.RequestUri!.AbsolutePath);

            Assert.False(
                request.Headers.Contains("X-NexoRuta-Acceso"));
            Assert.Null(request.Headers.Authorization);

            return Task.FromResult(new HttpResponseMessage(estado)
            {
                Content = new StringContent(
                    respuesta,
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }
}
