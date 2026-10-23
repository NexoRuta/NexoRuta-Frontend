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
    public bool CargaCompleta { get; private set; }
    public bool MostrarPoliticaEnviada { get; private set; }
    public bool MostrarVehiculoEnviado { get; private set; }
    public bool GuardadoBloqueadoPorCarga { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public Task<IActionResult> OnGetAsync() => CargarAsync(true);
    public Task<IActionResult> OnGetReintentarAsync() => CargarAsync(true);
    public async Task<IActionResult> OnPostPoliticaAsync()
    {
        MostrarPoliticaEnviada = true;
        GuardadoBloqueadoPorCarga = false;
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith("Vehiculo.", StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) Error = "Revisá los valores de la configuración.";
        else try
        {
            var carga = await CargarAsync(false);
            if (carga is not PageResult || !CargaCompleta)
            {
                GuardadoBloqueadoPorCarga = true;
                return carga;
            }
            Politica = await api.GuardarAsync(Politica, Token(), HttpContext.RequestAborted);
            Confirmacion = "Configuración guardada. Las rutas ya despachadas conservan su versión anterior.";
            MostrarPoliticaEnviada = false;
            ModelState.Clear();
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar al servicio."; }
        return Confirmacion is not null ? await CargarAsync(false) : Page();
    }
    public async Task<IActionResult> OnPostVehiculoAsync()
    {
        MostrarVehiculoEnviado = true;
        GuardadoBloqueadoPorCarga = false;
        foreach (var key in ModelState.Keys.Where(x => x.StartsWith("Politica.", StringComparison.Ordinal)).ToArray()) ModelState.Remove(key);
        if (!ModelState.IsValid) Error = "Revisá la matrícula y las capacidades del vehículo.";
        else try
        {
            var carga = await CargarAsync(false);
            if (carga is not PageResult || !CargaCompleta)
            {
                GuardadoBloqueadoPorCarga = true;
                return carga;
            }
            await api.GuardarVehiculoAsync(Vehiculo, Token(), HttpContext.RequestAborted);
            Confirmacion = "Vehículo guardado."; Vehiculo = new(); MostrarVehiculoEnviado = false; ModelState.Clear();
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar al servicio."; }
        return Confirmacion is not null ? await CargarAsync(true) : Page();
    }
    private async Task<IActionResult> CargarAsync(bool politica)
    {
        CargaCompleta = false;
        try
        {
            var actual = await api.ObtenerAsync(Token(), HttpContext.RequestAborted);
            var vehiculos = await api.VehiculosAsync(Token(), HttpContext.RequestAborted);
            if (politica) { Politica = actual ?? new(); Vehiculo = new(); }
            Vehiculos = vehiculos;
            CargaCompleta = true;
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (HttpRequestException) { Error = "No se pudo conectar al servicio."; }
        return Page();
    }
    private string Token() => SesionUsuario.ObtenerToken(User);
}
