using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NexoRuta.Admin.Features.Sesion.Pages;

// Logout stays available even after the platform permission is revoked or its service is down.
public sealed class SalirModel : PageModel
{
    public IActionResult OnGet() => LocalRedirect("/");
    public async Task<IActionResult> OnPostAsync()
    {
        await HttpContext.SignOutAsync();
        return LocalRedirect("/ingresar");
    }
}
