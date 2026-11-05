using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using NexoRuta.ApiClient.Core.Exceptions;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.Backoffice.Core.Security;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace NexoRuta.Backoffice.Features.Envios.Pages;

public class IndexModel(EnviosApiClient apiClient, UsuariosApiClient usuariosClient) : PageModel
{
    private static readonly TimeZoneInfo ZonaHoraria = TimeZoneInfo.FindSystemTimeZoneById("America/Montevideo");
    private const int TamanoPagina = 10;
    private static readonly string[] EstadosDelDominio =
    [
        "Admitido", "EnDeposito", "AsignadoRuta", "EnTransito", "Entregado", "NoEntregado",
        "Reprogramado", "EnDevolucion", "Devuelto", "Extraviado", "EntregaPendienteValidacion",
        "EntregaSinValidacion", "EntregadoParcialmente", "RetiroSolicitado", "RetiroAsignado", "Retirado", "RetiroFallido", "RetiroEnDeposito", "EntregadoComercio", "Cancelado"
    ];
    private IReadOnlyList<EnvioResponse> EnviosAutorizados { get; set; } = [];
    public IReadOnlyList<EnvioResponse> Envios { get; private set; } = [];
    public UsuarioActualResponse? UsuarioActual { get; private set; }
    public string? Error { get; private set; }
    public bool PuedeCancelar { get; private set; }
    public bool ConsultaCorrecta { get; private set; }
    public int TotalResultados { get; private set; }
    public int Pagina { get; private set; } = 1;
    public int TotalPaginas { get; private set; } = 1;
    public int PrimerResultado { get; private set; }
    public int UltimoResultado { get; private set; }
    public IReadOnlyList<string> EstadosDisponibles { get; private set; } = [];
    public IReadOnlyList<(Guid Id, string Nombre)> ComerciosDisponibles { get; private set; } = [];
    public IReadOnlyList<string> EstadosRapidos => new[] { "", "Admitido", "EnDeposito", "EnTransito", "Entregado" }
        .Concat(string.IsNullOrEmpty(Estado) ? [] : new[] { Estado }).Distinct(StringComparer.Ordinal).ToArray();
    private IReadOnlyDictionary<string, int> ConteosEstado { get; set; } = new Dictionary<string, int>();
    public int TotalRetiros => EnviosAutorizados.Count(e => e.EsRetiro);
    public int TotalParciales => EnviosAutorizados.Count(e => e.Estado == "EntregadoParcialmente");
    public int TotalAmbito { get; private set; }
    public int ContarEstado(string estado) => estado.Length == 0 ? TotalAmbito : ConteosEstado.GetValueOrDefault(estado);
    public EnvioResponse? EnvioSeleccionado => EnviosAutorizados.SingleOrDefault(e => e.Id == Seleccion);
    public IReadOnlyList<EnvioResponse> EnviosDetalle => EnvioSeleccionado is { } seleccionado && !Envios.Any(e => e.Id == seleccionado.Id)
        ? Envios.Append(seleccionado).ToArray() : Envios;
    public bool TieneFiltros => !string.IsNullOrWhiteSpace(Buscar) || !string.IsNullOrWhiteSpace(Estado) || ComercioId is not null;
    [BindProperty(SupportsGet = true), DisplayFormat(ConvertEmptyStringToNull = false)] public string Buscar { get; set; } = "";
    [BindProperty(SupportsGet = true), DisplayFormat(ConvertEmptyStringToNull = false)] public string Estado { get; set; } = "";
    [BindProperty(Name = "Pagina", SupportsGet = true)] public int PaginaSolicitada { get; set; } = 1;
    [BindProperty(SupportsGet = true)] public Guid? ComercioId { get; set; }
    [BindProperty(SupportsGet = true)] public Guid? Seleccion { get; set; }
    [BindProperty] public Guid EnvioId { get; set; }
    [BindProperty, Range(0, long.MaxValue)] public long Version { get; set; }
    [BindProperty, Required, StringLength(500)] public string Motivo { get; set; } = "";
    [TempData] public string? Confirmacion { get; set; }

    public async Task<IActionResult> OnPostCancelarAsync()
    {
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            var actor = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            if (actor.RolCodigo != "administrador_operador"
                || !(await usuariosClient.ObtenerPermisosActualesAsync(token, HttpContext.RequestAborted)).Contains("envios.gestionar")) return Forbid();
            if (!ModelState.IsValid || EnvioId == Guid.Empty || string.IsNullOrWhiteSpace(Motivo))
                Error = "Indicá el envío y el motivo de cancelación.";
            else
            {
                await apiClient.CancelarAsync(EnvioId, Version, Motivo.Trim(), token, HttpContext.RequestAborted);
                Confirmacion = "Cancelación registrada. Consultá el estado actualizado; los bultos no entregados deben volver al depósito.";
                return RedirectToPage("/Index", new { Buscar, Estado, ComercioId, Pagina = PaginaSolicitada, Seleccion = EnvioId });
            }
        }
        catch (NexoRutaApiException ex)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, ex);
            if (expired is not null) return expired;
            Error = ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "No tenés permiso para cancelar este envío."
                : "No se pudo confirmar la cancelación. Consultá el estado antes de reintentar.";
        }
        catch (HttpRequestException) { Error = "No se pudo confirmar la cancelación. Consultá el estado antes de reintentar."; }
        if (EnvioId != Guid.Empty) Seleccion = EnvioId;
        return await OnGetAsync();
    }

    public async Task<IActionResult> OnGetAsync()
    {
        Buscar = (Buscar ?? string.Empty).Trim();
        Estado = (Estado ?? string.Empty).Trim();
        if (!EstadosDelDominio.Contains(Estado, StringComparer.Ordinal))
            Estado = "";
        Pagina = Math.Max(PaginaSolicitada, 1);
        EstadosDisponibles = EstadosDelDominio
            .OrderBy(estado => EtiquetaEstado(estado), StringComparer.CurrentCulture)
            .ToArray();
        if (ModelState.TryGetValue(nameof(ComercioId), out var comercioState) && comercioState.Errors.Count > 0)
        {
            Error = "El filtro de comercio no es válido. Seleccioná un comercio de la lista.";
            return Page();
        }
        try
        {
            var token = SesionUsuario.ObtenerToken(User);
            UsuarioActual = await usuariosClient.ObtenerUsuarioActualAsync(token, HttpContext.RequestAborted);
            PuedeCancelar = UsuarioActual.RolCodigo == "administrador_operador"
                && (await usuariosClient.ObtenerPermisosActualesAsync(token, HttpContext.RequestAborted)).Contains("envios.gestionar");
            EnviosAutorizados = await apiClient.ListarEnviosAsync(token, HttpContext.RequestAborted);
            ComerciosDisponibles = EnviosAutorizados.GroupBy(e => e.ComercioId)
                .Select(g => (Id: g.Key, Nombre: ValorInformado(g.First().ComercioNombre)))
                .OrderBy(c => c.Nombre, StringComparer.CurrentCultureIgnoreCase).ToArray();
            ConsultaCorrecta = true;
            AplicarFiltrosYPaginacion();
        }
        catch (NexoRutaApiException exception)
        {
            var expired = await ApiSession.RedirectIfExpiredAsync(this, exception);
            if (expired is not null) return expired;
            Error = exception.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "No tenés permiso para consultar los envíos."
                : "No se pudo consultar el servicio de envíos.";
        }
        catch (HttpRequestException)
        {
            Error = "No se pudo consultar el servicio de envíos.";
        }

        return Page();
    }

    public string AbreviarId(Guid id)
    {
        var value = id.ToString("D");
        return $"{value[..8]}…{value[^4..]}";
    }

    public string EtiquetaEstado(string estado) => estado switch
    {
        "Admitido" => "Admitido",
        "EnDeposito" => "En depósito",
        "AsignadoRuta" => "Asignado a ruta",
        "EnTransito" => "En tránsito",
        "Entregado" => "Entregado",
        "EntregadoParcialmente" => "Entregado parcialmente", "RetiroSolicitado" => "Retiro solicitado", "RetiroAsignado" => "Retiro asignado", "Retirado" => "Retirado", "RetiroFallido" => "No retirado", "RetiroEnDeposito" => "Retiro en depósito", "EntregadoComercio" => "Entregado al comercio",
        "NoEntregado" => "No entregado",
        "Reprogramado" => "Reprogramado",
        "EnDevolucion" => "En devolución",
        "Devuelto" => "Devuelto",
        "Extraviado" => "Extraviado",
        "EntregaPendienteValidacion" => "Entrega pendiente de validación",
        "EntregaSinValidacion" => "Entrega sin validación",
        "Cancelado" => "Cancelado",
        _ => estado
    };

    public string ClaseEstado(string estado) => estado switch
    {
        "Entregado" => "success",
        "NoEntregado" or "Reprogramado" or "EntregaPendienteValidacion" or "EntregaSinValidacion" or "EnDevolucion" or "Devuelto" => "warning",
        "Extraviado" or "Cancelado" => "danger",
        _ => "info"
    };

    public string PesoTotal(EnvioResponse envio)
    {
        var kilogramos = envio.Bultos.Sum(bulto => bulto.PesoGramos) / 1000m;
        return kilogramos.ToString("0.##", CultureInfo.GetCultureInfo("es-UY"));
    }

    public string FechaLocal(DateTimeOffset fechaUtc) =>
        TimeZoneInfo.ConvertTime(fechaUtc, ZonaHoraria).ToString("dd/MM/yyyy HH:mm", CultureInfo.GetCultureInfo("es-UY"));

    public string ValorInformado(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "No informado" : value;

    private void AplicarFiltrosYPaginacion()
    {
        IEnumerable<EnvioResponse> filtrados = EnviosAutorizados;
        if (ComercioId is { } comercio) filtrados = filtrados.Where(envio => envio.ComercioId == comercio);

        if (!string.IsNullOrWhiteSpace(Buscar))
        {
            filtrados = filtrados.Where(envio =>
                envio.Id.ToString("D").Contains(Buscar, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(envio.ReferenciaExterna) && envio.ReferenciaExterna.Contains(Buscar, StringComparison.OrdinalIgnoreCase))
                || envio.Bultos.Any(bulto => bulto.Codigo.Contains(Buscar, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(envio.ComercioNombre)
                    && envio.ComercioNombre.Contains(Buscar, StringComparison.OrdinalIgnoreCase))
                || (!string.IsNullOrWhiteSpace(envio.DestinatarioNombre)
                    && envio.DestinatarioNombre.Contains(Buscar, StringComparison.OrdinalIgnoreCase)));
        }

        var ambito = filtrados.ToArray();
        TotalAmbito = ambito.Length;
        ConteosEstado = ambito.GroupBy(e => e.Estado).ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);
        filtrados = string.IsNullOrWhiteSpace(Estado) ? ambito : ambito.Where(e => e.Estado == Estado);
        var resultado = filtrados.ToArray();
        TotalResultados = resultado.Length;
        TotalPaginas = Math.Max(1, (int)Math.Ceiling(TotalResultados / (double)TamanoPagina));
        Pagina = Math.Clamp(Pagina, 1, TotalPaginas);
        PrimerResultado = TotalResultados == 0 ? 0 : (Pagina - 1) * TamanoPagina + 1;
        UltimoResultado = Math.Min(Pagina * TamanoPagina, TotalResultados);
        Envios = resultado.Skip((Pagina - 1) * TamanoPagina).Take(TamanoPagina).ToArray();
    }
}
