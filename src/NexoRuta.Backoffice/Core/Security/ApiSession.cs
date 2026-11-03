using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;

namespace NexoRuta.Backoffice.Core.Security;

public static class ApiSession
{
    public static Task<IActionResult?> RedirectIfExpiredAsync(PageModel page, NexoRutaApiException exception)
    {
        if (exception.StatusCode != System.Net.HttpStatusCode.Unauthorized) return Task.FromResult<IActionResult?>(null);
        return Task.FromResult<IActionResult?>(new RedirectResult(NexoRuta.ApiClient.Core.Session.RutaSesion.Ingreso(page.Request.Path,true,NexoRuta.ApiClient.Core.Session.SesionUsuario.IngresoWeb(page.User))));
    }
}
