using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;

namespace NexoRuta.ApiClient.Features.Tarifas.Clients;

public sealed class TarifasApiClient(HttpClient http)
{
    private const string Path = "api/configuracion/tarifas";

    public Task<TarifarioEstadoResponse> ObtenerEstadoAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<TarifarioEstadoResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path), token), ct);

    public Task<PaginaPublicacionesTarifarioResponse> ListarPublicacionesAsync(string token,
        int pagina = 1, int tamanoPagina = 20, CancellationToken ct = default)
    {
        var query = $"?pagina={pagina}&tamanoPagina={tamanoPagina}";
        return ApiHttp.SendAsync<PaginaPublicacionesTarifarioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path + "/publicaciones" + query), token), ct);
    }

    public Task<VersionTarifarioResponse> ObtenerPublicacionAsync(Guid versionId, string token,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionTarifarioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{Path}/publicaciones/{versionId}"), token), ct);

    public Task<VersionTarifarioResponse> ObtenerBorradorAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionTarifarioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path + "/borrador"), token), ct);

    public Task<VersionTarifarioResponse> CrearBorradorAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionTarifarioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, Path + "/borrador"), token), ct);

    public Task<VersionTarifarioResponse> ReemplazarTarifasBorradorAsync(
        ReemplazarTarifasBorradorRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionTarifarioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Put, Path + "/borrador/tarifas")
            { Content = JsonContent.Create(request) }, token), ct);

    public Task<VersionTarifarioResponse> PublicarBorradorAsync(
        PublicarBorradorTarifarioRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<VersionTarifarioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, Path + "/borrador/publicar")
            { Content = JsonContent.Create(request) }, token), ct);

    public Task<SimulacionTarifaResponse> SimularAsync(SimularTarifaRequest request, string token,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<SimulacionTarifaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, Path + "/simulaciones")
            { Content = JsonContent.Create(request) }, token), ct);
}
