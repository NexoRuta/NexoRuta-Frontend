using System.Security.Claims;
using NexoRuta.ApiClient.Contracts;

namespace NexoRuta.ApiClient;

public static class SesionUsuario
{
    public const string Operador = "Operador";
    public const string Comercio = "Comercio";
    public const string ClaimAccesoId = "nexoruta:acceso_id";
    public const string ClaimTipoAcceso = "nexoruta:tipo_acceso";

    public static ClaimsPrincipal CrearPrincipal(UsuarioActualResponse usuario, string esquema)
        => new(new ClaimsIdentity([
            new Claim(ClaimTypes.NameIdentifier, usuario.UsuarioId.ToString()),
            new Claim(ClaimTypes.Name, usuario.UsuarioEmail),
            new Claim(ClaimTipoAcceso, usuario.Tipo),
            new Claim(ClaimAccesoId, usuario.AccesoId.ToString())
        ], esquema));

    public static Guid ObtenerAccesoId(ClaimsPrincipal usuario)
        => Guid.TryParse(usuario.FindFirst(ClaimAccesoId)?.Value, out var accesoId) && accesoId != Guid.Empty
            ? accesoId
            : throw new InvalidOperationException("No hay un usuario seleccionado en esta sesión.");
}
