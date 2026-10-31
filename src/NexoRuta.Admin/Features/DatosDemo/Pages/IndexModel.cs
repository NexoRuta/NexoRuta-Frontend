using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;

namespace NexoRuta.Admin.Features.DatosDemo.Pages;

public sealed class IndexModel(DatosDemoApiClient datosDemo) : PageModel
{
    private static readonly TimeZoneInfo ZonaHoraria = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");

    public DatosDemoStatusResponse? Estado { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        try
        {
            Estado = await datosDemo.ObtenerEstadoAsync(SesionUsuario.ObtenerToken(User), HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            return Redirect("/api-indisponible");
        }
        catch (HttpRequestException)
        {
            return Redirect("/api-indisponible");
        }
        return Page();
    }

    public async Task<IActionResult> OnPostSembrarAsync()
    {
        var token = SesionUsuario.ObtenerToken(User);
        try
        {
            Estado = await datosDemo.SembrarAsync(token, HttpContext.RequestAborted);
            Confirmacion = "Demo data was loaded. It includes one week of shipments and delivery routes.";
        }
        catch (NexoRutaApiException error)
        {
            if (await ApiSession.RedirectAsync(this, error) is { } redirect) return redirect;
            if (error.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                Estado = await datosDemo.ObtenerEstadoAsync(token, HttpContext.RequestAborted);
                Error = "Demo data was already loaded. No second copy was created.";
            }
            else
            {
                Error = "Demo data could not be loaded. Refresh the status before trying again.";
            }
        }
        catch (HttpRequestException)
        {
            Error = "The API could not confirm whether the seed completed. Refresh the status before trying again.";
        }
        return Page();
    }

    public string FechaLocal(DateTimeOffset fechaUtc)
        => TimeZoneInfo.ConvertTime(fechaUtc, ZonaHoraria).ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-UY"));
}
