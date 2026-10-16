using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;
using NexoRuta.ApiClient.Features.Auth.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.Auth.Clients;

public sealed class AuthApiClient(HttpClient httpClient)
{
    public async Task<LoginResponse> LoginAsync(LoginRequest login, CancellationToken ct = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/auth/login", login, ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                ? "El correo o la contraseña no son válidos."
                : $"No se pudo completar el ingreso (HTTP {(int)response.StatusCode}).";
            throw new NexoRutaApiException(message, response.StatusCode);
        }

        return await response.Content.ReadFromJsonAsync<LoginResponse>(ct)
            ?? throw new NexoRutaApiException("La API devolvió una respuesta de ingreso vacía.", response.StatusCode);
    }
}
