using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.Backoffice.Core.Security;
namespace NexoRuta.Backoffice.Features.Reparto.Pages;
public sealed partial class RutasModel
{
    public IReadOnlyList<EstimacionRutaResponse> Estimaciones{get;private set;}=[];public string? ErrorEstimaciones{get;private set;}
    [BindProperty]public Guid EstimacionComando{get;set;}[BindProperty]public long EstimacionVersion{get;set;}[BindProperty]public Guid EstimacionRuta{get;set;}
    [BindProperty(SupportsGet=true)]public long? AntesDeEstimacion{get;set;}
    [BindProperty]public Guid? EstimacionOcupacion{get;set;}
    public bool EstimacionEnviada{get;private set;}
    private async Task CargarEstimaciones()
    {if(PuedeGestionarRutas&&RutaSeleccionada is {} ruta)try{Estimaciones=await api.EstimacionesAsync(ruta.Id,AntesDeEstimacion,Token(),HttpContext.RequestAborted);}catch(NexoRutaApiException e){ErrorEstimaciones=$"No se pudo consultar el histórico; falló GET /api/rutas/{ruta.Id}/estimaciones (HTTP {(int?)e.StatusCode}). Revisá permisos y la API configurada; no se presume una estimación vigente ni un histórico vacío.";}
        catch(Exception e)when(e is HttpRequestException or TaskCanceledException){ErrorEstimaciones="No se pudo consultar el histórico; no se presume una estimación vigente.";}}
    public Task<IActionResult> OnPostActualizarEstimacionAsync()=>EnviarEstimacion(true);
    public Task<IActionResult> OnPostEstimarAsync()=>EnviarEstimacion(false);
    private async Task<IActionResult> EnviarEstimacion(bool recorrido)
    {
        EstimacionEnviada=true;
        try{if(!ModelState.IsValid||EstimacionComando==Guid.Empty||EstimacionRuta==Guid.Empty||EstimacionVersion<0)throw new ArgumentException("Indicá ruta, comando y versión válidos.");if(recorrido&&(EstimacionOcupacion is null||EstimacionOcupacion==Guid.Empty))throw new ArgumentException("Indicá la ocupación exacta.");
        var r=recorrido?await api.ActualizarEstimacionAsync(EstimacionRuta,new(EstimacionComando,EstimacionVersion,EstimacionOcupacion!.Value),Token(),HttpContext.RequestAborted):await api.CalcularEstimacionAsync(EstimacionRuta,new(EstimacionComando,EstimacionVersion),Token(),HttpContext.RequestAborted);Confirmacion=$"Cálculo registrado como versión {r.NumeroVersion}: {r.Estado}. No confirma hechos físicos.";RutaSeleccionadaId=EstimacionRuta;return RedirectarDiario();}
        catch(NexoRutaApiException e){var redirect=await ApiSession.RedirectIfExpiredAsync(this,e);if(redirect is not null)return redirect;Error=e.Message;}
        catch(ArgumentException e){Error=e.Message;}
        catch(Exception e)when(e is HttpRequestException or TaskCanceledException){Error="Resultado desconocido. Se conserva el comando; consultá el histórico antes de repetir.";}
        return await Cargar();
    }
    public string MotivoVial(string motivo)=>motivo switch{
        "ActualizacionPosterior"=>"Existe una actualización exitosa posterior.","EntradasCambiadas"=>"Cambió la posición admitida, la composición pendiente, el perfil o la ejecución.",
        "GpsAnteriorProyeccion"=>"Las mediciones admisibles son anteriores a la última posición utilizada; se requiere un punto nuevo, no retroceder la proyección.",
        "GpsAntiguo"=>"La medición GPS es demasiado antigua; recibirla ahora no la vuelve reciente.","GpsImpreciso"=>"La precisión GPS excede el umbral de admisión.","GpsFuturo"=>"La medición GPS está en el futuro; no se admite adelanto del reloj del dispositivo.","GpsOtraEjecucion"=>"La medición no corresponde a la salida y ocupación vigentes.","GpsInvalido"=>"La posición no tiene coordenadas, fecha o precisión válidas.","SinPosicion"=>"No hay posición sincronizada admisible para esta ejecución; los puntos pendientes solo en el teléfono no son conocidos por el servidor.","EjecucionNoDisponible"=>"Se requiere salida registrada y ocupación vigente sin retorno confirmado ni liberación.",
        "PerfilPendiente"=>"Falta configurar o confirmar la compatibilidad específica del vehículo con drive.","PerfilNoCompatible"=>"El vehículo no es compatible con drive; no se calculó una ruta para otro perfil.",
        "ProveedorNoConfigurado"=>"Llamadas reales deshabilitadas o clave vial dedicada no configurada.","DatosInsuficientes"=>"Faltan base confirmada, salida, coordenadas o tiempos de servicio/margen.","CuotaExcedida"=>"Límite de consumo o cuota alcanzados; no hay reintento automático.","RutaNoEncontrada"=>"No se encontró un recorrido vial.","TiempoAgotado"=>"Se agotó el tiempo de cálculo; no se aplicaron horarios parciales.","SinParadas"=>"Sin paradas activas: no hay circuito estimable.","ProveedorNoDisponible"=>"Proveedor no disponible; se conserva el histórico.","RespuestaInvalida"=>"El proveedor no devolvió una respuesta válida y completa.",_=>motivo};
}
