using System.Net.Http.Json;
using System.Text.Json;
using NexoRuta.ApiClient.Contracts;
using NexoRuta.ApiClient.Excepciones;

namespace NexoRuta.ApiClient;

public sealed class EnviosApiClient(HttpClient httpClient)
{
    public async Task<IReadOnlyList<UsuarioActualResponse>> ListarAccesosAsync(
        string tipo, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/accesos?tipo={Uri.EscapeDataString(tipo)}");
        return await SendAsync<UsuarioActualResponse[]>(request, cancellationToken);
    }

    public async Task<IReadOnlyList<OperadorDisponibleResponse>> ListarOperadoresAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/operadores");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await SendAsync<OperadorDisponibleResponse[]>(request, cancellationToken);
    }

    public async Task<UsuarioActualResponse> ObtenerUsuarioActualAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/usuarios/actual");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await SendAsync<UsuarioActualResponse>(request, cancellationToken);
    }

    public async Task<IReadOnlyList<EnvioResponse>> ListarEnviosAsync(
        Guid accesoId, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/envios");
        request.Headers.Add("X-NexoRuta-Acceso", accesoId.ToString());
        return await SendAsync<EnvioResponse[]>(request, cancellationToken);
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
        return await SendAsync<EnvioCreadoResponse>(request, cancellationToken);
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
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
