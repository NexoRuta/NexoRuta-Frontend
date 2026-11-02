using System.Net;
using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Reparto.Contracts;

namespace NexoRuta.ApiClient.Features.Reparto.Clients;

public sealed class ConfiguracionRepartoApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<PerfilVialResponse>> PerfilesAsync(string token,CancellationToken ct)
    {using var request=ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get,"api/reparto/perfiles-viales"),token);return await ApiHttp.SendAsync<PerfilVialResponse[]>(http,request,ct);}
    public async Task<PerfilVialResponse> PerfilAsync(Guid id,ConfigurarPerfilVialRequest d,string token,CancellationToken ct)
    {using var request=ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Put,$"api/reparto/perfiles-viales/{id}"){Content=JsonContent.Create(d)},token);return await ApiHttp.SendAsync<PerfilVialResponse>(http,request,ct);}
    public async Task<BaseOperativaResponse?> BaseAsync(string token, CancellationToken ct)
    {
        try { return await Enviar<BaseOperativaResponse>(HttpMethod.Get, "base-operativa", null, token, ct); }
        catch (NexoRutaApiException e) when (e.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public Task<BaseOperativaResponse> GuardarBaseAsync(GuardarBaseOperativaRequest datos, string token, CancellationToken ct) => Enviar<BaseOperativaResponse>(HttpMethod.Put, "base-operativa", datos, token, ct);
    public Task<BaseOperativaResponse> ConfirmarBaseAsync(ConfirmarBaseOperativaRequest datos, string token, CancellationToken ct) => Enviar<BaseOperativaResponse>(HttpMethod.Post, "base-operativa/confirmacion", datos, token, ct);
    public Task<PropuestasBaseResponse> ProponerBaseAsync(string token, CancellationToken ct) => Enviar<PropuestasBaseResponse>(HttpMethod.Post, "base-operativa/propuestas", null, token, ct);
    public async Task<ConfiguracionRepartoResponse?> ObtenerAsync(string token, CancellationToken ct)
    {
        try { return await Enviar<ConfiguracionRepartoResponse>(HttpMethod.Get, "configuracion", null, token, ct); }
        catch (NexoRutaApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { return null; }
    }
    public Task<ConfiguracionRepartoResponse> GuardarAsync(ConfiguracionRepartoResponse datos, string token, CancellationToken ct) =>
        Enviar<ConfiguracionRepartoResponse>(HttpMethod.Put, "configuracion", datos, token, ct);
    public Task<VehiculoResponse[]> VehiculosAsync(string token, CancellationToken ct) =>
        Enviar<VehiculoResponse[]>(HttpMethod.Get, "vehiculos", null, token, ct);
    public Task<VehiculoResponse> GuardarVehiculoAsync(VehiculoResponse datos, string token, CancellationToken ct) =>
        Enviar<VehiculoResponse>(HttpMethod.Put, "vehiculos", datos, token, ct);
    private async Task<T> Enviar<T>(HttpMethod metodo, string ruta, object? datos, string token, CancellationToken ct) where T : class
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(metodo, $"api/reparto/{ruta}"), token);
        if (datos is not null) request.Content = JsonContent.Create(datos);
        return await ApiHttp.SendAsync<T>(http, request, ct);
    }
}
