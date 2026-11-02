using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.Backoffice.Core.Security;
namespace NexoRuta.Backoffice.Features.Reparto.Pages;
public sealed partial class ConfiguracionRepartoModel
{
    public IReadOnlyList<PerfilVialResponse> PerfilesViales{get;private set;}=[];public string? ErrorPerfil{get;private set;}public bool PerfilEnviado{get;private set;}
    [BindProperty]public Guid PerfilVehiculo{get;set;}[BindProperty]public long PerfilVersion{get;set;}[BindProperty]public string? Compatibilidad{get;set;}[BindProperty]public bool ConfirmarCompatibilidad{get;set;}[BindProperty]public string? MotivoPerfil{get;set;}
    private async Task CargarPerfilesAsync()
    {try{PerfilesViales=await api.PerfilesAsync(Token(),HttpContext.RequestAborted);}catch(NexoRutaApiException e){var fallo=$"No se pudo consultar la compatibilidad vial; no se presume confirmada. Falló GET /api/reparto/perfiles-viales (HTTP {(int?)e.StatusCode}). Revisá permisos y la API configurada.";ErrorPerfil=ErrorPerfil is null?fallo:ErrorPerfil+" "+fallo;}
        catch(Exception e)when(e is HttpRequestException or TaskCanceledException){ErrorPerfil??="No se pudo consultar la compatibilidad vial; no se presume confirmada.";}}
    public async Task<IActionResult> OnPostPerfilAsync()
    {
        PerfilEnviado=true;foreach(var k in ModelState.Keys.Where(k=>k is not(nameof(PerfilVehiculo) or nameof(PerfilVersion) or nameof(Compatibilidad) or nameof(ConfirmarCompatibilidad) or nameof(MotivoPerfil))).ToArray())ModelState.Remove(k);
        if(!ModelState.IsValid||PerfilVehiculo==Guid.Empty||PerfilVersion<0||string.IsNullOrWhiteSpace(MotivoPerfil)||MotivoPerfil.Length>500||Compatibilidad is not("SinDeterminar" or "DriveConfirmado" or "NoCompatible")||Compatibilidad=="DriveConfirmado"&&!ConfirmarCompatibilidad)ErrorPerfil="Revisá el perfil, su confirmación expresa y el motivo.";
        else try{await api.PerfilAsync(PerfilVehiculo,new(PerfilVersion,Compatibilidad!,ConfirmarCompatibilidad,MotivoPerfil!),Token(),HttpContext.RequestAborted);PerfilEnviado=false;ModelState.Clear();Confirmacion="Compatibilidad vial guardada. No cambia capacidad, ocupación ni custodia.";}
        catch(NexoRutaApiException e){var redirect=await ApiSession.RedirectIfExpiredAsync(this,e);if(redirect is not null)return redirect;ErrorPerfil=e.Message;}
        catch(Exception e)when(e is HttpRequestException or TaskCanceledException){ErrorPerfil="Resultado desconocido; conservá los datos y consultá antes de repetir con otra versión.";}
        return await CargarAsync(true);
    }
}
