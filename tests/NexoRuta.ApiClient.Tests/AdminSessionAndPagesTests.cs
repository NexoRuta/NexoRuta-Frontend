using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Options;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Session;

namespace NexoRuta.ApiClient.Tests;

public sealed class AdminSessionAndPagesTests
{
    private const string Password = "Correct-admin-password-2026!";
    private const string NewPassword = "New-admin-secret-2026!";
    private static readonly Guid OrganizationId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid CancellationRequestId = Guid.Parse("00000000-0000-0000-0000-000000000102");

    [Fact]
    public void AdminReferencesApiClientWithoutBackendOrOtherPortalAssemblies()
    {
        var references = typeof(NexoRuta.Admin.Features.Organizaciones.Pages.CrearModel).Assembly.GetReferencedAssemblies().Select(x => x.Name).ToArray();
        Assert.Contains("NexoRuta.ApiClient", references);
        Assert.DoesNotContain(references, x => x is "NexoRuta.Backoffice" or "NexoRuta.Commerce" or "NexoRuta.Tracking"
            or "NexoRuta.Api" or "NexoRuta.Infrastructure" or "NexoRuta.Application" or "NexoRuta.Domain");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/operadores")]
    [InlineData("/operadores/crear")]
    [InlineData("/operadores/00000000-0000-0000-0000-000000000101")]
    [InlineData("/comercios")]
    [InlineData("/comercios/crear")]
    [InlineData("/comercios/00000000-0000-0000-0000-000000000101")]
    public async Task DirectPagesRequireSession(string route)
    {
        using var factory = Factory(new FakeApi());
        using var client = Client(factory);
        using var response = await client.GetAsync(route);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/ingresar", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task RecoveryPageIsPublicAndLinkedFromPlatformLogin()
    {
        using var factory = Factory(new FakeApi());
        using var client = Client(factory);
        using var login = await client.GetAsync("/ingresar");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var loginHtml = await login.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/recuperar-contrasena\"", loginHtml);
        Assert.Contains("aria-label=\"Mostrar contraseña\"", loginHtml);
        Assert.Contains("Administración de plataforma", loginHtml);
        Assert.DoesNotContain("<footer", loginHtml, StringComparison.OrdinalIgnoreCase);
        using var recovery = await client.GetAsync("/recuperar-contrasena");
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var html = await recovery.Content.ReadAsStringAsync();
        Assert.Contains("cuenta asociada", WebUtility.HtmlDecode(html), StringComparison.OrdinalIgnoreCase);
        var styleLinks = html.Split("<link", StringSplitOptions.RemoveEmptyEntries).Select(part => part.Split('>').FirstOrDefault() ?? "");
        Assert.Contains(styleLinks, link => link.Contains("portal-design", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("<footer", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-referrer", recovery.Headers.GetValues("Referrer-Policy").Single());
    }

    [Fact]
    public async Task PlatformLoginUsesProtectedNonpersistentCookieBoundToJwtAndServerSideBearer()
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        using var login = await Login(client);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/", login.Headers.Location!.OriginalString);
        var cookie = login.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("NexoRuta.Admin=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jwt-platform", cookie);
        var options = factory.Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
        var ticket = options.TicketDataFormat.Unprotect(Uri.UnescapeDataString(cookie.Split(';')[0].Split('=', 2)[1]));
        Assert.NotNull(ticket);
        Assert.False(ticket.Properties.IsPersistent);
        Assert.InRange(ticket.Properties.ExpiresUtc!.Value, new DateTimeOffset(api.Expiry).AddSeconds(-1), new DateTimeOffset(api.Expiry));
        Assert.Equal("Plataforma", ticket.Principal.FindFirst(SesionUsuario.ClaimTipoAcceso)!.Value);
        using var home = await client.GetAsync("/");
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("/operadores", html);
        Assert.Contains("/comercios", html);
        Assert.Contains("/solicitudes-baja", html);
        Assert.Contains("/solicitudes-reactivacion", html);
        Assert.DoesNotContain("jwt-platform", html);
        Assert.DoesNotContain(Password, html);
        Assert.All(api.Calls.Where(x => x.Path != "/api/auth/login"), x => Assert.Equal("jwt-platform", x.Token));
        foreach (var asset in new[] { "/lib/bootstrap.min.css", "/js/site.js", "/css/site.css", "/css/portal-design.css" })
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(asset)).StatusCode);
        using var designCss = await client.GetAsync("/css/portal-design.css");
        Assert.Equal("text/css", designCss.Content.Headers.ContentType?.MediaType);
        Assert.Contains("--nr-canvas", await designCss.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        var script = await client.GetStringAsync("/js/site.js");
        Assert.Contains("form[data-single-submit]", script);
        Assert.Contains("data-password-toggle", script);
    }

    [Theory]
    [InlineData("Operador", true)]
    [InlineData("Comercio", true)]
    [InlineData("Plataforma", false)]
    [InlineData("Unknown", true)]
    public async Task NonPlatformOrUnprivilegedAccountsNeverKeepAdminSession(string type, bool permission)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client); // Even replacing an existing Admin login must fail closed.
        api.Type = type;
        api.Permissions = permission ? [AdminAccess.Permission] : [];
        using var rejected = await Login(client);
        Assert.Equal(HttpStatusCode.OK, rejected.StatusCode);
        var html = await rejected.Content.ReadAsStringAsync();
        Assert.Contains(type == "Plataforma" && !permission
            ? "no tiene permisos para administrar la plataforma"
            : "no tiene acceso a la administración de plataforma", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain(Password, html);
        Assert.Contains(rejected.Headers.GetValues("Set-Cookie"), x => x.StartsWith("NexoRuta.Admin=;", StringComparison.Ordinal));
        Assert.StartsWith("/ingresar", (await client.GetAsync("/")).Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, x => x.Path.StartsWith("/api/admin/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InvalidPasswordAndExpiredTokenDoNotCreateAdminSession()
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        var csrf = await Csrf(client, "/ingresar");
        using var rejected = await client.PostAsync("/ingresar", Form(csrf, ("Email", "platform@example.test"), ("Password", "wrong-secret")));
        var html = await rejected.Content.ReadAsStringAsync();
        Assert.Contains("El correo o la contraseña no son correctos", WebUtility.HtmlDecode(html));
        Assert.Contains("platform@example.test", html);
        Assert.DoesNotContain("wrong-secret", html);
        api.Expiry = DateTime.UtcNow.AddMinutes(-1);
        using var expired = await Login(client);
        Assert.Contains("expiró", WebUtility.HtmlDecode(await expired.Content.ReadAsStringAsync()));
        Assert.StartsWith("/ingresar", (await client.GetAsync("/")).Headers.Location!.AbsolutePath);
    }

    [Theory]
    [InlineData("https://external.example/", "/")]
    [InlineData("//external.example/", "/")]
    [InlineData("/\\external.example/", "/")]
    [InlineData("/operadores?pagina=2", "/operadores?pagina=2")]
    public async Task ReturnUrlIsLocalOnly(string returnUrl, string expected)
    {
        using var factory = Factory(new FakeApi());
        using var client = Client(factory);
        using var response = await Login(client, returnUrl);
        Assert.Equal(expected, response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task AntiforgeryProtectsLoginCreationAndPostOnlyLogoutEvenAfterPermissionRevocation()
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/ingresar", Form(""))).StatusCode);
        await Login(client);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/operadores/crear", CreateForm(""))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/salir", Form(""))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/salir")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/")).StatusCode);
        var csrf = await Csrf(client, "/");
        api.Permissions = [];
        using var logout = await client.PostAsync("/salir", Form(csrf));
        Assert.Equal("/ingresar", logout.Headers.Location!.OriginalString);
        Assert.StartsWith("/ingresar", (await client.GetAsync("/operadores")).Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, x => x.Method == HttpMethod.Post && x.Path.StartsWith("/api/admin/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("operadores")]
    [InlineData("comercios")]
    public async Task ListsArePaginatedAndDetailsRenderOnlyRealOrganizationFields(string kind)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        var html = await client.GetStringAsync($"/{kind}?pagina=2&tamanoPagina=1");
        Assert.Contains("Second organization", html);
        Assert.DoesNotContain("First organization", html);
        Assert.Contains($"/{kind}?pagina=1", html);
        Assert.Contains($"/{kind}?pagina=3", html);
        Assert.Contains(api.Calls, x => x.Path == $"/api/admin/{kind}" && x.Query == "?pagina=2&tamanoPagina=1");
        var detail = await client.GetStringAsync($"/{kind}/{OrganizationId}");
        Assert.Contains("First organization", detail);
        Assert.Contains(OrganizationId.ToString(), detail);
        Assert.DoesNotContain("jwt-platform", detail);
        using var empty = await client.GetAsync($"/{kind}?pagina=10");
        Assert.Contains("No organizations", await empty.Content.ReadAsStringAsync());
        foreach (var query in new[] { "pagina=0", "tamanoPagina=0", "tamanoPagina=101", "pagina=invalid" })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/{kind}?{query}")).StatusCode);
        using var missing = await client.GetAsync($"/{kind}/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Contains("Organization not found", await missing.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("operadores")]
    [InlineData("comercios")]
    public async Task CreationUsesExactApiContractThenRedirectsToDetailWithoutImpersonation(string kind)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        var csrf = await Csrf(client, $"/{kind}/crear");
        using var response = await client.PostAsync($"/{kind}/crear", CreateForm(csrf));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/{kind}/{api.CreatedId}", response.Headers.Location!.OriginalString);
        var html = await client.GetStringAsync(response.Headers.Location);
        Assert.Contains("Organization and its first administrator were created", html);
        Assert.Contains("New organization", html);
        Assert.DoesNotContain(NewPassword, html);
        Assert.DoesNotContain("jwt-platform", html);
        var call = Assert.Single(api.Calls, x => x.Method == HttpMethod.Post && x.Path == $"/api/admin/{kind}");
        using var body = JsonDocument.Parse(call.Body!);
        Assert.Equal(new[] { "administrador", "nombre" }, body.RootElement.EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(new[] { "email", "password" }, body.RootElement.GetProperty("administrador").EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(NewPassword, body.RootElement.GetProperty("administrador").GetProperty("password").GetString());
        Assert.Single(api.Calls, x => x.Path == "/api/auth/login");
        Assert.All(api.Calls.Where(x => x.Path.StartsWith("/api/admin/", StringComparison.Ordinal)), x => Assert.Equal("jwt-platform", x.Token));
    }

    [Theory]
    [InlineData("operadores")]
    [InlineData("comercios")]
    public async Task InvalidCreationPreservesSafeFieldsAndNeverRendersEitherPassword(string kind)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        foreach (var rule in new[] { "name", "email", "length", "confirmation" })
        {
            var csrf = await Csrf(client, $"/{kind}/crear");
            using var response = await client.PostAsync($"/{kind}/crear", Form(csrf,
                ("Nombre", rule == "name" ? new string('a', 161) : "Preserved name"),
                ("Email", rule == "email" ? "invalid" : "new@example.test"),
                ("Password", rule == "length" ? "short" : NewPassword),
                ("ConfirmPassword", rule == "confirmation" ? "Different-secret-2026!" : rule == "length" ? "short" : NewPassword)));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var html = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(NewPassword, html);
            Assert.DoesNotContain("Different-secret-2026!", html);
            Assert.DoesNotContain("value=\"short\"", html);
            if (rule == "confirmation") Assert.Contains("Passwords must match", html);
            if (rule == "length") Assert.Contains("minimum length", html);
            if (rule != "name") Assert.Contains("Preserved name", html);
        }
        Assert.DoesNotContain(api.Calls, x => x.Method == HttpMethod.Post && x.Path.StartsWith("/api/admin/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(400, "Review the name")]
    [InlineData(409, "email is already registered")]
    [InlineData(503, "API is unavailable")]
    [InlineData(0, "API is unavailable")]
    [InlineData(-1, "API is unavailable")]
    public async Task CreationErrorsAndTimeoutPreserveSafeFieldsWithoutRenderingUpstreamSecrets(int status, string message)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        var csrf = await Csrf(client, "/operadores/crear");
        api.ResourceStatus = status;
        using var response = await client.PostAsync("/operadores/crear", CreateForm(csrf));
        Assert.Equal(status is 400 or 409 ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains(message, html);
        Assert.Contains("New organization", html);
        Assert.Contains("new@example.test", html);
        Assert.DoesNotContain(NewPassword, html);
        Assert.DoesNotContain("jwt-platform", html);
    }

    [Theory]
    [InlineData(401, "/ingresar")]
    [InlineData(403, "/acceso-denegado")]
    [InlineData(503, "/api-indisponible")]
    [InlineData(0, "/api-indisponible")]
    [InlineData(-1, "/api-indisponible")]
    public async Task CurrentPermissionFailuresNeverGrantAccess(int status, string redirect)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        api.PermissionStatus = status;
        using var response = await client.GetAsync("/comercios");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(redirect, new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain(api.Calls, x => x.Path == "/api/admin/comercios");
        if (status == 401)
        {
            api.PermissionStatus = null;
            Assert.StartsWith("/ingresar", (await client.GetAsync("/")).Headers.Location!.AbsolutePath);
        }
    }

    [Theory]
    [InlineData(401, "/ingresar")]
    [InlineData(403, "/acceso-denegado")]
    [InlineData(503, "/api-indisponible")]
    [InlineData(0, "/api-indisponible")]
    [InlineData(-1, "/api-indisponible")]
    public async Task ResourceReadFailuresAreControlled(int status, string redirect)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        api.ResourceStatus = status;
        using var response = await client.GetAsync($"/comercios/{OrganizationId}");
        Assert.StartsWith(redirect, response.Headers.Location!.OriginalString);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(503)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task LoginPermissionFailureLeavesNoActiveSession(int status)
    {
        var api = new FakeApi { PermissionStatus = status };
        using var factory = Factory(api);
        using var client = Client(factory);
        using var response = await Login(client);
        Assert.Equal(status is 401 or 403 ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("platform@example.test", html);
        Assert.DoesNotContain(Password, html);
        Assert.DoesNotContain(NewPassword, html);
        Assert.StartsWith("/ingresar", (await client.GetAsync("/")).Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, x => x.Path.StartsWith("/api/admin/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task RevokingPermissionDeniesAllPagesButNotLogout()
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        api.Permissions = [];
        foreach (var route in new[] { "/", "/operadores", "/comercios/crear" })
            Assert.StartsWith("/acceso-denegado", (await client.GetAsync(route)).Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task CookieNamesAndCryptographicProtectionAreIsolatedAcrossTheThreePortals()
    {
        using var admin = Factory(new FakeApi());
        using var backoffice = new PortalFactory<NexoRuta.Backoffice.Core.Security.ApiPermissionAuthorizationHandler>(new FakeApi { Type = "Operador" });
        using var commerce = new PortalFactory<NexoRuta.Commerce.Components.App>(new FakeApi { Type = "Comercio" });
        using var adminClient = Client(admin);
        using var backofficeClient = Client(backoffice);
        using var commerceClient = Client(commerce);
        var adminLogin = await Login(adminClient);
        var backofficeLogin = await Login(backofficeClient);
        var commerceLogin = await Login(commerceClient);
        string Cookie(HttpResponseMessage response, string name) => response.Headers.GetValues("Set-Cookie")
            .Last(x => x.StartsWith(name + "=", StringComparison.Ordinal)).Split(';')[0];
        var adminCookie = Cookie(adminLogin, "NexoRuta.Admin");
        var backofficeCookie = Cookie(backofficeLogin, "NexoRuta.Backoffice");
        var commerceCookie = Cookie(commerceLogin, "NexoRuta.Commerce");
        using var browser = admin.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = false });
        browser.DefaultRequestHeaders.Add("Cookie", backofficeCookie + "; " + commerceCookie);
        Assert.StartsWith("/ingresar", (await browser.GetAsync("/")).Headers.Location!.AbsolutePath);
        browser.DefaultRequestHeaders.Remove("Cookie");
        browser.DefaultRequestHeaders.Add("Cookie", "NexoRuta.Admin=" + backofficeCookie.Split('=', 2)[1]);
        Assert.StartsWith("/ingresar", (await browser.GetAsync("/")).Headers.Location!.AbsolutePath);
        browser.DefaultRequestHeaders.Remove("Cookie");
        browser.DefaultRequestHeaders.Add("Cookie", adminCookie + "; " + backofficeCookie + "; " + commerceCookie);
        Assert.Equal(HttpStatusCode.OK, (await browser.GetAsync("/")).StatusCode);
    }

    [Fact]
    public async Task NonDevelopmentCookieRequiresHttpsAndRemainsNonpersistent()
    {
        using var factory = new PortalFactory<ApiPermissionAuthorizationHandler>(new FakeApi(), "Production");
        using var client = factory.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost/") });
        using var response = await Login(client);
        var cookie = response.Headers.GetValues("Set-Cookie").Last(x => x.StartsWith("NexoRuta.Admin=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(401, "/ingresar")]
    [InlineData(403, "/acceso-denegado")]
    public async Task CreationAuthorizationFailureDoesNotIssueAnAdministratorSession(int status, string redirect)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        await Login(client);
        var csrf = await Csrf(client, "/comercios/crear");
        api.ResourceStatus = status;
        using var response = await client.PostAsync("/comercios/crear", CreateForm(csrf));
        Assert.StartsWith(redirect, response.Headers.Location!.OriginalString);
        Assert.Single(api.Calls, x => x.Path == "/api/auth/login");
        Assert.DoesNotContain(NewPassword, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("Aprobar", "Aprobada")]
    [InlineData("Rechazar", "Rechazada")]
    public async Task RenderedCancellationDecisionButtonsResolveTheRequest(string decision, string expectedState)
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        using var login = await Login(client);
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var path = $"/solicitudes-baja/{CancellationRequestId}";
        using var rendered = await client.GetAsync(path);
        var html = await rendered.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, rendered.StatusCode);
        Assert.Contains("name=\"decision\" value=\"Aprobar\"", html);
        Assert.Contains("name=\"decision\" value=\"Rechazar\"", html);
        Assert.Contains("name=\"Version\"", html);
        Assert.Contains("name=\"Confirmar\"", html);
        Assert.Contains("name=\"__RequestVerificationToken\"", html);
        Assert.Contains("Aprobar y suspender", html);
        Assert.Contains("Rechazar solicitud", html);

        var csrf = await Csrf(client, path);
        using var response = await client.PostAsync(path + "?handler=Resolver", Form(csrf,
            ("Version", "0"), ("Motivo", "Resolución validada desde el formulario"),
            ("Confirmar", "true"), ("decision", decision)));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var resolution = Assert.Single(api.Calls, call => call.Path == $"/api/admin/solicitudes-baja/{CancellationRequestId}/resolver");
        Assert.Equal(HttpMethod.Post, resolution.Method);
        Assert.Equal("jwt-platform", resolution.Token);
        using var json = JsonDocument.Parse(resolution.Body!);
        Assert.Equal(decision, json.RootElement.GetProperty("decision").GetString());
        Assert.Equal("Resolución validada desde el formulario", json.RootElement.GetProperty("motivo").GetString());
        Assert.Equal(0, json.RootElement.GetProperty("version").GetInt64());

        using var result = await client.GetAsync(path);
        var resultHtml = WebUtility.HtmlDecode(await result.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.Contains(expectedState, resultHtml);
        Assert.Contains(decision == "Aprobar"
            ? "La solicitud fue aprobada y la organización quedó suspendida."
            : "La solicitud fue rechazada; el estado de la organización no cambió.", resultHtml);
    }

    [Fact]
    public async Task CancellationResolutionKeepsFieldValidationAndAuthorization()
    {
        var api = new FakeApi();
        using var factory = Factory(api);
        using var client = Client(factory);
        using var login = await Login(client);
        var path = $"/solicitudes-baja/{CancellationRequestId}";

        var invalidSubmissions = new[]
        {
            (Decision: (string?)null, Reason: "Motivo válido", Confirm: "true", Field: "Decision", Message: "Elegí aprobar o rechazar la solicitud."),
            (Decision: "Aprobar", Reason: "", Confirm: "true", Field: "Motivo", Message: "Ingresá un motivo."),
            (Decision: "Aprobar", Reason: new string('x', 2001), Confirm: "true", Field: "Motivo", Message: "El motivo no puede superar los 2000 caracteres."),
            (Decision: "Rechazar", Reason: "Motivo válido", Confirm: "false", Field: "Confirmar", Message: "Confirmá que entendés el efecto de esta decisión.")
        };

        foreach (var invalid in invalidSubmissions)
        {
            var csrf = await Csrf(client, path);
            var fields = new List<(string, string)>
            {
                ("Version", "0"), ("Motivo", invalid.Reason), ("Confirmar", invalid.Confirm)
            };
            if (invalid.Decision is not null) fields.Add(("decision", invalid.Decision));
            using var response = await client.PostAsync(path + "?handler=Resolver", Form(csrf, fields.ToArray()));
            var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains($"data-valmsg-for=\"{invalid.Field}\"", html);
            Assert.Contains(invalid.Message, html);
            Assert.DoesNotContain("Choose a decision, confirm it, and provide a reason", html);
            Assert.DoesNotContain(api.Calls, call => call.Path.EndsWith("/resolver", StringComparison.Ordinal));
        }

        var anonymous = factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var anonymousCsrf = await Csrf(anonymous, "/ingresar");
        using var unauthorized = await anonymous.PostAsync(path + "?handler=Resolver", Form(anonymousCsrf,
            ("Version", "0"), ("Motivo", "Intento sin sesión"), ("Confirmar", "true"), ("decision", "Aprobar")));
        Assert.Equal(HttpStatusCode.Redirect, unauthorized.StatusCode);
        Assert.Equal("/ingresar", unauthorized.Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, call => call.Path.EndsWith("/resolver", StringComparison.Ordinal));
    }

    private static PortalFactory<ApiPermissionAuthorizationHandler> Factory(FakeApi api) => new(api);
    private static HttpClient Client<T>(WebApplicationFactory<T> factory) where T : class
        => factory.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
    private static async Task<HttpResponseMessage> Login(HttpClient client, string returnUrl = "/")
        => await client.PostAsync("/ingresar", Form(await Csrf(client, "/ingresar"), ("Email", "platform@example.test"), ("Password", Password), ("ReturnUrl", returnUrl)));
    private static async Task<string> Csrf(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var match = Regex.Match(await response.Content.ReadAsStringAsync(), "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static FormUrlEncodedContent CreateForm(string csrf) => Form(csrf, ("Nombre", "New organization"),
        ("Email", "new@example.test"), ("Password", NewPassword), ("ConfirmPassword", NewPassword));
    private static FormUrlEncodedContent Form(string csrf, params (string, string)[] values)
        => new(values.Append(("__RequestVerificationToken", csrf)).ToDictionary(x => x.Item1, x => x.Item2));

    private sealed class PortalFactory<T>(FakeApi api, string environment = "Development") : WebApplicationFactory<T> where T : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment(environment);
            builder.ConfigureTestServices(services =>
            {
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.Configure<HttpClientFactoryOptions>("NexoRuta", options => options.HttpMessageHandlerBuilderActions
                    .Add(handler => handler.PrimaryHandler = new ApiHandler(api)));
            });
        }
    }
    private sealed class ApiHandler(FakeApi api) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => api.Send(request, ct);
    }
    private sealed record Call(string Path, string Query, HttpMethod Method, string? Token, string? Body);
    private sealed class FakeApi
    {
        public string Type { get; set; } = "Plataforma";
        public string[] Permissions { get; set; } = [AdminAccess.Permission];
        public int? PermissionStatus { get; set; }
        public int? ResourceStatus { get; set; }
        public DateTime Expiry { get; set; } = DateTime.UtcNow.AddMinutes(5);
        public Guid CreatedId { get; } = Guid.NewGuid();
        public ConcurrentQueue<Call> Calls { get; } = new();
        private string CancellationState { get; set; } = "Pendiente";
        private long CancellationVersion { get; set; }
        private Guid? ResolvedBy { get; set; }
        private DateTimeOffset? ResolvedAt { get; set; }
        private string? ResolutionReason { get; set; }
        private bool created;
        public async Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken ct)
        {
            var uri = request.RequestUri!;
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Enqueue(new(uri.AbsolutePath, uri.Query, request.Method, request.Headers.Authorization?.Parameter, body));
            if (uri.AbsolutePath == "/api/auth/login")
            {
                if (JsonDocument.Parse(body!).RootElement.GetProperty("password").GetString() != Password) return Json(401, new { message = "Invalid credentials" });
                return Json(200, new
                {
                    accessToken = "jwt-platform", tokenType = "Bearer", expiresAtUtc = Expiry,
                    contexto = Type, tipoOrganizacion = Type is "Comercio" or "Operador" ? Type : null
                });
            }
            if (uri.AbsolutePath == "/api/usuarios/actual") return Json(200, new
            {
                accesoId = Guid.NewGuid(), usuarioId = Guid.NewGuid(), usuarioEmail = "platform@example.test", tipo = Type,
                operadorId = Type == "Operador" ? (Guid?)OrganizationId : null, comercioId = Type == "Comercio" ? (Guid?)OrganizationId : null,
                operadorNombre = (string?)null, comercioNombre = (string?)null, rolId = (Guid?)null, rolCodigo = (string?)null, tipoAcceso = (int?)null
            });
            if (uri.AbsolutePath == "/api/usuarios/actual/permisos")
                return PermissionStatus is { } permissionStatus ? Failure(permissionStatus) : Json(200, Permissions);
            if (uri.AbsolutePath == $"/api/admin/solicitudes-baja/{CancellationRequestId}/resolver" && request.Method == HttpMethod.Post)
            {
                using var resolution = JsonDocument.Parse(body!);
                var decision = resolution.RootElement.GetProperty("decision").GetString()!;
                CancellationState = decision == "Aprobar" ? "Aprobada" : "Rechazada";
                CancellationVersion++;
                ResolvedBy = Guid.Parse("00000000-0000-0000-0000-000000000999");
                ResolvedAt = DateTimeOffset.UtcNow;
                ResolutionReason = resolution.RootElement.GetProperty("motivo").GetString();
                return Json(200, CancellationResponse());
            }
            if (uri.AbsolutePath == $"/api/admin/solicitudes-baja/{CancellationRequestId}")
                return Json(200, CancellationResponse());
            if (ResourceStatus is { } status) return Failure(status);
            if (request.Method == HttpMethod.Post)
            {
                created = true;
                return Json(201, new { organizacion = new { id = CreatedId, nombre = "New organization" }, usuarioId = Guid.NewGuid(), accesoId = Guid.NewGuid(), rolId = Guid.NewGuid() });
            }
            if (Guid.TryParse(uri.Segments.Last(), out var id)) return id == OrganizationId || created && id == CreatedId
                ? Json(200, new { id, nombre = id == CreatedId ? "New organization" : "First organization" }) : Json(404, new { message = "Not found" });
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(uri.Query);
            var page = query.TryGetValue("pagina", out var p) ? int.Parse(p!) : 1;
            var size = query.TryGetValue("tamanoPagina", out var s) ? int.Parse(s!) : 20;
            var items = new[] { new { id = OrganizationId, nombre = "First organization" }, new { id = Guid.NewGuid(), nombre = "Second organization" }, new { id = Guid.NewGuid(), nombre = "Third organization" } };
            return Json(200, new { items = items.Skip((page - 1) * size).Take(size), pagina = page, tamanoPagina = size, total = 3 });
        }
        private object CancellationResponse() => new
        {
            id = CancellationRequestId,
            tipoOrganizacion = "Comercio",
            organizacionId = OrganizationId,
            nombreOrganizacion = "Comercio de prueba",
            estado = CancellationState,
            motivo = "Solicitud de prueba",
            solicitadaPorUsuarioId = Guid.Parse("00000000-0000-0000-0000-000000000998"),
            solicitadaEnUtc = DateTimeOffset.UtcNow,
            resueltaPorUsuarioId = ResolvedBy,
            resueltaEnUtc = ResolvedAt,
            motivoResolucion = ResolutionReason,
            version = CancellationVersion
        };
        private static HttpResponseMessage Failure(int status) => status switch
        {
            0 => throw new HttpRequestException("Simulated transport failure"),
            -1 => throw new TaskCanceledException("Simulated timeout"),
            _ => Json(status, new { message = NewPassword }) // Never echo upstream sensitive messages into form HTML.
        };
        private static HttpResponseMessage Json(int status, object body) => new((HttpStatusCode)status) { Content = JsonContent.Create(body) };
    }
}
