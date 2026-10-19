using System.Net;
using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Seguimiento.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Seguimiento.Clients;

public sealed class SeguimientoApiClient(HttpClient httpClient)
{
    public async Task<SeguimientoEstadoResponse?> ConsultarAsync(
        string token,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/public/seguimiento/{Uri.EscapeDataString(token)}"
        );

        try
        {
            return await ApiHttp.SendAsync<SeguimientoEstadoResponse>(
                httpClient,
                request,
                cancellationToken
            );
        }
        catch (NexoRutaApiException ex)
            when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }
    public async Task SolicitarCodigoCancelacionAsync(string token, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/public/seguimiento/{Uri.EscapeDataString(token)}/cancelacion/codigo");
        await ApiHttp.SendNoContentAsync(httpClient, request, ct);
    }

    public async Task CancelarAsync(string token, string codigo, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"api/public/seguimiento/{Uri.EscapeDataString(token)}/cancelacion")
        { Content = JsonContent.Create(new { codigo }) };
        await ApiHttp.SendNoContentAsync(httpClient, request, ct);
    }
}