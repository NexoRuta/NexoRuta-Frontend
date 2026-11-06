using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Session;
namespace NexoRuta.Backoffice.Pages.Reprogramaciones;

[Authorize(Policy = "envios.consultar"), Authorize(Policy = "envios.gestionar")]
public sealed class IndexModel(IHttpClientFactory clients) : PageModel
{
    private sealed record Outcome(string Estado);
    public sealed record Item(Guid Id, Guid EnvioId, long VersionEnvio, DateTimeOffset CreadaEnUtc, string Estado);
    public IReadOnlyList<Item> Pendientes { get; private set; } = [];
    public string? Error { get; private set; }
    [TempData] public string? Mensaje { get; set; }
    [BindProperty] public Guid SolicitudId { get; set; }
    [BindProperty] public long VersionEnvio { get; set; }
    [BindProperty] public bool Aprobar { get; set; }
    [BindProperty] public string Motivo { get; set; } = "";
    public async Task OnGetAsync()
    {
        try
        {
            using var http = CreateClient();
            using var response = await http.GetAsync("api/operacion/reprogramaciones", HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode) { Error = "No se pudieron consultar las solicitudes. Reintentá en unos minutos."; return; }
            Pendientes = await response.Content.ReadFromJsonAsync<Item[]>(HttpContext.RequestAborted) ?? [];
        }
        catch (HttpRequestException) { Error = "No hay conexión con el servicio. Reintentá en unos minutos."; }
    }
    public async Task<IActionResult> OnPostAsync()
    {
        if (SolicitudId == Guid.Empty || string.IsNullOrWhiteSpace(Motivo) || Motivo.Length > 500)
        { Error = "Indicá un motivo de hasta 500 caracteres."; await OnGetAsync(); return Page(); }
        try
        {
            using var http = CreateClient();
            using var response = await http.PostAsJsonAsync($"api/operacion/reprogramaciones/{SolicitudId}/resolver", new { VersionEnvio, Aprobar, Motivo }, HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            { Error = response.StatusCode == System.Net.HttpStatusCode.Conflict ? "El envío cambió o ya no es elegible. Revisá el detalle operativo antes de resolver la solicitud." : "No se pudo resolver la solicitud. Reintentá más tarde."; await OnGetAsync(); return Page(); }
            var outcome = await response.Content.ReadFromJsonAsync<Outcome>(HttpContext.RequestAborted);
            Mensaje = outcome?.Estado switch
            {
                "Tratada" => "La solicitud fue aprobada. Programá la nueva visita desde Rutas.",
                "Rechazada" => "Solicitud rechazada.",
                _ => "No se pudo confirmar el resultado. Consultá el estado actual antes de reintentar."
            };
            return RedirectToPage();
        }
        catch (Exception error) when (error is HttpRequestException or System.Text.Json.JsonException)
        { Error = "No se pudo confirmar el resultado. Consultá el estado actual antes de reintentar."; await OnGetAsync(); return Page(); }
    }
    private HttpClient CreateClient()
    { var http = clients.CreateClient("NexoRuta"); http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", SesionUsuario.ObtenerToken(User)); return http; }
}
