using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.ReglasOperativas.Contracts;

namespace NexoRuta.ApiClient.Features.ReglasOperativas.Clients;

public sealed class ReglasOperativasApiClient(HttpClient http)
{
    public Task<ReglasOperativasEstadoResponse> ObtenerEstadoAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<ReglasOperativasEstadoResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, ReglasOperativasApiRoutes.Base), token), ct);

    public Task<VersionReglasOperativasResponse> CrearBorradorAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionReglasOperativasResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, ReglasOperativasApiRoutes.Draft), token), ct);

    public Task<VersionReglasOperativasResponse> ObtenerBorradorAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionReglasOperativasResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, ReglasOperativasApiRoutes.Draft), token), ct);

    public Task<VersionReglasOperativasResponse> ReemplazarBorradorAsync(
        ReemplazarBorradorReglasOperativasRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionReglasOperativasResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Put, ReglasOperativasApiRoutes.Draft)
            { Content = JsonContent.Create(request) }, token), ct);

    public Task<VersionReglasOperativasResponse> PublicarBorradorAsync(
        PublicarBorradorReglasOperativasRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionReglasOperativasResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, ReglasOperativasApiRoutes.Publish)
            { Content = JsonContent.Create(request) }, token), ct);

    public Task<PaginaPublicacionesReglasOperativasResponse> ListarPublicacionesAsync(
        string token, int pagina = 1, int tamanoPagina = 20, CancellationToken ct = default)
    {
        var query = $"?pagina={pagina}&tamanoPagina={tamanoPagina}";
        return ApiHttp.SendAsync<PaginaPublicacionesReglasOperativasResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, ReglasOperativasApiRoutes.Publications + query), token), ct);
    }

    public Task<VersionReglasOperativasResponse> ObtenerPublicacionAsync(
        Guid versionId, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionReglasOperativasResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{ReglasOperativasApiRoutes.Publications}/{versionId}"), token), ct);
}
