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


        const string token = "server-session-token";
        var principal = SesionUsuario.CrearPrincipal(usuario, token, "Cookies");

        Assert.True(principal.Identity!.IsAuthenticated);
        Assert.Equal(usuario.UsuarioEmail, principal.Identity.Name);
        Assert.Equal(tipo, principal.FindFirst(SesionUsuario.ClaimTipoAcceso)?.Value);
        Assert.Empty(principal.FindAll(ClaimTypes.Role));
        Assert.Equal(usuario.AccesoId, SesionUsuario.ObtenerAccesoId(principal));
        Assert.Equal(token, SesionUsuario.ObtenerToken(principal));
        Assert.DoesNotContain(token, principal.Identity!.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ObtenerAccesoId_NoInventaUnaIdentidadSiLaSesionEstaVacia()
        => Assert.Throws<InvalidOperationException>(() => SesionUsuario.ObtenerAccesoId(new ClaimsPrincipal()));

    [Fact]
    public void ObtenerToken_NoInventaUnaSesionSiFaltaElToken()
        => Assert.Throws<InvalidOperationException>(() => SesionUsuario.ObtenerToken(new ClaimsPrincipal()));

    [Fact]
    public void Principal_CadaSesionConservaSuPropioToken()
    {
        var user = new UsuarioActualResponse(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "admin@test.local",
            "Operator", null, SesionUsuario.Operador, Guid.NewGuid(), "dynamic-role");
        var first = SesionUsuario.CrearPrincipal(user, "jwt-first", "Cookies");
        var second = SesionUsuario.CrearPrincipal(user with { UsuarioEmail = "other@test.local" }, "jwt-second", "Cookies");

        Assert.Equal("jwt-first", SesionUsuario.ObtenerToken(first));
        Assert.Equal("jwt-second", SesionUsuario.ObtenerToken(second));
    }
}
