using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Plataforma.Clients;

public sealed class OrganizacionesAdminApiClient(HttpClient http)
{
    public async Task<PaginaOrganizacionesResponse> ListarAsync(string tipo, int pagina, int tamanoPagina, string token, string? estado = null, CancellationToken ct = default)
    {
        var filter = string.IsNullOrWhiteSpace(estado) ? "" : $"&estado={Uri.EscapeDataString(estado)}";
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get,
            FormattableString.Invariant($"{Path(tipo)}?pagina={pagina}&tamanoPagina={tamanoPagina}{filter}")), token);
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

    public Task<OrganizacionResponse> CambiarEstadoAsync(string tipo, Guid id, CambiarEstadoOrganizacionRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<OrganizacionResponse>(http, ApiHttp.WithBearerToken(
            new(HttpMethod.Put, $"{Path(tipo)}/{id}/estado") { Content = JsonContent.Create(request) }, token), ct);

    public Task<PaginaSolicitudesBajaResponse> ListarSolicitudesBajaAsync(
        string? estado, string? tipo, int pagina, int tamanoPagina, string token, CancellationToken ct = default)
    {
        var filters = new List<string> { $"pagina={pagina}", $"tamanoPagina={tamanoPagina}" };
        if (!string.IsNullOrWhiteSpace(estado)) filters.Add($"estado={Uri.EscapeDataString(estado)}");
        if (!string.IsNullOrWhiteSpace(tipo)) filters.Add($"tipo={Uri.EscapeDataString(tipo)}");
        return ApiHttp.SendAsync<PaginaSolicitudesBajaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"api/admin/solicitudes-baja?{string.Join('&', filters)}"), token), ct);
    }

    public Task<SolicitudBajaAdminResponse> ObtenerSolicitudBajaAsync(Guid id, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<SolicitudBajaAdminResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"api/admin/solicitudes-baja/{id}"), token), ct);

    public Task<SolicitudBajaAdminResponse> ResolverSolicitudBajaAsync(
        Guid id, ResolverSolicitudBajaRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<SolicitudBajaAdminResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, $"api/admin/solicitudes-baja/{id}/resolver")
            { Content = JsonContent.Create(request) }, token), ct);

    public Task<PaginaSolicitudesReactivacionResponse> ListarSolicitudesReactivacionAsync(
        string? estado, string? tipo, int pagina, int tamanoPagina, string token, CancellationToken ct = default)
    {
        var filters = new List<string> { $"pagina={pagina}", $"tamanoPagina={tamanoPagina}" };
        if (!string.IsNullOrWhiteSpace(estado)) filters.Add($"estado={Uri.EscapeDataString(estado)}");
        if (!string.IsNullOrWhiteSpace(tipo)) filters.Add($"tipo={Uri.EscapeDataString(tipo)}");
        return ApiHttp.SendAsync<PaginaSolicitudesReactivacionResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"api/admin/solicitudes-reactivacion?{string.Join('&', filters)}"), token), ct);
    }

    public Task<SolicitudReactivacionResponse> ObtenerSolicitudReactivacionAsync(Guid id, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<SolicitudReactivacionResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"api/admin/solicitudes-reactivacion/{id}"), token), ct);

    public Task<SolicitudReactivacionResponse> ResolverSolicitudReactivacionAsync(
        Guid id, string decision, string motivo, long version, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<SolicitudReactivacionResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, $"api/admin/solicitudes-reactivacion/{id}/resolver")
                { Content = JsonContent.Create(new { decision, motivo, version }) }, token), ct);

    private static string Path(string tipo) => tipo is "operadores" or "comercios"
        ? $"api/admin/{tipo}" : throw new ArgumentException("Unknown organization resource.", nameof(tipo));
}
