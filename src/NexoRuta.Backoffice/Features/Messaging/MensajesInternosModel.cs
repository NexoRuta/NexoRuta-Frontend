using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Messaging;
using NexoRuta.Backoffice.Core.Security;
namespace NexoRuta.Backoffice.Features.Messaging;

[Authorize(Policy = "configuracion.gestionar"), Authorize(Policy = "envios.consultar")]
public sealed class MensajesInternosModel(InternalMessagesApiClient api) : PageModel
{
    public FailedInternalMessage[] Mensajes { get; private set; } = [];
    [BindProperty(SupportsGet = true)] public int Pagina { get; set; } = 1;
    [BindProperty] public Guid MensajeId { get; set; }
    [BindProperty] public Guid SolicitudId { get; set; }
    [BindProperty] public int Intentos { get; set; }
    [BindProperty, Required(ErrorMessage = "Ingresá el motivo del reintento."), StringLength(500)]
    public string Motivo { get; set; } = "";
    public string? Error { get; private set; }
    [TempData] public string? Confirmacion { get; set; }
    public bool Cargado { get; private set; }
    public bool ConservarSolicitud { get; private set; }
    public bool SolicitudIncierta { get; private set; }
    public Task<IActionResult> OnGetAsync() => LoadAsync();
    public async Task<IActionResult> OnPostAsync()
    {
        ConservarSolicitud = MensajeId != Guid.Empty && SolicitudId != Guid.Empty && Intentos >= 0;
        if (MensajeId == Guid.Empty || SolicitudId == Guid.Empty || Intentos < 0 || string.IsNullOrWhiteSpace(Motivo))
            ModelState.AddModelError("", "Seleccioná un mensaje e ingresá el motivo del reintento.");
        if (!ModelState.IsValid) { Error = "Revisá el motivo y la selección."; return await LoadAsync(); }
        try
        {
            var response = await api.RetryAsync(MensajeId, new(SolicitudId, Intentos, Motivo.Trim()), Token(), HttpContext.RequestAborted);
            Confirmacion = response.Resultado == "Solicitado"
                ? "Reintento solicitado. El Worker volverá a publicar el mensaje."
                : "El mensaje ya no está disponible para reintento. La solicitud quedó registrada.";
            return RedirectToPage(new { Pagina });
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; SolicitudIncierta = (int)ex.StatusCode < 300; Error = SolicitudIncierta ? "No se pudo confirmar el resultado. Podés repetir la misma solicitud; se conservará su identidad." : "No se pudo solicitar el reintento. La solicitud original sigue disponible."; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        { SolicitudIncierta = true; Error = "No se pudo confirmar el resultado. Podés repetir la misma solicitud; se conservará su identidad."; }
        return await LoadAsync();
    }
    private async Task<IActionResult> LoadAsync()
    {
        Response.Headers.CacheControl = "no-store";
        if (Pagina is < 1 or > 100000) return BadRequest();
        try { Mensajes = await api.ListAsync(Pagina, Token(), HttpContext.RequestAborted); Cargado = true; }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error ??= "No se pudieron consultar los mensajes. Reintentá la carga."; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Error ??= "No se pudo conectar al servicio. Reintentá la carga."; }
        return Page();
    }
    private string Token() => SesionUsuario.ObtenerToken(User);
}
