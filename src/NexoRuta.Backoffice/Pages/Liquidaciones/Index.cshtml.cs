using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Authorization;
using NexoRuta.ApiClient.Features.Gestion;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Core.Exceptions;
namespace NexoRuta.Backoffice.Pages.Liquidaciones;
public sealed class IndexModel(GestionApiClient api,UsuariosApiClient usuarios,IAuthorizationService authorization):PageModel
{
    [BindProperty(SupportsGet=true)] public DateOnly Desde {get;set;}=PeriodoGestion.Hoy.AddDays(-30);
    [BindProperty(SupportsGet=true)] public DateOnly Hasta {get;set;}=PeriodoGestion.Hoy.AddDays(1);
    [BindProperty(SupportsGet=true)] public Guid? ComercioId {get;set;}
    [BindProperty(SupportsGet=true)] public Guid? DocumentoId {get;set;}
    [BindProperty(SupportsGet=true)] public int Pagina {get;set;}=1;
    [BindProperty] public Guid SolicitudId {get;set;}=Guid.NewGuid();
    [BindProperty] public Guid MovimientoId {get;set;}
    [BindProperty] public decimal ImporteUyu {get;set;}
    [BindProperty] public string? Motivo {get;set;}
    public CuentaGestion? Cuenta {get;private set;}
    public LiquidacionDetalle? Documento {get;private set;}
    public string? Error {get;private set;}
    public bool PuedeGestionar {get;private set;}
    private string Token=>SesionUsuario.ObtenerToken(User);
    private async Task<Guid> Operador()=> (await usuarios.ObtenerUsuarioActualAsync(Token,HttpContext.RequestAborted)).OperadorId!.Value;
    public async Task OnGetAsync()=>await Cargar();
    private async Task Cargar()
    {
        PuedeGestionar=(await authorization.AuthorizeAsync(User,"envios.gestionar")).Succeeded;
        if(!ModelState.IsValid){Error="Revisá las fechas y los identificadores.";return;}
        try {
            var op=await Operador();
            if(DocumentoId is {} id) Documento=await api.DocumentoAsync(op,id,Token,HttpContext.RequestAborted);
            if(ComercioId is {} comercio) Cuenta=await api.CuentaAsync(new(op,PeriodoGestion.Inicio(Desde),PeriodoGestion.Inicio(Hasta),comercio,Pagina:Pagina),Token,HttpContext.RequestAborted);
        }catch(HttpRequestException e){Error=Mensaje(e);}
    }
    private static string Mensaje(HttpRequestException e)=>e.StatusCode switch {
        System.Net.HttpStatusCode.Forbidden=>"No tenés permiso para esta acción.",
        System.Net.HttpStatusCode.NotFound=>"La liquidación no está disponible para tu cuenta.",
        System.Net.HttpStatusCode.Conflict=>"El documento cambió o sus conceptos ya fueron liquidados. Revisá el detalle; podés descartar el borrador y preparar otro.",
        System.Net.HttpStatusCode.BadRequest=>e is NexoRutaApiException ? e.Message : "Revisá el período y los conceptos liquidables.",
        _=>"El servicio no está disponible. Reintentá; la misma solicitud no duplicará efectos."};
    public Task<IActionResult> OnPostPrepararAsync()=>Ejecutar(async op=>{
        if(ComercioId is not {} comercio) throw new ArgumentException("Seleccioná el comercio.");
        Documento=await api.PrepararAsync(op,new(SolicitudId,comercio,PeriodoGestion.Inicio(Desde),PeriodoGestion.Inicio(Hasta)),Token,HttpContext.RequestAborted);DocumentoId=Documento.Documento.Id;
    });
    public Task<IActionResult> OnPostConfirmarAsync()=>Ejecutar(async op=>Documento=await api.TransicionAsync(op,DocumentoId??Guid.Empty,true,Token,HttpContext.RequestAborted));
    public Task<IActionResult> OnPostDescartarAsync()=>Ejecutar(async op=>Documento=await api.TransicionAsync(op,DocumentoId??Guid.Empty,false,Token,HttpContext.RequestAborted));
    public Task<IActionResult> OnPostCorregirAsync()=>Ejecutar(async op=>{await api.CorregirAsync(op,new(SolicitudId,MovimientoId,ImporteUyu,Motivo??""),Token,HttpContext.RequestAborted);});
    private async Task<IActionResult> Ejecutar(Func<Guid,Task> action)
    {
        if(!(await authorization.AuthorizeAsync(User,"envios.gestionar")).Succeeded)return Forbid();
        if(!ModelState.IsValid){await Cargar();return Page();}
        try{await action(await Operador());return RedirectToPage(new{Desde=Desde.ToString("yyyy-MM-dd"),Hasta=Hasta.ToString("yyyy-MM-dd"),ComercioId,DocumentoId,Pagina});}
        catch(HttpRequestException e){Error=Mensaje(e);}
        catch(ArgumentException e){Error=e.Message;}
        var error=Error;await Cargar();Error=error;return Page();
    }
}
