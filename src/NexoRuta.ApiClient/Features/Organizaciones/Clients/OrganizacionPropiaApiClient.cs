using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Requests;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Organizaciones.Clients;

public sealed class OrganizacionPropiaApiClient(HttpClient http)
{
    private const string Path = "api/organizacion/actual";

    public Task<OrganizacionPropiaResponse> ObtenerAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<OrganizacionPropiaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path), token), ct);

    public Task<OrganizacionPropiaResponse> ActualizarNombreAsync(
        ActualizarNombreOrganizacionRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<OrganizacionPropiaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Put, Path) { Content = JsonContent.Create(request) }, token), ct);

    public Task<IReadOnlyList<SolicitudBajaResponse>> ListarSolicitudesBajaAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<IReadOnlyList<SolicitudBajaResponse>>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{Path}/solicitudes-baja"), token), ct);

    public Task<SolicitudBajaResponse> SolicitarBajaAsync(SolicitarBajaRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<SolicitudBajaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, $"{Path}/solicitudes-baja") { Content = JsonContent.Create(request) }, token), ct);
}
