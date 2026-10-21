using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Reparto.Clients;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Reparto.Pages;

public sealed class ConfiguracionRepartoModel(ConfiguracionRepartoApiClient api) : PageModel
{
    [BindProperty] public ConfiguracionRepartoResponse Politica { get; set; } = new();
    [BindProperty] public VehiculoResponse Vehiculo { get; set; } = new();
    public IReadOnlyList<VehiculoResponse> Vehiculos { get; private set; } = [];
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public Task<IActionResult> OnGetAsync() => CargarAsync(true);
    public async Task<IActionResult> OnPostPoliticaAsync()
    {
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith("Vehiculo.", StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) Error = "Revisá los valores de la configuración.";
        else try
        {
            Politica = await api.GuardarAsync(Politica, Token(), HttpContext.RequestAborted);
            Confirmacion = "Configuración guardada. Las rutas ya despachadas conservan su versión anterior.";
            ModelState.Clear();
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar al servicio."; }
        return await CargarAsync(false);
    }
    public async Task<IActionResult> OnPostVehiculoAsync()
    {
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith("Politica.", StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) Error = "Revisá la matrícula y las capacidades del vehículo.";
        else try
        {
            await api.GuardarVehiculoAsync(Vehiculo, Token(), HttpContext.RequestAborted);
            Confirmacion = "Vehículo guardado."; Vehiculo = new(); ModelState.Clear();
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar al servicio."; }
        return await CargarAsync(true);
    }
    private async Task<IActionResult> CargarAsync(bool politica)
    {
        try
        {
            if (politica) { Politica = await api.ObtenerAsync(Token(), HttpContext.RequestAborted) ?? new(); }
            Vehiculos = await api.VehiculosAsync(Token(), HttpContext.RequestAborted);
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar al servicio."; }
        return Page();
    }
    private string Token() => SesionUsuario.ObtenerToken(User);
}
