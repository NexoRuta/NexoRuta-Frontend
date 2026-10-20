using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Reparto.Clients;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Reparto.Pages;

public sealed class RepartidoresModel(RepartidoresApiClient perfiles, UsuariosApiClient usuarios) : PageModel
{
    public IReadOnlyList<RepartidorResponse> Repartidores { get; private set; } = [];
    public IReadOnlyList<UsuarioOrganizacionResponse> Disponibles { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await CargarAsync();

    public async Task<IActionResult> OnPostGuardarAsync(Guid accesoId, string nombre, bool activo, long? version)
    {
        if (!ModelState.IsValid || accesoId == Guid.Empty || string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 120)
            Error = "Seleccioná un usuario e indicá un nombre de hasta 120 caracteres.";
        else
        {
            try
            {
                await perfiles.GuardarAsync(accesoId, nombre, activo, version, Token(), HttpContext.RequestAborted);
                Confirmacion = "Perfil de repartidor guardado. El rol debe incluir el permiso de ejecutar la hoja de ruta propia.";
            }
            catch (NexoRutaApiException ex)
            {
                var expirada = await ApiSession.RedirectIfExpiredAsync(this, ex);
                if (expirada is not null) return expirada;
                Error = ex.Message;
            }
            catch (HttpRequestException) { Error = "No se pudo conectar con el servicio. Intentá nuevamente."; }
        }
        return await CargarAsync();
    }

    private async Task<IActionResult> CargarAsync()
    {
        try
        {
            Repartidores = await perfiles.ListarAsync(Token(), HttpContext.RequestAborted);
            var ids = Repartidores.Select(x => x.AccesoId).ToHashSet();
            Disponibles = (await usuarios.ListarAsync(Token(), HttpContext.RequestAborted))
                .Where(x => x.Activo && x.RolId is not null && !ids.Contains(x.AccesoId)).ToArray();
        }
        catch (NexoRutaApiException ex)
        {
            var expirada = await ApiSession.RedirectIfExpiredAsync(this, ex);
            if (expirada is not null) return expirada;
            Error = ex.Message;
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con el servicio. Intentá nuevamente."; }
        return Page();
    }

    private string Token() => SesionUsuario.ObtenerToken(User);
}
