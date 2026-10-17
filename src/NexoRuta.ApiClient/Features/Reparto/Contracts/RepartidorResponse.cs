namespace NexoRuta.ApiClient.Features.Reparto.Contracts;

public sealed record RepartidorResponse(Guid AccesoId, Guid UsuarioId, Guid OperadorId,
    string Email, string Nombre, bool Activo, long Version);
