using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexoRuta.Backoffice.Features.Reparto.Pages;
namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    private static readonly Guid RutaPrueba = Guid.Parse("00000000-0000-0000-0000-000000000701");
    private static readonly Guid EnvioPrueba = Guid.Parse("00000000-0000-0000-0000-000000000702");
    private static readonly Guid VehiculoPrueba = Guid.Parse("00000000-0000-0000-0000-000000000703");

    [Fact]
    public async Task BackofficeShowsUnavailableStateInsteadOfDefaultsWhenRepartoEndpointsAreMissing()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar", "envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var delivery = await client.GetAsync("/configuracion-reparto");
        var deliveryHtml = await delivery.Content.ReadAsStringAsync();
        Assert.Contains("role=\"alert\"", deliveryHtml);
        Assert.Contains("Reintentar", deliveryHtml);
        Assert.DoesNotContain("Guardar nueva versión", deliveryHtml);
        Assert.DoesNotContain("No hay vehículos", deliveryHtml);

        using var couriers = await client.GetAsync("/repartidores");
        var couriersHtml = await couriers.Content.ReadAsStringAsync();
        Assert.Contains("role=\"alert\"", couriersHtml);
        Assert.Contains("Reintentar", couriersHtml);
        Assert.DoesNotContain("No hay cuentas activas con rol pendientes de vincular", couriersHtml);
        Assert.DoesNotContain("Habilitar un repartidor", couriersHtml);

        using var routes = await client.GetAsync("/rutas");
        var routesHtml = await routes.Content.ReadAsStringAsync();
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow,
            TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo")).DateTime).ToString("yyyy-MM-dd");
        Assert.Contains($"value=\"{date}\"", routesHtml);
        Assert.Contains("role=\"alert\"", routesHtml);
        Assert.Contains("Reintentar", routesHtml);
        Assert.DoesNotContain("No hay rutas para esta fecha", routesHtml);
        Assert.DoesNotContain("Planificar una ruta", routesHtml);
    }

    [Fact]
    public async Task ConfigurationPostDoesNotWriteUntilItsReadEndpointsAreAvailable()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/ingresar");

        using var response = await client.PostAsync("/configuracion-reparto?handler=Politica", Form(token,
            ("Politica.Version", "0"), ("Politica.ZonaHoraria", "America/Montevideo"),
            ("Politica.MaximoParadas", "100"), ("Politica.RequiereFotoIntento", "false"),
            ("Politica.BloquearDiscrepanciasCarga", "false")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/reparto/configuracion" && call.Method == HttpMethod.Put);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("America/Montevideo", html);
        Assert.Contains("role=\"alert\"", html);
    }

    [Fact]
    public async Task RepartidoresKeepSubmittedValuesWhenTheApiFailsAndCanBeRetried()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/ingresar");

        using var response = await client.PostAsync("/repartidores?handler=Guardar", Form(token,
            ("accesoId", TargetAccessId.ToString()), ("nombre", "Repartidor conservado"), ("activo", "true")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Repartidor conservado", html);
        Assert.Contains("Reintentar guardado", html);
        Assert.Contains("role=\"alert\"", html);
    }

    [Fact]
    public async Task RutasPermitePlanificarRecibirYPostergarConVersionesYHoraDelOperador()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");
        using var consulta = await client.GetAsync("/rutas?fecha=2026-10-20");
        var html = await consulta.Content.ReadAsStringAsync();
        Assert.Contains("Rutas y dep", html);
        Assert.Contains("CrearBorrador", html);
        Assert.Contains("name=\"versionEnvio\" value=\"8\"", html);
        Assert.DoesNotContain("jwt-user", html);
        using var plan = await client.PostAsync("/rutas?handler=Planificar", Form(token,
            ("Fecha", "2026-10-20"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId", EnvioPrueba.ToString()), ("Plan[0].Incluir", "true"), ("Plan[0].Orden", "1"), ("Plan[0].Hora", "10:30")));
        Assert.Equal(HttpStatusCode.Redirect, plan.StatusCode);
        var comando = Assert.Single(api.RepartoPayloads);
        Assert.Equal("/api/rutas", comando.Path);
        var parada = Assert.Single(comando.Body.GetProperty("paradas").EnumerateArray());
        Assert.Equal(DateTimeOffset.Parse("2026-10-20T13:30:00Z"), parada.GetProperty("llegadaEstimadaUtc").GetDateTimeOffset());
        using var recibir = await client.PostAsync("/rutas?handler=Recibir", Form(token,
            ("Fecha", "2026-10-20"), ("envioId", EnvioPrueba.ToString()), ("version", "8")));
        Assert.Equal(HttpStatusCode.Redirect, recibir.StatusCode);
        Assert.Equal(8, api.RepartoPayloads.Last().Body.GetProperty("version").GetInt64());
        using var reasignar = await client.PostAsync("/rutas?handler=Reasignar", Form(token,
            ("Fecha", "2026-10-20"), ("envioId", EnvioPrueba.ToString()), ("versionEnvio", "8"),
            ("destino", $"{RutaPrueba}:3"), ("hora", "14:00"), ("motivo", "Postergar para mañana")));
        Assert.Equal(HttpStatusCode.Redirect, reasignar.StatusCode);
        var mover = api.RepartoPayloads.Last();
        Assert.Equal($"/api/envios/{EnvioPrueba}/reasignacion", mover.Path);
        Assert.Equal(8, mover.Body.GetProperty("versionEnvio").GetInt64());
        Assert.Equal(3, mover.Body.GetProperty("versionRutaDestino").GetInt64());
        Assert.Equal(DateTimeOffset.Parse("2026-10-20T17:00:00Z"), mover.Body.GetProperty("llegadaEstimadaUtc").GetDateTimeOffset());
        Assert.All(api.RepartoPayloads, p => Assert.Equal("jwt-user", p.Token));
    }

    [Fact]
    public async Task CierreConIncidenciaMuestraHistorialCustodiaYRecepcionSinDecisionSobreLaEntrega()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeIncidenciaCierre = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/rutas?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Cierre con incidencia", html);
        Assert.Contains("TEST-FALTANTE", html);
        Assert.Contains("Custodia actual:", html);
        Assert.Contains("Repartidor", html);
        Assert.Contains("Confirmar recepción de todos los bultos", html);
        Assert.DoesNotContain("Aceptar entrega", html);
        Assert.DoesNotContain("Rechazar entrega", html);
    }

    [Fact]
    public async Task RutasSinPermisoNoExponeControlesNiLlamaContratosOperativos()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var denied = await client.GetAsync("/rutas");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        Assert.DoesNotContain(api.Calls, c => c.Path.StartsWith("/api/rutas", StringComparison.Ordinal));
    }

    [Fact]
    public void HoraAmbiguaONoExistenteNoEligeUnDesfaseSilenciosamente()
    {
        Assert.Throws<ArgumentException>(() => RutasModel.ConvertirLlegada(new(2026, 3, 8), new(2, 30), "America/New_York"));
        Assert.Throws<ArgumentException>(() => RutasModel.ConvertirLlegada(new(2026, 11, 1), new(1, 30), "America/New_York"));
    }

    private sealed partial class FakeApi
    {
        public bool IncludeReparto { get; init; }
        public bool IncludeIncidenciaCierre { get; init; }
        public bool IsCancellationAdmin { get; init; }
        public bool CancellationApplied { get; private set; }
        public System.Collections.Concurrent.ConcurrentQueue<ZonePayloadCall> RepartoPayloads { get; } = new();
        private async Task<HttpResponseMessage?> Reparto(HttpRequestMessage request, string path, string? token, CancellationToken ct)
        {
            var politica = new { version = 1, zonaHoraria = "America/Montevideo", maximoParadas = 100, maximoIntentos = 3, motivos = new[] { "Ausente" } };
            var bultoId = Guid.Parse("00000000-0000-0000-0000-000000000704");
            object[] paradas = IncludeIncidenciaCierre ? [new { envioId = EnvioPrueba, orden = 1, activa = false,
                llegadaEstimadaUtc = DateTimeOffset.Parse("2026-10-20T18:00:00Z"), estado = "EnTransito", envioVersion = 8,
                destinatario = "Destino de prueba", direccion = "Dirección de prueba", latitud = -34.9, longitud = -56.2,
                bultos = new[] { new { id = bultoId, codigo = "TEST-FALTANTE", custodia = "Repartidor", version = 2 } } }] : [];
            object[] incidenciasCierre = IncludeIncidenciaCierre ? [new { operacionId = Guid.NewGuid(), envioId = EnvioPrueba,
                bultoId, codigo = "TEST-FALTANTE", versionBulto = 2, ocurridaEnUtc = DateTimeOffset.UtcNow, recibidaEnUtc = DateTimeOffset.UtcNow }] : [];
            var ruta = new { id = RutaPrueba, jornadaId = Guid.NewGuid(), repartidorAccesoId = TargetAccessId, vehiculoId = VehiculoPrueba,
                matricula = "AAA 1234", fecha = "2026-10-20", estado = IncludeIncidenciaCierre ? "Finalizada" : "Planificada", version = 3, ultimaSecuencia = 0, politica, paradas, incidenciasCierre };
            if (path == "/api/rutas/recursos") return Json(HttpStatusCode.OK, new { politica,
                repartidores = new[] { new { accesoId = TargetAccessId, nombre = "Repartidor de prueba" } },
                vehiculos = new[] { new { id = VehiculoPrueba, matricula = "AAA 1234", pesoMaximoGramos = 10000m, volumenMaximoCm3 = 10000m, activo = true, version = 1 } } });
            if (path == "/api/rutas" && request.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, new[] { ruta });
            if (path == "/api/envios") return Json(HttpStatusCode.OK, new[] { new { id = EnvioPrueba, operadorId = Guid.NewGuid(), comercioId = Guid.NewGuid(),
                creadoPorUsuarioId = Guid.NewGuid(), usuarioEmail = "comercio@example.test", operadorNombre = "Operador", comercioNombre = "Comercio",
                destinatarioNombre = "Destino de prueba", direccion = "Dirección de prueba", estado = CancellationApplied ? "Cancelado" : "EnTransito", version = CancellationApplied ? 9 : 8,
                bultos = new[] { new { codigo = "TEST", pesoGramos = 100m, largoCentimetros = 1m, anchoCentimetros = 1m, altoCentimetros = 1m } } } });
            if (request.Method == HttpMethod.Post && path.EndsWith("/cancelacion", StringComparison.Ordinal))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                CancellationApplied = true;
                return Json(HttpStatusCode.OK, new { cancelacionId = Guid.NewGuid(), envioId = EnvioPrueba, version = 9, estado = "Cancelado" });
            }
            if (request.Method == HttpMethod.Post && (path == "/api/rutas" || path.EndsWith("/reasignacion", StringComparison.Ordinal) || path.EndsWith("/recepcion", StringComparison.Ordinal)))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                return path.EndsWith("/recepcion", StringComparison.Ordinal) ? new(HttpStatusCode.NoContent) : Json(HttpStatusCode.OK, ruta);
            }
            return null;
        }
    }
}
