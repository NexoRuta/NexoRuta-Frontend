using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Plataforma.Clients;

public sealed class DatosDemoApiClient(HttpClient http)
{
    private const string Path = "api/admin/demo-data";

    public Task<DatosDemoStatusResponse> ObtenerEstadoAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<DatosDemoStatusResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path), token), ct);

    public Task<DatosDemoStatusResponse> SembrarAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<DatosDemoStatusResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, Path) { Content = JsonContent.Create(new { }) }, token), ct);
}
