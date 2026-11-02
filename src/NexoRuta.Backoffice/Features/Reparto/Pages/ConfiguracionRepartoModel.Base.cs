using System.Globalization;
using NexoRuta.Backoffice.Core.Security;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
namespace NexoRuta.Backoffice.Features.Reparto.Pages;
public sealed partial class ConfiguracionRepartoModel
{
    [BindProperty] public long BaseVersion { get; set; }
    [BindProperty] public string? BaseDireccion { get; set; }
    [BindProperty, Range(0, int.MaxValue)] public int? BaseServicioMinutos { get; set; }
    [BindProperty, Range(0, int.MaxValue)] public int? BaseMargenMinutos { get; set; }
    [BindProperty] public long PuntoVersion { get; set; }
    [BindProperty] public string? BaseLatitud { get; set; }
    [BindProperty] public string? BaseLongitud { get; set; }
    public BaseOperativaResponse? BaseActual { get; private set; }
    public PropuestasBaseResponse? PropuestasBase { get; private set; }
    public string? ErrorBase { get; private set; }
    public bool BaseCargada { get; private set; }
    public bool BaseGuardarEnviado { get; private set; }
    public bool PuntoEnviado { get; private set; }
    private async Task CargarBaseAsync()
    {
        try
        {
            BaseActual = await api.BaseAsync(Token(), HttpContext.RequestAborted); BaseCargada = true;
            if (!BaseGuardarEnviado) { BaseVersion = BaseActual?.Version ?? 0; BaseDireccion = BaseActual?.Direccion; BaseServicioMinutos = BaseActual?.ServicioPredeterminadoMinutos; BaseMargenMinutos = BaseActual?.MargenRetornoMinutos; }
            if (!PuntoEnviado) { PuntoVersion = BaseActual?.Version ?? 0; BaseLatitud = BaseActual?.Latitud?.ToString(CultureInfo.InvariantCulture); BaseLongitud = BaseActual?.Longitud?.ToString(CultureInfo.InvariantCulture); }
        }
        catch (NexoRutaApiException e) { ErrorBase ??= e.Message; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { ErrorBase ??= "No se pudo consultar la base; no se presume confirmada."; }
    }
    private void ValidarSoloBase()
    {
        foreach (var k in ModelState.Keys.Where(k => !k.StartsWith("Base",StringComparison.Ordinal) && k != nameof(PuntoVersion)).ToArray()) ModelState.Remove(k);
    }
    public async Task<IActionResult> OnPostBaseAsync()
    {
        BaseGuardarEnviado = true; ValidarSoloBase();
        if (string.IsNullOrWhiteSpace(BaseDireccion) || BaseDireccion.Trim().Length > 500) ModelState.AddModelError(nameof(BaseDireccion), "Indicá dirección de hasta 500 caracteres.");
        if (!ModelState.IsValid) ErrorBase = "Revisá dirección y minutos no negativos.";
        else try
        {
            await api.GuardarBaseAsync(new(BaseVersion,BaseDireccion!,BaseServicioMinutos,BaseMargenMinutos),Token(),HttpContext.RequestAborted);
            BaseGuardarEnviado = false; ModelState.Clear(); Confirmacion = "Base y parámetros guardados. Las hojas existentes no cambian; si cambió la dirección, confirmá el nuevo punto.";
        }
        catch (NexoRutaApiException e) { var redirect=await ApiSession.RedirectIfExpiredAsync(this,e); if(redirect is not null)return redirect; ErrorBase=e.Message; }
        catch (Exception e) when(e is HttpRequestException or TaskCanceledException) { ErrorBase="No se pudo confirmar el guardado; consultá el estado antes de repetir. Se conserva la versión enviada."; }
        return await CargarAsync(true);
    }
    public async Task<IActionResult> OnPostPuntoBaseAsync()
    {
        PuntoEnviado = true; ValidarSoloBase();
        bool Coordenada(string? raw, double min, double max, string key, out double value)
        {
            if (double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out value) && double.IsFinite(value) && value>=min && value<=max) return true;
            ModelState.AddModelError(key,"Indicá coordenadas válidas con punto decimal."); return false;
        }
        Coordenada(BaseLatitud,-90,90,nameof(BaseLatitud),out var lat); Coordenada(BaseLongitud,-180,180,nameof(BaseLongitud),out var lon);
        if (!ModelState.IsValid) ErrorBase = "Revisá el punto a confirmar.";
        else try
        {
            await api.ConfirmarBaseAsync(new(PuntoVersion,lat,lon),Token(),HttpContext.RequestAborted);
            PuntoEnviado = false; ModelState.Clear(); Confirmacion = "Punto de salida y retorno confirmado. Esto no acredita retorno ni libera vehículos.";
        }
        catch (NexoRutaApiException e) { var redirect=await ApiSession.RedirectIfExpiredAsync(this,e); if(redirect is not null)return redirect; ErrorBase=e.Message; }
        catch (Exception e) when(e is HttpRequestException or TaskCanceledException) { ErrorBase="Resultado de confirmación desconocido; revisá el estado antes de repetir con otra versión."; }
        return await CargarAsync(true);
    }
    public async Task<IActionResult> OnPostProponerBaseAsync()
    {
        ValidarSoloBase();
        try { PropuestasBase=await api.ProponerBaseAsync(Token(),HttpContext.RequestAborted); }
        catch(NexoRutaApiException e) { var redirect=await ApiSession.RedirectIfExpiredAsync(this,e); if(redirect is not null)return redirect; ErrorBase=e.Message; }
        catch(Exception e) when(e is HttpRequestException or TaskCanceledException) { ErrorBase="No hay propuestas disponibles. Podés confirmar el punto manualmente."; }
        return await CargarAsync(true);
    }
}
