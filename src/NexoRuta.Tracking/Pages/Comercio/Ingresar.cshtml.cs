using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace NexoRuta.Tracking.Pages.Comercio;
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class IngresarModel(IConfiguration config) : PageModel
{
    [BindProperty] public string Clave {get;set;}="";
    public string? Error {get;private set;}
    public async Task<IActionResult> OnPostAsync()
    {
        if(Clave.Length>512 || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(Clave)),SHA256.HashData(Encoding.UTF8.GetBytes(config["CommerceDemo:AdminPassword"]!))))
        {Error="La clave de ensayo es incorrecta.";await Task.Delay(500,HttpContext.RequestAborted);return Page();}
        await HttpContext.SignInAsync("CommerceDemo",new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name,"Responsable del ensayo")],"CommerceDemo")),new AuthenticationProperties{IsPersistent=false,ExpiresUtc=DateTimeOffset.UtcNow.AddHours(4)});
        return LocalRedirect("/comercio");
    }
}
