namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record EnvioResponse(
    Guid Id,
    Guid OperadorId,
    Guid ComercioId,
    Guid CreadoPorUsuarioId,
    string UsuarioEmail,
    string OperadorNombre,
    string ComercioNombre,
    string DestinatarioNombre,
    string Direccion,
    string Estado,
    IReadOnlyList<BultoResponse> Bultos);
