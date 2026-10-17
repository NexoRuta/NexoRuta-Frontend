using System.Security.Claims;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

namespace NexoRuta.ApiClient.Core.Session;

public static class SesionUsuario
{
    public const string SinOrganizacion = "SinOrganizacion";
    public const string Plataforma = "Plataforma";
    public const string Operador = "Operador";
    public const string Comercio = "Comercio";
    public const string OrganizacionSuspendida = "OrganizacionSuspendida";
    public const string ClaimAccesoId = "nexoruta:acceso_id";
    public const string ClaimTipoAcceso = "nexoruta:tipo_acceso";
    public const string ClaimApiToken = "nexoruta:api_access_token";

    public static ClaimsPrincipal CrearPrincipal(UsuarioActualResponse usuario, string token, string esquema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioId.ToString()),
            new Claim(ClaimTypes.Name, usuario.UsuarioEmail),
            new Claim(ClaimTipoAcceso, usuario.Tipo),
            new Claim(ClaimApiToken, token)
        };
        if (usuario.AccesoId is Guid accessId)
            claims.Add(new Claim(ClaimAccesoId, accessId.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, esquema));
    }

    public static ClaimsPrincipal CrearPrincipalSuspendido(string token, string esquema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        return new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTipoAcceso, OrganizacionSuspendida), new Claim(ClaimApiToken, token)], esquema));
    }

    public static string ObtenerToken(ClaimsPrincipal usuario)
        => usuario.FindFirst(ClaimApiToken)?.Value is { Length: > 0 } token
            ? token
            : throw new InvalidOperationException("La sesión no contiene un token de API.");

    public static Guid ObtenerAccesoId(ClaimsPrincipal usuario)
        => Guid.TryParse(usuario.FindFirst(ClaimAccesoId)?.Value, out var accesoId) && accesoId != Guid.Empty
            ? accesoId
            : throw new InvalidOperationException("No hay un usuario seleccionado en esta sesión.");
}
