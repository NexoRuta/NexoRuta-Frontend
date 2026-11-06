namespace NexoRuta.ApiClient.Features.Seguimiento.Contracts.Responses;

public sealed record SeguimientoEstadoResponse(
    string Estado,
    NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Responses.IdentidadVisualPublicaResponse? Identidad = null)
{ public VentanaEstimadaPublicaResponse? VentanaEstimada { get; init; } public IReadOnlyList<EventoPublicoResponse> Historial { get; init; } = []; public IReadOnlyList<BultoPublicoResponse> Bultos { get; init; } = []; public int TotalBultos { get; init; } public int BultosEntregados { get; init; } public int BultosDevueltos { get; init; } public bool EsRetiro { get; init; } }

public sealed record EventoPublicoResponse(string Estado, DateTimeOffset? OcurridaEnUtc, DateTimeOffset RegistradaEnUtc);
public sealed record BultoPublicoResponse(int Numero, string Resultado);

public sealed record VentanaEstimadaPublicaResponse(DateTimeOffset DesdeUtc, DateTimeOffset HastaUtc, string Origen, DateTimeOffset ActualizadaEnUtc);
