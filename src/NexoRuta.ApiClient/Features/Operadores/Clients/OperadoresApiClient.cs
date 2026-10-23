using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;
using NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Operadores.Clients;

public sealed class OperadoresApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<OperadorDisponibleResponse>> ListarOperadoresAsync(
        string token, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/operadores"), token);
        return await ApiHttp.SendAsync<OperadorDisponibleResponse[]>(httpClient, request, cancellationToken);
    }

    public Task<IdentidadVisualPublicaResponse> ObtenerIdentidadVisualAsync(
        Guid operatorId, string token, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get,
            $"api/operadores/{operatorId:D}/identidad-visual"), token);
        return ApiHttp.SendAsync<IdentidadVisualPublicaResponse>(httpClient, request, cancellationToken);
    }
}
