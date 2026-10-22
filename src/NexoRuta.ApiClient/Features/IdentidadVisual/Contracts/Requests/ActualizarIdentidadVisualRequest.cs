namespace NexoRuta.ApiClient.Features.IdentidadVisual.Contracts.Requests;

public sealed record ActualizarIdentidadVisualRequest(
    int VersionEsperada,
    string? NombreComercial,
    string? CorreoPublico,
    string? TelefonoPublico,
    string? ColorPrimario,
    string? ColorSecundario,
    bool QuitarLogo,
    byte[]? LogoContenido);
