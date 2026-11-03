using System.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;

namespace NexoRuta.Backoffice.Pages.Envios;

[Authorize(Policy = "envios.consultar")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class EtiquetasModel(EnviosApiClient envios) : PageModel
{
    public async Task<IActionResult> OnGetAsync(Guid id, CancellationToken ct)
    {
        Response.Headers.CacheControl = "no-store";
        try
        {
            var pdf = await envios.DescargarEtiquetasAsync(id, SesionUsuario.ObtenerToken(User), ct);
            return File(pdf, "application/pdf", $"etiquetas-{id:D}.pdf");
        }
        catch (NexoRutaApiException error) when (error.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
        { return StatusCode((int)error.StatusCode!, "El envío no está disponible para esta cuenta."); }
        catch (HttpRequestException)
        { return StatusCode(503, "No pudimos descargar las etiquetas. Volvé al envío y reintentá."); }
    }
}
