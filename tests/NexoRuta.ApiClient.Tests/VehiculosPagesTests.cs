using System.Net;
namespace NexoRuta.ApiClient.Tests;
public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]public async Task VehiclesHaveIndependentMenuPageAndDoNotRequirePolicyBaseOrRoadQueries()
    {
        var api=new FakeApi{IncludeReparto=true,IncludeVehicleConfiguration=true};api.PermissionOverrides["jwt-user"]=["configuracion.gestionar","usuarios.gestionar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);await LoginAsync(client,"user@example.test");
        using var response=await client.GetAsync("/vehiculos");Assert.Equal(HttpStatusCode.OK,response.StatusCode);var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("href=\"/vehiculos\"",html);Assert.Contains("Operador y equipo",html);Assert.Contains("Agregar vehículo",html);Assert.Contains("ABC",html);Assert.Contains("Vehiculo.Version",html);
        Assert.DoesNotContain(api.Calls,x=>x.Path is "/api/reparto/configuracion" or "/api/reparto/base-operativa" or "/api/reparto/perfiles-viales");
    }
    [Fact]public async Task VehiclesMenuAndDirectPageUseTheExistingConfigurationPermission()
    {
        var api=new FakeApi();api.PermissionOverrides["jwt-user"]=["usuarios.gestionar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);await LoginAsync(client,"user@example.test");
        using var response=await client.GetAsync("/vehiculos");Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);Assert.Contains("/acceso-denegado",response.Headers.Location!.OriginalString);Assert.DoesNotContain(api.Calls,x=>x.Path=="/api/reparto/vehiculos");
        var html=await client.GetStringAsync("/mi-operador");Assert.DoesNotContain("href=\"/vehiculos\"",html);Assert.Contains("href=\"/repartidores\"",html);
    }
    [Theory][InlineData(HttpStatusCode.NotFound)][InlineData(HttpStatusCode.InternalServerError)]
    public async Task FailedFleetHistoryAndRoadQueriesStayExplicitInsteadOfPretendingEmptyData(HttpStatusCode status)
    {
        var api=new FakeApi{IncludeReparto=true,IncludeVehicleConfiguration=true,IncludeRoadEstimates=true,RepartoQueryFailure=status};api.PermissionOverrides["jwt-user"]=["configuracion.gestionar","envios.gestionar","envios.consultar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);await LoginAsync(client,"user@example.test");
        // The accepted task split keeps the day list free of global fleet/history queries.
        using var day = await client.GetAsync("/rutas?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, day.StatusCode);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/reparto/flota" || call.Path.EndsWith("/estimaciones", StringComparison.Ordinal));

        using var fleetResponse = await client.GetAsync("/flota");
        Assert.Equal(HttpStatusCode.OK, fleetResponse.StatusCode);
        var fleet = WebUtility.HtmlDecode(await fleetResponse.Content.ReadAsStringAsync());
        Assert.Contains(api.Calls, call => call.Method == HttpMethod.Get && call.Path == "/api/reparto/flota");
        Assert.Contains("GET /api/reparto/flota", fleet);
        Assert.Contains($"HTTP {(int)status}", fleet);
        Assert.Contains("Disponibilidad desconocida", fleet);
        Assert.Contains("Conteos no disponibles", fleet);
        Assert.DoesNotContain("No hay vehículos registrados", fleet);
        Assert.DoesNotContain("data-fleet-count=", fleet);

        using var detailResponse = await client.GetAsync($"/rutas/{RutaPrueba}?fecha=2026-10-20");
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detail = WebUtility.HtmlDecode(await detailResponse.Content.ReadAsStringAsync());
        Assert.Contains(api.Calls, call => call.Method == HttpMethod.Get && call.Path == $"/api/rutas/{RutaPrueba}/estimaciones");
        Assert.Contains($"GET /api/rutas/{RutaPrueba}/estimaciones", detail);
        Assert.Contains($"HTTP {(int)status}", detail);
        Assert.Contains("no se presume una estimación vigente ni un histórico vacío", detail);
        Assert.DoesNotContain("Sin cálculos registrados", detail);

        using var configResponse = await client.GetAsync("/configuracion-reparto");
        Assert.Equal(HttpStatusCode.OK, configResponse.StatusCode);
        var config = WebUtility.HtmlDecode(await configResponse.Content.ReadAsStringAsync());
        Assert.Contains(api.Calls, call => call.Method == HttpMethod.Get && call.Path == "/api/reparto/perfiles-viales");
        Assert.Contains("GET /api/reparto/perfiles-viales", config);
        Assert.Contains($"HTTP {(int)status}", config);
        Assert.Contains("no se presume confirmada", config);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public async Task VehicleReadFailureOrUnknownWriteRetainsInputAndNeverInventsAnEmptyList(bool unknownWrite)
    {
        var api=new FakeApi{IncludeReparto=true,IncludeVehicleConfiguration=true,VehiclesQueryFailure=unknownWrite?null:HttpStatusCode.ServiceUnavailable,VehicleWriteTimeout=unknownWrite};api.PermissionOverrides["jwt-user"]=["configuracion.gestionar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);await LoginAsync(client,"user@example.test");var token=await GetAntiforgeryTokenAsync(client,"/vehiculos");
        using var response=await client.PostAsync("/vehiculos?handler=Vehiculo",Form(token,("Vehiculo.Id",VehiculoPrueba.ToString()),("Vehiculo.Version","7"),("Vehiculo.Matricula","KEPT"),("Vehiculo.PesoMaximoGramos","1.000"),("Vehiculo.VolumenMaximoCm3","1.00"),("Vehiculo.Activo","true")));
        var html=WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());Assert.Contains("value=\"KEPT\"",html);Assert.Contains("value=\"7\"",html);Assert.DoesNotContain("No hay vehículos registrados",html);Assert.Contains(unknownWrite?"No se pudo confirmar el guardado":"No se intentó guardar",html);Assert.Equal(unknownWrite?1:0,api.Calls.Count(x=>x.Path=="/api/reparto/vehiculos"&&x.Method==HttpMethod.Put));
    }
    [Fact]public async Task VehicleManagementIsNotReplacedByRoadCompatibilityOrDuplicatedInConfiguration()
    {
        var api=new FakeApi{IncludeReparto=true,IncludeVehicleConfiguration=true,IncludeRoadEstimates=true};api.PermissionOverrides["jwt-user"]=["configuracion.gestionar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);await LoginAsync(client,"user@example.test");
        var config=WebUtility.HtmlDecode(await client.GetStringAsync("/configuracion-reparto"));Assert.Contains("perfiles-heading",config);Assert.Contains("href=\"/vehiculos\"",config);Assert.DoesNotContain("handler=Vehiculo",config);
        var vehicles=WebUtility.HtmlDecode(await client.GetStringAsync("/vehiculos"));Assert.Contains("handler=Vehiculo",vehicles);Assert.Contains("Vehiculo.PesoMaximoGramos",vehicles);Assert.Contains("Vehiculo.VolumenMaximoCm3",vehicles);Assert.Contains("Vehiculo.Activo",vehicles);Assert.Contains("Configurar compatibilidad vial",vehicles);
    }
}
