using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Features.Gestion;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Core.Exceptions;
namespace NexoRuta.Backoffice.Pages;
public sealed class ReporteOperacionesModel(GestionApiClient api,UsuariosApiClient usuarios):PageModel
{
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
        if(!ModelState.IsValid) {Error="Revisá las fechas y los identificadores de los filtros.";return;}
        try {
            var token=SesionUsuario.ObtenerToken(User);var actor=await usuarios.ObtenerUsuarioActualAsync(token,HttpContext.RequestAborted);
            Reporte=await api.ReporteAsync(new(actor.OperadorId!.Value,PeriodoGestion.Inicio(Desde),PeriodoGestion.Inicio(Hasta),ComercioId,ZonaId,RepartidorId,Pagina),token,HttpContext.RequestAborted);
        } catch(NexoRutaApiException e) {Error=e.StatusCode==System.Net.HttpStatusCode.Forbidden ? "No tenés permiso para consultar reportes." : "No pudimos consultar el reporte. Revisá el período (máximo 366 días) y reintentá.";}
        catch(HttpRequestException) {Error="El servicio no está disponible. Reintentá conservando los filtros.";}
    }
}
