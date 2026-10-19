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
        public bool IsCancellationAdmin { get; init; }
        public bool CancellationApplied { get; private set; }
        public System.Collections.Concurrent.ConcurrentQueue<ZonePayloadCall> RepartoPayloads { get; } = new();
        private async Task<HttpResponseMessage?> Reparto(HttpRequestMessage request, string path, string? token, CancellationToken ct)
        {
            var politica = new { version = 1, zonaHoraria = "America/Montevideo", maximoParadas = 100, maximoIntentos = 3, motivos = new[] { "Ausente" } };
            var ruta = new { id = RutaPrueba, jornadaId = Guid.NewGuid(), repartidorAccesoId = TargetAccessId, vehiculoId = VehiculoPrueba,
                matricula = "AAA 1234", fecha = "2026-10-20", estado = "Planificada", version = 3, ultimaSecuencia = 0, politica, paradas = Array.Empty<object>() };
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
