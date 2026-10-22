using System.Text.Json;

namespace NexoRuta.ApiClient.Features.ZonasCobertura.Contracts;

public sealed record GeoJsonGeometryDto(string Type, JsonElement Coordinates);
public sealed record CrearZonaCoberturaRequest(string Nombre, GeoJsonGeometryDto? Geometria = null, string? CodigoDepartamento = null);
public sealed record ActualizarZonaCoberturaRequest(string Nombre, GeoJsonGeometryDto Geometria, long Version);
public sealed record CambiarEstadoZonaRequest(string Estado, long Version);
public sealed record CrearFranjaEntregaRequest(short DiaSemanaIso, string HoraInicio, string HoraFin, long VersionZona);
public sealed record ActualizarFranjaEntregaRequest(short DiaSemanaIso, string HoraInicio, string HoraFin, string Estado, long VersionZona);
public sealed record ConsultarCoberturaRequest(double Longitud, double Latitud);

public sealed record ZonaCoberturaResumenResponse(Guid Id, string Nombre, string Estado, int FranjasActivas,
    long Version, int RevisionActual, DateTimeOffset ActualizadaUtc);
public sealed record PaginaZonasCoberturaResponse(IReadOnlyList<ZonaCoberturaResumenResponse> Items,
    int Pagina, int TamanoPagina, int Total);
public sealed record FranjaEntregaResponse(Guid Id, short DiaSemanaIso, string HoraInicio, string HoraFin, string Estado);
public sealed record ZonaCoberturaDetalleResponse(Guid Id, string Nombre, string Estado, GeoJsonGeometryDto Geometria,
    string ZonaHoraria, IReadOnlyList<FranjaEntregaResponse> Franjas, long Version, int RevisionActual,
    string OrigenGeometria, string? CodigoDepartamento, string? VersionFuente,
    DateTimeOffset CreadaUtc, DateTimeOffset ActualizadaUtc);
public sealed record FranjasEntregaResponse(IReadOnlyList<FranjaEntregaResponse> Items, long VersionZona, int RevisionActual);
public sealed record FranjaEntregaCambioResponse(FranjaEntregaResponse Franja, long VersionZona, int RevisionActual);
public sealed record DepartamentoReferenciaResponse(string Codigo, string VersionFuente, string Nombre,
    string UrlFuente, string Atribucion, string Licencia, DateTimeOffset ImportadaUtc);
public sealed record BoundsGeoJsonResponse(double Oeste, double Sur, double Este, double Norte);
public sealed record DepartamentoReferenciaGeometriaResponse(string Codigo, string VersionFuente, string Nombre,
    GeoJsonGeometryDto Geometria, BoundsGeoJsonResponse Limites);
public sealed record TerritorioUruguayResponse(string CodigoPais, string VersionFuente,
    GeoJsonGeometryDto Geometria, BoundsGeoJsonResponse Limites);
public sealed record CoincidenciaCoberturaResponse(Guid ZonaId, string Nombre, int RevisionActual,
    IReadOnlyList<FranjaEntregaResponse> FranjasActivas);
public sealed record ConsultaCoberturaResponse(string Resultado, bool RequiereSeleccionDeZona,
    IReadOnlyList<CoincidenciaCoberturaResponse> Coincidencias);
