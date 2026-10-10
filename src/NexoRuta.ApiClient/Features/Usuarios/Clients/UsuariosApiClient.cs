using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Usuarios.Clients;

public sealed class UsuariosApiClient(HttpClient httpClient)
{
    public async Task<UsuarioActualResponse> ObtenerUsuarioActualAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/usuarios/actual");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await ApiHttp.SendAsync<UsuarioActualResponse>(httpClient, request, cancellationToken);
    }
}
