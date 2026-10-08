using System.Security.Claims;
using NexoRuta.ApiClient.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class SesionUsuarioTests
{
    [Theory]
    [InlineData(SesionUsuario.Comercio)]
    [InlineData(SesionUsuario.Operador)]
    public void CrearPrincipal_ConservaLaIdentidadYElTipoDevueltosPorLaApi(string tipo)
    {
        var usuario = new UsuarioActualResponse(Guid.CreateVersion7(), Guid.CreateVersion7(), tipo == SesionUsuario.Operador ? Guid.CreateVersion7() : null,
            tipo == SesionUsuario.Comercio ? Guid.CreateVersion7() : null,
            "usuario@empresa.local", tipo == SesionUsuario.Operador ? "Operador de prueba" : null,
            tipo == SesionUsuario.Comercio ? "Comercio de prueba" : null, tipo, tipo == SesionUsuario.Comercio);

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
