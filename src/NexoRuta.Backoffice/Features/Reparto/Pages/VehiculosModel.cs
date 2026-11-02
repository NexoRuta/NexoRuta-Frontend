using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Reparto.Clients;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
using NexoRuta.Backoffice.Core.Security;

namespace NexoRuta.Backoffice.Features.Reparto.Pages;

public sealed class VehiculosModel(ConfiguracionRepartoApiClient api) : PageModel
{
    [BindProperty] public VehiculoResponse Vehiculo { get; set; } = new();
    public IReadOnlyList<VehiculoResponse> Vehiculos { get; private set; } = [];
    public bool CargaCompleta { get; private set; }
    public bool MostrarVehiculoEnviado { get; private set; }
    public bool GuardadoBloqueadoPorCarga { get; private set; }
    public string? Error { get; private set; }
    public string? Confirmacion { get; private set; }
    public Task<IActionResult> OnGetAsync() => CargarAsync(true);
    public Task<IActionResult> OnGetReintentarAsync() => CargarAsync(true);
    public async Task<IActionResult> OnPostVehiculoAsync()
    {
        MostrarVehiculoEnviado = true;
        GuardadoBloqueadoPorCarga = false;
        ValidarCapacidad(nameof(Vehiculo.PesoMaximoGramos), x => Vehiculo.PesoMaximoGramos = x);
        ValidarCapacidad(nameof(Vehiculo.VolumenMaximoCm3), x => Vehiculo.VolumenMaximoCm3 = x);
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
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Error = "No se pudo confirmar el guardado. Los datos y la versión se conservan; consultá el listado antes de repetir."; }
        return Confirmacion is not null ? await CargarAsync(true) : Page();
    }
    private async Task<IActionResult> CargarAsync(bool reiniciar)
    {
        CargaCompleta = false;
        try
        {
            Vehiculos = await api.VehiculosAsync(Token(), HttpContext.RequestAborted);
            if (reiniciar) Vehiculo = new();
            CargaCompleta = true;
        }
        catch (NexoRutaApiException ex) { var redirect = await ApiSession.RedirectIfExpiredAsync(this, ex); if (redirect is not null) return redirect; Error = ex.Message; }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { Error = "No se pudo consultar vehículos; no se presume un listado vacío."; }
        return Page();
    }
    private string Token() => SesionUsuario.ObtenerToken(User);
    private void ValidarCapacidad(string nombre, Action<decimal> asignar)
    {
        // HTML number posts invariant decimals; the current culture may otherwise parse 1.005 as 1005.
        var key = $"Vehiculo.{nombre}";
        var raw = Request.Form[key];
        ModelState.Remove(key);
        ModelState.SetModelValue(key, new ValueProviderResult(raw, CultureInfo.InvariantCulture));
        if (raw.Count != 1 || !decimal.TryParse(raw[0], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture, out var value))
        {
            ModelState.AddModelError(key, "Ingresá un número válido sin separadores de miles.");
            return;
        }
        asignar(value);
        var errores = new List<ValidationResult>();
        Validator.TryValidateProperty(value, new ValidationContext(Vehiculo) { MemberName = nombre }, errores);
        foreach (var error in errores) ModelState.AddModelError(key, error.ErrorMessage!);
        if (errores.Count == 0) ModelState[key]!.ValidationState = ModelValidationState.Valid;
    }
}
