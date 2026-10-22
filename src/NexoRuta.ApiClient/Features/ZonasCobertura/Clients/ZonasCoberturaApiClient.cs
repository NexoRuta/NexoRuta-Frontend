using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;

namespace NexoRuta.ApiClient.Features.ZonasCobertura.Clients;

public sealed class ZonasCoberturaApiClient(HttpClient http)
{
    private const string Path = "api/configuracion/zonas";
    private const string DepartmentsPath = Path + "/departamentos";

    public Task<PaginaZonasCoberturaResponse> ListarAsync(string token, string? estado = null,
        int pagina = 1, int tamanoPagina = 20, CancellationToken ct = default)
    {
        var query = $"?pagina={pagina}&tamanoPagina={tamanoPagina}" +
            (estado is null ? "" : $"&estado={Uri.EscapeDataString(estado)}");
        return ApiHttp.SendAsync<PaginaZonasCoberturaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path + query), token), ct);
    }

    public Task<ZonaCoberturaDetalleResponse> ObtenerAsync(Guid zonaId, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<ZonaCoberturaDetalleResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{Path}/{zonaId}"), token), ct);

    public Task<ZonaCoberturaDetalleResponse> CrearAsync(CrearZonaCoberturaRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<ZonaCoberturaDetalleResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, Path) { Content = JsonContent.Create(request) }, token), ct);

    public Task<ZonaCoberturaDetalleResponse> ActualizarAsync(Guid zonaId, ActualizarZonaCoberturaRequest request,
        string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<ZonaCoberturaDetalleResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Put, $"{Path}/{zonaId}") { Content = JsonContent.Create(request) }, token), ct);

    public Task<ZonaCoberturaDetalleResponse> CambiarEstadoAsync(Guid zonaId, CambiarEstadoZonaRequest request,
        string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<ZonaCoberturaDetalleResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Put, $"{Path}/{zonaId}/estado") { Content = JsonContent.Create(request) }, token), ct);

    public Task<FranjasEntregaResponse> ListarFranjasAsync(Guid zonaId, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<FranjasEntregaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{Path}/{zonaId}/franjas"), token), ct);

    public Task<FranjaEntregaCambioResponse> CrearFranjaAsync(Guid zonaId, CrearFranjaEntregaRequest request,
        string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<FranjaEntregaCambioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, $"{Path}/{zonaId}/franjas") { Content = JsonContent.Create(request) }, token), ct);

    public Task<FranjaEntregaCambioResponse> ActualizarFranjaAsync(Guid zonaId, Guid franjaId,
        ActualizarFranjaEntregaRequest request, string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<FranjaEntregaCambioResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Put, $"{Path}/{zonaId}/franjas/{franjaId}") { Content = JsonContent.Create(request) }, token), ct);

    public Task<IReadOnlyList<DepartamentoReferenciaResponse>> ListarDepartamentosAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<IReadOnlyList<DepartamentoReferenciaResponse>>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, DepartmentsPath), token), ct);

    public Task<DepartamentoReferenciaGeometriaResponse> ObtenerDepartamentoAsync(string codigo, string token,
        CancellationToken ct = default)
        => ApiHttp.SendAsync<DepartamentoReferenciaGeometriaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{DepartmentsPath}/{Uri.EscapeDataString(codigo)}"), token), ct);

    public Task<TerritorioUruguayResponse> ObtenerTerritorioUruguayAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<TerritorioUruguayResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{DepartmentsPath}/territorio"), token), ct);

    public Task<ConsultaCoberturaResponse> ConsultarCoberturaAsync(ConsultarCoberturaRequest request,
        string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<ConsultaCoberturaResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Post, $"{Path}/cobertura") { Content = JsonContent.Create(request) }, token), ct);
}
