using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
namespace NexoRuta.ApiClient.Features.Envios.Clients;

public sealed class DestinosEnvioApiClient(HttpClient http)
{
    public async Task<DestinoEnvioResponse> ObtenerAsync(Guid id, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, $"api/envios/{id}/destino"), token);
        return await ApiHttp.SendAsync<DestinoEnvioResponse>(http, request, ct);
    }
    public async Task<PropuestasDestinoResponse> ProponerAsync(Guid id, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Post, $"api/envios/{id}/destino/propuestas"), token);
        return await ApiHttp.SendAsync<PropuestasDestinoResponse>(http, request, ct);
    }
    public async Task ConfirmarAsync(Guid id, long version, double latitud, double longitud, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Put, $"api/envios/{id}/destino")
        { Content = JsonContent.Create(new { version, latitud, longitud }) }, token);
        await ApiHttp.SendNoContentAsync(http, request, ct);
    }
}
