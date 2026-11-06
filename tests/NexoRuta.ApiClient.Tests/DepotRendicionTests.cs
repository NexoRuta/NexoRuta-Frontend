using System.Net;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.Backoffice.Features.Reparto.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [Theory]
    [InlineData("EnTransito")]
    [InlineData("NoEntregado")]
    public async Task DepotShowsReturnedParcelsWithoutInventingACloseIncident(string state)
    {
        var api = new FakeApi
        {
            IncludeReparto = true, ShipmentStateOverride = state,
            BultoCustodyOverride = "PendienteDeposito"
        };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        var list = WebUtility.HtmlDecode(await client.GetStringAsync(
            "/deposito?fecha=2026-10-21&filtroRecepcion=Otras%20confirmaciones"));
        Assert.Contains("class=\"nr-receipt-card\"", list);
        Assert.Contains("Rendición declarada", list);
        Assert.DoesNotContain("vinculada a una incidencia de cierre", list);

        var selected = WebUtility.HtmlDecode(await client.GetStringAsync(
            $"/deposito?fecha=2026-10-21&envioSeleccionadoId={EnvioPrueba}"));
        Assert.Contains("Confirmar recepción de bultos pendientes", selected);
        Assert.Contains("name=\"version\" value=\"8\"", selected);
        Assert.DoesNotContain("Este envío no admite recepción", selected);
        Assert.DoesNotContain(api.Calls, c => c.Method == HttpMethod.Post && c.Path.EndsWith("/recepcion", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("EnTransito", "Pendiente", "PendienteDeposito", false, true)]
    [InlineData("NoEntregado", "Pendiente", "PendienteDeposito", false, true)]
    [InlineData("Reprogramado", "Pendiente", "PendienteDeposito", false, true)]
    [InlineData("AsignadoRuta", "Pendiente", "Repartidor", false, false)]
    [InlineData("EnTransito", "Pendiente", "Repartidor", false, false)]
    [InlineData("NoEntregado", "Pendiente", "Repartidor", false, false)]
    [InlineData("EnTransito", "Pendiente", "Deposito", false, false)]
    [InlineData("EntregadoParcialmente", "Entregado", "Destinatario", false, false)]
    [InlineData("Entregado", "Pendiente", "PendienteDeposito", false, false)]
    [InlineData("EntregaPendienteValidacion", "Pendiente", "PendienteDeposito", false, false)]
    [InlineData("EnDevolucion", "EnDevolucion", "Deposito", false, true)]
    [InlineData("Retirado", "Pendiente", "PendienteDeposito", true, true)]
    [InlineData("Retirado", "Pendiente", "PendienteDeposito", false, false)]
    public void DepotReceiptRequiresAReceivableResultAndPhysicalCustody(
        string state, string result, string custody, bool pickup, bool expected)
    {
        var model = new RutasModel(null!, null!, null!, null!);
        var shipment = ReceiptShipment(state, [ReceiptParcel("B1", result, custody)]) with { EsRetiro = pickup };
        Assert.Equal(expected, model.AdmiteRecepcion(shipment));
    }

    [Theory]
    [InlineData("Entregado", "Destinatario", true)]
    [InlineData("Pendiente", "Repartidor", false)]
    [InlineData("Pendiente", "Deposito", true)]
    public void DepotReceiptDoesNotReceiveDeliveredParcelsOrHideAnUnreturnedParcel(
        string otherResult, string otherCustody, bool expected)
    {
        var model = new RutasModel(null!, null!, null!, null!);
        var shipment = ReceiptShipment("EntregadoParcialmente",
            [ReceiptParcel("RETURNED", "Pendiente", "PendienteDeposito"),
             ReceiptParcel("OTHER", otherResult, otherCustody)]);
        Assert.Equal(expected, model.AdmiteRecepcion(shipment));
    }

    [Fact]
    public void DepotReceiptDoesNotAdvertiseAnEmptyParcelSet()
    {
        var model = new RutasModel(null!, null!, null!, null!);
        Assert.False(model.AdmiteRecepcion(ReceiptShipment("EnTransito", [])));
    }

    [Fact]
    public async Task DepotDoesNotUseAnOutdatedCloseIncidentToReceiveCourierCustody()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeIncidenciaCierre = true,
            BultoCustodyOverride = "Repartidor", DepotCloseIncidentVersion = 1 };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var html = await client.GetStringAsync($"/deposito?fecha=2026-10-20&envioSeleccionadoId={EnvioPrueba}");
        Assert.DoesNotContain("handler=Recibir", html);
    }

    private sealed partial class FakeApi
    {
        public long DepotCloseIncidentVersion { get; init; } = 2;
    }

    private static BultoResponse ReceiptParcel(string code, string result, string custody) =>
        new(code, 100m, 1m, 1m, 1m) { Resultado = result, Custodia = custody };

    private static EnvioResponse ReceiptShipment(string state, IReadOnlyList<BultoResponse> parcels) =>
        new(EnvioPrueba, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "test@example.test",
            "Operator", "Commerce", "Recipient", "Synthetic depot", state, 8, parcels, DateTimeOffset.UtcNow);
}
