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
    public bool CargaCompleta { get; private set; }
    public bool FormularioEnviado { get; private set; }
    public Guid AccesoIdEnviado { get; private set; }
    public string NombreEnviado { get; private set; } = "";
    public bool ActivoEnviado { get; private set; }
    public long? VersionEnviada { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }

    public async Task<IActionResult> OnGetAsync() => await CargarAsync();
    public Task<IActionResult> OnGetReintentarAsync() => CargarAsync();

    public async Task<IActionResult> OnPostGuardarAsync(Guid accesoId, string nombre, bool activo, long? version)
    {
        FormularioEnviado = true;
        AccesoIdEnviado = accesoId;
        NombreEnviado = nombre;
        ActivoEnviado = activo;
        VersionEnviada = version;
        if (!ModelState.IsValid || accesoId == Guid.Empty || string.IsNullOrWhiteSpace(nombre) || nombre.Trim().Length > 120)
            Error = "Seleccioná un usuario e indicá un nombre de hasta 120 caracteres.";
        else
        {
            try
            {
                await perfiles.GuardarAsync(accesoId, nombre, activo, version, Token(), HttpContext.RequestAborted);
                Confirmacion = "Perfil de repartidor guardado. El rol debe incluir el permiso de ejecutar la hoja de ruta propia.";
                FormularioEnviado = false;
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
        CargaCompleta = false;
        try
        {
            var repartidores = await perfiles.ListarAsync(Token(), HttpContext.RequestAborted);
            var ids = repartidores.Select(x => x.AccesoId).ToHashSet();
            var disponibles = (await usuarios.ListarAsync(Token(), HttpContext.RequestAborted))
                .Where(x => x.Activo && x.RolId is not null && !ids.Contains(x.AccesoId)).ToArray();
            Repartidores = repartidores;
            Disponibles = disponibles;
            CargaCompleta = true;
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
