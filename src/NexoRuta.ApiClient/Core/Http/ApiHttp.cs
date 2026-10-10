using System.Net.Http.Json;
using System.Text.Json;
using NexoRuta.ApiClient.Core.Exceptions;

namespace NexoRuta.ApiClient.Core.Http;

internal static class ApiHttp
{
    internal static async Task<T> SendAsync<T>(HttpClient httpClient, HttpRequestMessage request, CancellationToken cancellationToken)
        where T : class
    {
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new NexoRutaApiException(await ReadErrorAsync(response, cancellationToken), response.StatusCode);

        try
        {
            return await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                ?? throw new NexoRutaApiException("La API devolvió una respuesta vacía.", response.StatusCode);
        }
        catch (JsonException)
        {
            throw new NexoRutaApiException("La API devolvió una respuesta con un formato inesperado.", response.StatusCode);
        }
    }

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var error = await response.Content.ReadFromJsonAsync<ApiError>(cancellationToken);
            if (error?.Errors is { Count: > 0 })
            {
                var messages = error.Errors.Values.SelectMany(x => x).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToArray();
                if (messages.Length > 0)
                    return string.Join(" ", messages);
            }

            foreach (var message in new[] { error?.Message, error?.Detail, error?.Title })
            {
                if (!string.IsNullOrWhiteSpace(message))
                    return message;
            }
        }
        catch (JsonException)
        {
            // A proxy or server can return HTML/plain text instead of the API error contract.
        }

        return $"No se pudo completar la operación (HTTP {(int)response.StatusCode}).";
    }

    private sealed record ApiError(string? Message, string? Detail, string? Title, Dictionary<string, string[]>? Errors);
}
