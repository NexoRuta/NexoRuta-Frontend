using System.ComponentModel.DataAnnotations;
using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Reparto.Clients;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.Backoffice.Core.Security;
namespace NexoRuta.Backoffice.Pages.Deposito;
[Authorize(Policy="envios.gestionar")]
[ResponseCache(NoStore=true, Location=ResponseCacheLocation.None)]
public sealed class RecepcionModel(RutasApiClient api) : PageModel
{
    [BindProperty(SupportsGet=true),StringLength(80)] public string? Codigo { get; set; }
    [BindProperty] public Guid BultoId { get; set; }
    [BindProperty] public CotejoFormulario Cotejo { get; set; } = new();
    public ConsultaRecepcionResponse? Recepcion { get; private set; }
    public BultoDepositoResponse? Bulto => Recepcion?.Bultos.Single(b=>b.Id==Recepcion.BultoSeleccionadoId);
    public string? Error { get; private set; }
    [TempData] public string? Confirmacion { get; set; }
    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrWhiteSpace(Codigo)) return Page();
        var salida = await Cargar();
        if (Bulto is {} b)
        {
            BultoId=b.Id;
            Cotejo=new() { ComandoId=Guid.NewGuid(),VersionEnvio=Recepcion!.VersionEnvio,VersionBulto=b.Version,
                PesoObservado=b.Declarado.PesoGramos,LargoObservado=b.Declarado.LargoCentimetros,
                AnchoObservado=b.Declarado.AnchoCentimetros,AltoObservado=b.Declarado.AltoCentimetros };
        }
        return salida;
    }
    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            if (!ModelState.IsValid || BultoId==Guid.Empty) throw new ArgumentException("Revisá los valores del cotejo.");
            var r=await api.CotejarBultoAsync(BultoId,new(Cotejo.ComandoId,Cotejo.VersionEnvio,Cotejo.VersionBulto,Cotejo.Conforme,
                Cotejo.PesoObservado,Cotejo.LargoObservado,Cotejo.AnchoObservado,Cotejo.AltoObservado,Cotejo.Observaciones),SesionUsuario.ObtenerToken(User),HttpContext.RequestAborted);
            Confirmacion=r.Repetida ? "Este cotejo ya estaba registrado. No se duplicó la recepción."
                : r.Recepcion.EstadoEnvio=="EnDeposito" ? "Recepción completa. Todos los bultos tienen cotejo conforme."
                : "Bulto recibido. El envío conserva pendientes de recepción o cotejo.";
            return RedirectToPage(new { codigo=BultoId.ToString("D") });
        }
        catch(NexoRutaApiException e)
        { var redirect=await ApiSession.RedirectIfExpiredAsync(this,e);if(redirect is not null)return redirect;Error=e.Message; }
        catch(ArgumentException e) { Error=e.Message; }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
        { Error="No pudimos confirmar el resultado. Reintentá este mismo cotejo; conservamos el comando para evitar duplicados."; }
        return await Cargar();
    }
    private async Task<IActionResult> Cargar()
    {
        try { Recepcion=await api.ConsultarRecepcionAsync(Codigo??"",SesionUsuario.ObtenerToken(User),HttpContext.RequestAborted); }
        catch(NexoRutaApiException e)
        {
            var redirect=await ApiSession.RedirectIfExpiredAsync(this,e);if(redirect is not null)return redirect;
            Error??=e.StatusCode==HttpStatusCode.NotFound ? "No encontramos ese bulto en este operador. Revisá el código de la etiqueta." : e.Message;
        }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException)
        { Error??="No pudimos consultar el bulto. Conservamos el código; volvé a buscar."; }
        return Page();
    }
    public static string Estado(string valor) => valor switch
    {
        "EnDeposito" => "En depósito", "AsignadoRuta" => "Asignado a ruta", "EnTransito" => "En tránsito",
        "EnDevolucion" => "En devolución", "NoEntregado" => "No entregado",
        "EntregaPendienteValidacion" => "Entrega pendiente de validación", "EntregaSinValidacion" => "Entrega sin validación", _ => valor
    };
    public static string Custodia(string valor) => valor switch
    {
        "SinRecepcion" => "Sin recepción", "PendienteDeposito" => "Pendiente de depósito", "Deposito" => "Depósito", _ => valor
    };
    public sealed class CotejoFormulario
    {
        public Guid ComandoId { get; set; }
        [Range(0,long.MaxValue)] public long VersionEnvio { get; set; }
        [Range(0,long.MaxValue)] public long VersionBulto { get; set; }
        public bool Conforme { get; set; }=true;
        public decimal? PesoObservado { get; set; } public decimal? LargoObservado { get; set; }
        public decimal? AnchoObservado { get; set; } public decimal? AltoObservado { get; set; }
        [StringLength(500)] public string? Observaciones { get; set; }
    }
}
