using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Operadores.Clients;

public sealed class OperadoresApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<OperadorDisponibleResponse>> ListarOperadoresAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/comercio/operadores");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await ApiHttp.SendAsync<OperadorDisponibleResponse[]>(httpClient, request, cancellationToken);
    }
}
