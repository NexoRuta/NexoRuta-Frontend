using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace NexoRuta.Commerce.Features.Sesion.Pages;

[AllowAnonymous]
public sealed class OrganizacionCreadaModel(IConfiguration configuration) : PageModel
{
    public bool Operador { get; private set; }
    public string? IngresoUrl { get; private set; }
    public IActionResult OnGet(string tipo)
    {
        if (tipo is not ("operador" or "comercio")) return NotFound();
        Operador = tipo == "operador";
        if (!Operador) IngresoUrl = "/ingresar";
        else if (Uri.TryCreate(configuration["Portales:BackofficeLoginUrl"], UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            IngresoUrl = uri.AbsoluteUri;
        return Page();
    }
}
