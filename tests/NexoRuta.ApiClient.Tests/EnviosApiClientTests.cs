using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class EnviosApiClientTests
{
    private static readonly Guid AccesoId = Guid.Parse("00000000-0000-0000-0000-000000000005");
    [Fact]
    public async Task CrearEnvioAsync_EnviaElContratoJsonYLeeLaRespuestaCreada()
    {
        using var http = CreateHttpClient(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/envios", request.RequestUri!.AbsolutePath);
            Assert.Equal(AccesoId.ToString(), Assert.Single(request.Headers.GetValues("X-NexoRuta-Acceso")));
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("Ana", body.RootElement.GetProperty("destinatarioNombre").GetString());
            Assert.Equal(1250m, body.RootElement.GetProperty("pesoGramos").GetDecimal());
            Assert.Equal("00000000-0000-0000-0000-000000000002", body.RootElement.GetProperty("operadorId").GetString());
            Assert.False(body.RootElement.TryGetProperty("comercioId", out _));
            Assert.False(body.RootElement.TryGetProperty("creadoPorUsuarioId", out _));
            return JsonResponse(HttpStatusCode.Created, """
                {"id":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
                "comercioId":"00000000-0000-0000-0000-000000000003","creadoPorUsuarioId":"00000000-0000-0000-0000-000000000004",
                "usuarioEmail":"ana@comercio.local","operadorNombre":"Distribución Sur","comercioNombre":"Comercio Centro",
                "codigoBulto":"B-001","pesoGramos":1250,"largoCentimetros":30,"anchoCentimetros":20,"altoCentimetros":10}
                """);
        });

        var created = await new EnviosApiClient(http).CrearEnvioAsync(ValidRequest(), AccesoId);

        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000001"), created.Id);
        Assert.Equal("Comercio Centro", created.ComercioNombre);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000003"), created.ComercioId);
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
                "comercioId":"00000000-0000-0000-0000-000000000003","creadoPorUsuarioId":"00000000-0000-0000-0000-000000000004",
                "usuarioEmail":"ana@comercio.local","operadorNombre":"Distribución Sur","comercioNombre":"Comercio Centro",
                "destinatarioNombre":"Ana","direccion":"18 de Julio 123","estado":"Admitido",
                "bultos":[{"codigo":"B-001","pesoGramos":"1250","largoCentimetros":30,"anchoCentimetros":20,"altoCentimetros":10}]}]
                """));
        });

        var shipment = Assert.Single(await new EnviosApiClient(http).ListarEnviosAsync(AccesoId));

        Assert.Equal("Ana", shipment.DestinatarioNombre);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000003"), shipment.ComercioId);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000002"), shipment.OperadorId);
        Assert.Equal(1250m, Assert.Single(shipment.Bultos).PesoGramos);
    }

    [Fact]
    public async Task ObtenerUsuarioActualAsync_ConsultaLaIdentidadDelServidor()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal("/api/usuarios/actual", request.RequestUri!.AbsolutePath);
            Assert.Equal(AccesoId.ToString(), Assert.Single(request.Headers.GetValues("X-NexoRuta-Acceso")));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {"accesoId":"00000000-0000-0000-0000-000000000005","tipo":"Comercio",
                "usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":null,"esPropietario":true,
                "comercioId":"00000000-0000-0000-0000-000000000003","usuarioEmail":"ana@comercio.local",
                "operadorNombre":null,"comercioNombre":"Comercio Centro"}
                """));
        });

        var usuario = await new EnviosApiClient(http).ObtenerUsuarioActualAsync(AccesoId);
        Assert.Equal(AccesoId, usuario.AccesoId);
        Assert.Equal("Comercio", usuario.Tipo);
        Assert.Equal("ana@comercio.local", usuario.UsuarioEmail);
        Assert.Equal("Comercio Centro", usuario.ComercioNombre);
    }

    [Theory]
    [InlineData("{\"message\":\"El peso debe ser positivo.\"}", "El peso debe ser positivo.")]
    [InlineData("{\"title\":\"Error\",\"detail\":\"No hay acceso al comercio.\"}", "No hay acceso al comercio.")]
    [InlineData("{\"title\":\"Error\",\"errors\":{\"PesoGramos\":[\"Peso inválido.\"],\"Direccion\":[\"Falta dirección.\"]}}", "Peso inválido. Falta dirección.")]
    public async Task CrearEnvioAsync_ExponeMensajesDeErrorSinMostrarJson(string body, string expected)
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.BadRequest, body)));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).CrearEnvioAsync(ValidRequest(), AccesoId));
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
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ListarEnviosAsync(AccesoId));
        Assert.Equal("No se pudo completar la operación (HTTP 502).", error.Message);
    }

    [Theory]
    [InlineData("null", "La API devolvió una respuesta vacía.")]
    [InlineData("not-json", "La API devolvió una respuesta con un formato inesperado.")]
    public async Task ListarEnviosAsync_RechazaRespuestasExitosasInvalidas(string body, string expected)
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ListarEnviosAsync(AccesoId));
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

    [Fact]
    public async Task ListarAccesosAsync_ConsultaElTipoSinUnaIdentidadPreseleccionada()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal("/api/accesos", request.RequestUri!.AbsolutePath);
            Assert.Equal("?tipo=Comercio", request.RequestUri.Query);
            Assert.False(request.Headers.Contains("X-NexoRuta-Acceso"));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                [{"accesoId":"00000000-0000-0000-0000-000000000005","tipo":"Comercio",
                "usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":null,"esPropietario":true,
                "comercioId":"00000000-0000-0000-0000-000000000003","usuarioEmail":"ana@comercio.local",
                "operadorNombre":null,"comercioNombre":"Comercio Centro"}]
                """));
        });

        var usuario = Assert.Single(await new EnviosApiClient(http).ListarAccesosAsync(SesionUsuario.Comercio));
        Assert.Equal(AccesoId, usuario.AccesoId);
        Assert.Equal("Comercio", usuario.Tipo);
    }

    [Fact]
    public async Task ObtenerUsuarioActualAsync_UnOperadorNoTieneUnComercioAsignado()
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """
            {"accesoId":"00000000-0000-0000-0000-000000000005","tipo":"Operador",
            "usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
            "comercioId":null,"esPropietario":false,"usuarioEmail":"operador@empresa.local",
            "operadorNombre":"Distribución Sur","comercioNombre":null}
            """)));

        var usuario = await new EnviosApiClient(http).ObtenerUsuarioActualAsync(AccesoId);
        Assert.Equal(SesionUsuario.Operador, usuario.Tipo);
        Assert.Null(usuario.ComercioId);
        Assert.Null(usuario.ComercioNombre);
    }

    [Fact]
    public async Task ListarOperadoresAsync_LeeLasOpcionesDelComercioAutenticado()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal("/api/comercio/operadores", request.RequestUri!.AbsolutePath);
            Assert.Equal(AccesoId.ToString(), Assert.Single(request.Headers.GetValues("X-NexoRuta-Acceso")));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                [{"operadorId":"00000000-0000-0000-0000-000000000002",
                "nombre":"Distribución Sur"}]
                """));
        });

        var operador = Assert.Single(await new EnviosApiClient(http).ListarOperadoresAsync(AccesoId));
        Assert.Equal("Distribución Sur", operador.Nombre);
        Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000002"), operador.OperadorId);
    }

    [Fact]
    public void Formulario_ExigeSeleccionarElOperadorDelEnvio()
    {
        var request = ValidRequest();
        request.OperadorId = null;
        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("operadores")]
    [InlineData("alta")]
    public async Task Peticion_PropagaLaCancelacionAlTransporteHttp(string operacion)
    {
        using var cancelacion = new CancellationTokenSource();
        var iniciada = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = CreateHttpClient(async (_, cancellationToken) =>
        {
            iniciada.SetResult(cancellationToken);
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("La petición debía cancelarse.");
        });
        var cliente = new EnviosApiClient(http);
        Task peticion = operacion switch
        {
            "usuario" => cliente.ObtenerUsuarioActualAsync(AccesoId, cancelacion.Token),
            "operadores" => cliente.ListarOperadoresAsync(AccesoId, cancelacion.Token),
            _ => cliente.CrearEnvioAsync(ValidRequest(), AccesoId, cancelacion.Token)
        };
        var tokenDelTransporte = await iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancelacion.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peticion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(tokenDelTransporte.IsCancellationRequested);
    }

    private static CrearEnvioRequest ValidRequest() => new()
    {
        OperadorId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
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
