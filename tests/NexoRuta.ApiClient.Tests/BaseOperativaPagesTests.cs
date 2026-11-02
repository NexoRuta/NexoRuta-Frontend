using System.Net;
using System.Diagnostics;
namespace NexoRuta.ApiClient.Tests;
public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task BaseConfigurationShowsUnknownStateAndSeparateConfirmationWithoutPretendingRoadEstimates()
    {
        var api = new FakeApi { IncludeReparto = true, IncludeVehicleConfiguration = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar", "envios.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory); await LoginAsync(client, "user@example.test");
        using var response = await client.GetAsync("/configuracion-reparto");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("Base operativa de salida y retorno", html); Assert.Contains("Base no configurada", html);
        Assert.Contains("Servicio por parada (minutos)", html); Assert.Contains("Margen de retorno (minutos)", html);
        Assert.Contains("Guardar base y parámetros", html); Assert.DoesNotContain("Confirmar punto de salida y retorno", html);
    }
    [Fact]
    public async Task TemporalPlanPostsFullLocalDatesAndExplicitMinutesWithoutInventingRoadTimes()
    {
        var api=new FakeApi { IncludeReparto=true,ShipmentStateOverride="EnDeposito" };
        api.PermissionOverrides["jwt-user"]=["envios.gestionar","envios.consultar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);await LoginAsync(client,"user@example.test");
        var token=await GetAntiforgeryTokenAsync(client,"/rutas/planificar?fecha=2026-10-20");
        using var response=await client.PostAsync("/rutas/planificar?handler=Planificar",Form(token,
            ("FechaSalida","2026-10-20"),("SalidaPlanificadaLocal","2026-10-20T23:00"),("RepartidorAccesoId",TargetAccessId.ToString()),("VehiculoId",VehiculoPrueba.ToString()),
            ("Plan[0].EnvioId",EnvioPrueba.ToString()),("Plan[0].Incluir","true"),("Plan[0].Orden","1"),("Plan[0].FechaLlegada","2026-10-21"),("Plan[0].Hora","00:15"),("Plan[0].ServicioMinutos","20")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);var body=Assert.Single(api.RepartoPayloads).Body;
        Assert.Equal(DateTimeOffset.Parse("2026-10-21T02:00:00Z"),body.GetProperty("salidaPlanificadaEnUtc").GetDateTimeOffset());
        var stop=body.GetProperty("paradas")[0];Assert.Equal(DateTimeOffset.Parse("2026-10-21T03:15:00Z"),stop.GetProperty("llegadaEstimadaUtc").GetDateTimeOffset());Assert.Equal(20,stop.GetProperty("servicioMinutos").GetInt32());
    }
    [BaseBrowserFact]
    public async Task BaseBrowserUsesRealRazorAndIsolatedApi()
    {
        var api=new FakeApi { IncludeReparto=true,IncludeVehicleConfiguration=true,IncludeBaseConfiguration=true,ShipmentStateOverride="EnDeposito" };
        api.PermissionOverrides["jwt-user"]=["configuracion.gestionar","envios.gestionar","envios.consultar"];
        using var factory=new BackofficeFactory(api); factory.UseKestrel(0); using var client=factory.CreateClient();
        var start=new ProcessStartInfo("node") { RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false };
        start.ArgumentList.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tests/browser/cu06-b3.cjs")));
        start.Environment["B3_BASE_URL"]=client.BaseAddress!.ToString();
        using var process=Process.Start(start)!;var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); } catch(OperationCanceledException) {process.Kill(true);throw;}
        Assert.True(process.ExitCode==0,await stdout+"\n"+await stderr);
        Assert.Equal(3,api.RepartoPayloads.Count(c=>c.Path=="/api/reparto/base-operativa"));
        Assert.Single(api.RepartoPayloads,c=>c.Path.EndsWith("/confirmacion"));
    }
    private sealed class BaseBrowserFactAttribute:FactAttribute
    {
        public BaseBrowserFactAttribute() {if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_B3_BROWSER_DIR")))Skip="Opt-in B3 Chromium isolated Razor fixture.";}
    }
}
