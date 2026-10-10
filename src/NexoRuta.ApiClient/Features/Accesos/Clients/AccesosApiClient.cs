using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Accesos.Clients;

public sealed class AccesosApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<UsuarioActualResponse>> ListarAccesosAsync(
        string tipo, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/accesos?tipo={Uri.EscapeDataString(tipo)}");
        return await ApiHttp.SendAsync<UsuarioActualResponse[]>(httpClient, request, cancellationToken);
    }
}
