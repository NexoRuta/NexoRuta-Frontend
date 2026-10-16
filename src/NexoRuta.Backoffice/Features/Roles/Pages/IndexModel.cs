using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Roles.Contracts.Requests;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Roles.Pages;

public sealed class IndexModel(RolesApiClient rolesClient) : PageModel
{
    public IReadOnlyList<RolResponse> Roles { get; private set; } = [];
    public IReadOnlyList<PermisoResponse> Permisos { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public string? NombreIntentado { get; private set; }
    public Guid? RolEditadoId { get; private set; }
    public IReadOnlyList<string>? PermisosIntentados { get; private set; }

    [BindProperty, Required, StringLength(80)]
    public string Codigo { get; set; } = "";

    [BindProperty, Required, StringLength(160)]
    public string Nombre { get; set; } = "";

    public async Task<IActionResult> OnGetAsync() => await CargarAsync();

    public async Task<IActionResult> OnPostCrearAsync()
    {
        if (!ModelState.IsValid)
        {
            Error = "El código y el nombre son obligatorios y deben respetar los límites indicados.";
            await CargarAsync();
            return Page();
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
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de roles."; }

        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
    }

    public async Task<IActionResult> OnPostRenombrarAsync(Guid id, string nombre)
    {
        try
        {
            await rolesClient.RenombrarAsync(id, nombre.Trim(), Token(), HttpContext.RequestAborted);
            Confirmacion = "Rol renombrado.";
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
            RolEditadoId = id;
            NombreIntentado = nombre;
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de roles."; }

        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
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
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de roles."; }

        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
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
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = MensajeError(exception);
            if (exception.StatusCode == System.Net.HttpStatusCode.BadRequest)
            {
                RolEditadoId = id;
                PermisosIntentados = permisos ?? [];
            }
        }
        catch (HttpRequestException) { Error = "No se pudo conectar con la API de roles."; }

        var loaded = await CargarAsync();
        return loaded is PageResult ? Page() : loaded;
    }

    private async Task<IActionResult> CargarAsync()
    {
        try
        {
            var token = Token();
            Roles = await rolesClient.ListarAsync(token, HttpContext.RequestAborted);
            Permisos = await rolesClient.ListarPermisosAsync(token, HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error ??= exception.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "No tenés permiso para administrar roles y permisos."
                : exception.Message;
        }
        catch (HttpRequestException) { Error ??= "No se pudo cargar la información de roles."; }
        return Page();
    }

    private string Token() => SesionUsuario.ObtenerToken(User);

    private static string MensajeError(NexoRutaApiException exception) => exception.StatusCode switch
    {
        System.Net.HttpStatusCode.BadRequest => exception.Message,
        System.Net.HttpStatusCode.Forbidden => "No tenés permiso para realizar esta operación.",
        System.Net.HttpStatusCode.Conflict => $"Conflicto: {exception.Message} Recargá el estado actual antes de volver a intentar.",
        _ => exception.Message
    };
}
