using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
namespace NexoRuta.ApiClient.Features.Reparto.Clients;

public sealed class RutasApiClient(HttpClient http)
{
    public Task<EstimacionRutaResponse> ActualizarEstimacionAsync(Guid ruta,ActualizarEstimacionRequest d,string token,CancellationToken ct)=>EnviarResponseAsync<EstimacionRutaResponse,ActualizarEstimacionRequest>($"api/rutas/{ruta}/estimaciones/recorrido",d,token,ct);
    public Task<EstimacionRutaResponse> CalcularEstimacionAsync(Guid ruta,CalcularEstimacionRequest d,string token,CancellationToken ct)=>EnviarResponseAsync<EstimacionRutaResponse,CalcularEstimacionRequest>($"api/rutas/{ruta}/estimaciones",d,token,ct);
    public async Task<IReadOnlyList<EstimacionRutaResponse>> EstimacionesAsync(Guid ruta,long? antes,string token,CancellationToken ct)
    {using var request=ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get,$"api/rutas/{ruta}/estimaciones"+(antes is {} n?$"?antesDeVersion={n}":"")),token);return await ApiHttp.SendAsync<EstimacionRutaResponse[]>(http,request,ct);}
    public async Task<IReadOnlyList<VehiculoFlotaResponse>> FlotaAsync(string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get, "api/reparto/flota"), token);
        return await ApiHttp.SendAsync<VehiculoFlotaResponse[]>(http, request, ct);
    }
    public Task<ResultadoFlotaResponse> ConfirmarFlotaAsync(Guid vehiculoId, string accion, ComandoFlotaRequest datos, string token, CancellationToken ct) =>
        EnviarResponseAsync<ResultadoFlotaResponse, ComandoFlotaRequest>($"api/reparto/flota/{vehiculoId}/{accion}", datos, token, ct);
    public async Task<RecursosPlanificacionResponse> RecursosAsync(string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get, "api/rutas/recursos"), token);
        return await ApiHttp.SendAsync<RecursosPlanificacionResponse>(http, request, ct);
    }
    public async Task<IReadOnlyList<HojaRutaResponse>> ListarAsync(DateOnly fecha, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get, $"api/rutas?fecha={fecha:yyyy-MM-dd}"), token);
        return await ApiHttp.SendAsync<HojaRutaResponse[]>(http, request, ct);
    }
    public Task<HojaRutaResponse> PlanificarAsync(PlanificarRutaRequest datos, string token, CancellationToken ct) =>
        EnviarAsync("api/rutas", datos, token, ct);
    public Task<HojaRutaResponse> DespacharAsync(Guid id, long version, string token, CancellationToken ct) =>
        EnviarAsync($"api/rutas/{id}/despacho", new { version }, token, ct);
    public Task<HojaRutaResponse> CancelarRutaAsync(Guid id, long version, string token, CancellationToken ct) =>
        EnviarAsync($"api/rutas/{id}/cancelacion", new { version }, token, ct);
    public Task<HojaRutaResponse> ReasignarAsync(Guid envio, ReasignarEnvioRequest datos, string token, CancellationToken ct) =>
        EnviarAsync($"api/envios/{envio}/reasignacion", datos, token, ct);
    public async Task<ReprogramacionEnvioResponse> ReprogramarAsync(Guid envio, long version, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post, $"api/envios/{envio}/reprogramacion")
        { Content = JsonContent.Create(new { versionEnvio = version }) }, token);
        return await ApiHttp.SendAsync<ReprogramacionEnvioResponse>(http, request, ct);
    }
    public Task<DecisionDevolucionEnvioResponse> IniciarDevolucionAsync(Guid envio,
        IniciarDevolucionEnvioRequest datos, string token, CancellationToken ct) =>
        EnviarResponseAsync<DecisionDevolucionEnvioResponse, IniciarDevolucionEnvioRequest>(
            $"api/envios/{envio}/iniciar-devolucion", datos, token, ct);
    public Task<ConfirmacionExtravioEnvioResponse> ConfirmarExtravioAsync(Guid envio,
        ConfirmarExtravioEnvioRequest datos, string token, CancellationToken ct) =>
        EnviarResponseAsync<ConfirmacionExtravioEnvioResponse, ConfirmarExtravioEnvioRequest>(
            $"api/envios/{envio}/confirmar-extravio", datos, token, ct);
    public async Task<ConsultaRecepcionResponse> ConsultarRecepcionAsync(string codigo,string token,CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get,"api/deposito/consulta?codigo="+Uri.EscapeDataString(codigo)),token);
        return await ApiHttp.SendAsync<ConsultaRecepcionResponse>(http,request,ct);
    }
    public Task<ResultadoRecepcionResponse> CotejarBultoAsync(Guid id,CotejoBultoRequest datos,string token,CancellationToken ct) =>
        EnviarResponseAsync<ResultadoRecepcionResponse,CotejoBultoRequest>($"api/deposito/bultos/{id}/recepcion",datos,token,ct);
    public async Task RecibirAsync(Guid envio, long version, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post, $"api/deposito/envios/{envio}/recepcion")
        { Content = JsonContent.Create(new { version }) }, token);
        await ApiHttp.SendNoContentAsync(http, request, ct);
    }
    private async Task<HojaRutaResponse> EnviarAsync<T>(string path, T datos, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post, path) { Content = JsonContent.Create(datos) }, token);
        return await ApiHttp.SendAsync<HojaRutaResponse>(http, request, ct);
    }
    private async Task<TResponse> EnviarResponseAsync<TResponse, TRequest>(string path, TRequest datos, string token, CancellationToken ct)
        where TResponse : class
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post, path) { Content = JsonContent.Create(datos) }, token);
        return await ApiHttp.SendAsync<TResponse>(http, request, ct);
    }
}
