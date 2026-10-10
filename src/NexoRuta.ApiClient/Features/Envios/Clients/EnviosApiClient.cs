using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Envios.Clients;

public sealed class EnviosApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<EnvioResponse>> ListarEnviosAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/envios");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await ApiHttp.SendAsync<EnvioResponse[]>(httpClient, request, cancellationToken);
    }

    public async Task<EnvioCreadoResponse> CrearEnvioAsync(
        CrearEnvioRequest envio,
        Guid accesoId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/envios")
        {
            Content = JsonContent.Create(envio)
        };
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await ApiHttp.SendAsync<EnvioCreadoResponse>(httpClient, request, cancellationToken);
    }
}
