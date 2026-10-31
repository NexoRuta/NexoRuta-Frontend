using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text;
using System.Net.Http.Headers;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Envios.Clients;

public sealed class EnviosApiClient(HttpClient httpClient)
{
    private const long MaximumImportFileBytes = 5 * 1024 * 1024;

    public async Task<EnvioCanceladoResponse> CancelarAsync(Guid envioId, long version, string motivo, string token, CancellationToken ct = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Post, $"api/envios/{envioId}/cancelacion")
        { Content = JsonContent.Create(new { Version = version, Motivo = motivo }) }, token);
        return await ApiHttp.SendAsync<EnvioCanceladoResponse>(httpClient, request, ct);
    }
    public async Task<Uri?> ObtenerSeguimientoAsync(Guid envioId, string token,
        Uri trackingBase, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, $"api/envios/{envioId}/seguimiento"), token);
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

        var seguimientoToken = apiUrl.Segments.Last();
        if (seguimientoToken.Length != 64 || seguimientoToken.Any(c => !Uri.IsHexDigit(c)) ||
            !apiUrl.AbsolutePath.EndsWith("/api/public/seguimiento/" + seguimientoToken, StringComparison.Ordinal) ||
            !trackingBase.IsAbsoluteUri || (trackingBase.Scheme != "http" && trackingBase.Scheme != "https") ||
            trackingBase.UserInfo.Length != 0 || trackingBase.Query.Length != 0 || trackingBase.Fragment.Length != 0)
            throw new NexoRutaApiException(mensaje, response.StatusCode);

        // ponytail: adapt the current API link locally; generation and expiry stay backend-owned.
        return new Uri(trackingBase.AbsoluteUri.TrimEnd('/') + "/seguimiento/" + seguimientoToken);
    }

    private sealed record EnlaceSeguimiento(string Estado, string? Url, DateTimeOffset? ExpiraEnUtc);

    public async Task<IReadOnlyList<EnvioResponse>> ListarEnviosAsync(
        string token, CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Get, "api/envios"), token);
        return await ApiHttp.SendAsync<EnvioResponse[]>(httpClient, request, cancellationToken);
    }

    public async Task<EnvioCreadoResponse> CrearEnvioAsync(
        CrearEnvioRequest envio,
        string token,
        CancellationToken cancellationToken = default)
    {
        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Post, "api/envios")
        {
            Content = JsonContent.Create(envio)
        }, token);
        return await ApiHttp.SendAsync<EnvioCreadoResponse>(httpClient, request, cancellationToken);
    }

    public async Task<ImportacionEnviosApiResult> ImportarAsync(
        string fileName,
        long fileSize,
        Guid operatorId,
        Func<CancellationToken, ValueTask<Stream>> openFileStream,
        string token,
        Func<Task>? onUploadCompleted = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);
        ArgumentOutOfRangeException.ThrowIfNegative(fileSize);
        if (fileSize > MaximumImportFileBytes)
            throw new ArgumentOutOfRangeException(nameof(fileSize), "El archivo supera el límite de 5 MiB.");
        ArgumentNullException.ThrowIfNull(openFileStream);
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Seleccioná un operador para el archivo.", nameof(operatorId));

        await using var fileStream = await openFileStream(cancellationToken);
        if (!fileStream.CanRead)
            throw new ArgumentException("El archivo seleccionado no se puede leer.", nameof(openFileStream));

        using var multipart = new MultipartFormDataContent();
        using var fileContent = new UploadStreamContent(fileStream, onUploadCompleted);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv") { CharSet = "utf-8" };
        var uploadName = Path.GetFileName(fileName);
        multipart.Add(fileContent, "archivo", string.IsNullOrWhiteSpace(uploadName) ? "envios.csv" : uploadName);
        multipart.Add(new StringContent(operatorId.ToString("D"), Encoding.UTF8), "operadorId");

        using var request = ApiHttp.WithBearerToken(new HttpRequestMessage(HttpMethod.Post, "api/envios/importaciones")
        {
            Content = multipart
        }, token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict or HttpStatusCode.UnprocessableEntity)
        {
            try
            {
                var result = await response.Content.ReadFromJsonAsync<ImportacionEnviosResponse>(cancellationToken);
                if (result is not null && IsValidImportResponse(result, response.StatusCode))
                    return new ImportacionEnviosApiResult(response.StatusCode, result);
            }
            catch (JsonException)
            {
                // The response body is not the import-result contract.
            }

            throw new NexoRutaApiException("La API devolvió una respuesta de importación con un formato inesperado.", response.StatusCode);
        }

        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge)
        {
            try
            {
                var error = await response.Content.ReadFromJsonAsync<ErrorImportacionEnviosResponse>(cancellationToken);
                if (!string.IsNullOrWhiteSpace(error?.Mensaje))
                    throw new NexoRutaApiException(error.Mensaje, response.StatusCode);
            }
            catch (JsonException)
            {
                // Fall through to the shared safe error formatter for unexpected bodies.
            }
        }

        throw new NexoRutaApiException(await ApiHttp.ReadErrorAsync(response, cancellationToken), response.StatusCode);
    }

    private static bool IsValidImportResponse(ImportacionEnviosResponse result, HttpStatusCode statusCode)
    {
        if (result.Estado != (statusCode == HttpStatusCode.OK ? "Completada" : "RechazadaAtomica")
            || result.Resumen is null
            || result.Filas is null)
            return false;

        return result.Filas.All(row => row is not null
            && row.Registro > 0
            && (row.Estado is "Creado" or "YaExistente" or "DuplicadoDe" or "Conflicto" or "Invalido" or "NoProcesado")
            && row.Errores is not null
            && row.Errores.All(error => error is not null && !string.IsNullOrWhiteSpace(error.Codigo)));
    }

    private sealed class UploadStreamContent(Stream stream, Func<Task>? onUploadCompleted) : StreamContent(stream)
    {
        private int notified;

        protected override async Task SerializeToStreamAsync(
            Stream target, System.Net.TransportContext? context, CancellationToken cancellationToken)
        {
            await base.SerializeToStreamAsync(target, context, cancellationToken);
            if (Interlocked.Exchange(ref notified, 1) == 0 && onUploadCompleted is not null)
                await onUploadCompleted();
        }
    }
}
