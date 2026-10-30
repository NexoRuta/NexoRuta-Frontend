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
    long Version,
    IReadOnlyList<BultoResponse> Bultos)
{
    public bool DestinoConfirmado { get; init; }
    public string? ReferenciaExterna { get; init; }
    public string? DestinatarioEmail { get; init; }
    public bool AdmiteCancelacion() => Estado is "Admitido" or "EnDeposito" or "AsignadoRuta" or "EnTransito" or "NoEntregado" or "Reprogramado" or "EnDevolucion";
}
