using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.Commerce.Core.Security;

namespace NexoRuta.Commerce.Features.Usuarios.Pages;

[Authorize(Policy = ApiPermissionAuthorizationHandler.UsersManagePolicy)]
public sealed class IndexModel(UsuariosApiClient usuariosClient, RolesApiClient rolesClient) : PageModel
{
    public IReadOnlyList<UsuarioOrganizacionResponse> Usuarios { get; private set; } = [];
    public IReadOnlyList<RolResponse> Roles { get; private set; } = [];
    public Guid UsuarioActualId { get; private set; }
    public Guid? AccesoEditadoId { get; private set; }
    public Guid? RolIntentado { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public bool CanManage { get; private set; }
    public bool HayConflicto { get; private set; }

    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [BindProperty, Required, MinLength(12)]
    public string Password { get; set; } = "";

    [BindProperty, Required]
    public Guid? RolId { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var redirect = await CargarAsync();
        return redirect ?? Page();
    }

    public async Task<IActionResult> OnPostCrearAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Revisá el correo, la contraseña (12 caracteres como mínimo) y el rol.";
            LimpiarPassword();
            return await VolverACargarAsync();
        }

        try
        {
            await usuariosClient.CrearAsync(
                new CrearUsuarioRequest(Email.Trim(), Password, RolId!.Value),
                Token(),
                HttpContext.RequestAborted);
            Email = "";
            Password = "";
            RolId = null;
            ModelState.Clear();
            Confirmacion = "Usuario creado con acceso al comercio.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
            LimpiarPassword();
        }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de usuarios. Intentá nuevamente más tarde.";
            LimpiarPassword();
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "La API tardó demasiado en responder. Intentá nuevamente.";
            LimpiarPassword();
        }

        return await VolverACargarAsync();
    }

    public async Task<IActionResult> OnPostEstadoAsync(Guid usuarioId, bool activo)
    {
        try
        {
            await usuariosClient.CambiarEstadoAsync(usuarioId, activo, Token(), HttpContext.RequestAborted);
            Confirmacion = activo
                ? "Acceso activado."
                : "Acceso desactivado; la identidad del usuario se conserva.";
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (HttpRequestException)
        {
            Error = "No pudimos comunicarnos con la API de usuarios. Intentá nuevamente más tarde.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error = "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return await VolverACargarAsync();
    }

    public async Task<IActionResult> OnPostRolAsync(Guid accesoId, Guid? rolId)
    {
        try
        {
            await rolesClient.AsignarAsync(accesoId, rolId, Token(), HttpContext.RequestAborted);
            Confirmacion = rolId is null ? "Rol retirado del acceso." : "Rol asignado al acceso.";
        }
        catch (NexoRutaApiException exception)
        {
            AccesoEditadoId = accesoId;
            RolIntentado = rolId;
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
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

    private async Task<IActionResult> VolverACargarAsync()
    {
        var redirect = await CargarAsync();
        return redirect ?? Page();
    }

    private async Task<IActionResult?> CargarAsync()
    {
        CanManage = false;
        Usuarios = [];
        Roles = [];
        try
        {
            var token = Token();
            var actual = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            var usuarios = await usuariosClient.ListarAsync(token, HttpContext.RequestAborted);
            var roles = await rolesClient.ListarAsignablesAsync(token, HttpContext.RequestAborted);
            UsuarioActualId = actual.UsuarioId;
            Usuarios = usuarios;
            Roles = roles;
            CanManage = true;
        }
        catch (NexoRutaApiException exception)
        {
            var redirect = await ManejarApiExceptionAsync(exception);
            if (redirect is not null) return redirect;
        }
        catch (InvalidOperationException)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/ingresar?expirada=true");
        }
        catch (HttpRequestException)
        {
            Error ??= "No pudimos cargar los usuarios del comercio porque la API no está disponible.";
        }
        catch (OperationCanceledException) when (!HttpContext.RequestAborted.IsCancellationRequested)
        {
            Error ??= "La API tardó demasiado en responder. Intentá nuevamente.";
        }

        return null;
    }

    private async Task<IActionResult?> ManejarApiExceptionAsync(NexoRutaApiException exception)
    {
        if (exception.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Redirect("/ingresar?expirada=true");
        }

        Error = exception.StatusCode switch
        {
            System.Net.HttpStatusCode.BadRequest => exception.Message,
            System.Net.HttpStatusCode.Forbidden => "No tenés permiso para realizar esta operación.",
            System.Net.HttpStatusCode.Conflict => $"Conflicto: {exception.Message} Recargá el estado actual antes de volver a intentar.",
            _ => "No pudimos completar la operación porque la API no está disponible. Intentá más tarde."
        };
        HayConflicto = exception.StatusCode == System.Net.HttpStatusCode.Conflict;
        return null;
    }

    private string Token() => SesionUsuario.ObtenerToken(User);

    private void LimpiarPassword()
    {
        Password = "";
        ModelState.Remove(nameof(Password));
    }
}
