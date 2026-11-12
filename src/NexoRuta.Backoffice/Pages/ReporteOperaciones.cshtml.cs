using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Features.Gestion;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Core.Exceptions;
namespace NexoRuta.Backoffice.Pages;
public sealed class ReporteOperacionesModel(GestionApiClient api,UsuariosApiClient usuarios):PageModel
{
    public Dictionary<string,OpcionesGestion> Opciones {get;}=[];
    public ReferenciasGestion Referencias {get;}=new(api);
    public async Task<IActionResult> OnGetOpcionesAsync(string tipo,string? buscar,int pagina=1)
    {
        try {var token=SesionUsuario.ObtenerToken(User);var actor=await usuarios.ObtenerUsuarioActualAsync(token,HttpContext.RequestAborted);
        return new JsonResult(await api.OpcionesAsync(actor.OperadorId!.Value,tipo,token,buscar??"",pagina,ct:HttpContext.RequestAborted));}
        catch(HttpRequestException){return new StatusCodeResult(503);}
    }
    private async Task CargarOpciones(Guid op,string token)
    {
        foreach(var (tipo,id) in new (string,Guid?)[]{("Comercio",ComercioId),("Zona",ZonaId),("Repartidor",RepartidorId)})
        {
            var options=await api.OpcionesAsync(op,tipo,token,ct:HttpContext.RequestAborted);
            if(id is {} selected && !options.Opciones.Any(o=>o.Id==selected)){
                var current=await api.OpcionesAsync(op,tipo,token,seleccionado:selected,ct:HttpContext.RequestAborted);
                options=options with {Opciones=options.Opciones.Concat(current.Opciones).ToArray()};
            }
            Opciones[tipo]=options;
        }
    }
    [BindProperty(SupportsGet=true)] public DateOnly Desde {get;set;}=PeriodoGestion.Hoy.AddDays(-30);
    [BindProperty(SupportsGet=true)] public DateOnly Hasta {get;set;}=PeriodoGestion.Hoy.AddDays(1);
    [BindProperty(SupportsGet=true)] public Guid? ComercioId {get;set;}
    [BindProperty(SupportsGet=true)] public Guid? ZonaId {get;set;}
    [BindProperty(SupportsGet=true)] public Guid? RepartidorId {get;set;}
    [BindProperty(SupportsGet=true)] public int Pagina {get;set;}=1;
    public ReporteGestion? Reporte {get;private set;}
    public string? Error {get;private set;}
    public string Enlace(int pagina,Guid? comercio=null,Guid? zona=null,Guid? repartidor=null)
        => $"?Desde={Desde:yyyy-MM-dd}&Hasta={Hasta:yyyy-MM-dd}&ComercioId={comercio??ComercioId}&ZonaId={zona??ZonaId}&RepartidorId={repartidor??RepartidorId}&Pagina={pagina}";
    public async Task OnGetAsync()
    {
        if(!ModelState.IsValid) {Error="Revisá el período y los filtros.";return;}
        try {
            var token=SesionUsuario.ObtenerToken(User);var actor=await usuarios.ObtenerUsuarioActualAsync(token,HttpContext.RequestAborted);
            await CargarOpciones(actor.OperadorId!.Value,token);
            Reporte=await api.ReporteAsync(new(actor.OperadorId!.Value,PeriodoGestion.Inicio(Desde),PeriodoGestion.Inicio(Hasta),ComercioId,ZonaId,RepartidorId,Pagina),token,HttpContext.RequestAborted);
            foreach(var tipo in new[]{"Comercio","Zona","Repartidor"})
                await Referencias.Cargar(actor.OperadorId!.Value,tipo,Reporte.Indicadores.Where(i=>i.Corte==tipo && Guid.TryParse(i.Clave,out _)).Select(i=>Guid.Parse(i.Clave)),token,HttpContext.RequestAborted);
            await Referencias.Cargar(actor.OperadorId!.Value,"Envio",Reporte.Detalles.Select(d=>d.EnvioId).Concat(Reporte.DetallesMotivos.Select(d=>d.EnvioId)),token,HttpContext.RequestAborted);
            await Referencias.Cargar(actor.OperadorId!.Value,"Repartidor",Reporte.DetallesMotivos.Select(d=>d.RepartidorId),token,HttpContext.RequestAborted);
        } catch(NexoRutaApiException e) {Error=e.StatusCode==System.Net.HttpStatusCode.Forbidden ? "No tenés permiso para consultar reportes." : "No pudimos consultar el reporte. Revisá el período (máximo 366 días) y reintentá.";}
        catch(HttpRequestException) {Error="El servicio no está disponible. Reintentá conservando los filtros.";}
    }
}
