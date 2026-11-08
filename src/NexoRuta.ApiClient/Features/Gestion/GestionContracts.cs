namespace NexoRuta.ApiClient.Features.Gestion;

public sealed record FiltroGestion(Guid OperadorId, DateTimeOffset Desde, DateTimeOffset Hasta,
    Guid? ComercioId = null, Guid? ZonaId = null, Guid? RepartidorId = null, int Pagina = 1, int Tamano = 25)
{
    public void Validar()
    {
        if (OperadorId == Guid.Empty || Desde == default || Hasta <= Desde || Hasta - Desde > TimeSpan.FromDays(366)
            || Pagina is < 1 or > 10000 || Tamano is < 1 or > 100 || ComercioId == Guid.Empty || ZonaId == Guid.Empty || RepartidorId == Guid.Empty)
            throw new ArgumentException("Indicá operador, período de hasta 366 días y página válida.");
    }
}
public sealed record Indicador(string Corte, string Clave, string Unidad, int Cantidad, int Cumplidos,
    int Incumplidos, int SinCompromiso, int EnPlazo, int PruebaPendiente, int NoEvaluables,
    int Bultos, decimal PesoKg, decimal VolumenM3, int Entregados, int Pendientes, int Devueltos);
public sealed record DetalleOperacion(Guid EnvioId, Guid ComercioId, Guid? ZonaId, string Zona,
    bool EsRetiro, string Estado, DateTimeOffset AdmitidaEnUtc, DateTimeOffset? CompromisoHastaUtc,
    DateTimeOffset? CompletadaEnUtc, string Cumplimiento, int Bultos, decimal PesoKg, decimal VolumenM3);
public sealed record MotivoGestion(string Tipo, string Motivo, string Unidad, int Cantidad);
public sealed record DetalleBultoGestion(Guid BultoId, Guid? RepartidorId, Guid? RutaId, DateTimeOffset? EntregadoEnUtc, string Resultado, bool? EnFranja);
public sealed record DetalleGestion(DetalleOperacion Operacion, IReadOnlyList<DetalleBultoGestion> Bultos);
public sealed record DetalleMotivo(Guid OperacionId, Guid EnvioId, Guid RepartidorId, Guid RutaId,
    DateTimeOffset OcurridaEnUtc, string Tipo, string Motivo, int Bultos);
public sealed record ReporteGestion(DateTimeOffset ConsultadaEnUtc, DateTimeOffset? ActualizadaEnUtc,
    int PendientesProyeccion, string EstadoProyeccion, IReadOnlyList<Indicador> Indicadores, IReadOnlyList<MotivoGestion> Motivos,
    int Total, IReadOnlyList<DetalleOperacion> Detalles, IReadOnlyList<DetalleMotivo> DetallesMotivos, int TotalMotivos);
public sealed record MovimientoCuenta(Guid Id, Guid EnvioId, DateTimeOffset FechaUtc, string Concepto,
    decimal ImporteUyu, decimal TotalPersistidoUyu, string Tratamiento, Guid? LiquidacionId);
public sealed record LiquidacionResumen(Guid Id, Guid ComercioId, string Estado, DateTimeOffset DesdeUtc,
    DateTimeOffset HastaUtc, DateTimeOffset CorteUtc, decimal TotalUyu, Guid PreparadaPor,
    DateTimeOffset PreparadaEnUtc, Guid? ConfirmadaPor, DateTimeOffset? ConfirmadaEnUtc);
public sealed record LineaLiquidacion(Guid MovimientoId, Guid EnvioId, DateTimeOffset FechaUtc,
    string Concepto, decimal ImporteUyu, string Fuente);
public sealed record TransicionLiquidacion(string Estado, Guid ActorId, DateTimeOffset FechaUtc);
public sealed record LiquidacionDetalle(LiquidacionResumen Documento, IReadOnlyList<LineaLiquidacion> Lineas, IReadOnlyList<TransicionLiquidacion> Transiciones);
public sealed record CuentaGestion(Guid OperadorId, Guid ComercioId, decimal SaldoOperativoUyu,
    decimal TotalPeriodoUyu, decimal PendienteLiquidarUyu, int Total, IReadOnlyList<MovimientoCuenta> Movimientos,
    IReadOnlyList<LiquidacionResumen> Liquidaciones);
public sealed record CorreccionRegistrada(Guid Id);
public sealed record CorregirCuenta(Guid Id, Guid MovimientoOriginalId, decimal ImporteUyu, string Motivo);
public sealed record PrepararLiquidacion(Guid Id, Guid ComercioId, DateTimeOffset DesdeUtc, DateTimeOffset HastaUtc);

public sealed record OpcionGestion(Guid Id, string Nombre);
public sealed record OpcionesGestion(IReadOnlyList<OpcionGestion> Opciones, bool HayMas);
