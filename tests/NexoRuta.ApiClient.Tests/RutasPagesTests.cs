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
    public async Task RutasPermitePlanificarYRecibirEnvioEnDepositoConVersionesYHoraDelOperador()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas/planificar?fecha=2026-10-20");
        using var consulta = await client.GetAsync("/rutas?fecha=2026-10-20");
        var html = await consulta.Content.ReadAsStringAsync();
        Assert.Contains("<h1>Rutas</h1>", html);
        Assert.DoesNotContain("name=\"CrearBorrador\"", html);
        Assert.Contains("rutas del día", WebUtility.HtmlDecode(html));
        Assert.Contains("/deposito", html);
        Assert.Contains("Planificar ruta", html);
        Assert.DoesNotContain("<summary class=\"h4\">Planificar una ruta</summary>", html);
        Assert.DoesNotContain("jwt-user", html);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000735:4", html);
        using var plan = await client.PostAsync("/rutas/planificar?handler=Planificar", Form(token,
            ("FechaSalida", "2026-10-20"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
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
        Assert.All(api.RepartoPayloads, p => Assert.Equal("jwt-user", p.Token));
    }

    [Fact]
    public async Task PlanificarRutaMuestraElegibilidadYResumenDinamicoConDatosDeLaApi()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/rutas/planificar?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Planificar ruta", html);
        Assert.Contains("Repartidor y vehículo", html);
        Assert.Contains("Envíos y paradas", html);
        Assert.Contains("Resumen de la ruta", html);
        Assert.Contains("Destino confirmado", html);
        Assert.Contains("Bulto recibido en depósito", html);
        Assert.Contains("data-plan-summary", html);
        Assert.Contains("name=\"CrearBorrador\"", html);
        Assert.Contains("aria-live=\"polite\"", html);
        Assert.Contains($"Plan[{0}].EnvioId", html);
        Assert.Contains("En depósito", html);
        Assert.DoesNotContain("Notificación enviada", html);
        Assert.DoesNotContain("En tránsito", html);
    }

    [Fact]
    public async Task PlanificacionIndicaAlComercioDondeConfirmarUnDestinoPendiente()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito", DestinationConfirmedOverride = false };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/rutas/planificar?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Destino pendiente", html);
        Assert.Contains("el comercio debe confirmar el destino desde Commerce > Mis envíos > Ubicación del destino", html);
        Assert.DoesNotContain("<a href=\"/envios/", html);
    }

    [Fact]
    public async Task PlanificarRutaConservaElContextoYVuelveAlPanelDiarioAlGuardar()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas/planificar?fecha=2026-10-20");

        using var response = await client.PostAsync("/rutas/planificar?handler=Planificar", Form(token,
            ("FechaSalida", "2026-10-20"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId", EnvioPrueba.ToString()), ("Plan[0].Incluir", "true"), ("Plan[0].Orden", "1"), ("Plan[0].Hora", "10:30")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/rutas?fecha=2026-10-20&pestana=rutas", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
        Assert.Single(api.RepartoPayloads, item => item.Path == "/api/rutas");
    }

    [Fact]
    public async Task PlanificarUsaLaFechaEditadaYNoPublicaDosControlesConElMismoNombre()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas/planificar?fecha=2026-10-29");

        using var initial = await client.GetAsync("/rutas/planificar?fecha=2026-10-29");
        var html = await initial.Content.ReadAsStringAsync();
        Assert.Equal(1, html.Split("name=\"FechaSalida\"", StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("name=\"Fecha\"", html);

        using var response = await client.PostAsync("/rutas/planificar?handler=Planificar&fecha=2026-10-29", Form(token,
            ("FechaSalida", "2026-10-30"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId", EnvioPrueba.ToString()), ("Plan[0].Incluir", "true"), ("Plan[0].Orden", "1"), ("Plan[0].Hora", "10:30")));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var comando = Assert.Single(api.RepartoPayloads, item => item.Path == "/api/rutas");
        Assert.Equal("2026-10-30", comando.Body.GetProperty("fecha").GetString());
        Assert.Contains("fecha=2026-10-30", response.Headers.Location!.OriginalString, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FechaDePlanificacionInvalidaNoSeSustituyePorHoyNiSeEnviaAlApi()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas/planificar?fecha=2026-10-29");

        using var response = await client.PostAsync("/rutas/planificar?handler=Planificar&fecha=2026-10-29", Form(token,
            ("FechaSalida", "fecha-invalida"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId", EnvioPrueba.ToString()), ("Plan[0].Incluir", "true"), ("Plan[0].Orden", "1"), ("Plan[0].Hora", "10:30")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("La fecha de salida no es válida", html);
        Assert.Contains("Fecha no válida", html);
        Assert.Contains("fecha-invalida", html);
        Assert.DoesNotContain(api.RepartoPayloads, call => call.Path == "/api/rutas");
    }

    [Fact]
    public async Task ConflictoDeReservaUsaLaFechaEditadaYEnlazaLaRutaQueOcupaElRecurso()
    {
        var api = new FakeApi
        {
            IncludeReparto = true,
            ShipmentStateOverride = "EnDeposito",
            RouteDateOverride = "2026-10-30",
            PlanificationConflictDetail = $"No se puede planificar. La ruta {RutaPrueba} del 2026-10-30 reserva el vehículo AAA 1234. Es una ruta vacía que conserva la reserva para recibir reasignaciones."
        };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas/planificar?fecha=2026-10-29");

        using var response = await client.PostAsync("/rutas/planificar?handler=Planificar&fecha=2026-10-29", Form(token,
            ("FechaSalida", "2026-10-30"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId", EnvioPrueba.ToString()), ("Plan[0].Incluir", "true"), ("Plan[0].Orden", "1"), ("Plan[0].Hora", "10:30")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("2026-10-30", html);
        Assert.Contains("ruta vacía que conserva la reserva", html);
        Assert.Contains($"/rutas?fecha=2026-10-30&pestana=rutas&rutaSeleccionadaId={RutaPrueba}", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(api.RepartoPayloads, call => call.Path == "/api/rutas" && call.Body.GetProperty("fecha").GetString() == "2026-10-29");
        Assert.Contains(api.RepartoPayloads, call => call.Path == "/api/rutas" && call.Body.GetProperty("fecha").GetString() == "2026-10-30");
    }

    [Fact]
    public async Task RutasSoloPermiteReasignarUnEnvioAsignadoConCustodiaEnDeposito()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "AsignadoRuta", AssignedStopActiveOverride = false, AssignedStopOmittedOverride = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Reasignar a esa ruta", html);
        Assert.Contains("no registra movimiento físico", html);
        var token = await GetAntiforgeryTokenAsync(client, "/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        using var reassigned = await client.PostAsync("/rutas?handler=Reasignar", Form(token,
            ("Fecha", "2026-10-20"), ("envioId", EnvioPrueba.ToString()), ("versionEnvio", "8"),
            ("destino", "00000000-0000-0000-0000-000000000734:3"), ("hora", "14:00"), ("motivo", "Cambio operativo")));
        Assert.Equal(HttpStatusCode.Redirect, reassigned.StatusCode);
        var command = Assert.Single(api.RepartoPayloads);
        Assert.Equal($"/api/envios/{EnvioPrueba}/reasignacion", command.Path);
        Assert.Equal(8, command.Body.GetProperty("versionEnvio").GetInt64());
        Assert.Equal(3, command.Body.GetProperty("versionRutaDestino").GetInt64());
        Assert.Equal(DateTimeOffset.Parse("2026-10-20T17:00:00Z"), command.Body.GetProperty("llegadaEstimadaUtc").GetDateTimeOffset());
    }

    [Fact]
    public async Task ReasignacionOfreceRutasVaciasIntencionalesPeroNoRutasCanceladas()
    {
        var api = new FakeApi
        {
            IncludeReparto = true,
            ShipmentStateOverride = "AsignadoRuta",
            AssignedStopOmittedOverride = true,
            IncludeReleasedPlanificada = true,
            ReleasedRouteStateOverride = "Cancelada"
        };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Reasignar a esa ruta", html);
        Assert.Contains("00000000-0000-0000-0000-000000000734:3", html);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000735:4", html);
    }

    [Fact]
    public async Task RutaCanceladaConservaHistorialPeroNoAparecePorDespacharNiOfreceAccionesIncompatibles()
    {
        var api = new FakeApi
        {
            IncludeReparto = true,
            ShipmentStateOverride = "AsignadoRuta",
            AssignedStopOmittedOverride = true,
            IncludeReleasedPlanificada = true,
            ReleasedRouteStateOverride = "Cancelada"
        };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/rutas?fecha=2026-10-20&pestana=rutas&rutaSeleccionadaId=00000000-0000-0000-0000-000000000735");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Ruta cancelada", html);
        Assert.Contains("Se conservan el historial", html);
        using var dayList = await client.GetAsync("/rutas?fecha=2026-10-20&filtroRuta=Cancelada");
        var dayHtml = WebUtility.HtmlDecode(await dayList.Content.ReadAsStringAsync());
        Assert.Contains("1 de 3", dayHtml);
        Assert.Contains("ruta cancelada", dayHtml.ToLowerInvariant());
        Assert.DoesNotContain("Despachar ruta", dayHtml);
        Assert.DoesNotContain("00000000-0000-0000-0000-000000000735:4", html);
        Assert.DoesNotContain("Despachar ruta", html);
    }

    [Fact]
    public async Task RutaVaciaPlanificadaPuedeCancelarseExplicitamenteYConflictoActualizaElDetalle()
    {
        var api = new FakeApi
        {
            IncludeReparto = true,
            ShipmentStateOverride = "AsignadoRuta",
            AssignedStopOmittedOverride = true,
            IncludeReleasedPlanificada = true,
            ReleasedReservationActive = true
        };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/rutas?fecha=2026-10-20&pestana=rutas&rutaSeleccionadaId=00000000-0000-0000-0000-000000000734");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("La ruta conserva su reserva", html);
        Assert.Contains("Cancelar ruta y liberar recursos", html);

        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");
        using var canceled = await client.PostAsync("/rutas?handler=CancelarRuta", Form(token,
            ("Fecha", "2026-10-20"), ("Pestana", "rutas"),
            ("RutaSeleccionadaId", "00000000-0000-0000-0000-000000000734"),
            ("rutaId", "00000000-0000-0000-0000-000000000734"), ("version", "3")));
        Assert.Equal(HttpStatusCode.Redirect, canceled.StatusCode);
        var call = Assert.Single(api.RepartoPayloads, x => x.Path.EndsWith("/cancelacion", StringComparison.Ordinal));
        Assert.Equal("/api/rutas/00000000-0000-0000-0000-000000000734/cancelacion", call.Path);
        Assert.Equal(3, call.Body.GetProperty("version").GetInt64());

        api.RouteCancelConflictOnce = true;
        api.ReleasedRouteStateOverride = "Planificada";
        api.ReleasedReservationActive = true;
        using var conflict = await client.PostAsync("/rutas?handler=CancelarRuta", Form(token,
            ("Fecha", "2026-10-20"), ("Pestana", "rutas"),
            ("RutaSeleccionadaId", "00000000-0000-0000-0000-000000000735"),
            ("rutaId", "00000000-0000-0000-0000-000000000735"), ("version", "4")));
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var updated = WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync());
        Assert.Contains("Se actualizó la ruta", updated);
        Assert.Contains("Ruta cancelada; recursos liberados", updated);
        Assert.DoesNotContain("name=\"rutaId\" value=\"00000000-0000-0000-0000-000000000735\"", updated);
    }

    [Fact]
    public async Task ApiAnteriorSinCampoDeReservaNoSeInterpretaComoRecursosLiberados()
    {
        var api = new FakeApi
        {
            IncludeReparto = true,
            ShipmentStateOverride = "AsignadoRuta",
            AssignedStopOmittedOverride = true,
            IncludeReleasedPlanificada = true,
            ReleasedReservationActive = null
        };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain("Recursos liberados", html);
        Assert.Contains("00000000-0000-0000-0000-000000000735:4", html);
    }

    [Fact]
    public async Task RutasRenderizaLaVersionDelEnvioComoEtiquetaLegible()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "AsignadoRuta" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync($"/rutas?fecha=2026-10-20&pestana=rutas&rutaSeleccionadaId={RutaPrueba:D}");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("class=\"nr-route-version\">Versión del envío <span>8</span>", html);
        Assert.DoesNotContain("v@parada.EnvioVersion", html);
    }

    [Fact]
    public async Task RutasNoPermiteReasignarUnEnvioEnTransito()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnTransito" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Los envíos en tránsito no se reasignan", html);
        Assert.DoesNotContain("Reasignar a esa ruta", html);
    }

    [Fact]
    public async Task RutasNoPermiteReasignarHastaRecibirLaCustodiaEnDeposito()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "AsignadoRuta", BultoCustodyOverride = "Repartidor" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("No se puede reasignar hasta recibir todos sus bultos físicamente en depósito", html);
        Assert.DoesNotContain("Reasignar a esa ruta", html);
    }

    [Fact]
    public async Task RutasFiltraDespachadasConBusquedaYSelecciónConservadas()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "AsignadoRuta" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync($"/rutas?fecha=2026-10-20&pestana=rutas&buscarRuta=AAA&filtroRuta=Despachada");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Contains("Despachadas", html);
        Assert.Contains("aria-current=\"page\"", html);
        Assert.Contains("1 de 2", html);
        Assert.Contains("value=\"AAA\"", html);
        Assert.Contains("/rutas/", html);
        Assert.Contains("AAA 1234", html);
    }

    [Fact]
    public async Task RutasSoloOfreceReprogramacionExplicitaConIntentosDisponibles()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeNoEntregado = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");

        using var consulta = await client.GetAsync("/rutas/00000000-0000-0000-0000-000000000701?fecha=2026-10-20");
        var html = WebUtility.HtmlDecode(await consulta.Content.ReadAsStringAsync());
        Assert.Contains("Requiere reprogramación a una ruta nueva", html);
        Assert.Contains("Intentos fallidos: 1 de 3", html);
        Assert.Contains("Próxima tentativa elegible desde 2026-10-20 15:30", html);
        Assert.Contains("data-single-submit", html);
        Assert.DoesNotContain("Postergar a esa ruta", html);

        using var response = await client.PostAsync("/rutas?handler=Reprogramar", Form(token,
            ("Fecha", "2026-10-20"), ("envioId", EnvioPrueba.ToString()), ("versionEnvio", "8")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = Assert.Single(api.RepartoPayloads, x => x.Path == $"/api/envios/{EnvioPrueba}/reprogramacion");
        Assert.Equal("jwt-user", request.Token);
        Assert.Equal(8, request.Body.GetProperty("versionEnvio").GetInt64());
    }

    [Fact]
    public async Task RutasNoOfreceReprogramacionCuandoLosIntentosEstanAgotados()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeNoEntregado = true, IntentosAgotados = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var consulta = await client.GetAsync("/rutas/00000000-0000-0000-0000-000000000701?fecha=2026-10-20");
        var html = WebUtility.HtmlDecode(await consulta.Content.ReadAsStringAsync());
        Assert.Contains("No quedan intentos disponibles", html);
        Assert.DoesNotContain("Reprogramar para una ruta nueva", html);
        Assert.DoesNotContain(api.RepartoPayloads, x => x.Path.EndsWith("/reprogramacion", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RutasIniciaDecisionOperativaConMotivoConfirmadoYPermiteReintentoSeguro()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeNoEntregado = true, IntentosAgotados = true,
            CanManageOperationalDecisions = true,
            DevolucionTransportFailureOnce = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");
        using var initial = await client.GetAsync("/rutas/00000000-0000-0000-0000-000000000701?fecha=2026-10-20");
        var initialHtml = WebUtility.HtmlDecode(await initial.Content.ReadAsStringAsync());
        Assert.Contains("No se puede reprogramar ni realizar otra tentativa", initialHtml);
        Assert.Contains("Iniciar devolución", initialHtml);
        Assert.Contains("Confirmo iniciar la devolución", initialHtml);
        Assert.Contains("no confirma que el bulto haya llegado al depósito", initialHtml);
        Assert.Contains("data-single-submit", initialHtml);

        var commandId = Guid.Parse("00000000-0000-0000-0000-000000000710");
        var values = Form(token, ("Fecha", "2026-10-20"), ("RutaSeleccionadaId", RutaPrueba.ToString()), ("DevolucionEnvioId", EnvioPrueba.ToString()),
            ("VersionEnvio", "8"), ("ComandoId", commandId.ToString()), ("MotivoDevolucion", " Motivo confirmado "),
            ("ConfirmarDevolucion", "true"));
        using var missingConfirmation = await client.PostAsync("/rutas?handler=IniciarDevolucion", Form(token,
            ("Fecha", "2026-10-20"), ("RutaSeleccionadaId", RutaPrueba.ToString()), ("DevolucionEnvioId", EnvioPrueba.ToString()), ("VersionEnvio", "8"),
            ("ComandoId", commandId.ToString()), ("MotivoDevolucion", " Motivo confirmado ")));
        Assert.Equal(HttpStatusCode.OK, missingConfirmation.StatusCode);
        Assert.DoesNotContain(api.RepartoPayloads, x => x.Path.EndsWith("/iniciar-devolucion", StringComparison.Ordinal));

        using var uncertain = await client.PostAsync("/rutas?handler=IniciarDevolucion", values);
        Assert.Equal(HttpStatusCode.OK, uncertain.StatusCode);
        var uncertainHtml = WebUtility.HtmlDecode(await uncertain.Content.ReadAsStringAsync());
        Assert.Contains("La decisión podría haberse guardado", uncertainHtml);
        Assert.Contains("value=\"00000000-0000-0000-0000-000000000710\"", uncertainHtml);
        Assert.Contains("Motivo confirmado", uncertainHtml);

        using var retry = await client.PostAsync("/rutas?handler=IniciarDevolucion", Form(token,
            ("Fecha", "2026-10-20"), ("RutaSeleccionadaId", RutaPrueba.ToString()), ("DevolucionEnvioId", EnvioPrueba.ToString()), ("VersionEnvio", "8"),
            ("ComandoId", commandId.ToString()), ("MotivoDevolucion", " Motivo confirmado "), ("ConfirmarDevolucion", "true")));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        var requests = api.RepartoPayloads.Where(x => x.Path == $"/api/envios/{EnvioPrueba}/iniciar-devolucion").ToArray();
        Assert.Equal(2, requests.Length);
        Assert.All(requests, request =>
        {
            Assert.Equal("jwt-user", request.Token);
            Assert.Equal(commandId, request.Body.GetProperty("comandoId").GetGuid());
            Assert.Equal(8, request.Body.GetProperty("versionEnvio").GetInt64());
            Assert.Equal("Motivo confirmado", request.Body.GetProperty("motivo").GetString());
        });
    }

    [Fact]
    public async Task RutasConfirmaExtravioConMotivoYPermiteReintentarMismoComando()
    {
        var api = new FakeApi { IncludeReparto = true, CanManageOperationalDecisions = true,
            ExtravioTransportFailureOnce = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");

        using var initial = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        var html = WebUtility.HtmlDecode(await initial.Content.ReadAsStringAsync());
        Assert.Contains("Confirmar extravío", html);
        Assert.Contains("esto no acredita dónde está físicamente el bulto", html);
        Assert.Contains("data-single-submit", html);

        var commandId = Guid.Parse("00000000-0000-0000-0000-000000000720");
        var values = Form(token, ("Fecha", "2026-10-20"), ("ExtravioEnvioId", EnvioPrueba.ToString()),
            ("VersionEnvio", "8"), ("ComandoId", commandId.ToString()),
            ("MotivoExtravio", " Motivo conservado "), ("ConfirmarExtravio", "true"));
        using var uncertain = await client.PostAsync("/rutas?handler=ConfirmarExtravio", values);
        Assert.Equal(HttpStatusCode.OK, uncertain.StatusCode);
        var uncertainHtml = WebUtility.HtmlDecode(await uncertain.Content.ReadAsStringAsync());
        var uncertainAlert = System.Text.RegularExpressions.Regex.Match(uncertainHtml,
            "<div[^>]*class=\"[^\"]*alert-danger[^\"]*\"[^>]*>(.*?)</div>",
            System.Text.RegularExpressions.RegexOptions.Singleline).Groups[1].Value;
        Assert.True(uncertainAlert.Contains("podría haberse guardado", StringComparison.Ordinal),
            $"Alert: {uncertainAlert}; API calls: {string.Join(" | ", api.Calls.Select(call => call.Path))}");
        Assert.Contains(commandId.ToString(), uncertainHtml);
        Assert.Contains("Motivo conservado", uncertainHtml);

        using var retry = await client.PostAsync("/rutas?handler=ConfirmarExtravio", Form(token,
            ("Fecha", "2026-10-20"), ("ExtravioEnvioId", EnvioPrueba.ToString()),
            ("VersionEnvio", "8"), ("ComandoId", commandId.ToString()),
            ("MotivoExtravio", " Motivo conservado "), ("ConfirmarExtravio", "true")));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        var requests = api.RepartoPayloads.Where(x => x.Path == $"/api/envios/{EnvioPrueba}/confirmar-extravio").ToArray();
        Assert.Equal(2, requests.Length);
        Assert.All(requests, request =>
        {
            Assert.Equal(commandId, request.Body.GetProperty("comandoId").GetGuid());
            Assert.Equal(8, request.Body.GetProperty("versionEnvio").GetInt64());
            Assert.Equal("Motivo conservado", request.Body.GetProperty("motivo").GetString());
        });
    }

    [Fact]
    public async Task RutasOcultaConfirmacionDeExtravioSinCapacidadOperativa()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/deposito?fecha=2026-10-20&envioSeleccionadoId=00000000-0000-0000-0000-000000000702");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("<h2>Confirmar extravío</h2>", html);
        Assert.DoesNotContain("confirmar-extravio", html);
    }

    [Fact]
    public async Task RutasRefrescaVersionTrasConflictoSinReutilizarComandoRechazado()
    {
        var api = new FakeApi { IncludeReparto = true, CanManageOperationalDecisions = true,
            ExtravioConflictOnce = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");
        var commandId = Guid.Parse("00000000-0000-0000-0000-000000000722");

        using var conflict = await client.PostAsync("/rutas?handler=ConfirmarExtravio", Form(token,
            ("Fecha", "2026-10-20"), ("ExtravioEnvioId", EnvioPrueba.ToString()),
            ("VersionEnvio", "7"), ("ComandoId", commandId.ToString()),
            ("MotivoExtravio", " Motivo preservado "), ("ConfirmarExtravio", "true")));

        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var html = WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync());
        Assert.Contains("El envío cambió", html);
        Assert.Contains("Motivo preservado", html);
        var refreshedVersion = System.Text.RegularExpressions.Regex.Match(html,
            @"name=""VersionEnvio"" value=""(\d+)""").Groups[1].Value;
        var refreshedCommand = System.Text.RegularExpressions.Regex.Match(html,
            @"name=""ComandoId"" value=""([^""]+)""").Groups[1].Value;
        Assert.Equal("9", refreshedVersion);
        Assert.True(Guid.TryParse(refreshedCommand, out var newCommandId));
        Assert.NotEqual(commandId, newCommandId);

        using var retry = await client.PostAsync("/rutas?handler=ConfirmarExtravio", Form(token,
            ("Fecha", "2026-10-20"), ("ExtravioEnvioId", EnvioPrueba.ToString()),
            ("VersionEnvio", refreshedVersion), ("ComandoId", refreshedCommand),
            ("MotivoExtravio", " Motivo preservado "), ("ConfirmarExtravio", "true")));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode);
        var requests = api.RepartoPayloads.Where(x => x.Path == $"/api/envios/{EnvioPrueba}/confirmar-extravio").ToArray();
        Assert.Equal(2, requests.Length);
        Assert.Equal(commandId, requests[0].Body.GetProperty("comandoId").GetGuid());
        Assert.Equal(7, requests[0].Body.GetProperty("versionEnvio").GetInt64());
        Assert.Equal(newCommandId, requests[1].Body.GetProperty("comandoId").GetGuid());
        Assert.Equal(9, requests[1].Body.GetProperty("versionEnvio").GetInt64());
        Assert.All(requests, request => Assert.Equal("Motivo preservado", request.Body.GetProperty("motivo").GetString()));
    }

    [Fact]
    public async Task RutasRetiraAccionTrasConflictoCuandoElEstadoActualYaNoLaPermite()
    {
        var api = new FakeApi { IncludeReparto = true, CanManageOperationalDecisions = true,
            ExtravioConflictOnce = true, ExtravioConflictState = "Cancelado" };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");

        using var conflict = await client.PostAsync("/rutas?handler=ConfirmarExtravio", Form(token,
            ("Fecha", "2026-10-20"), ("ExtravioEnvioId", EnvioPrueba.ToString()),
            ("VersionEnvio", "7"), ("ComandoId", Guid.NewGuid().ToString()),
            ("MotivoExtravio", "Motivo preservado"), ("ConfirmarExtravio", "true")));

        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var html = WebUtility.HtmlDecode(await conflict.Content.ReadAsStringAsync());
        Assert.Contains("Estado actualizado: Cancelado", html);
        Assert.Contains("Motivo preservado", html);
        Assert.DoesNotContain("name=\"ExtravioEnvioId\"", html);
    }

    [Fact]
    public async Task CierreConIncidenciaMuestraHistorialCustodiaYRecepcionSinDecisionSobreLaEntrega()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeIncidenciaCierre = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/rutas/00000000-0000-0000-0000-000000000701?fecha=2026-10-20");
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
        public object? FleetResponse { get; init; }
        public bool FleetUnavailable { get; init; }
        public bool IncludeRoadEstimates { get; init; }
        public HttpStatusCode? RepartoQueryFailure { get; init; }
        public bool IncludeRoadUpdates { get; init; }
        public bool RoadConflictOnce {get;set;}
        public bool RoadProfileConflictOnce {get;set;}
        public long RoadProfileVersion {get;set;}
        public string RoadCompatibility {get;set;}="SinDeterminar";
        public List<object> RoadHistory {get;}=[];
        public bool IncludeBaseConfiguration { get; init; }
        public bool BaseConflictOnce { get; set; }
        public NexoRuta.ApiClient.Features.Reparto.Contracts.BaseOperativaResponse? BaseFixture { get; set; }
        public bool IncludeReparto { get; init; }
        public bool IncludeVehicleConfiguration { get; init; }
        public HttpStatusCode? VehiclesQueryFailure { get; init; }
        public bool VehicleWriteTimeout { get; init; }
        public decimal VehicleVolumeCapacity { get; init; } = 10000m;
        public bool IncludeIncidenciaCierre { get; init; }
        public bool IncludeNoEntregado { get; init; }
        public bool IntentosAgotados { get; init; }
        public bool DevolucionTransportFailureOnce { get; set; }
        public bool ExtravioTransportFailureOnce { get; set; }
        public bool ExtravioConflictOnce { get; set; }
        public string ExtravioConflictState { get; set; } = "EnDeposito";
        public string? ShipmentStateOverride { get; set; }
        public string? RouteDateOverride { get; init; }
        public string? PlanificationConflictDetail { get; init; }
        public bool DestinationConfirmedOverride { get; init; } = true;
        public string BultoCustodyOverride { get; set; } = "Deposito";
        public bool? AssignedStopActiveOverride { get; init; }
        public bool AssignedStopOmittedOverride { get; init; }
        public bool IncludeReleasedPlanificada { get; init; }
        public bool? ReleasedReservationActive { get; set; } = false;
        public string ReleasedRouteStateOverride { get; set; } = "Planificada";
        public bool RouteCancelConflictOnce { get; set; }
        public long? ShipmentVersionOverride { get; set; }
        public bool IsCancellationAdmin { get; init; }
        public bool CancellationApplied { get; private set; }
        public System.Collections.Concurrent.ConcurrentQueue<ZonePayloadCall> RepartoPayloads { get; } = new();
        public static object RoadUpdate(int number,Guid command,string motive="Calculada")=>new {
            RutaId=RutaPrueba,ComandoId=command,NumeroVersion=number,VersionRuta=2,ActorId=TargetAccessId,RegistradaEnUtc=DateTimeOffset.Parse("2026-10-21T03:59:30Z"),TipoCalculo="DuranteRecorrido",OcupacionId=Guid.Parse("33333333-3333-3333-3333-333333333333"),
            Estado=motive=="Calculada"?"Calculada":"PosicionNoAdmisible",Motivo=motive,Vigencia="Desconocida",ZonaHoraria="America/Montevideo",CalculadoEnUtc=motive=="Calculada"?(DateTimeOffset?)DateTimeOffset.Parse("2026-10-21T03:59:30Z"):null,ValidoHastaUtc=(DateTimeOffset?)null,
            RegresoEstimadoBaseEnUtc=motive=="Calculada"?(DateTimeOffset?)DateTimeOffset.Parse("2026-10-21T04:29:30Z"):null,ListoPrevistoEnUtc=motive=="Calculada"?(DateTimeOffset?)DateTimeOffset.Parse("2026-10-21T04:36:30Z"):null,
            PosicionFuente=motive=="Calculada"?new {Id=Guid.Parse("55555555-5555-5555-5555-555555555555"),MedidaEnUtc=DateTimeOffset.Parse("2026-10-21T03:59:20Z"),RecibidaEnUtc=DateTimeOffset.Parse("2026-10-21T03:59:30Z"),PrecisionMetros=20d}:null,
            CriteriosGps=new {AntiguedadMaximaSegundos=120,PrecisionMaximaMetros=100d},
            Paradas=motive=="Calculada"?new[]{new{EnvioId=EnvioPrueba,LlegadaManualEnUtc=DateTimeOffset.Parse("2026-10-21T04:00:00Z"),LlegadaCalculadaEnUtc=DateTimeOffset.Parse("2026-10-21T04:09:30Z"),SalidaCalculadaEnUtc=DateTimeOffset.Parse("2026-10-21T04:19:30Z")}}:[],
            Tramos=motive=="Calculada"?new[]{new {Indice=0,DuracionSegundos=600d,DistanciaMetros=1000d,Proveedor="Geoapify",Modelo="drive/free_flow",Fuente="RespuestaControlada",Trafico="SinTrafico",Atribucion="Powered by Geoapify · © OpenStreetMap contributors",CalculadoEnUtc=DateTimeOffset.Parse("2026-10-21T03:59:30Z"),ValidoHastaUtc=(DateTimeOffset?)null}}:[]};
        public static object RoadEstimate(int number,Guid command)=>new {RutaId=RutaPrueba,ComandoId=command,NumeroVersion=number,VersionRuta=2,ActorId=TargetAccessId,RegistradaEnUtc=DateTimeOffset.Parse("2026-10-20T12:00:00Z"),Estado="Calculada",Motivo="Calculada",Vigencia=number==1?"Invalidada":"Desconocida",ZonaHoraria="America/Montevideo",CalculadoEnUtc=(DateTimeOffset?)DateTimeOffset.Parse("2026-10-20T12:00:00Z"),ValidoHastaUtc=(DateTimeOffset?)null,RegresoEstimadoBaseEnUtc=(DateTimeOffset?)DateTimeOffset.Parse("2026-10-21T03:20:00Z"),ListoPrevistoEnUtc=(DateTimeOffset?)DateTimeOffset.Parse("2026-10-21T03:27:00Z"),Paradas=new[]{new{EnvioId=EnvioPrueba,LlegadaManualEnUtc=DateTimeOffset.Parse("2026-10-21T04:00:00Z"),LlegadaCalculadaEnUtc=DateTimeOffset.Parse("2026-10-21T03:00:00Z"),SalidaCalculadaEnUtc=DateTimeOffset.Parse("2026-10-21T03:10:00Z")}},Tramos=new[]{new{Indice=0,DuracionSegundos=600d,DistanciaMetros=1000d,Proveedor="Geoapify",Modelo="drive/free_flow",Fuente="RespuestaControlada",Trafico="SinTrafico",Atribucion="Powered by Geoapify · © OpenStreetMap contributors",CalculadoEnUtc=DateTimeOffset.Parse("2026-10-20T12:00:00Z"),ValidoHastaUtc=(DateTimeOffset?)null}}};
        private async Task<HttpResponseMessage?> Reparto(HttpRequestMessage request, string path, string? token, CancellationToken ct)
        {
            if(RepartoQueryFailure is {} failed && request.Method==HttpMethod.Get && (path is "/api/reparto/flota" or "/api/reparto/perfiles-viales" || path.EndsWith("/estimaciones",StringComparison.Ordinal)))return Json(failed,new{detail="Controlled integration failure"});
            if(IncludeRoadEstimates && path.StartsWith("/api/reparto/perfiles-viales",StringComparison.Ordinal))
            {
                if(request.Method==HttpMethod.Get)return Json(HttpStatusCode.OK,new[]{new{VehiculoId=VehiculoPrueba,Matricula="AAA 1234",Version=RoadProfileVersion,Compatibilidad=RoadCompatibility,ActorId=(Guid?)TargetAccessId,ConfirmadoEnUtc=(DateTimeOffset?)DateTimeOffset.UtcNow,Motivo="Fixture"}});
                var body=(await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();RepartoPayloads.Enqueue(new(path,request.Method,token,body));
                if(RoadProfileConflictOnce||body.GetProperty("version").GetInt64()!=RoadProfileVersion){if(RoadProfileConflictOnce)RoadProfileVersion++;RoadProfileConflictOnce=false;return Json(HttpStatusCode.Conflict,new{detail="El perfil cambió. Conservá los datos y revisá su versión."});}
                RoadProfileVersion++;RoadCompatibility=body.GetProperty("compatibilidad").GetString()!;return Json(HttpStatusCode.OK,new{VehiculoId=VehiculoPrueba,Matricula="AAA 1234",Version=RoadProfileVersion,Compatibilidad=RoadCompatibility,ActorId=(Guid?)TargetAccessId,ConfirmadoEnUtc=(DateTimeOffset?)DateTimeOffset.UtcNow,Motivo="Fixture"});
            }
            if(IncludeRoadUpdates&&path.EndsWith("/estimaciones/recorrido",StringComparison.Ordinal)){
                var body=(await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();RepartoPayloads.Enqueue(new(path,request.Method,token,body));
                if(RoadConflictOnce){RoadConflictOnce=false;return Json(HttpStatusCode.Conflict,new{detail="La posición o la ocupación cambiaron durante el cálculo. Se descartó la respuesta obsoleta."});}
                var updated=RoadUpdate(RoadHistory.Count+1,body.GetProperty("comandoId").GetGuid());RoadHistory.Add(updated);return Json(HttpStatusCode.OK,updated);
            }
            if(IncludeRoadEstimates&&path.EndsWith("/estimaciones",StringComparison.Ordinal))
            {
                if(request.Method==HttpMethod.Get)return Json(HttpStatusCode.OK,RoadHistory.AsEnumerable().Reverse().ToArray());
                var body=(await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();RepartoPayloads.Enqueue(new(path,request.Method,token,body));
                if(RoadConflictOnce){RoadConflictOnce=false;return Json(HttpStatusCode.Conflict,new{detail="La ruta o su perfil cambiaron durante el cálculo. Se descartó la respuesta obsoleta."});}
                var record=RoadEstimate(RoadHistory.Count+1,body.GetProperty("comandoId").GetGuid());RoadHistory.Add(record);return Json(HttpStatusCode.OK,record);
            }
            if (IncludeBaseConfiguration && path.StartsWith("/api/reparto/base-operativa", StringComparison.Ordinal))
            {
                if (request.Method == HttpMethod.Get) return BaseFixture is null ? Json(HttpStatusCode.NotFound,new { }) : Json(HttpStatusCode.OK,BaseFixture);
                if (path.EndsWith("/propuestas",StringComparison.Ordinal)) return BaseFixture is null ? Json(HttpStatusCode.NotFound,new { }) : Json(HttpStatusCode.OK,new { BaseFixture.Version,BaseFixture.Direccion,Atribucion="Geocodificador de prueba",Propuestas=new[]{new{Descripcion="Punto de prueba",Latitud=-34.9,Longitud=-56.2}} });
                var body=(await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone(); RepartoPayloads.Enqueue(new(path,request.Method,token,body));
                var version=body.GetProperty("version").GetInt64();
                if (BaseConflictOnce || version != (BaseFixture?.Version ?? 0)) { BaseConflictOnce=false; return Json(HttpStatusCode.Conflict,new{detail="La base cambió; revisá la versión antes de guardar."}); }
                if (path.EndsWith("/confirmacion",StringComparison.Ordinal))
                {
                    if (BaseFixture is null)return Json(HttpStatusCode.NotFound,new{});
                    BaseFixture=BaseFixture with {Version=version+1,Latitud=body.GetProperty("latitud").GetDouble(),Longitud=body.GetProperty("longitud").GetDouble(),ConfirmadaEnUtc=DateTimeOffset.UtcNow,ConfirmadaPorAccesoId=TargetAccessId};
                }
                else
                {
                    var address=body.GetProperty("direccion").GetString()!; var same=BaseFixture?.Direccion==address;
                    int? Minutes(string key)=>body.GetProperty(key).ValueKind==JsonValueKind.Null?null:body.GetProperty(key).GetInt32();
                    BaseFixture=new(BaseFixture is null?1:version+1,address,same?BaseFixture?.Latitud:null,same?BaseFixture?.Longitud:null,same?BaseFixture?.ConfirmadaEnUtc:null,same?BaseFixture?.ConfirmadaPorAccesoId:null,Minutes("servicioPredeterminadoMinutos"),Minutes("margenRetornoMinutos"));
                }
                return Json(HttpStatusCode.OK,BaseFixture);
            }
            var politica = new { version = 1, zonaHoraria = "America/Montevideo", maximoParadas = 100, maximoIntentos = 3, motivos = new[] { "Ausente" } };
            if (IncludeVehicleConfiguration && path == "/api/reparto/configuracion") return Json(HttpStatusCode.OK, politica);
            if (IncludeVehicleConfiguration && path == "/api/reparto/vehiculos")
            {
                if(request.Method==HttpMethod.Get&&VehiclesQueryFailure is {} failure)return Json(failure,new{detail="Vehicle query unavailable"});
                if(request.Method==HttpMethod.Put&&VehicleWriteTimeout)throw new TaskCanceledException("Controlled vehicle write timeout");
                if (request.Method == HttpMethod.Put) return Json(HttpStatusCode.Conflict, new { detail = "El vehículo cambió. Recargalo antes de guardar." });
                return Json(HttpStatusCode.OK, new[] { new { id = VehiculoPrueba, matricula = "ABC", pesoMaximoGramos = 3000m, volumenMaximoCm3 = 2000m, activo = true, version = 7 } });
            }
            var bultoId = Guid.Parse("00000000-0000-0000-0000-000000000704");
            var reglasEnvio = new { versionId = Guid.NewGuid(), numeroVersion = 1, maximoIntentos = 3,
                intervaloEntreIntentosMinutos = 30, politicaDevolucionAlAgotarIntentos = "DecisionOperativa",
                requiereFirma = false, requiereFoto = false, requiereNombre = true, requiereDocumento = false,
                motivos = new[] { new { codigo = "AUSENTE", descripcion = "Ausente" } } };
            object[] paradas = IncludeNoEntregado ? [new { envioId = EnvioPrueba, orden = 1, activa = false,
                llegadaEstimadaUtc = DateTimeOffset.Parse("2026-10-20T18:00:00Z"), estado = "NoEntregado", envioVersion = 8,
                destinatario = "Destino de prueba", direccion = "Dirección de prueba", latitud = -34.9, longitud = -56.2,
                bultos = new[] { new { id = bultoId, codigo = "TEST-FALLIDO", pesoGramos = 100m, volumenCm3 = 1m, custodia = "Deposito", version = 2 } },
                reglas = reglasEnvio, intentosFallidos = IntentosAgotados ? 3 : 1,
                proximoIntentoElegibleUtc = (DateTimeOffset?)DateTimeOffset.Parse("2026-10-20T18:30:00Z") }]
                : IncludeIncidenciaCierre ? [new { envioId = EnvioPrueba, orden = 1, activa = false,
                    llegadaEstimadaUtc = DateTimeOffset.Parse("2026-10-20T18:00:00Z"), estado = "EnTransito", envioVersion = 8,
                    destinatario = "Destino de prueba", direccion = "Dirección de prueba", latitud = -34.9, longitud = -56.2,
                    bultos = new[] { new { id = bultoId, codigo = "TEST-FALTANTE", custodia = "Repartidor", version = 2 } } }]
                : ShipmentStateOverride == "AsignadoRuta" && !AssignedStopOmittedOverride ? [new { envioId = EnvioPrueba, orden = 1, activa = AssignedStopActiveOverride ?? true,
                    llegadaEstimadaUtc = DateTimeOffset.Parse("2026-10-20T18:00:00Z"), estado = "AsignadoRuta", envioVersion = 8,
                    destinatario = "Destino de prueba", direccion = "Dirección de prueba", latitud = -34.9, longitud = -56.2,
                    bultos = new[] { new { id = bultoId, codigo = "TEST-ASIGNADO", pesoGramos = 100m, volumenCm3 = 1m, custodia = BultoCustodyOverride, version = 2 } },
                    reglas = reglasEnvio, intentosFallidos = 0, proximoIntentoElegibleUtc = (DateTimeOffset?)null }]
                : [];
            object[] incidenciasCierre = IncludeIncidenciaCierre ? [new { operacionId = Guid.NewGuid(), envioId = EnvioPrueba,
                bultoId, codigo = "TEST-FALTANTE", versionBulto = 2, ocurridaEnUtc = DateTimeOffset.UtcNow, recibidaEnUtc = DateTimeOffset.UtcNow }] : [];
            var ruta = new { id = RutaPrueba, jornadaId = Guid.NewGuid(), repartidorAccesoId = TargetAccessId, vehiculoId = VehiculoPrueba,
                matricula = "AAA 1234", fecha = RouteDateOverride ?? "2026-10-20", estado = IncludeIncidenciaCierre || ShipmentStateOverride == "AsignadoRuta" && AssignedStopActiveOverride == false ? "Finalizada"
                    : ShipmentStateOverride == "AsignadoRuta" ? "EnCurso" : "Planificada", version = 3, ultimaSecuencia = 0,
                reservaRecursosActiva = (bool?)true, politica, paradas, incidenciasCierre };
            if (path.StartsWith("/api/reparto/flota/", StringComparison.Ordinal) && request.Method == HttpMethod.Post)
            {
                var payload = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone(); RepartoPayloads.Enqueue(new(path, request.Method, token, payload));
                return Json(HttpStatusCode.Conflict, new { detail = "La ocupación cambió; no se liberó otro trabajo." });
            }
            if (path == "/api/reparto/flota" && request.Method == HttpMethod.Get) return FleetUnavailable
                ? Json(HttpStatusCode.ServiceUnavailable, new { detail = "No se pudo consultar flota." })
                : Json(HttpStatusCode.OK, FleetResponse ?? Array.Empty<object>());
            if (path == "/api/rutas/recursos") return Json(HttpStatusCode.OK, new { politica,
                repartidores = new[] { new { accesoId = TargetAccessId, nombre = "Repartidor de prueba" } },
                vehiculos = new[] { new { id = VehiculoPrueba, matricula = "AAA 1234", pesoMaximoGramos = 10000m, volumenMaximoCm3 = VehicleVolumeCapacity, activo = true, version = 1 } }, baseOperativa = BaseFixture });
            if (path == "/api/rutas" && request.Method == HttpMethod.Get)
            {
                if (ShipmentStateOverride != "AsignadoRuta") return Json(HttpStatusCode.OK, new[] { ruta });
                var rutaDestino = new { id = Guid.Parse("00000000-0000-0000-0000-000000000734"), jornadaId = ruta.jornadaId,
                    repartidorAccesoId = TargetAccessId, vehiculoId = VehiculoPrueba, matricula = "BBB 4567",
                    fecha = "2026-10-20", estado = "Planificada", version = 3, ultimaSecuencia = 0, reservaRecursosActiva = (bool?)true, politica,
                    paradas = Array.Empty<object>(), incidenciasCierre };
                if (IncludeReleasedPlanificada)
                {
                    var paradaAnterior = new { envioId = Guid.Parse("00000000-0000-0000-0000-000000000736"), orden = 1, activa = false,
                        motivoSuspension = "Cancelado", llegadaEstimadaUtc = DateTimeOffset.Parse("2026-10-20T17:00:00Z"),
                        estado = "Cancelado", envioVersion = 2, destinatario = "Anterior", direccion = "DirecciÃ³n anterior",
                        latitud = -34.9, longitud = -56.2,
                        bultos = new[] { new { id = bultoId, codigo = "INACTIVO", pesoGramos = 100m, volumenCm3 = 1m, custodia = "Deposito", version = 1 } },
                        reglas = reglasEnvio, intentosFallidos = 0, proximoIntentoElegibleUtc = (DateTimeOffset?)null };
                    object rutaLiberada = ReleasedReservationActive is { } reserva
                        ? new { id = Guid.Parse("00000000-0000-0000-0000-000000000735"), jornadaId = ruta.jornadaId,
                            repartidorAccesoId = TargetAccessId, vehiculoId = VehiculoPrueba, matricula = "CCC 7890",
                            fecha = "2026-10-20", estado = ReleasedRouteStateOverride, version = 4, ultimaSecuencia = 0, reservaRecursosActiva = (bool?)reserva, politica,
                            paradas = new object[] { paradaAnterior }, incidenciasCierre }
                        : new { id = Guid.Parse("00000000-0000-0000-0000-000000000735"), jornadaId = ruta.jornadaId,
                            repartidorAccesoId = TargetAccessId, vehiculoId = VehiculoPrueba, matricula = "CCC 7890",
                            fecha = "2026-10-20", estado = ReleasedRouteStateOverride, version = 4, ultimaSecuencia = 0, politica,
                            paradas = new object[] { paradaAnterior }, incidenciasCierre };
                    return Json(HttpStatusCode.OK, new[] { ruta, rutaDestino, rutaLiberada });
                }
                return Json(HttpStatusCode.OK, new[] { ruta, rutaDestino });
            }
            if (path == "/api/envios") return Json(HttpStatusCode.OK, new[] { new { id = EnvioPrueba, operadorId = Guid.NewGuid(), comercioId = Guid.NewGuid(),
                creadoPorUsuarioId = Guid.NewGuid(), usuarioEmail = "comercio@example.test", operadorNombre = "Operador", comercioNombre = "Comercio",
                destinatarioNombre = "Destino de prueba", direccion = "Dirección de prueba", estado = CancellationApplied ? "Cancelado" : ShipmentStateOverride ?? (IncludeNoEntregado ? "NoEntregado" : "EnTransito"), version = ShipmentVersionOverride ?? (CancellationApplied ? 9 : 8),
                destinoConfirmado = DestinationConfirmedOverride,
                bultos = new[] { new { codigo = "TEST", pesoGramos = 100m, largoCentimetros = 1m, anchoCentimetros = 1m, altoCentimetros = 1m, custodia = BultoCustodyOverride } } } });
            if (request.Method == HttpMethod.Post && path.StartsWith("/api/envios/", StringComparison.Ordinal)
                && path.EndsWith("/cancelacion", StringComparison.Ordinal))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                CancellationApplied = true;
                return Json(HttpStatusCode.OK, new { cancelacionId = Guid.NewGuid(), envioId = EnvioPrueba, version = 9, estado = "Cancelado" });
            }
            if (request.Method == HttpMethod.Post && path.StartsWith("/api/rutas/", StringComparison.Ordinal)
                && path.EndsWith("/cancelacion", StringComparison.Ordinal))
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                RepartoPayloads.Enqueue(new(path, request.Method, token, body));
                var routeId = Guid.Parse(path.Split('/')[3]);
                if (RouteCancelConflictOnce)
                {
                    RouteCancelConflictOnce = false;
                    ReleasedRouteStateOverride = "Cancelada";
                    ReleasedReservationActive = false;
                    return Json(HttpStatusCode.Conflict, new { detail = "La ruta cambió de versión." });
                }
                return Json(HttpStatusCode.OK, new { id = routeId, jornadaId = Guid.NewGuid(), repartidorAccesoId = TargetAccessId,
                    vehiculoId = VehiculoPrueba, matricula = "CCC 7890", fecha = "2026-10-20", estado = "Cancelada", version = 5,
                    ultimaSecuencia = 0, reservaRecursosActiva = false, politica, paradas = Array.Empty<object>(), incidenciasCierre = Array.Empty<object>() });
            }
            if (request.Method == HttpMethod.Post && (path == "/api/rutas" || path.EndsWith("/reasignacion", StringComparison.Ordinal) || path.EndsWith("/recepcion", StringComparison.Ordinal)))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                if (path == "/api/rutas" && PlanificationConflictDetail is { } detail)
                    return Json(HttpStatusCode.Conflict, new { detail });
                return path.EndsWith("/recepcion", StringComparison.Ordinal) ? new(HttpStatusCode.NoContent) : Json(HttpStatusCode.OK, ruta);
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/reprogramacion", StringComparison.Ordinal))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                return Json(HttpStatusCode.OK, new { envioId = EnvioPrueba, versionEnvio = 9, intentosFallidos = 1,
                    maximoIntentos = 3, proximoIntentoElegibleUtc = DateTimeOffset.Parse("2026-10-20T18:30:00Z") });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/iniciar-devolucion", StringComparison.Ordinal))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                if (DevolucionTransportFailureOnce)
                {
                    DevolucionTransportFailureOnce = false;
                    throw new HttpRequestException("Simulated lost response after the server may have committed.");
                }
                return Json(HttpStatusCode.OK, new { envioId = EnvioPrueba, decisionId = Guid.NewGuid(), comandoId = Guid.NewGuid(),
                    estadoDecision = "EnDevolucion", versionDecision = 9, motivo = "Motivo confirmado",
                    decididaEnUtc = DateTimeOffset.Parse("2026-10-20T18:30:00Z"), estadoActual = "EnDevolucion",
                    versionActual = 9, repetida = false });
            }
            if (request.Method == HttpMethod.Post && path.EndsWith("/confirmar-extravio", StringComparison.Ordinal))
            {
                RepartoPayloads.Enqueue(new(path, request.Method, token, (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone()));
                if (ExtravioConflictOnce)
                {
                    ExtravioConflictOnce = false;
                    ShipmentStateOverride = ExtravioConflictState;
                    ShipmentVersionOverride = 9;
                    return Json(HttpStatusCode.Conflict, new { detail = "El envío cambió de versión." });
                }
                if (ExtravioTransportFailureOnce)
                {
                    ExtravioTransportFailureOnce = false;
                    throw new HttpRequestException("Simulated lost response after the server may have committed.");
                }
                return Json(HttpStatusCode.OK, new { envioId = EnvioPrueba,
                    confirmacionId = Guid.Parse("00000000-0000-0000-0000-000000000721"),
                    comandoId = Guid.Parse("00000000-0000-0000-0000-000000000720"), estadoConfirmado = "Extraviado",
                    versionAnterior = 8, versionNueva = 9, motivo = "Motivo conservado",
                    confirmadaEnUtc = DateTimeOffset.Parse("2026-10-20T18:30:00Z"), estadoActual = "Extraviado",
                    versionActual = 9, repetida = true });
            }
            return null;
        }
    }
}
