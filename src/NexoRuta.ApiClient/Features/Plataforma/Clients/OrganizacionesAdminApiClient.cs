using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Plataforma.Clients;

public sealed class OrganizacionesAdminApiClient(HttpClient http)
{
    public async Task<PaginaOrganizacionesResponse> ListarAsync(string tipo, int pagina, int tamanoPagina, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get,
            FormattableString.Invariant($"{Path(tipo)}?pagina={pagina}&tamanoPagina={tamanoPagina}")), token);
        return await ApiHttp.SendAsync<PaginaOrganizacionesResponse>(http, request, ct);
    }

    public async Task<OrganizacionResponse> ObtenerAsync(string tipo, Guid id, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{Path(tipo)}/{id}"), token);
        return await ApiHttp.SendAsync<OrganizacionResponse>(http, request, ct);
    }

    public async Task<AltaOrganizacionResponse> CrearAsync(string tipo, CrearOrganizacionRequest organization, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post, Path(tipo)) { Content = JsonContent.Create(organization) }, token);
        return await ApiHttp.SendAsync<AltaOrganizacionResponse>(http, request, ct);
    }

    private static string Path(string tipo) => tipo is "operadores" or "comercios"
        ? $"api/admin/{tipo}" : throw new ArgumentException("Unknown organization resource.", nameof(tipo));
}
