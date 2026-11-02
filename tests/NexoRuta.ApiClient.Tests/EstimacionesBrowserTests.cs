using System.Diagnostics;
namespace NexoRuta.ApiClient.Tests;
public sealed partial class BackofficeSessionAndPagesTests
{
    [RoadBrowserFact]
    public async Task RoadEstimatesUseRealRazorWithIsolatedControlledData()
    {
        var api=new FakeApi{IncludeReparto=true,IncludeVehicleConfiguration=true,IncludeRoadEstimates=true,RoadConflictOnce=true,RoadProfileConflictOnce=true};api.RoadHistory.Add(FakeApi.RoadEstimate(1,Guid.NewGuid()));api.RoadHistory.Add(FakeApi.RoadEstimate(2,Guid.NewGuid()));
        api.PermissionOverrides["jwt-user"]=["configuracion.gestionar","envios.gestionar","envios.consultar"];
        using var factory=new BackofficeFactory(api);factory.UseKestrel(0);using var client=factory.CreateClient();
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};start.ArgumentList.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tests/browser/cu06-b4.cjs")));start.Environment["B4_BASE_URL"]=client.BaseAddress!.ToString();
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);throw;}
        Assert.True(process.ExitCode==0,await output+"\n"+await error);Assert.Equal(3,api.RoadHistory.Count);Assert.Equal(2,api.RepartoPayloads.Count(p=>p.Path.EndsWith("/estimaciones")));Assert.Equal(2,api.RepartoPayloads.Count(p=>p.Path.Contains("/perfiles-viales/")));
    }
    [RoadUpdateBrowserFact]
    public async Task RoadUpdatesShowGpsManualInitialAndConflictInRealChromium()
    {
        var occupation=Guid.Parse("33333333-3333-3333-3333-333333333333");
        var api=new FakeApi{IncludeReparto=true,IncludeRoadEstimates=true,IncludeRoadUpdates=true,RoadConflictOnce=true,ShipmentStateOverride="AsignadoRuta",
            FleetResponse=new[]{new{Id=Guid.NewGuid(),Matricula="B5-FIXTURE",Activo=true,Version=1,VerificacionInicialEnUtc=(DateTimeOffset?)DateTimeOffset.Parse("2026-10-20T10:00:00Z"),CargaPendiente=1,UltimaLiberacionEnUtc=(DateTimeOffset?)null,CargasIniciales=Array.Empty<object>(),Ocupaciones=new[]{new{Id=occupation,RutaId=RutaPrueba,RepartidorAccesoId=TargetAccessId,Fecha=new DateOnly(2026,10,20),EstadoRuta="EnCurso",Version=1,Legada=false,AdquiridaEnUtc=(DateTimeOffset?)DateTimeOffset.Parse("2026-10-20T10:00:00Z"),RetornoEnUtc=(DateTimeOffset?)null,CargaPendiente=1,PuedeAnular=false}}}}};
        api.RoadHistory.Add(FakeApi.RoadEstimate(1,Guid.NewGuid()));api.RoadHistory.Add(FakeApi.RoadUpdate(2,Guid.NewGuid()));api.RoadHistory.Add(FakeApi.RoadUpdate(3,Guid.NewGuid(),"GpsAntiguo"));
        api.PermissionOverrides["jwt-user"]=["envios.gestionar","envios.consultar"];
        using var factory=new BackofficeFactory(api);factory.UseKestrel(0);using var client=factory.CreateClient();
        var start=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};start.ArgumentList.Add(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../tests/browser/cu06-b5.cjs")));start.Environment["B5_BASE_URL"]=client.BaseAddress!.ToString();
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));try{await process.WaitForExitAsync(timeout.Token);}catch(OperationCanceledException){process.Kill(true);throw;}
        Assert.True(process.ExitCode==0,await output+"\n"+await error);Assert.Equal(4,api.RoadHistory.Count);Assert.Equal(2,api.RepartoPayloads.Count(p=>p.Path.EndsWith("/estimaciones/recorrido")));
    }
    private sealed class RoadUpdateBrowserFactAttribute:FactAttribute
    {public RoadUpdateBrowserFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_B5_BROWSER_DIR")))Skip="Opt-in isolated Chromium B5.";}}
    private sealed class RoadBrowserFactAttribute:FactAttribute
    {public RoadBrowserFactAttribute(){if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_B4_BROWSER_DIR")))Skip="Opt-in Chromium/Kestrel and in-memory API; no operational data.";}}
}
