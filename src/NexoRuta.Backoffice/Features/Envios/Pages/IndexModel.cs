using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Envios.Pages;

public class IndexModel(EnviosApiClient apiClient, UsuariosApiClient usuariosClient) : PageModel
{
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public UsuarioActualResponse? UsuarioActual { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            UsuarioActual = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            Envios = await apiClient.ListarEnviosAsync(token, HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = exception.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "No tenés permiso para consultar los envíos."
                : exception.Message;
        }
        catch (HttpRequestException)
        {
            Error = "No se pudo consultar el servicio de envíos.";
        }

        return Page();
    }
}
