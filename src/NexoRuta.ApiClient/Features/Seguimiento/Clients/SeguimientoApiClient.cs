using System.Net;
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
}