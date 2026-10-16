using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Usuarios.Clients;

public sealed class UsuariosApiClient(HttpClient httpClient)
{
    public Task<UsuarioActualResponse> ObtenerUsuarioActualAsync(string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/usuarios/actual"), token);
        return ApiHttp.SendAsync<UsuarioActualResponse>(httpClient, request, ct);
    }

    public async Task<IReadOnlyList<string>> ObtenerPermisosActualesAsync(string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/usuarios/actual/permisos"), token);
        return await ApiHttp.SendAsync<string[]>(httpClient, request, ct);
    }

    public async Task<IReadOnlyList<UsuarioOrganizacionResponse>> ListarAsync(string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/usuarios"), token);
        return await ApiHttp.SendAsync<UsuarioOrganizacionResponse[]>(httpClient, request, ct);
    }

    public Task<UsuarioOrganizacionResponse> CrearAsync(CrearUsuarioRequest usuario, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Post, "api/usuarios")
        {
            Content = JsonContent.Create(usuario)
        }, token);
        return ApiHttp.SendAsync<UsuarioOrganizacionResponse>(httpClient, request, ct);
    }

    public Task CambiarEstadoAsync(Guid usuarioId, bool activo, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Patch, $"api/usuarios/{usuarioId}/estado")
        {
            Content = JsonContent.Create(new { Activo = activo })
        }, token);
        return ApiHttp.SendNoContentAsync(httpClient, request, ct);
    }
}
