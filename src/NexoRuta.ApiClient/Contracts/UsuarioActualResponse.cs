namespace NexoRuta.ApiClient.Contracts;

public sealed record UsuarioActualResponse(
    Guid AccesoId,
    Guid UsuarioId,
    Guid? OperadorId,
    Guid? ComercioId,
    string UsuarioEmail,
    string? OperadorNombre,
    string? ComercioNombre,
    string Tipo,
    bool EsPropietario);
