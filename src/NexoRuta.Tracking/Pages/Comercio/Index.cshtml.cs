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
    public static string EventLabel(string type)=>type switch{"EnvioCreado"=>"Envío creado","OperacionActualizada"=>"Cambio de estado","BultoActualizado"=>"Resultado de bulto","NuevaVisita"=>"Nueva visita","RetiroSolicitado"=>"Retiro solicitado","RetiroRetirado"=>"Retirado en domicilio","RetiroEntregadoComercio"=>"Retiro entregado al comercio",_=>type};
    public static string? ParcelSummary(string payload)
    {
        using var json=System.Text.Json.JsonDocument.Parse(payload);
        if(!json.RootElement.TryGetProperty("bulto",out var b) || b.ValueKind!=System.Text.Json.JsonValueKind.Object)return null;
        return $"{b.GetProperty("codigo").GetString()} · {b.GetProperty("resultado").GetString()} · Custodia: {b.GetProperty("custodia").GetString()}";
    }
    public void OnGet() => Inbox=store.Inbox(Pagina);
    public IActionResult OnPost(string modo) {if(modo is not ("Disponible" or "FallaSimulada" or "RespuestaPerdida")) return BadRequest();store.Mode(modo);return RedirectToPage(new{Pagina=1});}
}
