namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;

public sealed record CambiarEstadoOrganizacionRequest(string Estado, string Motivo, long Version);
