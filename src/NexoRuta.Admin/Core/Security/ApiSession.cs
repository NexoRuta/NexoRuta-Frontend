using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;

namespace NexoRuta.Admin.Core.Security;

public static class ApiSession
{
    public static async Task<IActionResult?> RedirectAsync(PageModel page, NexoRutaApiException error)
    {
        if (error.StatusCode == HttpStatusCode.Unauthorized)
        {
            await page.HttpContext.SignOutAsync();
            return new RedirectResult("/ingresar?expirada=true");
        }
        return error.StatusCode == HttpStatusCode.Forbidden ? new RedirectResult("/acceso-denegado") : null;
    }
}
