using System.Net;
using System.Text.Json;
namespace NexoRuta.ApiClient.Tests;
public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task FleetQueryFailureDoesNotImplyAvailability()
    {
        var api = new FakeApi { IncludeReparto = true, FleetUnavailable = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/flota?fecha=2026-10-20");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Disponibilidad desconocida", html);
        Assert.DoesNotContain("Disponibilidad confirmada", html);
        Assert.DoesNotContain("Confirmar disponibilidad y liberar", html);
    }
    [Fact]
    public async Task FleetPendingFromPreviousDateHasExactOccupationFormsAndRouteLink()
    {
        var occupation = Guid.NewGuid();
        var api = new FakeApi { IncludeReparto = true, FleetResponse = new[] { new {
            id = VehiculoPrueba, matricula = "FLEET-320", activo = false, version = 8,
            verificacionInicialEnUtc = DateTimeOffset.UtcNow, cargaPendiente = 1,
            ocupaciones = new[] { new { id = occupation, rutaId = RutaPrueba, repartidorAccesoId = TargetAccessId,
                fecha = "2020-01-02", estadoRuta = "Finalizada", version = 5, legada = false,
                adquiridaEnUtc = DateTimeOffset.UtcNow, retornoEnUtc = (DateTimeOffset?)null, cargaPendiente = 1, puedeAnular = false } },
            ultimaLiberacionEnUtc = (DateTimeOffset?)null, cargasIniciales = Array.Empty<object>() } } };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/flota?fecha=2026-10-20");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("FLEET-320", html); Assert.Contains("2020-01-02", html);
        Assert.Contains("Retorno pendiente", html); Assert.Contains("FlotaOcupacion", html);
        Assert.Contains(occupation.ToString(), html); Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain("Confirmar disponibilidad y liberar", html);
        Assert.Contains("maxlength=\"500\"", html);
        var readable = WebUtility.HtmlDecode(html);
        Assert.Contains("Falta confirmar el retorno físico", readable);
        Assert.Contains("Falta recibir o conciliar 1 bulto", readable);
        Assert.Contains("Repartidor de prueba", readable);
        Assert.Contains("Cierre de jornada: cerrada", readable);
        Assert.Contains("Revisar recepción de carga", readable);
    }
    [Fact]
    public async Task FleetConflictPreservesExactCommandVersionAndReasonWithoutAutomaticRetry()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas?fecha=2026-10-20");
        var command = Guid.NewGuid(); var occupation = Guid.NewGuid();
        using var response = await client.PostAsync("/rutas?handler=Flota", Form(token,
            ("Fecha", "2026-10-20"), ("FlotaVehiculo", VehiculoPrueba.ToString()), ("FlotaOcupacion", occupation.ToString()),
            ("FlotaComando", command.ToString()), ("FlotaVersion", "5"), ("FlotaAccion", "liberar"), ("FlotaMotivo", "Inspección física confirmada.")));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La ocupación cambió", html); Assert.Contains(command.ToString(), html);
        Assert.Contains(occupation.ToString(), html); Assert.Contains("Inspección física confirmada.", html);
        Assert.Contains("Reenviar la misma confirmación", html);
        Assert.Single(api.RepartoPayloads, x => x.Path.Contains("/api/reparto/flota/"));
    }

}
