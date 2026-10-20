using System.Net;
using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Reparto.Contracts;

namespace NexoRuta.ApiClient.Features.Reparto.Clients;

public sealed class ConfiguracionRepartoApiClient(HttpClient http)
{
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
