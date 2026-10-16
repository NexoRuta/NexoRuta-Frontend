using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;

namespace NexoRuta.Backoffice.Core.Security;

public static class ApiSession
{
    public static async Task<IActionResult?> RedirectIfExpiredAsync(PageModel page, NexoRutaApiException exception)
    {
        if (exception.StatusCode != System.Net.HttpStatusCode.Unauthorized) return null;
        await page.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return new RedirectResult("/ingresar?expirada=true");
    }
}
