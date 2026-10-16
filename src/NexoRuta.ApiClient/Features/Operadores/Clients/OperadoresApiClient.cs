using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Operadores.Clients;

public sealed class OperadoresApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<OperadorDisponibleResponse>> ListarOperadoresAsync(
        string token, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/comercio/operadores"), token);
        return await ApiHttp.SendAsync<OperadorDisponibleResponse[]>(httpClient, request, cancellationToken);
    }
}
