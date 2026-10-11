using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Features.Envios.Clients;

namespace NexoRuta.ApiClient.Tests;

public sealed class EnlaceSeguimientoTests
{
    private static readonly Guid Acceso = Guid.NewGuid();
    private static readonly Guid Envio = Guid.NewGuid();
    private static readonly string Token = new('a', 64);
    private static readonly Uri Tracking = new("http://tracking.test/portal/");

    [Fact]
    public async Task Vigente_AbreTrackingConTokenOriginal()
    {
        using var http = Cliente(HttpStatusCode.OK, Enlace());
        var url = await new EnviosApiClient(http).ObtenerSeguimientoAsync(Envio, Acceso, Tracking);
        Assert.Equal($"http://tracking.test/portal/seguimiento/{Token}", url!.AbsoluteUri);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"estado\":\"pendiente\"}")]
    public async Task Pendiente_NoAbreEnlace(string body)
    {
        using var http = Cliente(HttpStatusCode.Accepted, body);
        Assert.Null(await new EnviosApiClient(http).ObtenerSeguimientoAsync(Envio, Acceso, Tracking));
    }

    [Theory]
    [InlineData(HttpStatusCode.Gone)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Error_ConservaCodigoHttp(HttpStatusCode status)
    {
        using var http = Cliente(status, "{}");
        var error = await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            new EnviosApiClient(http).ObtenerSeguimientoAsync(Envio, Acceso, Tracking));
        Assert.Equal(status, error.StatusCode);
    }

    [Theory]
    [InlineData("vigente", "https://api.test/api/public/seguimiento/", true)]
    [InlineData("revocado", "https://api.test/api/public/seguimiento/", false)]
    [InlineData("vigente", "javascript:", false)]
    [InlineData("vigente", "https://api.test/unrelated/", false)]
    [InlineData("vigente", "https://user:secret@api.test/api/public/seguimiento/", false)]
    public async Task Respuesta_ValidaEstadoRutaYExpiracion(string estado, string prefix, bool expirada)
    {
        using var http = Cliente(HttpStatusCode.OK, Enlace(estado, prefix, expirada));
        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            new EnviosApiClient(http).ObtenerSeguimientoAsync(Envio, Acceso, Tracking));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("not-json")]
    [InlineData("{\"estado\":\"vigente\"}")]
    public async Task RespuestaIncompleta_NoNavega(string body)
    {
        using var http = Cliente(HttpStatusCode.OK, body);
        await Assert.ThrowsAnyAsync<HttpRequestException>(() =>
            new EnviosApiClient(http).ObtenerSeguimientoAsync(Envio, Acceso, Tracking));
    }

    private static string Enlace(string estado = "vigente",
        string prefix = "https://api.test/api/public/seguimiento/", bool expirada = false) =>
        JsonSerializer.Serialize(new { estado, url = prefix + Token,
            expiraEnUtc = DateTimeOffset.UtcNow.AddHours(expirada ? -1 : 1) });

    private static HttpClient Cliente(HttpStatusCode status, string body) =>
        new(new Handler(status, body)) { BaseAddress = new Uri("http://api.test/") };

    private sealed class Handler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal($"/api/envios/{Envio}/seguimiento", request.RequestUri!.AbsolutePath);
            Assert.Equal(Acceso.ToString(), Assert.Single(request.Headers.GetValues("X-NexoRuta-Acceso")));
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }
}
