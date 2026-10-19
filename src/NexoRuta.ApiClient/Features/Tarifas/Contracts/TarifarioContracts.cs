using System.Text.Json.Serialization;

namespace NexoRuta.ApiClient.Features.Tarifas.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<ModalidadTarifaria>))]
public enum ModalidadTarifaria { Estandar, Urgente }

[JsonConverter(typeof(JsonStringEnumConverter<TipoAjusteTarifario>))]
public enum TipoAjusteTarifario { RecargoFijo, RecargoPorcentual, BonificacionFija, BonificacionPorcentual }

[JsonConverter(typeof(JsonStringEnumConverter<MonedaTarifaria>))]
public enum MonedaTarifaria { UYU }

[JsonConverter(typeof(JsonStringEnumConverter<EstadoVersionTarifario>))]
public enum EstadoVersionTarifario { Borrador, Publicada }

[JsonConverter(typeof(JsonStringEnumConverter<BaseAjusteTarifario>))]
public enum BaseAjusteTarifario { ImporteFijo, SubtotalOriginal }

[JsonConverter(typeof(JsonStringEnumConverter<OrigenSimulacionTarifario>))]
public enum OrigenSimulacionTarifario { Borrador, Publicacion }

public sealed record ReemplazarTarifasBorradorRequest(long VersionConcurrencia,
    IReadOnlyList<TarifaZonaModalidadRequest> Tarifas);
public sealed record PublicarBorradorTarifarioRequest(long VersionConcurrencia);
public sealed record TarifaZonaModalidadRequest(Guid ZonaId, ModalidadTarifaria Modalidad,
    decimal ImporteBaseUyu, decimal PrecioPorKilogramoUyu, decimal PrecioPorMetroCubicoUyu,
    IReadOnlyList<AjusteTarifarioRequest> Ajustes);
public sealed record AjusteTarifarioRequest(string Nombre, TipoAjusteTarifario Tipo,
    decimal? ImporteFijoUyu, decimal? Porcentaje);
public sealed record SimularTarifaRequest(Guid VersionTarifarioId, Guid ZonaId,
    ModalidadTarifaria Modalidad, IReadOnlyList<BultoSimulacionRequest> Bultos);
public sealed record BultoSimulacionRequest(decimal PesoGramos, decimal LargoCentimetros,
    decimal AnchoCentimetros, decimal AltoCentimetros);

public sealed record TarifarioEstadoResponse(bool Configurado,
    VersionTarifarioResumenResponse? PublicacionVigente, VersionTarifarioResumenResponse? Borrador);
public sealed record PaginaPublicacionesTarifarioResponse(IReadOnlyList<VersionTarifarioResumenResponse> Items,
    int Pagina, int TamanoPagina, int Total);
public sealed record VersionTarifarioResumenResponse(Guid VersionTarifarioId, EstadoVersionTarifario Estado,
    int? NumeroVersionPublicada, long VersionConcurrencia, MonedaTarifaria Moneda,
    DateTimeOffset CreadaUtc, DateTimeOffset? PublicadaUtc);
public sealed record VersionTarifarioResponse(Guid VersionTarifarioId, EstadoVersionTarifario Estado,
    int? NumeroVersionPublicada, long VersionConcurrencia, MonedaTarifaria Moneda,
    DateTimeOffset CreadaUtc, DateTimeOffset? PublicadaUtc, IReadOnlyList<TarifaZonaModalidadResponse> Tarifas);
public sealed record TarifaZonaModalidadResponse(Guid ZonaId, ModalidadTarifaria Modalidad,
    decimal ImporteBaseUyu, decimal PrecioPorKilogramoUyu, decimal PrecioPorMetroCubicoUyu,
    IReadOnlyList<AjusteTarifarioResponse> Ajustes);
public sealed record AjusteTarifarioResponse(string Nombre, TipoAjusteTarifario Tipo,
    decimal? ImporteFijoUyu, decimal? Porcentaje);
public sealed record SimulacionTarifaResponse(OrigenSimulacionTarifario Origen, Guid VersionTarifarioId,
    int? NumeroVersionPublicada, long VersionConcurrencia, Guid ZonaId, ModalidadTarifaria Modalidad,
    MonedaTarifaria Moneda, decimal PesoTotalKg, decimal VolumenTotalM3, decimal ImporteBaseUyu,
    decimal ComponentePesoUyu, decimal ComponenteVolumenUyu,
    IReadOnlyList<AjusteTarifarioAplicadoResponse> Ajustes, decimal SubtotalUyu, decimal RecargosUyu,
    decimal BonificacionesUyu, decimal TotalPrevioPisoUyu, bool PisoCeroAplicado, decimal TotalFinalUyu);
public sealed record AjusteTarifarioAplicadoResponse(string Nombre, TipoAjusteTarifario Tipo,
    BaseAjusteTarifario Base, decimal ValorConfigurado, decimal ImporteUyu);
