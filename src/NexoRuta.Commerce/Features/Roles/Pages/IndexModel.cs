using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Roles.Contracts.Requests;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;
using NexoRuta.Commerce.Core.Security;

namespace NexoRuta.Commerce.Features.Roles.Pages;

[Authorize(Policy = ApiPermissionAuthorizationHandler.ConfigManagePolicy)]
public sealed class IndexModel(RolesApiClient rolesClient) : PageModel
{
    public IReadOnlyList<RolResponse> Roles { get; private set; } = [];
    public IReadOnlyList<PermisoResponse> Permisos { get; private set; } = [];
    public Guid? RolEditadoId { get; private set; }
    public string? NombreIntentado { get; private set; }
    public IReadOnlyList<string>? PermisosIntentados { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public bool HayConflicto { get; private set; }

    [BindProperty, Required, StringLength(80)]
    public string Codigo { get; set; } = "";

    [BindProperty, Required, StringLength(160)]
    public string Nombre { get; set; } = "";

    public async Task<IActionResult> OnGetAsync()
        => await CargarAsync() ?? Page();

    public async Task<IActionResult> OnPostCrearAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Revisá el código y el nombre del rol.";
            return await VolverACargarAsync(preservarAlta: true);
        }

        try
        {
            await rolesClient.CrearAsync(new CrearRolRequest(Codigo.Trim(), Nombre.Trim()), Token(), HttpContext.RequestAborted);
            Codigo = "";
            Nombre = "";
            ModelState.Clear();
            Confirmacion = "Rol creado.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (InvalidOperationException)
        {
            return await CerrarSesionExpiradaAsync();
        }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de roles. Intentá nuevamente más tarde.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return await VolverACargarAsync(preservarAlta: true);
    }

    public async Task<IActionResult> OnPostRenombrarAsync(Guid id, string nombre)
    {
        try
        {
            await rolesClient.RenombrarAsync(id, nombre, Token(), HttpContext.RequestAborted);
            Confirmacion = "Rol renombrado.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
            if (exception.StatusCode == HttpStatusCode.BadRequest)
            {
                RolEditadoId = id;
                NombreIntentado = nombre;
            }
        }
        catch (InvalidOperationException)
        {
            return await CerrarSesionExpiradaAsync();
        }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de roles. Intentá nuevamente más tarde.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return await VolverACargarAsync();
    }

    public async Task<IActionResult> OnPostPermisosAsync(Guid id, string[]? permisos)
    {
        try
        {
            await rolesClient.ActualizarPermisosAsync(id, permisos ?? [], Token(), HttpContext.RequestAborted);
            Confirmacion = "Permisos del rol actualizados.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
            if (exception.StatusCode == HttpStatusCode.BadRequest)
            {
                RolEditadoId = id;
                PermisosIntentados = permisos ?? [];
            }
        }
        catch (InvalidOperationException)
        {
            return await CerrarSesionExpiradaAsync();
        }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de roles. Intentá nuevamente más tarde.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return await VolverACargarAsync();
    }

    public async Task<IActionResult> OnPostEliminarAsync(Guid id)
    {
        try
        {
            await rolesClient.EliminarAsync(id, Token(), HttpContext.RequestAborted);
            Confirmacion = "Rol eliminado.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (InvalidOperationException)
        {
            return await CerrarSesionExpiradaAsync();
        }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de roles. Intentá nuevamente más tarde.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return await VolverACargarAsync();
    }

    private async Task<IActionResult> VolverACargarAsync(bool preservarAlta = false)
    {
        if (!preservarAlta)
        {
            ModelState.Remove(nameof(Codigo));
            ModelState.Remove(nameof(Nombre));
            Codigo = Nombre = "";
        }
        return await CargarAsync() ?? Page();
    }

    private async Task<IActionResult?> CargarAsync()
    {
        Roles = [];
        Permisos = [];
        try
        {
            var token = Token();
            Roles = await rolesClient.ListarAsync(token, HttpContext.RequestAborted);
            Permisos = await rolesClient.ListarPermisosAsync(token, HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            return await ManejarApiExceptionAsync(exception);
        }
        catch (InvalidOperationException)
        {
            return await CerrarSesionExpiradaAsync();
        }
        catch (HttpRequestException)
        {
            Error ??= "No pudimos cargar los roles y permisos porque la API no está disponible.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error ??= "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return null;
    }

    private async Task<IActionResult?> ManejarApiExceptionAsync(NexoRutaApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.Unauthorized)
            return await CerrarSesionExpiradaAsync();

        Error = exception.StatusCode switch
        {
            HttpStatusCode.BadRequest => exception.Message,
            HttpStatusCode.Forbidden => "No tenés permiso para realizar esta operación.",
            HttpStatusCode.Conflict => $"Conflicto: {exception.Message} Recargá el estado actual antes de volver a intentar.",
            _ => "No pudimos completar la operación porque la API no está disponible. Intentá más tarde."
        };
        HayConflicto = exception.StatusCode == HttpStatusCode.Conflict;
        return null;
    }

    private async Task<IActionResult> CerrarSesionExpiradaAsync()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Redirect("/ingresar?expirada=true");
    }

    private string Token() => SesionUsuario.ObtenerToken(User);
}
