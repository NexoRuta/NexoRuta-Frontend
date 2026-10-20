using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;
using System.ComponentModel.DataAnnotations;

namespace NexoRuta.Backoffice.Features.Envios.Pages;

public class IndexModel(EnviosApiClient apiClient, UsuariosApiClient usuariosClient) : PageModel
{
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public UsuarioActualResponse? UsuarioActual { get; private set; }
    public string? Error { get; private set; }
    public bool PuedeCancelar { get; private set; }
    [BindProperty] public Guid EnvioId { get; set; }
    [BindProperty, Range(0, long.MaxValue)] public long Version { get; set; }
    [BindProperty, Required, StringLength(500)] public string Motivo { get; set; } = "";
    [TempData] public string? Confirmacion { get; set; }

    public async Task<IActionResult> OnPostCancelarAsync()
    {
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            var actor = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            if (actor.RolCodigo != "administrador_operador"
                || !(await usuariosClient.ObtenerPermisosActualesAsync(token, HttpContext.RequestAborted)).Contains("envios.gestionar")) return Forbid();
            if (!ModelState.IsValid || EnvioId == Guid.Empty || string.IsNullOrWhiteSpace(Motivo))
                Error = "Indicá el envío y el motivo de cancelación.";
            else
            {
                await apiClient.CancelarAsync(EnvioId, Version, Motivo.Trim(), token, HttpContext.RequestAborted);
                Confirmacion = "Cancelación registrada. Consultá el estado actualizado; los bultos no entregados deben volver al depósito.";
                return RedirectToPage("/Index");
            }
        }
        catch (NexoRutaApiException ex)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, ex);
            if (expired is not null) return expired;
            Error = ex.StatusCode == System.Net.HttpStatusCode.Forbidden ? "No tenés permiso para cancelar este envío." : ex.Message;
        }
        catch (HttpRequestException) { Error = "No se pudo confirmar la cancelación. Consultá el estado antes de reintentar."; }
        return await OnGetAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            UsuarioActual = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            PuedeCancelar = UsuarioActual.RolCodigo == "administrador_operador"
                && (await usuariosClient.ObtenerPermisosActualesAsync(token, HttpContext.RequestAborted)).Contains("envios.gestionar");
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
