namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;

public sealed record ResolverSolicitudBajaRequest(string Decision, string Motivo, long Version);
