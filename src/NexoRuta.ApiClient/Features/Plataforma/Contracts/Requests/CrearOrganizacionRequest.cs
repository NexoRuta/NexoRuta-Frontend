using System.ComponentModel.DataAnnotations;

namespace NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;

public sealed class CrearOrganizacionRequest
{
    [Required, StringLength(160)]
    public string Nombre { get; init; } = "";
    [Required]
    public PrimerAdministradorRequest Administrador { get; init; } = null!;
}

public sealed class PrimerAdministradorRequest
{
    [Required, EmailAddress, StringLength(254)]
    public string Email { get; init; } = "";
    [Required, MinLength(12)]
    public string Password { get; init; } = "";
}
