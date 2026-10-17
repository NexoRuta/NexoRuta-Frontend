using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;
using System.Net.Http.Json;

namespace NexoRuta.ApiClient.Features.Organizaciones.Clients;

public sealed class OrganizacionSuspendidaApiClient(HttpClient http)
{
    public Task<OrganizacionSuspendidaResponse> ObtenerAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<OrganizacionSuspendidaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, "api/organizacion/suspendida"), token), ct);

    public Task<IReadOnlyList<SolicitudReactivacionResponse>> ListarReactivacionesAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<IReadOnlyList<SolicitudReactivacionResponse>>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, "api/organizacion/suspendida/solicitudes-reactivacion"), token), ct);

    public Task<SolicitudReactivacionResponse> SolicitarReactivacionAsync(string motivo, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<SolicitudReactivacionResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, "api/organizacion/suspendida/solicitudes-reactivacion")
                { Content = JsonContent.Create(new { motivo }) }, token), ct);
}
