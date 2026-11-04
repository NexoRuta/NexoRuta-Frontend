using System.Net;
using System.Text.RegularExpressions;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
namespace NexoRuta.ApiClient.Tests;
public sealed partial class BackofficeSessionAndPagesTests
{
    private static VehiculoFlotaResponse FleetVehicle(int id, string name, bool active = true, bool verified = true,
        string? routeState = null, int cargo = 0, bool returned = false) => new(
        new Guid(id, 0, 0, new byte[8]), name, active, 8, verified ? DateTimeOffset.Parse("2026-10-20T15:00:00Z") : null,
        cargo, routeState is null ? [] : [new(new Guid(id, 1, 0, new byte[8]), RutaPrueba, TargetAccessId,
            new(2020, 1, 2), routeState, 5, false, DateTimeOffset.Parse("2020-01-02T15:00:00Z"),
            returned ? DateTimeOffset.Parse("2020-01-02T18:00:00Z") : null, cargo, false)],
        routeState is null && verified ? DateTimeOffset.Parse("2026-10-20T18:30:00Z") : null, []);

    [Fact]
    public async Task FleetCardsHaveGlobalCountsAndNoInlineOperationForms()
    {
        var api = new FakeApi { IncludeReparto = true, FleetResponse = new[] {
            FleetVehicle(901, "ABCD", verified:false), FleetVehicle(902, "CU06-LOCAL-V", routeState:"EnCurso", cargo:3),
            FleetVehicle(903, "DEMO-A-001"), FleetVehicle(904, "INACTIVO", active:false) } };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/flota?buscarVehiculo=DEMO&disponibilidad=Disponible");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("data-fleet-count=\"total\">4", html);
        Assert.Contains("data-fleet-count=\"available\">2", html);
        Assert.Contains("data-fleet-count=\"occupied\">1", html);
        Assert.Contains("data-fleet-count=\"verification\">1", html);
        var cards = Regex.Matches(html, "<article[^>]*data-fleet-card[^>]*>.*?</article>", RegexOptions.Singleline);
        Assert.Equal(4, cards.Count);
        Assert.All(cards.Cast<Match>(), card => Assert.DoesNotContain("<form", card.Value));
        Assert.Contains("3 bultos bajo custodia", html);
        Assert.DoesNotContain("Descarga o conciliación pendiente", html);
        Assert.Contains("data-fleet-template", html);
        Assert.Contains("id=\"flota-operacion\"", html);
        Assert.Contains("15:30", html); // Persisted release displayed in operational Montevideo time.
    }
    [Fact]
    public async Task FleetQueryFailureDoesNotRenderInventedAvailabilityCounters()
    {
        var api = new FakeApi { IncludeReparto = true, FleetUnavailable = true };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/flota"));
        Assert.Contains("Disponibilidad desconocida", html);
        Assert.DoesNotContain("data-fleet-count=", html);
    }
    [Fact]
    public void FleetAvailabilityPresentationNeverEquatesEnabledWithPhysicallyAvailable()
    {
        Assert.Equal("Ocupado", NexoRuta.Backoffice.Features.Reparto.Pages.RutasModel.DisponibilidadFlota(FleetVehicle(1,"A",routeState:"EnCurso")));
        Assert.Equal("Pendiente", NexoRuta.Backoffice.Features.Reparto.Pages.RutasModel.DisponibilidadFlota(FleetVehicle(2,"B",verified:false)));
        Assert.Equal("Pendiente", NexoRuta.Backoffice.Features.Reparto.Pages.RutasModel.DisponibilidadFlota(FleetVehicle(3,"C",cargo:1)));
        Assert.Equal("Disponible", NexoRuta.Backoffice.Features.Reparto.Pages.RutasModel.DisponibilidadFlota(FleetVehicle(4,"D",active:false)));
        Assert.Equal("Disponible", NexoRuta.Backoffice.Features.Reparto.Pages.RutasModel.DisponibilidadFlota(FleetVehicle(5,"E")));
    }
    [Fact]
    public void FleetActionPresentationPreservesOccupationGuardsWithoutNewPhysicalState()
    {
        var model = new NexoRuta.Backoffice.Features.Reparto.Pages.RutasModel(null!, null!, null!, null!) { Fecha = new(2026,10,20) };
        var v = FleetVehicle(20, "V", routeState:"EnCurso");
        var o = v.Ocupaciones.Single();
        Assert.Equal(new[] { "retorno" }, model.AccionesFlota(v,o).Select(a=>a.Accion));
        Assert.Equal(new[] { "conciliar", "retorno" }, model.AccionesFlota(v,o with { Legada=true, AdquiridaEnUtc=null }).Select(a=>a.Accion));
        Assert.Equal(new[] { "anular" }, model.AccionesFlota(v,o with { EstadoRuta="Despachada", PuedeAnular=true }).Select(a=>a.Accion));
        Assert.Empty(model.AccionesFlota(v,o with { RetornoEnUtc=DateTimeOffset.UtcNow }));
        Assert.Equal(new[] { "liberar" }, model.AccionesFlota(v,o with { EstadoRuta="Finalizada", RetornoEnUtc=DateTimeOffset.UtcNow }).Select(a=>a.Accion));
        Assert.Empty(model.AccionesFlota(v,o with { EstadoRuta="Finalizada", RetornoEnUtc=DateTimeOffset.UtcNow, CargaPendiente=1 }));
        Assert.All(model.AccionesFlota(v,o), a => { Assert.Equal(o.Id,a.Ocupacion); Assert.Equal(o.Version,a.Version); });
    }

    [Fact]
    public async Task FleetSixCommandsStayInInertTemplatesWithExactResourceVersions()
    {
        var legacy = FleetVehicle(911,"LEGACY",routeState:"EnCurso");
        legacy = legacy with { Ocupaciones = [legacy.Ocupaciones.Single() with { Legada=true, AdquiridaEnUtc=null }] };
        var cancel = FleetVehicle(912,"ANULAR",routeState:"Despachada");
        cancel = cancel with { Ocupaciones = [cancel.Ocupaciones.Single() with { PuedeAnular=true }] };
        var historic = FleetVehicle(913,"HISTORICA",cargo:1) with { CargasIniciales = [new(EnvioPrueba,11,RutaPrueba,new(2020,1,2),1)] };
        var api = new FakeApi { IncludeReparto=true, FleetResponse = new[] {
            legacy, cancel, historic, FleetVehicle(914,"INICIAL",verified:false), FleetVehicle(915,"LIBERAR",routeState:"Finalizada",returned:true) } };
        api.PermissionOverrides["jwt-user"] = ["envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client,"user@example.test");
        var html = WebUtility.HtmlDecode(await client.GetStringAsync("/flota"));
        foreach (var command in new[] { "conciliar", "retorno", "liberar", "anular", "recibir-carga-inicial", "verificar-inicial" })
            Assert.Contains($"name=\"FlotaAccion\" value=\"{command}\"",html);
        Assert.Contains("name=\"FlotaVersionEnvio\" value=\"11\"",html);
        Assert.Contains("name=\"FlotaVersion\" value=\"5\"",html);
        Assert.Empty(api.RepartoPayloads);
    }

}
