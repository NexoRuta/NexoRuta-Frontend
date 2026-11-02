namespace NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

public sealed record EnvioCreadoResponse(
    Guid Id,
    Guid OperadorId,
    Guid ComercioId,
    Guid CreadoPorUsuarioId,
    string UsuarioEmail,
    string OperadorNombre,
    string ComercioNombre,
    IReadOnlyList<BultoResponse> Bultos,
    DateTimeOffset CreadoEnUtc);
