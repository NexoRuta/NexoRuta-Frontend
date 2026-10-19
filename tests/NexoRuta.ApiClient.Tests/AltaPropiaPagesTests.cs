using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Configuration;

namespace NexoRuta.ApiClient.Tests;

public sealed class AltaPropiaPagesTests
{
    [Fact]
    public async Task PublicHomeAndCredentialPagesExposeNavigableEntryWithoutSession()
    {
        using var factory = new Factory(new Api());
        using var client = Client(factory);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = WebUtility.HtmlDecode(await home.Content.ReadAsStringAsync());
        Assert.Contains("Crear cuenta", html);
        Assert.Contains("href=\"/registrarse\"", html);
        Assert.Contains("href=\"/ingresar\"", html);
        Assert.Contains("gestión de envíos", html);
        foreach (var (path, reciprocal) in new[] { ("/ingresar", "/registrarse"), ("/registrarse", "/ingresar") })
        {
            using var response = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var content = await response.Content.ReadAsStringAsync();
            Assert.Contains($"href=\"{reciprocal}\"", content);
            Assert.Contains("href=\"/\"", content);
        }
        Assert.Contains("href=\"/recuperar-contrasena\"", await client.GetStringAsync("/ingresar"));
        foreach (var path in new[] { "/envios/crear", "/envios", "/usuarios", "/roles", "/organizacion" })
            Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task ResetLinkIsRemovedFromRenderedUrlAndProtectedInAnHttpOnlyCookie()
    {
        var api = new Api();
        using var factory = new Factory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
        const string token = "identity-reset-token-secret";
        var userId = Guid.NewGuid();

        using var link = await client.GetAsync($"/restablecer-contrasena?userId={userId:D}&token={token}");
        Assert.Equal(HttpStatusCode.Redirect, link.StatusCode);
        Assert.Equal("/restablecer-contrasena", link.Headers.Location!.OriginalString);
        var cookie = link.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("NexoRuta.PasswordRecovery=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=strict", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(token, cookie, StringComparison.Ordinal);

        using var page = await client.GetAsync(link.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Equal("no-referrer", page.Headers.GetValues("Referrer-Policy").Single());
        Assert.Contains("no-store", page.Headers.CacheControl!.ToString(), StringComparison.OrdinalIgnoreCase);
        var html = await page.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, html, StringComparison.Ordinal);
        Assert.DoesNotContain(userId.ToString("D"), html, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);

        var csrf = await Token(client, "/restablecer-contrasena");
        const string password = "A-new-browser-password-2026!";
        using var saved = await client.PostAsync("/restablecer-contrasena", Form(csrf, ("NuevaPassword", password)));
        Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        var result = await saved.Content.ReadAsStringAsync();
        Assert.Contains("La contraseña se guardó", result, StringComparison.Ordinal);
        Assert.DoesNotContain(token, result, StringComparison.Ordinal);
        Assert.DoesNotContain(password, result, StringComparison.Ordinal);
        Assert.Equal(userId, api.ResetRequest!.Value.GetProperty("usuarioId").GetGuid());
        Assert.Equal(token, api.ResetRequest.Value.GetProperty("token").GetString());
        Assert.Equal(password, api.ResetRequest.Value.GetProperty("nuevaPassword").GetString());
    }

    [Theory]
    [InlineData(false, "/organizacion")]
    [InlineData(true, "/envios")]
    public async Task HomeOffersOnlyTheWorkAreaForTheCurrentCommerceSession(bool member, string destination)
    {
        using var factory = new Factory(new Api { Member = member });
        using var client = Client(factory);
        using var login = await client.PostAsync("/ingresar?ReturnUrl=//evil.test", Form(await Token(client, "/ingresar"), ("Email", "person@example.test"), ("Password", "Secret-password-123")));
        Assert.Equal(destination, login.Headers.Location!.OriginalString);
        using var home = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = WebUtility.HtmlDecode(await home.Content.ReadAsStringAsync());
        Assert.Contains($"href=\"{destination}\"", html);
        Assert.Contains(member ? "Área de trabajo" : "Crear mi organización", html);
        Assert.DoesNotContain("identity-jwt", html);
        Assert.DoesNotContain("evil.test", html);
        if (member)
        {
            using var create = await client.GetAsync("/envios/crear");
            Assert.Equal(HttpStatusCode.OK, create.StatusCode);
            Assert.Contains("envio-operador", await create.Content.ReadAsStringAsync());
            using var shipments = await client.GetAsync("/envios");
            Assert.Equal(HttpStatusCode.OK, shipments.StatusCode);
            Assert.Contains("href=\"/envios/crear\"", await shipments.Content.ReadAsStringAsync());
        }
        if (!member)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/organizacion")).StatusCode);
            foreach (var path in new[] { "/envios/crear", "/envios", "/usuarios", "/roles" })
            {
                using var denied = await client.GetAsync(path);
                Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
                Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath);
            }
        }
    }

    [Theory]
    [InlineData("Operador")]
    [InlineData("Plataforma")]
    public async Task OtherPortalAccountsHaveConfiguredLinksButAreNotAdmittedToCommerce(string type)
    {
        using var factory = new Factory(new Api { TypeOverride = type });
        using var client = Client(factory);
        using var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("http://localhost:5046/ingresar", html);
        Assert.Contains("http://localhost:5104/ingresar", html);
        using var login = await client.PostAsync("/ingresar", Form(await Token(client, "/ingresar"), ("Email", "person@example.test"), ("Password", "Secret-password-123")));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.False(login.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(x => x.StartsWith("NexoRuta.Commerce=")));
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/envios/crear")).StatusCode);
    }

    [Fact]
    public async Task CrossPortalLinksUseConfiguredBrowserAddresses()
    {
        using var factory = new Factory(new Api(), new Dictionary<string, string?> {
            ["Portales:BackofficeLoginUrl"] = "https://operator.example.test/ingresar",
            ["Portales:AdminLoginUrl"] = "https://support.example.test/ingresar"
        });
        using var client = Client(factory);
        var html = await client.GetStringAsync("/");
        Assert.Contains("href=\"https://operator.example.test/ingresar\"", html);
        Assert.Contains("href=\"https://support.example.test/ingresar\"", html);
        Assert.DoesNotContain("http://backoffice:", html);
        Assert.DoesNotContain("http://admin:", html);
    }

    [Fact]
    public async Task PublicRegistrationValidatesConfirmationAndDoesNotRenderSecrets()
    {
        var api = new Api();
        using var factory = new Factory(api);
        using var client = Client(factory);
        var token = await Token(client, "/registrarse");
        var fields = new[] { ("Email", "person@example.test"), ("Password", "Secret-password-123"), ("ConfirmPassword", "different-secret") };
        using var invalid = await client.PostAsync("/registrarse", Form(token, fields));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var html = await invalid.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Secret-password-123", html);
        Assert.DoesNotContain("different-secret", html);
        Assert.Empty(api.Registration);
        using var success = await client.PostAsync("/registrarse", Form(token, ("Email", "person@example.test"), ("Password", "Secret-password-123"), ("ConfirmPassword", "Secret-password-123")));
        Assert.Equal(HttpStatusCode.Redirect, success.StatusCode);
        Assert.StartsWith("/ingresar", success.Headers.Location!.OriginalString);
        Assert.Equal(new[] { "email", "password" }, api.Registration.Single().EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/registrarse", Form("invalid"))).StatusCode);
    }

    [Theory]
    [InlineData("comercio")]
    [InlineData("operador")]
    public async Task IdentityOnlyLoginAndSelfServiceKeepJwtServerSideAndRequireDestinationLogin(string kind)
    {
        var api = new Api();
        using var factory = new Factory(api);
        using var client = Client(factory);
        using var login = await client.PostAsync("/ingresar?ReturnUrl=https://evil.test", Form(await Token(client, "/ingresar"), ("Email", "person@example.test"), ("Password", "Secret-password-123")));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/organizacion", login.Headers.Location!.OriginalString);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("NexoRuta.Commerce="));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("identity-jwt", cookie);
        using var page = await client.GetAsync("/organizacion");
        var html = WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Contains("Todavía no pertenecés a una organización", html);
        Assert.DoesNotContain("identity-jwt", html);
        Assert.Contains("data-single-submit", html);
        using var created = await client.PostAsync("/organizacion", Form(await Token(client, "/organizacion"), ("Tipo", kind), ("Nombre", "My business"), ("UsuarioId", Guid.NewGuid().ToString()), ("Permisos", "anything")));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal($"/organizacion-creada/{kind}", created.Headers.Location!.OriginalString);
        Assert.Equal(kind == "operador" ? "/api/alta-propia/operadores" : "/api/alta-propia/comercios", api.CreatedPath);
        Assert.Equal(new[] { "nombre" }, api.Creation!.Value.EnumerateObject().Select(x => x.Name));
        Assert.Equal("identity-jwt", api.CreationToken);
        using var destination = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, destination.StatusCode);
        var destinationHtml = WebUtility.HtmlDecode(await destination.Content.ReadAsStringAsync());
        Assert.Contains(kind == "operador" ? "Backoffice" : "Ingresar", destinationHtml);
        Assert.DoesNotContain("identity-jwt", destinationHtml);
        Assert.DoesNotContain("access_token", destinationHtml);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/organizacion")).StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task OnboardingHandlesApiFailures(HttpStatusCode failure)
    {
        var api = new Api { Failure = failure };
        using var factory = new Factory(api);
        using var client = Client(factory);
        await client.PostAsync("/ingresar", Form(await Token(client, "/ingresar"), ("Email", "person@example.test"), ("Password", "Secret-password-123")));
        using var result = await client.PostAsync("/organizacion", Form(await Token(client, "/organizacion"), ("Tipo", "comercio"), ("Nombre", "Business")));
        Assert.Equal(failure == HttpStatusCode.Unauthorized ? HttpStatusCode.Redirect : HttpStatusCode.OK, result.StatusCode);
        Assert.DoesNotContain("identity-jwt", await result.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ExistingMembershipDoesNotRenderOrSubmitOnboardingForm()
    {
        var api = new Api { Member = true };
        using var factory = new Factory(api);
        using var client = Client(factory);
        var loginToken = await Token(client, "/ingresar");
        await client.PostAsync("/ingresar", Form(loginToken, ("Email", "person@example.test"), ("Password", "Secret-password-123")));
        Assert.Equal("/envios", (await client.GetAsync("/organizacion")).Headers.Location!.OriginalString);
        using var forged = await client.PostAsync("/organizacion", Form(await Token(client, "/ingresar"), ("Tipo", "comercio"), ("Nombre", "Second")));
        Assert.Equal(HttpStatusCode.Redirect, forged.StatusCode);
        Assert.Null(api.CreatedPath);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task RegistrationHandlesControlledErrorsWithoutEchoingPasswords(HttpStatusCode status)
    {
        var api = new Api { RegistrationFailure = status };
        using var factory = new Factory(api);
        using var client = Client(factory);
        using var response = await client.PostAsync("/registrarse", Form(await Token(client, "/registrarse"),
            ("Email", "person@example.test"), ("Password", "Secret-password-123"), ("ConfirmPassword", "Secret-password-123")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Controlled registration error", html);
        Assert.DoesNotContain("Secret-password-123", html);
    }

    private static HttpClient Client(Factory factory) => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    private static async Task<string> Token(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var input = Regex.Matches(html, "<input[^>]*>").Select(x => x.Value).First(x => x.Contains("name=\"__RequestVerificationToken\""));
        return WebUtility.HtmlDecode(Regex.Match(input, "value=\"([^\"]+)\"").Groups[1].Value);
    }
    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] fields)
        => new(fields.Append(("__RequestVerificationToken", token)).ToDictionary(x => x.Item1, x => x.Item2));

    private sealed class Factory(Api api, Dictionary<string, string?>? settings = null) : WebApplicationFactory<NexoRuta.Commerce.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            if (settings is not null) builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(settings));
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("NexoRuta", options =>
                options.HttpMessageHandlerBuilderActions.Add(handler => handler.PrimaryHandler = api)));
        }
    }
    private sealed class Api : HttpMessageHandler
    {
        public List<JsonElement> Registration { get; } = [];
        public string? CreatedPath { get; private set; }
        public JsonElement? Creation { get; private set; }
        public JsonElement? ResetRequest { get; private set; }
        public string? CreationToken { get; private set; }
        public HttpStatusCode? RegistrationFailure { get; init; }
        public string? TypeOverride { get; init; }
        public bool Member { get; set; }
        public HttpStatusCode? Failure { get; init; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path == "/api/auth/register")
            {
                Registration.Add(await request.Content!.ReadFromJsonAsync<JsonElement>(ct));
                if (RegistrationFailure is not null) return Json(RegistrationFailure.Value, new { message = "Controlled registration error" });
                return Json(HttpStatusCode.Created, new { UsuarioId = Guid.NewGuid(), Email = "person@example.test" });
            }
            if (path == "/api/auth/login") return Json(HttpStatusCode.OK, new { AccessToken = "identity-jwt", TokenType = "Bearer", ExpiresAtUtc = DateTime.UtcNow.AddMinutes(15) });
            if (path == "/api/auth/password-recovery/reset")
            {
                ResetRequest = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/api/usuarios/actual") return Json(HttpStatusCode.OK, new {
                UsuarioId = Guid.NewGuid(), AccesoId = Member ? Guid.NewGuid() : (Guid?)null,
                UsuarioEmail = "person@example.test", Tipo = TypeOverride ?? (Member ? "Comercio" : "SinOrganizacion")
            });
            if (path.StartsWith("/api/alta-propia/"))
            {
                CreatedPath = path; Creation = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                CreationToken = request.Headers.Authorization?.Parameter;
                if (Failure is not null) return Json(Failure.Value, new { message = "Controlled error" });
                Member = true;
                return Json(HttpStatusCode.Created, new { UsuarioId = Guid.NewGuid() });
            }
            return Json(HttpStatusCode.Forbidden, new { message = "Denied" });
        }
        private static HttpResponseMessage Json(HttpStatusCode status, object value) => new(status) { Content = JsonContent.Create(value) };
    }
}
