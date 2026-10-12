using System.Security.Claims;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.ApiClient.Core.Session;

namespace NexoRuta.ApiClient.Tests;

public sealed class SesionUsuarioTests
{
    [Theory]
    [InlineData(SesionUsuario.Comercio)]
    [InlineData(SesionUsuario.Operador)]
    public void CrearPrincipal_ConservaLaIdentidadYElTipoDevueltosPorLaApi(string tipo)
    {

        var usuario = new UsuarioActualResponse(
            AccesoId: Guid.CreateVersion7(),
            UsuarioId: Guid.CreateVersion7(),
            OperadorId: tipo == SesionUsuario.Operador
                ? Guid.CreateVersion7()
                : null,
            ComercioId: tipo == SesionUsuario.Comercio
                ? Guid.CreateVersion7()
                : null,
            UsuarioEmail: "usuario@empresa.local",
            OperadorNombre: tipo == SesionUsuario.Operador
                ? "Operador de prueba"
                : null,
            ComercioNombre: tipo == SesionUsuario.Comercio
                ? "Comercio de prueba"
                : null,
            Tipo: tipo,
            RolId: Guid.CreateVersion7(),
            RolCodigo: tipo == SesionUsuario.Operador
                ? "administrador_operador"
                : "propietario_comercio"
        );


        var principal = SesionUsuario.CrearPrincipal(usuario, "Cookies");

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal(usuario.UsuarioEmail, principal.Identity.Name);
        Assert.Equal(tipo, principal.FindFirst(SesionUsuario.ClaimTipoAcceso)?.Value);
        Assert.Empty(principal.FindAll(ClaimTypes.Role));
        Assert.Equal(usuario.AccesoId, SesionUsuario.ObtenerAccesoId(principal));
    }

    [Fact]
    public void ObtenerAccesoId_NoInventaUnaIdentidadSiLaSesionEstaVacia()
        => Assert.Throws<InvalidOperationException>(() => SesionUsuario.ObtenerAccesoId(new ClaimsPrincipal()));
}
