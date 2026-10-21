namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record IncidenciaCierreResponse(Guid OperacionId, Guid EnvioId, Guid BultoId, string Codigo,
    long VersionBulto, DateTimeOffset OcurridaEnUtc, DateTimeOffset RecibidaEnUtc);
