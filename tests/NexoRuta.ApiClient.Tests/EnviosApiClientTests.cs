using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class EnviosApiClientTests
{
    [Fact]
    public async Task CrearEnvioAsync_EnviaElContratoJsonYLeeLaRespuestaCreada()
    {
        using var http = CreateHttpClient(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/envios", request.RequestUri!.AbsolutePath);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("Ana", body.RootElement.GetProperty("destinatarioNombre").GetString());
            Assert.Equal(1250m, body.RootElement.GetProperty("pesoGramos").GetDecimal());
            Assert.False(body.RootElement.TryGetProperty("operadorId", out _));
            return JsonResponse(HttpStatusCode.Created, """
                {"id":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
                "operadorComercioId":"00000000-0000-0000-0000-000000000003","creadoPorUsuarioId":"00000000-0000-0000-0000-000000000004",
                "usuarioEmail":"demo@nexoruta.local","operadorNombre":"Operador Demo","comercioNombre":"Comercio Demo",
                "codigoBulto":"B-001","pesoGramos":1250,"largoCentimetros":30,"anchoCentimetros":20,"altoCentimetros":10}
                """);
        });

        var created = await new EnviosApiClient(http).CrearEnvioAsync(ValidRequest());

        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), created.Id);
        Assert.Equal("Comercio Demo", created.ComercioNombre);
        Assert.Equal("B-001", created.CodigoBulto);
        Assert.Equal(1250m, created.PesoGramos);
    }

    [Fact]
    public async Task ListarEnviosAsync_LeeLosBultosYLosIdentificadoresDelListado()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/envios", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                [{"id":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
                "operadorComercioId":"00000000-0000-0000-0000-000000000003","creadoPorUsuarioId":"00000000-0000-0000-0000-000000000004",
                "usuarioEmail":"demo@nexoruta.local","operadorNombre":"Operador Demo","comercioNombre":"Comercio Demo",
                "destinatarioNombre":"Ana","direccion":"18 de Julio 123","estado":"Admitido",
                "bultos":[{"codigo":"B-001","pesoGramos":"1250","largoCentimetros":30,"anchoCentimetros":20,"altoCentimetros":10}]}]
                """));
        });

        var shipment = Assert.Single(await new EnviosApiClient(http).ListarEnviosAsync());

        Assert.Equal("Ana", shipment.DestinatarioNombre);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000002"), shipment.OperadorId);
        Assert.Equal(1250m, Assert.Single(shipment.Bultos).PesoGramos);
    }

    [Fact]
    public async Task ObtenerContextoDemoAsync_ConsultaLaIdentidadDelServidor()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal("/api/demo/context", request.RequestUri!.AbsolutePath);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {"usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
                "operadorComercioId":"00000000-0000-0000-0000-000000000003","usuarioEmail":"demo@nexoruta.local",
                "operadorNombre":"Operador Demo","comercioNombre":"Comercio Demo"}
                """));
        });

        var context = await new EnviosApiClient(http).ObtenerContextoDemoAsync();
        Assert.Equal("demo@nexoruta.local", context.UsuarioEmail);
        Assert.Equal("Comercio Demo", context.ComercioNombre);
    }

    [Theory]
    [InlineData("{\"message\":\"El peso debe ser positivo.\"}", "El peso debe ser positivo.")]
    [InlineData("{\"title\":\"Error\",\"detail\":\"No hay acceso al comercio.\"}", "No hay acceso al comercio.")]
    [InlineData("{\"title\":\"Error\",\"errors\":{\"PesoGramos\":[\"Peso inválido.\"],\"Direccion\":[\"Falta dirección.\"]}}", "Peso inválido. Falta dirección.")]
    public async Task CrearEnvioAsync_ExponeMensajesDeErrorSinMostrarJson(string body, string expected)
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, body)));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).CrearEnvioAsync(ValidRequest()));
        Assert.Equal(expected, error.Message);
        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
    }

    [Fact]
    public async Task ListarEnviosAsync_UnErrorHtmlNoSeMuestraComoContenidoDelServidor()
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Internal proxy details</html>", Encoding.UTF8, "text/html")
        }));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ListarEnviosAsync());
        Assert.Equal("No se pudo completar la operación (HTTP 502).", error.Message);
    }

    [Theory]
    [InlineData("null", "La API devolvió una respuesta vacía.")]
    [InlineData("not-json", "La API devolvió una respuesta con un formato inesperado.")]
    public async Task ListarEnviosAsync_RechazaRespuestasExitosasInvalidas(string body, string expected)
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ListarEnviosAsync());
        Assert.Equal(expected, error.Message);
    }

    [Theory]
    [InlineData("0.01", true)]
    [InlineData("999999999", true)]
    [InlineData("0.009", false)]
    [InlineData("1000000000", false)]
    public void Formulario_RespetaLosLimitesNumericosDeLaApi(string value, bool expected)
    {
        var request = ValidRequest();
        var number = decimal.Parse(value, CultureInfo.InvariantCulture);
        request.PesoGramos = request.LargoCentimetros = request.AnchoCentimetros = request.AltoCentimetros = number;
        Assert.Equal(expected, Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
    }

    private static CrearEnvioRequest ValidRequest() => new()
    {
        DestinatarioNombre = "Ana", Direccion = "18 de Julio 123", CodigoBulto = "B-001",
        PesoGramos = 1250m, LargoCentimetros = 30m, AnchoCentimetros = 20m, AltoCentimetros = 10m
    };

    private static HttpClient CreateHttpClient(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        => new(new TestHttpHandler(send)) { BaseAddress = new Uri("http://api.test/") };

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json)
        => new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class TestHttpHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => send(request, cancellationToken);
    }
}
