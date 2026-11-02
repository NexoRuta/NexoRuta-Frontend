using System.Net;
using System.ComponentModel.DataAnnotations;
using NexoRuta.ApiClient.Features.Reparto.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task CapacityConfigurationRequiresTheSamePermissionAsItsMenuAndApi()
    {
        var api = new FakeApi(); api.PermissionOverrides["jwt-user"] = [];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/configuracion-reparto");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/acceso-denegado", response.Headers.Location!.OriginalString);
        Assert.DoesNotContain(api.Calls, c => c.Path == "/api/reparto/vehiculos");
    }

    [Fact]
    public async Task CapacitySummaryRendersCorrectUnitsAndVolumeWithoutJavaScriptAfterError()
    {
        var api = new FakeApi { IncludeReparto = true, ShipmentStateOverride = "EnDeposito", PlanificationConflictDetail = "Capacity conflict", VehicleVolumeCapacity = 4000000m };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/rutas/planificar?fecha=2026-10-20");
        using var response = await client.PostAsync("/rutas/planificar?handler=Planificar", Form(token,
            ("FechaSalida", "2026-10-20"), ("RepartidorAccesoId", TargetAccessId.ToString()), ("VehiculoId", VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId", EnvioPrueba.ToString()), ("Plan[0].Incluir", "true"), ("Plan[0].Orden", "1"), ("Plan[0].Hora", "10:30")));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("4 m³", html);
        Assert.Contains("data-summary-volume", html);
        Assert.Contains("data-summary-weight-capacity", html);
        Assert.Contains("data-summary-volume-capacity", html);
        Assert.Contains("data-summary-weight-excess", html);
        Assert.Contains("data-summary-volume-excess", html);
        Assert.Contains("data-volume=\"1\"", html);
    }

    [Theory]
    [InlineData("1.005", false)]
    [InlineData("1.000", true)]
    public void VehicleFormUsesEffectivePrecision(string text, bool valid)
    {
        var value = decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
        var vehicle = new VehiculoResponse { Matricula = "ABC", PesoMaximoGramos = value, VolumenMaximoCm3 = value };
        var errors = new List<ValidationResult>();
        Assert.Equal(valid, Validator.TryValidateObject(vehicle, new ValidationContext(vehicle), errors, true));
        if (!valid) Assert.Contains(errors, x => x.MemberNames.Contains(nameof(VehiculoResponse.PesoMaximoGramos)));
    }

    [Theory]
    [InlineData("1.005", false)]
    [InlineData("1.000", true)]
    public async Task VehicleFormKeepsValuesAndVersionOnValidationOrConflictWithoutRetry(string weight, bool sends)
    {
        var api = new FakeApi { IncludeReparto = true, IncludeVehicleConfiguration = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/vehiculos");
        using var response = await client.PostAsync("/vehiculos?handler=Vehiculo", Form(token,
            ("Vehiculo.Id", VehiculoPrueba.ToString()), ("Vehiculo.Version", "7"), ("Vehiculo.Matricula", " KEPT "),
            ("Vehiculo.PesoMaximoGramos", weight), ("Vehiculo.VolumenMaximoCm3", "1.000"), ("Vehiculo.Activo", "true")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("value=\" KEPT \"", html); Assert.Contains($"value=\"{weight}\"", html);
        Assert.Contains("value=\"7\"", html);
        Assert.Equal(sends ? 1 : 0, api.Calls.Count(x => x.Path == "/api/reparto/vehiculos" && x.Method == HttpMethod.Put));
        Assert.Contains(sends ? "Recargalo" : "dos decimales efectivos", html);
        if (!sends) { Assert.Contains("data-valmsg-for=\"Vehiculo.PesoMaximoGramos\"", html); Assert.Contains("aria-invalid=\"true\"", html); }
    }
}
