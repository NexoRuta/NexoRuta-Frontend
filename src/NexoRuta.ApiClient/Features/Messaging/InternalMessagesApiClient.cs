using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
namespace NexoRuta.ApiClient.Features.Messaging;

public sealed record FailedInternalMessage(Guid Id, string Tipo, string Destino, string Estado, int Intentos,
    string Diagnostico, DateTimeOffset ProximoIntentoEnUtc);
public sealed record RetryInternalMessage(Guid SolicitudId, int Intentos, string Motivo);
public sealed record InternalRetryResult(Guid SolicitudId, string Resultado);
public sealed class InternalMessagesApiClient(HttpClient http)
{
    public async Task<FailedInternalMessage[]> ListAsync(int page, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Get, $"api/admin/mensajes-internos?pagina={page}&tamano=25"), token);
        return await ApiHttp.SendAsync<FailedInternalMessage[]>(http, request, ct);
    }
    public async Task<InternalRetryResult> RetryAsync(Guid id, RetryInternalMessage data, string token, CancellationToken ct)
    {
        using var request = ApiHttp.WithBearerToken(new(HttpMethod.Post, $"api/admin/mensajes-internos/{id}/reintentos") { Content = JsonContent.Create(data) }, token);
        return await ApiHttp.SendAsync<InternalRetryResult>(http, request, ct);
    }
}
