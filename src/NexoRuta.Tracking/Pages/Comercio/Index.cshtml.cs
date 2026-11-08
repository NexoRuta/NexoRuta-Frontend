using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Tracking.CommerceDemo;
namespace NexoRuta.Tracking.Pages.Comercio;
[Authorize(AuthenticationSchemes="CommerceDemo")]
[ResponseCache(NoStore=true,Location=ResponseCacheLocation.None)]
public sealed class IndexModel(ReceiverStore store) : PageModel
{
    [BindProperty(SupportsGet=true)] public int Pagina {get;set;}=1;
    public ReceiverInbox Inbox {get;private set;}=null!;
    public void OnGet() => Inbox=store.Inbox(Pagina);
    public IActionResult OnPost(string modo) {if(modo is not ("Disponible" or "FallaSimulada" or "RespuestaPerdida")) return BadRequest();store.Mode(modo);return RedirectToPage(new{Pagina=1});}
}
