using System.Text.Json.Serialization;

namespace NexoRuta.ApiClient.Features.ReglasOperativas.Contracts;

public static class ReglasOperativasApiRoutes
{
    public const string Base = "api/configuracion/reglas-operativas";
    public const string Draft = Base + "/borrador";
    public const string Publish = Draft + "/publicar";
    public const string Publications = Base + "/publicaciones";
}

[JsonConverter(typeof(TipoPruebaEntregaJsonConverter))]
public enum TipoPruebaEntrega { FirmaEnPantalla, Fotografia, NombreReceptor, DocumentoReceptor }

[JsonConverter(typeof(PoliticaDevolucionJsonConverter))]
public enum PoliticaDevolucion { DecisionOperativa, IniciarAutomaticamente }

[JsonConverter(typeof(EstadoVersionReglasOperativasJsonConverter))]
public enum EstadoVersionReglasOperativas { Borrador, Publicada }

public sealed class TipoPruebaEntregaJsonConverter()
    : JsonStringEnumConverter<TipoPruebaEntrega>(allowIntegerValues: false) { }

public sealed class PoliticaDevolucionJsonConverter()
    : JsonStringEnumConverter<PoliticaDevolucion>(allowIntegerValues: false) { }

public sealed class EstadoVersionReglasOperativasJsonConverter()
    : JsonStringEnumConverter<EstadoVersionReglasOperativas>(allowIntegerValues: false) { }

public sealed record ReemplazarBorradorReglasOperativasRequest(
    long VersionConcurrencia,
    int? MaxIntentosEntrega,
    int? IntervaloEntreIntentosMinutos,
    PoliticaDevolucion? PoliticaDevolucionAlAgotarIntentos,
    int? PlazoEstandarMinutos,
    int? PlazoUrgenteMinutos,
    int? PlazoValidacionPruebaMinutos,
    IReadOnlyList<TipoPruebaEntrega> RequisitosPrueba,
    IReadOnlyList<MotivoNoEntregaRequest> Motivos);

public sealed record PublicarBorradorReglasOperativasRequest(long VersionConcurrencia);
public sealed record MotivoNoEntregaRequest(string Codigo, string Descripcion, bool Disponible);
public sealed record MotivoNoEntregaResponse(string Codigo, string Descripcion, bool Disponible);

public sealed record ReglasOperativasEstadoResponse(
    bool Configurado,
    VersionReglasOperativasResumenResponse? PublicacionVigente,
    VersionReglasOperativasResumenResponse? Borrador);

public sealed record VersionReglasOperativasResumenResponse(
    Guid VersionReglasOperativasId,
    EstadoVersionReglasOperativas Estado,
    int? NumeroVersionPublicada,
    long VersionConcurrencia,
    DateTimeOffset CreadaUtc,
    DateTimeOffset? PublicadaUtc);

public sealed record PaginaPublicacionesReglasOperativasResponse(
    IReadOnlyList<VersionReglasOperativasResumenResponse> Items,
    int Pagina,
    int TamanoPagina,
    int Total);

public sealed record VersionReglasOperativasResponse(
    Guid VersionReglasOperativasId,
    EstadoVersionReglasOperativas Estado,
    int? NumeroVersionPublicada,
    long VersionConcurrencia,
    int? MaxIntentosEntrega,
    int? IntervaloEntreIntentosMinutos,
    PoliticaDevolucion? PoliticaDevolucionAlAgotarIntentos,
    int? PlazoEstandarMinutos,
    int? PlazoUrgenteMinutos,
    int? PlazoValidacionPruebaMinutos,
    IReadOnlyList<TipoPruebaEntrega> RequisitosPrueba,
    IReadOnlyList<MotivoNoEntregaResponse> Motivos,
    DateTimeOffset CreadaUtc,
    DateTimeOffset? PublicadaUtc);
