using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Features.Gestion;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Core.Session;
namespace NexoRuta.Backoffice.Pages;
public sealed class DetalleGestionModel(GestionApiClient api,UsuariosApiClient usuarios):PageModel
{
    public DetalleGestion? Detalle{get;private set;}
    public string? Error{get;private set;}
    public string Volver{get;private set;}="/ReporteOperaciones";
    public async Task OnGetAsync(Guid id,string? volver)
    {
        if(Url.IsLocalUrl(volver))Volver=volver!;
        try{var token=SesionUsuario.ObtenerToken(User);var actor=await usuarios.ObtenerUsuarioActualAsync(token,HttpContext.RequestAborted);Detalle=await api.DetalleAsync(actor.OperadorId!.Value,id,token,HttpContext.RequestAborted);}
        catch(HttpRequestException e){Error=e.StatusCode==System.Net.HttpStatusCode.Forbidden?"No tenés permiso para consultar este detalle.":"La operación no está disponible o su proyección está pendiente.";}
    }
}
