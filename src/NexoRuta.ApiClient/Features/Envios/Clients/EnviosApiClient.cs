using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Envios.Clients;

public sealed class EnviosApiClient(HttpClient httpClient)
{
    public async Task<Uri?> ObtenerSeguimientoAsync(Guid envioId, Guid accesoId,
        Uri trackingBase, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/envios/{envioId}/seguimiento");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.Accepted) return null;
        response.EnsureSuccessStatusCode();

        const string mensaje = "No se pudo recuperar un enlace de seguimiento vigente.";
        EnlaceSeguimiento? enlace;
        try { enlace = await response.Content.ReadFromJsonAsync<EnlaceSeguimiento>(cancellationToken); }
        catch (JsonException) { throw new NexoRutaApiException(mensaje, response.StatusCode); }

        if (response.StatusCode != HttpStatusCode.OK || enlace?.Estado != "vigente" ||
            enlace.ExpiraEnUtc is null || enlace.ExpiraEnUtc <= DateTimeOffset.UtcNow ||
            !Uri.TryCreate(enlace.Url, UriKind.Absolute, out var apiUrl) ||
            (apiUrl.Scheme != "http" && apiUrl.Scheme != "https") ||
            apiUrl.UserInfo.Length != 0 || apiUrl.Query.Length != 0 || apiUrl.Fragment.Length != 0)
            throw new NexoRutaApiException(mensaje, response.StatusCode);

        var token = apiUrl.Segments.Last();
        if (token.Length != 64 || token.Any(c => !Uri.IsHexDigit(c)) ||
            !apiUrl.AbsolutePath.EndsWith("/api/public/seguimiento/" + token, StringComparison.Ordinal) ||
            !trackingBase.IsAbsoluteUri || (trackingBase.Scheme != "http" && trackingBase.Scheme != "https") ||
            trackingBase.UserInfo.Length != 0 || trackingBase.Query.Length != 0 || trackingBase.Fragment.Length != 0)
            throw new NexoRutaApiException(mensaje, response.StatusCode);

        // ponytail: adapt the current API link locally; generation and expiry stay backend-owned.
        return new Uri(trackingBase.AbsoluteUri.TrimEnd('/') + "/seguimiento/" + token);
    }

    private sealed record EnlaceSeguimiento(string Estado, string? Url, DateTimeOffset? ExpiraEnUtc);

    public async Task<IReadOnlyList<EnvioResponse>> ListarEnviosAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/envios");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await ApiHttp.SendAsync<EnvioResponse[]>(httpClient, request, cancellationToken);
    }

    public async Task<EnvioCreadoResponse> CrearEnvioAsync(
        CrearEnvioRequest envio,
        Guid accesoId,
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/envios")
        {
            Content = JsonContent.Create(envio)
        };
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await ApiHttp.SendAsync<EnvioCreadoResponse>(httpClient, request, cancellationToken);
    }
}
