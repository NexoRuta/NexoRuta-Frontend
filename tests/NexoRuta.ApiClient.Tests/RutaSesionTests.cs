using NexoRuta.ApiClient.Core.Session;
namespace NexoRuta.ApiClient.Tests;
public sealed class RutaSesionTests
{
    [Theory]
    [InlineData("/deposito/recepcion?codigo=123&token=private#data","/deposito/recepcion")]
    [InlineData("/rutas#private","/rutas")]
    [InlineData("/envios/crear","/envios/crear")]
    [InlineData("/usuarios","/usuarios")]
    public void OnlyLocalNavigationPathSurvives(string value,string expected)
    {
        Assert.Equal(expected,RutaSesion.Destino(value));
        Assert.DoesNotContain("private",RutaSesion.Ingreso(value,true));
    }
    [Theory]
    [InlineData("https://outside.example/test")]
    [InlineData("//outside.example/test")]
    [InlineData("/%2foutside.example")]
    [InlineData("/%252foutside.example")]
    [InlineData("/\\outside.example")]
    [InlineData("/../salir")]
    [InlineData("/./ingresar")]
    [InlineData("/ingresar?ReturnUrl=/ingresar")]
    [InlineData("/salir")]
    [InlineData("/api-indisponible")]
    [InlineData("/api/auth/mobile/refresh")]
    [InlineData("/rutas%0d%0aLocation:evil")]
    [InlineData("/restablecer-contrasena?token=private")]
    public void UnsafeOrLoopingDestinationsAreRejected(string value)
    { Assert.Null(RutaSesion.Destino(value));Assert.Equal("/ingresar?expirada=true",RutaSesion.Ingreso(value,true)); }
}
