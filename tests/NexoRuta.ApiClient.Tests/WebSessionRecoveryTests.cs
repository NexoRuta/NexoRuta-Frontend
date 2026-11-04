using System.Net;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSameSignInStopsAtLoginWithoutDeletingCookieOrLooping(bool commerce)
    {
        var api = new FakeApi { PermissionStatusOverride = HttpStatusCode.Unauthorized };
        using WebApplicationFactory<NexoRuta.Commerce.Components.App>? cf = commerce ? new CommerceFactory(api) : null;
        using var bf = commerce ? null : new BackofficeFactory(api);
        using var client = commerce ? cf!.CreateClient(new() {AllowAutoRedirect=false,HandleCookies=true}) : Client(bf!);
        using var login = await LoginAsync(client, commerce ? "commerce@example.test" : "user@example.test");
        using var rejected = await client.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
        var uri = new Uri(client.BaseAddress!, rejected.Headers.Location!);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
        Assert.Equal("true", query["expirada"]);
        Assert.True(Guid.TryParseExact(query["rechazo"],"N",out _));
        Assert.False(rejected.Headers.TryGetValues("Set-Cookie",out var cookies) && cookies.Any(c=>c.StartsWith("NexoRuta.")));
        var before = api.Calls.Count(c=>c.Path=="/api/usuarios/actual");
        using var recovery = await client.GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK,recovery.StatusCode);
        Assert.Contains("Tu sesión ya no es válida",WebUtility.HtmlDecode(await recovery.Content.ReadAsStringAsync()));
        Assert.Equal(before,api.Calls.Count(c=>c.Path=="/api/usuarios/actual"));
        // A forged/different correlation marker permits one probe, never an endless cycle.
        using var forged=await client.GetAsync("/ingresar?expirada=true&ReturnUrl=%2Fusuarios&rechazo="+Guid.NewGuid().ToString("N"));
        Assert.Equal(HttpStatusCode.Redirect,forged.StatusCode);
        using var stillRejected=await client.GetAsync(forged.Headers.Location);
        using var stops=await client.GetAsync(stillRejected.Headers.Location);
        Assert.Equal(HttpStatusCode.OK,stops.StatusCode);
        Assert.False(recovery.Headers.TryGetValues("Set-Cookie",out cookies) && cookies.Any(c=>c.StartsWith("NexoRuta.")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CookieExpiryRequiresExplicitSignInAndDoesNotCallApi(bool commerce)
    {
        var api=new FakeApi(); var clock=new CookieClock();
        using var cf=commerce ? new CommerceFactory(api).WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,o=>o.TimeProvider=clock))) : null;
        using var bf=commerce ? null : new BackofficeFactory(api).WithWebHostBuilder(b=>b.ConfigureTestServices(s=>s.Configure<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme,o=>o.TimeProvider=clock)));
        using var client=commerce ? cf!.CreateClient(new(){AllowAutoRedirect=false,HandleCookies=true}) : Client(bf!);
        using var login=await LoginAsync(client,commerce?"commerce@example.test":"user@example.test");
        clock.Now=DateTimeOffset.UtcNow.AddHours(2);var calls=api.Calls.Count;
        using var rejected=await client.GetAsync("/usuarios?private=never-copy");
        Assert.Equal(HttpStatusCode.Redirect,rejected.StatusCode);Assert.DoesNotContain("never-copy",rejected.Headers.Location!.OriginalString);
        Assert.Equal(calls,api.Calls.Count);
        using var recovery=await client.GetAsync(rejected.Headers.Location);
        Assert.Equal(HttpStatusCode.OK,recovery.StatusCode);
        Assert.Contains("Tu sesión ya no es válida",WebUtility.HtmlDecode(await recovery.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task RecoveryNeverEchoesQuerySecretsAndMarkersAreNotCredentials()
    {
        var api=new FakeApi();using var factory=new BackofficeFactory(api);using var client=Client(factory);
        using var response=await client.GetAsync("/ingresar?ReturnUrl="+Uri.EscapeDataString("/usuarios?token=private-secret#form"));
        var html=await response.Content.ReadAsStringAsync();Assert.DoesNotContain("private-secret",html);Assert.Contains("value=\"/usuarios\"",html);
        var user=new UsuarioActualResponse(OwnAccessId,CurrentUserId,Guid.NewGuid(),null,"synthetic@example.test","Operator",null,SesionUsuario.Operador,OwnRoleId,"role");
        var first=SesionUsuario.CrearPrincipal(user,"secret-token","Cookies");var second=SesionUsuario.CrearPrincipal(user,"secret-token","Cookies");
        Assert.NotEqual(SesionUsuario.IngresoWeb(first),SesionUsuario.IngresoWeb(second));
        var location=RutaSesion.Ingreso("/usuarios",true,SesionUsuario.IngresoWeb(first));Assert.DoesNotContain("secret-token",location);
        Assert.Equal("secret-token",SesionUsuario.ObtenerToken(first));
    }
    private sealed class CookieClock:TimeProvider { public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now; }
}
