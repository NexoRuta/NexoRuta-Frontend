using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Devoluciones.Contracts;

namespace NexoRuta.ApiClient.Features.Devoluciones.Clients;

public sealed class DevolucionesApiClient(HttpClient http)
{
    public async Task<PaginaDevolucionesResponse> ListarAsync(Guid? operadorId, DateTimeOffset desdeUtc,
        DateTimeOffset hastaUtc, string? situacion, string? referencia, string? cursor, int limite,
        string token, CancellationToken ct)
    {
        var query = new List<string>
        {
            $"desdeUtc={Uri.EscapeDataString(desdeUtc.ToUniversalTime().ToString("O"))}",
            $"hastaUtc={Uri.EscapeDataString(hastaUtc.ToUniversalTime().ToString("O"))}",
            $"limite={limite}"
        };
        if (operadorId is { } op) query.Add($"operadorId={op:D}");
        if (!string.IsNullOrWhiteSpace(situacion)) query.Add($"situacion={Uri.EscapeDataString(situacion)}");
        if (!string.IsNullOrWhiteSpace(referencia)) query.Add($"referencia={Uri.EscapeDataString(referencia)}");
        if (!string.IsNullOrWhiteSpace(cursor)) query.Add($"cursor={Uri.EscapeDataString(cursor)}");
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get, "api/devoluciones?" + string.Join('&', query)), token);
        return await ApiHttp.SendAsync<PaginaDevolucionesResponse>(http, request, ct);
    }

    public async Task<DevolucionDetalleResponse> ObtenerAsync(Guid operadorId, Guid envioId,
        int desdeBulto, int limite, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get,
            $"api/devoluciones/envios/{envioId:D}?operadorId={operadorId:D}&desdeBulto={desdeBulto}&limite={limite}"), token);
        return await ApiHttp.SendAsync<DevolucionDetalleResponse>(http, request, ct);
    }

    public async Task<IReadOnlyList<HechoDevolucionResponse>> HistorialAsync(Guid operadorId, Guid envioId,
        string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get,
            $"api/devoluciones/envios/{envioId:D}/historial?operadorId={operadorId:D}"), token);
        return await ApiHttp.SendAsync<HechoDevolucionResponse[]>(http, request, ct);
    }

    public async Task<TransferenciaDevolucionResponse> TransferirAsync(Guid envioId,
        TransferirDevolucionRequest datos, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post,
            $"api/devoluciones/envios/{envioId:D}/transferencias") { Content = JsonContent.Create(datos) }, token);
        return await ApiHttp.SendAsync<TransferenciaDevolucionResponse>(http, request, ct);
    }

    public async Task<AcuseDevolucionResponse> AcusarAsync(Guid operadorId, Guid transferenciaId,
        AcusarDevolucionRequest datos, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post,
            $"api/devoluciones/transferencias/{transferenciaId:D}/acuses?operadorId={operadorId:D}")
        { Content = JsonContent.Create(datos) }, token);
        return await ApiHttp.SendAsync<AcuseDevolucionResponse>(http, request, ct);
    }
}
