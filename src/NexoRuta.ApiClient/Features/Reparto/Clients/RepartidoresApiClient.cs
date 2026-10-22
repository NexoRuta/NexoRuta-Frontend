using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Reparto.Contracts;

namespace NexoRuta.ApiClient.Features.Reparto.Clients;

public sealed class RepartidoresApiClient(HttpClient http)
{
    public async Task<IReadOnlyList<RepartidorResponse>> ListarAsync(string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/repartidores"), token);
        return await ApiHttp.SendAsync<RepartidorResponse[]>(http, request, ct);
    }

    public async Task<RepartidorResponse> GuardarAsync(Guid accesoId, string nombre, bool activo, long? version,
        string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Put, $"api/repartidores/{accesoId}")
        { Content = JsonContent.Create(new { nombre, activo, version }) }, token);
        return await ApiHttp.SendAsync<RepartidorResponse>(http, request, ct);
    }
}
