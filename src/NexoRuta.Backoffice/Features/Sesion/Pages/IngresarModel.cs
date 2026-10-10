using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Features.Accesos.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

namespace NexoRuta.Backoffice.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class IngresarModel(AccesosApiClient accesosClient, UsuariosApiClient usuariosClient) : PageModel
{
    public IReadOnlyList<UsuarioActualResponse> Usuarios { get; private set; } = [];
    public string? Error { get; private set; }

    [BindProperty, Required(ErrorMessage = "Seleccioná un usuario.")]
    public Guid? AccesoId { get; set; }

    public Task OnGetAsync() => CargarUsuariosAsync();

    public async Task<IActionResult> OnPostAsync()
    {
        if (ModelState.IsValid && AccesoId is { } accesoId && accesoId != Guid.Empty)
        {
            try
            {
                var usuario = await usuariosClient.ObtenerUsuarioActualAsync(accesoId, HttpContext.RequestAborted);
                if (usuario.Tipo == SesionUsuario.Operador)
                {
                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                        SesionUsuario.CrearPrincipal(usuario, CookieAuthenticationDefaults.AuthenticationScheme));
                    return LocalRedirect("/");
                }
                Error = "El usuario seleccionado no puede ingresar a esta aplicación.";
            }
            catch (NexoRutaApiException exception) { Error = exception.Message; }
            catch (HttpRequestException) { Error = "No se pudo conectar con el servicio de usuarios."; }
        }
        else
        {
            Error = "Seleccioná un usuario.";
        }

        await CargarUsuariosAsync();
        return Page();
    }

    private async Task CargarUsuariosAsync()
    {
        try { Usuarios = await accesosClient.ListarAccesosAsync(SesionUsuario.Operador, HttpContext.RequestAborted); }
        catch (NexoRutaApiException exception) { Error = exception.Message; }
        catch (HttpRequestException) { Error = "No se pudo consultar la lista de usuarios."; }
    }
}
