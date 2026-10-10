using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Backoffice.Features.Envios.Pages;

public class IndexModel(EnviosApiClient apiClient, UsuariosApiClient usuariosClient) : PageModel
{
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public UsuarioActualResponse? UsuarioActual { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        try
        {
            var accesoId = SesionUsuario.ObtenerAccesoId(User);
            UsuarioActual = await usuariosClient.ObtenerUsuarioActualAsync(accesoId, HttpContext.RequestAborted);
            Envios = await apiClient.ListarEnviosAsync(accesoId, HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            Error = exception.Message;
        }
        catch (HttpRequestException)
        {
            Error = "No se pudo consultar el servicio de envíos.";
        }
    }
}
