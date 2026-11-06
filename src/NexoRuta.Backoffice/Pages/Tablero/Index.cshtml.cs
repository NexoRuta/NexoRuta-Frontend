using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
namespace NexoRuta.Backoffice.Pages.Tablero;

[Authorize(Policy = "envios.consultar")]
public sealed class IndexModel : PageModel
{
    public void OnGet() { }
}
