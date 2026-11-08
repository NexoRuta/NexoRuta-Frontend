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
    public static string Fecha(string? value)=>DateTimeOffset.TryParse(value,System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out var date)?date.ToUniversalTime().ToString("dd/MM/yyyy HH:mm:ss"):"Sin fecha en el contrato";
    public static string EventLabel(string type)=>type switch{"EnvioCreado"=>"Envío creado","OperacionActualizada"=>"Cambio de estado","BultoActualizado"=>"Resultado de bulto","NuevaVisita"=>"Nueva visita","RetiroSolicitado"=>"Retiro solicitado","RetiroRetirado"=>"Retirado en domicilio","RetiroEntregadoComercio"=>"Retiro entregado al comercio",_=>type};
    public static string? ParcelSummary(string payload)
    {
        using var json=System.Text.Json.JsonDocument.Parse(payload);
        if(!json.RootElement.TryGetProperty("bulto",out var b) || b.ValueKind!=System.Text.Json.JsonValueKind.Object)return null;
        return $"{b.GetProperty("codigo").GetString()} · {NexoRuta.ApiClient.Features.Gestion.EtiquetasGestion.Texto(b.GetProperty("resultado").GetString()??"Sin dato")} · Custodia: {NexoRuta.ApiClient.Features.Gestion.EtiquetasGestion.Texto(b.GetProperty("custodia").GetString()??"Sin dato")}";
    }
    public void OnGet() => Inbox=store.Inbox(Pagina);
    public IActionResult OnPost(string modo) {if(modo is not ("Disponible" or "FallaSimulada" or "RespuestaPerdida")) return BadRequest();store.Mode(modo);return RedirectToPage(new{Pagina=1});}
}
