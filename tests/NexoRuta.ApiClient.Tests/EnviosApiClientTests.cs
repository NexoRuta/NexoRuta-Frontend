using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Core.Session;

namespace NexoRuta.ApiClient.Tests;

public sealed class EnviosApiClientTests
{
    private static readonly Guid AccesoId = Guid.Parse("00000000-0000-0000-0000-000000000005");
    private const string ApiToken = "session-jwt";
    [Fact]
    public async Task CrearEnvioAsync_EnviaElContratoJsonYLeeLaRespuestaCreada()
    {
        using var http = CreateHttpClient(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal("/api/envios", request.RequestUri!.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(ApiToken, request.Headers.Authorization.Parameter);
            Assert.Equal("application/json", request.Content!.Headers.ContentType!.MediaType);
            using var body = JsonDocument.Parse(await request.Content.ReadAsStringAsync(cancellationToken));
            Assert.Equal("Ana", body.RootElement.GetProperty("destinatarioNombre").GetString());
            Assert.Equal("destinatario@example.test", body.RootElement.GetProperty("destinatarioEmail").GetString());
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

        var created = await new EnviosApiClient(http).CrearEnvioAsync(ValidRequest(), ApiToken);

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
            Assert.Equal(ApiToken, request.Headers.Authorization!.Parameter);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                [{"id":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
                "comercioId":"00000000-0000-0000-0000-000000000003","creadoPorUsuarioId":"00000000-0000-0000-0000-000000000004",
                "usuarioEmail":"ana@comercio.local","operadorNombre":"Distribución Sur","comercioNombre":"Comercio Centro",
                "destinatarioNombre":"Ana","destinatarioEmail":"ana.destino@example.test",
                "referenciaExterna":"PED-2026-0001","direccion":"18 de Julio 123","estado":"Admitido",
                "bultos":[{"codigo":"B-001","pesoGramos":"1250","largoCentimetros":30,"anchoCentimetros":20,"altoCentimetros":10}]}]
                """));
        });

        var shipment = Assert.Single(await new EnviosApiClient(http).ListarEnviosAsync(ApiToken));

        Assert.Equal("Ana", shipment.DestinatarioNombre);
        Assert.Equal("ana.destino@example.test", shipment.GetType().GetProperty("DestinatarioEmail")?.GetValue(shipment));
        Assert.Equal("PED-2026-0001", shipment.GetType().GetProperty("ReferenciaExterna")?.GetValue(shipment));
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
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
            Assert.Equal(ApiToken, request.Headers.Authorization.Parameter);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                {"accesoId":"00000000-0000-0000-0000-000000000005","tipo":"Comercio",
                "usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":null,
                "rolId": "00000000-0000-0000-0000-000000000007",
                "rolCodigo": "administrador_operador",
                "comercioId":"00000000-0000-0000-0000-000000000003","usuarioEmail":"ana@comercio.local",
                "operadorNombre":null,"comercioNombre":"Comercio Centro"}
                """));
        });

        var usuario = await new UsuariosApiClient(http).ObtenerUsuarioActualAsync(ApiToken);
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
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).CrearEnvioAsync(ValidRequest(), ApiToken));
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
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ListarEnviosAsync(ApiToken));
        Assert.Equal("No se pudo completar la operación (HTTP 502).", error.Message);
    }

    [Theory]
    [InlineData("null", "La API devolvió una respuesta vacía.")]
    [InlineData("not-json", "La API devolvió una respuesta con un formato inesperado.")]
    public async Task ListarEnviosAsync_RechazaRespuestasExitosasInvalidas(string body, string expected)
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, body)));
        var error = await Assert.ThrowsAsync<NexoRutaApiException>(() => new EnviosApiClient(http).ListarEnviosAsync(ApiToken));
        Assert.Equal(expected, error.Message);
    }

    [Theory]
    [InlineData("0.01", true)]
    [InlineData("999999999", true)]
    [InlineData("0.009", false)]
    [InlineData("1000000000", false)]
    public void Formulario_RespetaLosLimitesNumericosDeLaApi(string value, bool expected)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-UY");
            var request = ValidRequest();
            var number = decimal.Parse(value, CultureInfo.InvariantCulture);
            request.PesoGramos = request.LargoCentimetros = request.AnchoCentimetros = request.AltoCentimetros = number;
            Assert.Equal(expected, Validator.TryValidateObject(request, new ValidationContext(request), [], validateAllProperties: true));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    [Fact]
    public async Task ObtenerUsuarioActualAsync_UnaCuentaDelOperadorNoPerteneceAUnComercio()
    {
        using var http = CreateHttpClient((_, _) => Task.FromResult(JsonResponse(HttpStatusCode.OK, """
            {"accesoId":"00000000-0000-0000-0000-000000000005","tipo":"Operador",
            "usuarioId":"00000000-0000-0000-0000-000000000001","operadorId":"00000000-0000-0000-0000-000000000002",
            "comercioId":null,
            "rolId": "00000000-0000-0000-0000-000000000007",
            "rolCodigo": "administrador_operador",
            "usuarioEmail":"operador@empresa.local",
            "operadorNombre":"Distribución Sur","comercioNombre":null}
            """)));

        var usuario = await new UsuariosApiClient(http).ObtenerUsuarioActualAsync(ApiToken);
        Assert.Equal(SesionUsuario.Operador, usuario.Tipo);
        Assert.Null(usuario.ComercioId);
        Assert.Null(usuario.ComercioNombre);
    }

    [Fact]
    public async Task ListarOperadoresAsync_ConsultaElCatalogoGlobalConBearer()
    {
        using var http = CreateHttpClient((request, _) =>
        {
            Assert.Equal("/api/operadores", request.RequestUri!.AbsolutePath);
            Assert.Equal(ApiToken, request.Headers.Authorization!.Parameter);
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, """
                [{"operadorId":"00000000-0000-0000-0000-000000000002",
                "nombre":"Distribución Sur"}]
                """));
        });

        var operador = Assert.Single(await new OperadoresApiClient(http).ListarOperadoresAsync(ApiToken));
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

    [Fact]
    public void Formulario_EmailInvalidoMuestraUnMensajeDeValidacionEnEspanol()
    {
        var request = ValidRequest();
        request.DestinatarioEmail = "no-es-un-correo";
        var errors = new List<ValidationResult>();

        Assert.False(Validator.TryValidateObject(request, new ValidationContext(request), errors, validateAllProperties: true));
        Assert.Contains(errors, error => error.ErrorMessage == "Ingresá un correo electrónico válido.");
    }

    [Theory]
    [InlineData("usuario")]
    [InlineData("operadores")]
    [InlineData("alta")]
    [InlineData("listado")]
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
        Task peticion = operacion switch
        {
            "usuario" => new UsuariosApiClient(http).ObtenerUsuarioActualAsync(ApiToken, cancelacion.Token),
            "operadores" => new OperadoresApiClient(http).ListarOperadoresAsync(ApiToken, cancelacion.Token),
            "listado" => new EnviosApiClient(http).ListarEnviosAsync(ApiToken, cancelacion.Token),
            _ => new EnviosApiClient(http).CrearEnvioAsync(ValidRequest(), ApiToken, cancelacion.Token)
        };
        var tokenDelTransporte = await iniciada.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancelacion.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => peticion.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.True(tokenDelTransporte.IsCancellationRequested);
    }

    private static CrearEnvioRequest ValidRequest() => new()
    {
        OperadorId = Guid.Parse("00000000-0000-0000-0000-000000000002"),
        DestinatarioNombre = "Ana", DestinatarioEmail = "destinatario@example.test",
        Direccion = "18 de Julio 123",
        CodigoBulto = "B-001",
        PesoGramos = 1250m,
        LargoCentimetros = 30m,
        AnchoCentimetros = 20m,
        AltoCentimetros = 10m
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
