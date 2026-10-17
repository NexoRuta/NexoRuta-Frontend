using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Usuarios.Pages;

public sealed class IndexModel(UsuariosApiClient usuariosClient, RolesApiClient rolesClient) : PageModel
{
    public IReadOnlyList<UsuarioOrganizacionResponse> Usuarios { get; private set; } = [];
    public IReadOnlyList<RolResponse> Roles { get; private set; } = [];
    public Guid UsuarioActualId { get; private set; }
    public Guid? AccesoEditadoId { get; private set; }
    public Guid? RolIntentado { get; private set; }
    public Guid? EmailEditadoId { get; private set; }
    public string? EmailIntentado { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }

    [BindProperty, Required, EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [BindProperty, Required, MinLength(12)]
    public string Password { get; set; } = "";

    [BindProperty, Required]
    public Guid? RolId { get; set; }

    public async Task<IActionResult> OnGetAsync() => await CargarAsync();

    public async Task<IActionResult> OnPostCrearAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "Revisá el correo, la contraseña (12 caracteres como mínimo) y el rol.";
            LimpiarPassword();
            await CargarAsync();
            return Page();
        }

        try
        {
            await usuariosClient.CrearAsync(new CrearUsuarioRequest(Email.Trim(), Password, RolId!.Value), Token(), HttpContext.RequestAborted);
            Email = "";
            RolId = null;
            ModelState.Clear();
            Confirmacion = "Usuario y acceso organizacional creados.";
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
            LimpiarPassword();
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de usuarios."; LimpiarPassword(); }

        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
    }

    public async Task<IActionResult> OnPostEstadoAsync(Guid usuarioId, bool activo)
    {
        try
        {
            await usuariosClient.CambiarEstadoAsync(usuarioId, activo, Token(), HttpContext.RequestAborted);
            Confirmacion = activo ? "Acceso activado." : "Acceso desactivado; la identidad del usuario se conserva.";
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de usuarios."; }

        ModelState.Clear();
        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
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
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
            if (exception.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                AccesoEditadoId = accesoId;
                RolIntentado = rolId;
            }
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de roles."; }

        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
    }

    public async Task<IActionResult> OnPostEmailAsync(Guid usuarioId, string nuevoEmail, long version)
    {
        EmailEditadoId = usuarioId;
        EmailIntentado = nuevoEmail;
        var normalizado = nuevoEmail?.Trim();
        if (string.IsNullOrWhiteSpace(normalizado) || normalizado.Length > 254 ||
            !new EmailAddressAttribute().IsValid(normalizado) || version < 0)
            Error = "Ingresá un correo válido de hasta 254 caracteres.";
        else
        {
            try
            {
                await usuariosClient.CambiarEmailAsync(usuarioId,
                    new CambiarEmailUsuarioRequest(normalizado, version), Token(), HttpContext.RequestAborted);
                Confirmacion = "Correo actualizado. El usuario afectado deberá iniciar sesión nuevamente con el nuevo correo y su contraseña actual.";
                EmailEditadoId = null;
            }
            catch (NexoRutaApiException exception)
            {
                var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
                if (expired is not null) return expired;
                Error = MensajeError(exception);
                if (exception.StatusCode == System.Net.HttpStatusCode.Conflict) EmailEditadoId = null;
            }
            catch (HttpRequestException) { Error = "No se pudo conectar con la API de usuarios."; }
        }
        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
    }

    private async Task<IActionResult> CargarAsync()
    {
        try
        {
            var token = Token();
            var usuarioActual = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            UsuarioActualId = usuarioActual.UsuarioId;
            Usuarios = await usuariosClient.ListarAsync(token, HttpContext.RequestAborted);
            Roles = await rolesClient.ListarAsignablesAsync(token, HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error ??= exception.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "No tenés permiso para gestionar estos accesos."
                : exception.Message;
        }
        catch (HttpRequestException) { Error ??= "No se pudo cargar la información de usuarios."; }
        return Page();
    }

    private string Token() => SesionUsuario.ObtenerToken(User);

    private string MensajeError(NexoRutaApiException exception) => exception.StatusCode switch
    {
        System.Net.HttpStatusCode.BadRequest => exception.Message,
        System.Net.HttpStatusCode.Forbidden => "No tenés permiso para realizar esta operación.",
        System.Net.HttpStatusCode.NotFound => "El usuario no pertenece a esta organización o ya no existe.",
        System.Net.HttpStatusCode.Conflict => $"Conflicto: {exception.Message} Recargá el estado actual antes de volver a intentar.",
        _ => exception.Message
    };

    private void LimpiarPassword()
    {
        Password = "";
        ModelState.Remove(nameof(Password));
    }
}
