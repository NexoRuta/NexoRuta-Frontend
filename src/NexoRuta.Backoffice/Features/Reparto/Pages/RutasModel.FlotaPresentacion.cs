using Microsoft.AspNetCore.Mvc;
using NexoRuta.ApiClient.Features.Reparto.Contracts;
namespace NexoRuta.Backoffice.Features.Reparto.Pages;

public sealed partial class RutasModel
{
    public bool ConsultaFlotaCompleta { get; private set; }
    [BindProperty(SupportsGet = true)] public string? BuscarVehiculo { get; set; }
    [BindProperty(SupportsGet = true)] public string? Disponibilidad { get; set; }
    public string FiltroFlota => new[] { "Todas", "Disponible", "Ocupado", "Pendiente", "NoHabilitado" }
        .FirstOrDefault(x => string.Equals(x, Disponibilidad, StringComparison.OrdinalIgnoreCase)) ?? "Todas";
    public string ZonaFlota => Recursos?.Politica.ZonaHoraria ?? ZonaHorariaPredeterminada;
    // Presentation only: administrative activation is deliberately not a physical-availability state.
    public static string DisponibilidadFlota(VehiculoFlotaResponse v) => v.Ocupaciones.Count > 0 ? "Ocupado"
        : v.VerificacionInicialEnUtc is null || v.CargaPendiente > 0 ? "Pendiente" : "Disponible";
    public bool CoincideFlota(VehiculoFlotaResponse v) =>
        (string.IsNullOrWhiteSpace(BuscarVehiculo) || v.Matricula.Contains(BuscarVehiculo.Trim(), StringComparison.CurrentCultureIgnoreCase))
        && (FiltroFlota == "Todas" || FiltroFlota == "NoHabilitado" && !v.Activo || FiltroFlota == DisponibilidadFlota(v));
    public string FechaFlota(DateTimeOffset value) => FechaHora(value, ZonaFlota);
    public IEnumerable<AccionFlotaFormulario> AccionesFlota(VehiculoFlotaResponse v, OcupacionFlotaResponse o)
    {
        if (o.Legada && o.AdquiridaEnUtc is null && o.RetornoEnUtc is null)
            yield return new(v.Id, o.Id, o.Version, "conciliar", "Confirmar ocupación vigente tras inspección", Fecha);
        if (o.RetornoEnUtc is null && o.EstadoRuta != "Despachada")
            yield return new(v.Id, o.Id, o.Version, "retorno", "Confirmar retorno físico (sin descarga)", Fecha);
        if (o.RetornoEnUtc is not null && o.EstadoRuta == "Finalizada" && o.CargaPendiente == 0)
            yield return new(v.Id, o.Id, o.Version, "liberar", "Confirmar disponibilidad y liberar", Fecha);
        if (o.PuedeAnular && o.RetornoEnUtc is null)
            yield return new(v.Id, o.Id, o.Version, "anular", "Anular autorización sin inicio ni carga", Fecha);
    }
    public static string IdAccionFlota(AccionFlotaFormulario a) => $"flota-accion-{a.Vehiculo}-{a.Ocupacion}-{a.Accion}-{a.Envio}";
    public static string ConsecuenciaFlota(string accion) => accion switch
    {
        "retorno" => "Acredita únicamente el regreso físico. No recibe carga ni libera el vehículo.",
        "liberar" => "Confirma disponibilidad y libera esta ocupación. Requiere cierre, retorno y carga recibida o conciliada.",
        "anular" => "Anula la autorización sin inventar un retorno; solo corresponde si nunca se inició ni cargó.",
        "conciliar" => "Acredita la vigencia física de una asignación anterior a B2 mediante inspección.",
        "recibir-carga-inicial" => "Confirma que todos los bultos históricos indicados están físicamente en depósito. No acredita entrega.",
        _ => "Confirma mediante inspección que el vehículo está retornado y sin carga. No cambia su habilitación administrativa."
    };
}
