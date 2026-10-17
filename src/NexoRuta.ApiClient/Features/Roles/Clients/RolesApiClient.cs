using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Roles.Contracts.Requests;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Roles.Clients;

public sealed class RolesApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<RolResponse>> ListarAsync(string token, CancellationToken ct = default)
        => await GetAsync<RolResponse[]>("api/roles", token, ct);

    public async Task<IReadOnlyList<RolResponse>> ListarAsignablesAsync(string token, CancellationToken ct = default)
        => await GetAsync<RolResponse[]>("api/roles/asignables", token, ct);

    public async Task<IReadOnlyList<PermisoResponse>> ListarPermisosAsync(string token, CancellationToken ct = default)
        => await GetAsync<PermisoResponse[]>("api/roles/permisos", token, ct);

    public async Task<Guid> CrearAsync(CrearRolRequest rol, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Post, "api/roles")
        {
            Content = JsonContent.Create(rol)
        }, token);
        return (await ApiHttp.SendAsync<CrearRolResponse>(httpClient, request, ct)).Id;
    }

    public Task RenombrarAsync(Guid id, string nombre, string token, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"api/roles/{id}", new { Nombre = nombre }, token, ct);

    public Task EliminarAsync(Guid id, string token, CancellationToken ct = default)
        => SendAsync(HttpMethod.Delete, $"api/roles/{id}", null, token, ct);

    public Task ActualizarPermisosAsync(Guid id, IReadOnlyList<string> permisos, string token, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"api/roles/{id}/permisos", new ActualizarPermisosRolRequest(permisos), token, ct);

    public Task AsignarAsync(Guid accesoId, Guid? rolId, string token, CancellationToken ct = default)
        => SendAsync(HttpMethod.Put, $"api/roles/accesos/{accesoId}", new { RolId = rolId }, token, ct);

    private async Task<T> GetAsync<T>(string path, string token, CancellationToken ct) where T : class
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, path), token);
        return await ApiHttp.SendAsync<T>(httpClient, request, ct);
    }

    private Task SendAsync(HttpMethod method, string path, object? body, string token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        return ApiHttp.SendNoContentAsync(httpClient, ApiHttp.WithBearerToken(request, token), ct);
    }
}
