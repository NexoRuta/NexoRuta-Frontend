using System.Net.Http.Headers;
using System.Net.Http.Json;
using NexoRuta.ApiClient.Core.Http;
using NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Requests;
using NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Responses;

namespace NexoRuta.ApiClient.Features.IdentidadVisual.Clients;

public sealed class IdentidadVisualApiClient(HttpClient http)
{
    private const string Path = "api/configuracion/identidad-visual";

    public Task<IdentidadVisualAdministracionResponse> ObtenerAsync(string token, CancellationToken ct = default)
        => ApiHttp.SendAsync<IdentidadVisualAdministracionResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, Path), token), ct);

    public Task<IdentidadVisualHistorialResponse> HistorialAsync(string token,
        int? antesDeVersion = null, CancellationToken ct = default)
    {
        var cursor = antesDeVersion is { } version ? $"&antesDeVersion={version}" : "";
        return ApiHttp.SendAsync<IdentidadVisualHistorialResponse>(http,
            ApiHttp.WithBearerToken(new(HttpMethod.Get, $"{Path}/versiones?limite=10{cursor}"), token), ct);
    }

    public async Task<IdentidadVisualAdministracionResponse> GuardarAsync(
        ActualizarIdentidadVisualRequest request, string token, CancellationToken ct = default)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(request.VersionEsperada.ToString(System.Globalization.CultureInfo.InvariantCulture)), "VersionEsperada");
        form.Add(new StringContent(request.NombreComercial ?? ""), "NombreComercial");
        form.Add(new StringContent(request.CorreoPublico ?? ""), "CorreoPublico");
        form.Add(new StringContent(request.TelefonoPublico ?? ""), "TelefonoPublico");
        form.Add(new StringContent(request.ColorPrimario ?? ""), "ColorPrimario");
        form.Add(new StringContent(request.ColorSecundario ?? ""), "ColorSecundario");
        form.Add(new StringContent(request.QuitarLogo ? "true" : "false"), "QuitarLogo");
        if (request.LogoContenido is { Length: > 0 } logo)
        {
            var file = new ByteArrayContent(logo);
            file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(file, "Logo", "logo");
        }
        using var message = ApiHttp.WithBearerToken(new(HttpMethod.Put, Path) { Content = form }, token);
        return await ApiHttp.SendAsync<IdentidadVisualAdministracionResponse>(http, message, ct);
    }
}
