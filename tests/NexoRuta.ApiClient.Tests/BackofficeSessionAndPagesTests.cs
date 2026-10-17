using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace NexoRuta.ApiClient.Tests;

public sealed class BackofficeSessionAndPagesTests
{
    private static readonly Guid TargetUserId = Guid.Parse("00000000-0000-0000-0000-000000000101");
    private static readonly Guid TargetAccessId = Guid.Parse("00000000-0000-0000-0000-000000000102");
    private static readonly Guid RoleId = Guid.Parse("00000000-0000-0000-0000-000000000103");
    private static readonly Guid CurrentUserId = Guid.Parse("00000000-0000-0000-0000-000000000201");
    private static readonly Guid OwnAccessId = Guid.Parse("00000000-0000-0000-0000-000000000202");
    private static readonly Guid OwnRoleId = Guid.Parse("00000000-0000-0000-0000-000000000203");
    private static readonly Guid ProtectedRoleId = Guid.Parse("00000000-0000-0000-0000-000000000204");

    [Fact]
    public async Task LoginUsesIsolatedProtectedCookiesAndCurrentApiPermissions()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var userClient = Client(factory);
        using var roleClient = Client(factory);

        var userLogin = await LoginAsync(userClient, "user@example.test");
        var roleLogin = await LoginAsync(roleClient, "roles@example.test");
        Assert.Equal(HttpStatusCode.Redirect, userLogin.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, roleLogin.StatusCode);
        Assert.Equal("/", userLogin.Headers.Location!.OriginalString);
        Assert.Equal("/", roleLogin.Headers.Location!.OriginalString);

        var userCookie = userLogin.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("NexoRuta.Backoffice=", StringComparison.Ordinal));
        Assert.Contains("httponly", userCookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jwt-user", userCookie, StringComparison.Ordinal);
        Assert.DoesNotContain("jwt-roles", roleLogin.Headers.GetValues("Set-Cookie").Single(x => x.StartsWith("NexoRuta.Backoffice=", StringComparison.Ordinal)), StringComparison.Ordinal);

        using var users = await userClient.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        Assert.Contains("data-single-submit", await users.Content.ReadAsStringAsync());
        using var backofficeScript = await userClient.GetAsync("/js/site.js");
        Assert.Equal(HttpStatusCode.OK, backofficeScript.StatusCode);
        Assert.Contains("form[data-single-submit]", await backofficeScript.Content.ReadAsStringAsync());
        Assert.Contains("Accesos de la organización", await users.Content.ReadAsStringAsync());
        using var deniedRoles = await userClient.GetAsync("/roles");
        Assert.Equal(HttpStatusCode.Redirect, deniedRoles.StatusCode);
        Assert.StartsWith("/acceso-denegado", deniedRoles.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        using var allowedRoles = await roleClient.GetAsync("/roles");
        Assert.Equal(HttpStatusCode.OK, allowedRoles.StatusCode);

        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios/actual/permisos" && call.Token == "jwt-user");
        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios/actual/permisos" && call.Token == "jwt-roles");
        Assert.All(api.Calls.Where(call => call.Path != "/api/auth/login"), call => Assert.False(string.IsNullOrWhiteSpace(call.Token)));

        var logoutToken = await GetAntiforgeryTokenAsync(userClient, "/usuarios");
        using var logout = await userClient.PostAsync("/salir", Form(logoutToken));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/ingresar", logout.Headers.Location!.OriginalString);
        using var afterLogout = await userClient.GetAsync("/usuarios");
        Assert.StartsWith("/ingresar", afterLogout.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InvalidCredentialsDoNotCreateSessionOrRenderPassword()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/ingresar");

        using var response = await client.PostAsync("/ingresar", Form(antiforgery,
            ("Email", "user@example.test"), ("Password", "incorrect-password")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.TryGetValues("Set-Cookie", out var cookies) && cookies.Any(value => value.StartsWith("NexoRuta.Backoffice=", StringComparison.Ordinal)));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("user@example.test", html);
        Assert.DoesNotContain("incorrect-password", html);
    }

    [Fact]
    public async Task PermissionChangesTakeEffectWithoutCreatingANewSession()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var denied = await client.GetAsync("/roles");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath, StringComparison.Ordinal);

        api.PermissionOverrides["jwt-user"] = ["envios.consultar", "usuarios.gestionar", "configuracion.gestionar"];
        using var granted = await client.GetAsync("/roles");

        Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        Assert.True(api.Calls.Count(call => call.Path == "/api/usuarios/actual/permisos" && call.Token == "jwt-user") >= 2);
    }

    [Fact]
    public async Task CommerceLoginUsesItsOwnHttpOnlySessionAndLogoutEndsIt()
    {
        var api = new FakeApi();
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/ingresar");

        using var login = await client.PostAsync("/ingresar", Form(antiforgery,
            ("Email", "commerce@example.test"), ("Password", "correct-password")));

        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.Equal("/envios", login.Headers.Location!.OriginalString);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith("NexoRuta.Commerce=", StringComparison.Ordinal));
        Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("jwt-commerce", cookie, StringComparison.Ordinal);
        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios/actual" && call.Token == "jwt-commerce");

        using var commercePage = await client.GetAsync("/envios");
        var commerceHtml = await commercePage.Content.ReadAsStringAsync();
        Assert.Contains("data-single-submit", commerceHtml);
        Assert.Contains("js/site.js", commerceHtml);
        using var commerceScript = await client.GetAsync("/js/site.js");
        Assert.Equal(HttpStatusCode.OK, commerceScript.StatusCode);
        Assert.Contains("form[data-single-submit]", await commerceScript.Content.ReadAsStringAsync());
        var logoutAntiforgery = await GetAntiforgeryTokenAsync(client, "/envios");
        using var logout = await client.PostAsync("/salir", Form(logoutAntiforgery));
        Assert.Equal(HttpStatusCode.Redirect, logout.StatusCode);
        Assert.Equal("/", logout.Headers.Location!.OriginalString);
        using var anonymous = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, anonymous.StatusCode);
        Assert.Contains("/registrarse", await anonymous.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("/usuarios")]
    [InlineData("/roles")]
    public async Task CommerceRazorAdministrationHasNavigationOnDirectEntry(string path)
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = ["usuarios.gestionar", "configuracion.gestionar"];
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");
        var before = api.Calls.Count(x => x.Path == "/api/usuarios/actual/permisos");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains("aria-label=\"Navegación del comercio\"", html);
        foreach (var target in new[] { "/", "/envios", "/envios/crear", "/mi-comercio", "/usuarios", "/roles" })
            Assert.Contains($"href=\"{target}\"", html);
        Assert.Contains("Roles y permisos", html);
        Assert.DoesNotContain("jwt-commerce", html);
        Assert.Equal(before + 1, api.Calls.Count(x => x.Path == "/api/usuarios/actual/permisos"));
    }

    [Fact]
    public async Task OwnOrganizationPagesAreReachableDirectlyAndUseTheAuthenticatedContext()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = ["envios.consultar", "usuarios.gestionar", "configuracion.gestionar"];
        using var commerceFactory = new CommerceFactory(api);
        using var commerce = commerceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(commerce, "commerce@example.test");
        using var commercePage = await commerce.GetAsync("/mi-comercio");
        var commerceHtml = await commercePage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, commercePage.StatusCode);
        Assert.Contains("Mi comercio", commerceHtml);
        Assert.Contains("Integration commerce", commerceHtml);
        Assert.Contains("Solicitud de baja", commerceHtml);
        Assert.Contains("resolución de plataforma", commerceHtml);
        Assert.Contains("maxlength=\"160\"", commerceHtml);
        Assert.Contains("__RequestVerificationToken", commerceHtml);
        Assert.DoesNotContain("jwt-commerce", commerceHtml);
        foreach (var path in new[] { "/usuarios", "/roles" })
        {
            using var page = await commerce.GetAsync(path);
            Assert.Contains("href=\"/mi-comercio\"", await page.Content.ReadAsStringAsync());
        }
        Assert.Contains(api.Calls, call => call.Path == "/api/organizacion/actual" && call.Method == HttpMethod.Get && call.Token == "jwt-commerce");

        using var backofficeFactory = new BackofficeFactory(api);
        using var backoffice = Client(backofficeFactory);
        await LoginAsync(backoffice, "operator@example.test");
        using var operatorPage = await backoffice.GetAsync("/mi-operador");
        var operatorHtml = await operatorPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, operatorPage.StatusCode);
        Assert.Contains("Mi operador", operatorHtml);
        Assert.Contains("Operator", operatorHtml);
        Assert.Contains("maxlength=\"160\"", operatorHtml);
        Assert.Contains("name=\"Version\"", operatorHtml);
        Assert.Contains("__RequestVerificationToken", operatorHtml);
        Assert.DoesNotContain("jwt-user", operatorHtml);
        Assert.Contains("href=\"/mi-operador\"", operatorHtml);
        Assert.Contains(api.Calls, call => call.Path == "/api/organizacion/actual" && call.Method == HttpMethod.Get && call.Token == "jwt-user");

        var antiforgery = await GetAntiforgeryTokenAsync(backoffice, "/mi-operador");
        using var updated = await backoffice.PostAsync("/mi-operador?handler=Guardar", Form(antiforgery,
            ("Nombre", "Updated operator"), ("Version", "0")));
        Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
        Assert.Contains("Nombre del operador actualizado", await updated.Content.ReadAsStringAsync());
        Assert.Contains(api.Calls, call => call.Path == "/api/organizacion/actual" && call.Method == HttpMethod.Put && call.Token == "jwt-user");
    }

    [Fact]
    public async Task OwnerlessOrganizationPagesRemainReadOnlyAndExplainWhy()
    {
        var api = new FakeApi { OrganizationHasOwner = false, IsOrganizationOwner = false };
        using var commerceFactory = new CommerceFactory(api);
        using var commerce = commerceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(commerce, "commerce@example.test");
        using var commercePage = await commerce.GetAsync("/mi-comercio");
        var commerceHtml = await commercePage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, commercePage.StatusCode);
        Assert.Contains("no tiene un dueño registrado", commerceHtml);
        Assert.DoesNotContain("id=\"nombre-comercio\"", commerceHtml);

        using var backofficeFactory = new BackofficeFactory(api);
        using var backoffice = Client(backofficeFactory);
        await LoginAsync(backoffice, "operator@example.test");
        using var operatorPage = await backoffice.GetAsync("/mi-operador");
        var operatorHtml = await operatorPage.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, operatorPage.StatusCode);
        Assert.Contains("no tiene un dueño registrado", operatorHtml);
        Assert.DoesNotContain("name=\"Nombre\"", operatorHtml);
        Assert.DoesNotContain(api.Calls, call => call.Method == HttpMethod.Put && call.Path == "/api/organizacion/actual");
    }

    [Fact]
    public async Task SuspensionLoginUsesOnlyTheRestrictedStatusEndpointAndKeepsLogoutAvailable()
    {
        var api = new FakeApi();
        using var commerceFactory = new CommerceFactory(api);
        using var commerce = commerceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var commerceLogin = await LoginAsync(commerce, "suspended-commerce@example.test");
        Assert.Equal("/organizacion-suspendida", commerceLogin.Headers.Location!.OriginalString);
        using var commerceStatus = await commerce.GetAsync("/organizacion-suspendida");
        Assert.Equal(HttpStatusCode.OK, commerceStatus.StatusCode);
        Assert.Contains("Organización suspendida", await commerceStatus.Content.ReadAsStringAsync());
        Assert.Contains(api.Calls, x => x.Path == "/api/organizacion/suspendida" && x.Token == "jwt-suspended-commerce");
        Assert.DoesNotContain(api.Calls, x => x.Token == "jwt-suspended-commerce" && x.Path != "/api/organizacion/suspendida");
        var commerceXsrf = await GetAntiforgeryTokenAsync(commerce, "/organizacion-suspendida");
        using var commerceLogout = await commerce.PostAsync("/salir", Form(commerceXsrf));
        Assert.Equal(HttpStatusCode.Redirect, commerceLogout.StatusCode);

        using var backofficeFactory = new BackofficeFactory(api);
        using var backoffice = Client(backofficeFactory);
        var operatorLogin = await LoginAsync(backoffice, "suspended-operator@example.test");
        Assert.Equal("/OrganizacionSuspendida", operatorLogin.Headers.Location!.OriginalString);
        using var operatorStatus = await backoffice.GetAsync("/OrganizacionSuspendida");
        Assert.Equal(HttpStatusCode.OK, operatorStatus.StatusCode);
        Assert.Contains("Organización suspendida", await operatorStatus.Content.ReadAsStringAsync());
        using var blockedWork = await backoffice.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, blockedWork.StatusCode);
        Assert.Equal("/OrganizacionSuspendida", blockedWork.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task BackofficeOwnerCanSubmitCancellationWithAntiforgeryAndSeePendingState()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "operator@example.test");
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/mi-operador");
        using var response = await client.PostAsync("/mi-operador?handler=SolicitarBaja", Form(antiforgery,
            ("MotivoBaja", "Cierre del operador"), ("ConfirmarBaja", "true")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(api.Calls, call => call.Path == "/api/organizacion/actual/solicitudes-baja" && call.Method == HttpMethod.Post && call.Token == "jwt-user");
        Assert.True(api.CancellationRequestPending);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Pendiente", html);
        Assert.Contains("activo", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PlatformCanFilterOrganizationsAndResolveCancellationRequestsThroughAntiforgeryForms()
    {
        var api = new FakeApi { CancellationRequestPending = true, OrganizationState = "Suspendida" };
        using var factory = new AdminFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        var login = await LoginAsync(client, "platform@example.test");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        using var listing = await client.GetAsync("/comercios?Estado=Suspendida");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        Assert.Contains("Suspendida", await listing.Content.ReadAsStringAsync());
        using var detail = await client.GetAsync("/comercios/00000000-0000-0000-0000-000000000203");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var organizationXsrf = await GetAntiforgeryTokenAsync(client, "/comercios/00000000-0000-0000-0000-000000000203");
        using var reactivate = await client.PostAsync("/comercios/00000000-0000-0000-0000-000000000203?handler=CambiarEstado",
            Form(organizationXsrf, ("estado", "Activa"), ("Version", "0"), ("Motivo", "Reactivated by support"), ("Confirmar", "true")));
        Assert.Equal(HttpStatusCode.Redirect, reactivate.StatusCode);
        Assert.Equal("Activa", api.OrganizationState);

        using var requests = await client.GetAsync("/solicitudes-baja?Estado=Pendiente");
        Assert.Equal(HttpStatusCode.OK, requests.StatusCode);
        Assert.Contains("Cancellation requests", await requests.Content.ReadAsStringAsync());
        using var requestDetail = await client.GetAsync("/solicitudes-baja/00000000-0000-0000-0000-000000000301");
        Assert.Equal(HttpStatusCode.OK, requestDetail.StatusCode);
        var requestXsrf = await GetAntiforgeryTokenAsync(client, "/solicitudes-baja/00000000-0000-0000-0000-000000000301");
        using var resolution = await client.PostAsync("/solicitudes-baja/00000000-0000-0000-0000-000000000301?handler=Resolver",
            Form(requestXsrf, ("Decision", "Rechazar"), ("Version", "0"), ("Motivo", "Rejected by support"), ("Confirmar", "true")));
        Assert.Equal(HttpStatusCode.Redirect, resolution.StatusCode);
        Assert.Contains(api.Calls, x => x.Path == "/api/admin/solicitudes-baja" && x.Method == HttpMethod.Get && x.Token == "jwt-platform");
        Assert.Contains(api.Calls, x => x.Path.EndsWith("/resolver", StringComparison.Ordinal) && x.Method == HttpMethod.Post && x.Token == "jwt-platform");
    }

    [Fact]
    public async Task OrganizationProfileConflictReloadsCurrentDataInsteadOfRenderingServerError()
    {
        var api = new FakeApi { OrganizationUpdateStatusOverride = HttpStatusCode.Conflict };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "operator@example.test");
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/mi-operador");

        using var response = await client.PostAsync("/mi-operador?handler=Guardar", Form(antiforgery,
            ("Nombre", "Stale operator name"), ("Version", "0")));

        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("cambió desde la última consulta", html);
        Assert.Contains("name=\"Nombre\"", html);
        Assert.Contains("Operator", html);
        Assert.DoesNotContain("Stale operator name</dd>", html);
    }

    [Theory]
    [InlineData("/usuarios", "usuarios.gestionar", "/roles")]
    [InlineData("/roles", "configuracion.gestionar", "/usuarios")]
    public async Task CommerceRazorNavigationHidesLinksWithoutCurrentPermission(string path, string permission, string hidden)
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = [permission];
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains($"href=\"{path}\"", html);
        Assert.DoesNotContain($"href=\"{hidden}\"", html);
        using var denied = await client.GetAsync(hidden);
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/acceso-denegado", denied.Headers.Location!.AbsolutePath);
        api.PermissionOverrides["jwt-commerce"] = ["usuarios.gestionar", "configuracion.gestionar"];
        using var granted = await client.GetAsync(path);
        Assert.Contains($"href=\"{hidden}\"", await granted.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CommerceUserAssignmentDoesNotValidateTheUnsubmittedCreationForm()
    {
        var api = new FakeApi();
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/usuarios");
        using var assigned = await client.PostAsync("/usuarios?handler=Rol", Form(token, ("accesoId", TargetAccessId.ToString()), ("rolId", RoleId.ToString())));
        var html = await assigned.Content.ReadAsStringAsync();
        Assert.Contains("Rol asignado al acceso.", html);
        Assert.DoesNotContain("The Email field is required.", Regex.Replace(html, "<[^>]+>", ""));
        Assert.DoesNotContain("The Password field is required.", Regex.Replace(html, "<[^>]+>", ""));
        using var invalid = await client.PostAsync("/usuarios?handler=Crear", Form(token));
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("The Email field is required.", Regex.Replace(invalidHtml, "<[^>]+>", ""));
        Assert.Contains("The RolId field is required.", Regex.Replace(invalidHtml, "<[^>]+>", ""));
        Assert.Contains("12 caracteres", invalidHtml);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/usuarios" && call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task CommerceUserAdministrationLoadsOnlyAfterCurrentPermissionCheck()
    {
        var api = new FakeApi();
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");

        using var home = await client.GetAsync("/envios");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("href=\"/usuarios\"", await home.Content.ReadAsStringAsync());

        using var users = await client.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        var html = await users.Content.ReadAsStringAsync();
        Assert.Contains("target@example.test", html);
        Assert.Contains("Dynamic role", html);
        Assert.Contains("Activo", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.Contains("data-single-submit", html);
        Assert.Contains("confirm(", html);
        Assert.Contains("/js/site.js", html);
        Assert.DoesNotContain("jwt-commerce", html);
        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios/actual/permisos" && call.Token == "jwt-commerce");
        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios" && call.Method == HttpMethod.Get && call.Token == "jwt-commerce");
        Assert.Contains(api.Calls, call => call.Path == "/api/roles/asignables" && call.Token == "jwt-commerce");
        using var siteScript = await client.GetAsync("/js/site.js");
        Assert.Contains("form[data-single-submit]", await siteScript.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task CommerceUserAdministrationHidesLinkAndDeniesDirectPageWithoutPermission()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = [];
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        using var anonymous = await client.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.Redirect, anonymous.StatusCode);
        Assert.StartsWith("/ingresar", anonymous.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        await LoginAsync(client, "commerce@example.test");

        using var home = await client.GetAsync("/envios");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.DoesNotContain("href=\"/usuarios\"", await home.Content.ReadAsStringAsync());

        using var denied = await client.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/acceso-denegado", denied.Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/usuarios" && call.Method == HttpMethod.Get);

        api.PermissionOverrides["jwt-commerce"] = ["usuarios.gestionar"];
        using var grantedWithoutRelogin = await client.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.OK, grantedWithoutRelogin.StatusCode);
    }

    [Fact]
    public async Task CommerceRoleAdministrationUsesCurrentPermissionAndKeepsOwnRolesReadOnly()
    {
        var api = new FakeApi { IncludeOwnAccessRow = true };
        api.PermissionOverrides["jwt-commerce"] = ["configuracion.gestionar", "usuarios.gestionar"];
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");

        using var home = await client.GetAsync("/envios");
        Assert.Contains("href=\"/roles\"", await home.Content.ReadAsStringAsync());
        using var response = await client.GetAsync("/roles");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Dynamic role", html);
        Assert.Contains("Own role", html);
        Assert.Contains("System role", html);
        Assert.Contains("solo podés consultarlo", html);
        Assert.Contains("envios.crear", html);
        Assert.DoesNotContain("envios.gestionar", html);
        Assert.Contains("data-single-submit", html);
        Assert.Contains("confirm(", html);
        Assert.Contains("__RequestVerificationToken", html);
        Assert.DoesNotContain($"name=\"id\" value=\"{OwnRoleId}\"", html);
        Assert.DoesNotContain($"name=\"id\" value=\"{ProtectedRoleId}\"", html);
        Assert.Contains(api.Calls, call => call.Path == "/api/roles" && call.Method == HttpMethod.Get && call.Token == "jwt-commerce");
        Assert.Contains(api.Calls, call => call.Path == "/api/roles/permisos" && call.Token == "jwt-commerce");

        var antiforgery = await GetAntiforgeryTokenAsync(client, "/roles");
        using var created = await client.PostAsync("/roles?handler=Crear", Form(antiforgery,
            ("Codigo", "commerce-role"), ("Nombre", "Commerce role")));
        Assert.Contains("Rol creado", await created.Content.ReadAsStringAsync());
        using var renamed = await client.PostAsync("/roles?handler=Renombrar", Form(antiforgery,
            ("id", RoleId.ToString()), ("nombre", "Renamed role")));
        Assert.Contains("Rol renombrado", await renamed.Content.ReadAsStringAsync());
        using var grants = await client.PostAsync("/roles?handler=Permisos", Form(antiforgery,
            ("id", RoleId.ToString()), ("permisos", "envios.crear")));
        var grantHtml = await grants.Content.ReadAsStringAsync();
        Assert.Contains("Permisos del rol actualizados", grantHtml);
        Assert.DoesNotContain("The Codigo field is required.", Regex.Replace(grantHtml, "<[^>]+>", ""));
        Assert.DoesNotContain("The Nombre field is required.", Regex.Replace(grantHtml, "<[^>]+>", ""));
        using var withdrawn = await client.PostAsync("/roles?handler=Permisos", Form(antiforgery,
            ("id", RoleId.ToString())));
        Assert.Contains("Permisos del rol actualizados", await withdrawn.Content.ReadAsStringAsync());
        using var deleted = await client.PostAsync("/roles?handler=Eliminar", Form(antiforgery, ("id", RoleId.ToString())));
        Assert.Contains("Rol eliminado", await deleted.Content.ReadAsStringAsync());
        using var invalidCreate = await client.PostAsync("/roles?handler=Crear", Form(antiforgery));
        var invalidText = Regex.Replace(await invalidCreate.Content.ReadAsStringAsync(), "<[^>]+>", "");
        Assert.Contains("The Codigo field is required.", invalidText);
        Assert.Contains("The Nombre field is required.", invalidText);
    }

    [Fact]
    public async Task CommerceRolePageHidesNavigationAndDeniesDirectAccessWithoutCurrentPermission()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = ["usuarios.gestionar"];
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");

        using var home = await client.GetAsync("/envios");
        Assert.DoesNotContain("href=\"/roles\"", await home.Content.ReadAsStringAsync());
        using var denied = await client.GetAsync("/roles");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/acceso-denegado", denied.Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/roles" && call.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task CommerceRolePostsRequireAntiforgeryAndHandleValidationAndConflict()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = ["configuracion.gestionar"];
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/roles");

        using var missingAntiforgery = await client.PostAsync("/roles?handler=Crear",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["Codigo"] = "user-input", ["Nombre"] = "Preserved role name" }));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);

        api.RoleMutationStatusOverride = HttpStatusCode.BadRequest;
        using var invalid = await client.PostAsync("/roles?handler=Crear", Form(antiforgery,
            ("Codigo", "user-input"), ("Nombre", "Preserved role name")));
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.True(invalidHtml.Contains("user-input", StringComparison.Ordinal), invalidHtml[..Math.Min(invalidHtml.Length, 2400)]);
        Assert.Contains("Preserved role name", invalidHtml);
        Assert.Contains("Invalid role input", invalidHtml);

        api.RoleMutationStatusOverride = HttpStatusCode.Conflict;
        using var conflict = await client.PostAsync("/roles?handler=Eliminar", Form(antiforgery,
            ("id", RoleId.ToString())));
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Conflicto", conflictHtml);
        Assert.Contains("Recargar estado", conflictHtml);

        api.RoleMutationStatusOverride = HttpStatusCode.Unauthorized;
        using var expired = await client.PostAsync("/roles?handler=Eliminar", Form(antiforgery,
            ("id", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
        Assert.Equal("/ingresar?expirada=true", expired.Headers.Location!.OriginalString);
        using var anonymous = await client.GetAsync("/roles");
        Assert.StartsWith("/ingresar", anonymous.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CommerceUserAdministrationPostsAreAntiforgeryProtectedAndHandleValidationAndConflicts()
    {
        var api = new FakeApi();
        using var factory = new CommerceFactory(api);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(client, "commerce@example.test");
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/usuarios");

        using var missingAntiforgery = await client.PostAsync("/usuarios?handler=Crear", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "missing-token@example.test",
            ["Password"] = "long-enough-password",
            ["RolId"] = RoleId.ToString()
        }));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);

        using var invalid = await client.PostAsync("/usuarios?handler=Crear", Form(antiforgery,
            ("Email", "invalid@example.test"), ("Password", "secret-that-must-not-render"), ("RolId", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("invalid@example.test", invalidHtml);
        Assert.Contains("Correo ya registrado", invalidHtml);
        Assert.DoesNotContain("secret-that-must-not-render", invalidHtml);

        using var forbidden = await client.PostAsync("/usuarios?handler=Crear", Form(antiforgery,
            ("Email", "forbidden@example.test"), ("Password", "secret-that-must-not-render"), ("RolId", RoleId.ToString())));
        var forbiddenHtml = await forbidden.Content.ReadAsStringAsync();
        Assert.Contains("permiso", forbiddenHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("forbidden@example.test", forbiddenHtml);
        Assert.DoesNotContain("secret-that-must-not-render", forbiddenHtml);

        using var created = await client.PostAsync("/usuarios?handler=Crear", Form(antiforgery,
            ("Email", "commerce-created@example.test"), ("Password", "long-enough-password"), ("RolId", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var createdHtml = await created.Content.ReadAsStringAsync();
        Assert.Contains("Usuario creado", createdHtml);
        Assert.DoesNotContain("long-enough-password", createdHtml);
        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios" && call.Method == HttpMethod.Post && call.Token == "jwt-commerce");

        using var assigned = await client.PostAsync("/usuarios?handler=Rol", Form(antiforgery,
            ("accesoId", TargetAccessId.ToString()), ("rolId", RoleId.ToString())));
        Assert.Contains("Rol asignado", await assigned.Content.ReadAsStringAsync());
        using var withdrawn = await client.PostAsync("/usuarios?handler=Rol", Form(antiforgery,
            ("accesoId", TargetAccessId.ToString()), ("rolId", "")));
        Assert.Contains("Rol retirado", await withdrawn.Content.ReadAsStringAsync());

        using var activated = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "true")));
        Assert.Contains("Acceso activado", await activated.Content.ReadAsStringAsync());
        using var conflict = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "false")));
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Conflicto", conflictHtml);
        Assert.Contains("Recargar estado", conflictHtml);
    }

    [Fact]
    public async Task CommercePermission401SignsOutWhile403AndMissingApiRemainControlled()
    {
        var revokedApi = new FakeApi { PermissionStatusOverride = HttpStatusCode.Unauthorized };
        using (var factory = new CommerceFactory(revokedApi))
        using (var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true }))
        {
            await LoginAsync(client, "commerce@example.test");
            using var expired = await client.GetAsync("/usuarios");
            Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
            Assert.Equal("/ingresar?expirada=true", expired.Headers.Location!.OriginalString);
            using var login = await client.GetAsync(expired.Headers.Location);
            Assert.Contains("Tu sesi", await login.Content.ReadAsStringAsync());
            using var anonymous = await client.GetAsync("/usuarios");
            Assert.StartsWith("/ingresar", anonymous.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        }

        foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound })
        {
            var failingApi = new FakeApi { PermissionStatusOverride = status };
            using var factory = new CommerceFactory(failingApi);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            await LoginAsync(client, "commerce@example.test");
            using var response = await client.GetAsync("/usuarios");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var redirectPath = response.Headers.Location!.IsAbsoluteUri
                ? response.Headers.Location.AbsolutePath
                : response.Headers.Location.OriginalString;
            Assert.StartsWith(status == HttpStatusCode.Forbidden ? "/acceso-denegado" : "/api-indisponible", redirectPath, StringComparison.Ordinal);
            Assert.DoesNotContain(failingApi.Calls, call => call.Path == "/api/usuarios" && call.Method == HttpMethod.Get);
        }
    }

    [Fact]
    public async Task CommercePermissionTransportAndTimeoutFailuresFailClosedWithout500()
    {
        foreach (var failure in new Exception[] { new HttpRequestException("offline"), new TaskCanceledException("timeout") })
        {
            var api = new FakeApi { PermissionFailure = failure };
            using var factory = new CommerceFactory(api);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            await LoginAsync(client, "commerce@example.test");

            using var response = await client.GetAsync("/usuarios");

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Equal("/api-indisponible", response.Headers.Location!.OriginalString);
            Assert.DoesNotContain(api.Calls, call => call.Path == "/api/usuarios" && call.Method == HttpMethod.Get);
        }
    }

    [Fact]
    public async Task DirectPagesRequireAuthenticationAndFormsRequireAntiforgery()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);

        using var anonymous = await client.GetAsync("/usuarios");
        Assert.True(anonymous.StatusCode == HttpStatusCode.Redirect,
            $"Expected redirect, got {(int)anonymous.StatusCode}: {await anonymous.Content.ReadAsStringAsync()}");
        Assert.StartsWith("/ingresar", anonymous.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
        await LoginAsync(client, "user@example.test");

        var response = await client.PostAsync("/usuarios?handler=Crear", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Email"] = "created@example.test",
            ["Password"] = "long-enough-password",
            ["RolId"] = RoleId.ToString()
        }));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Api401ForRevokedTokenSignsOutAndRequestsLogin()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "revoked@example.test");

        using var response = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/ingresar?expirada=true", response.Headers.Location!.OriginalString);
        using var page = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Tu sesi", await page.Content.ReadAsStringAsync());
        using var afterSignOut = await client.GetAsync("/usuarios");
        Assert.StartsWith("/ingresar", afterSignOut.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Api404WhileLoadingPermissionsDeniesPageAndReportsApiUnavailable()
    {
        var api = new FakeApi { PermissionStatusOverride = HttpStatusCode.NotFound };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var denied = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Equal("/api-indisponible", denied.Headers.Location!.OriginalString);
        using var unavailable = await client.GetAsync(denied.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        Assert.Contains("API no disponible", await unavailable.Content.ReadAsStringAsync());
        Assert.Contains(api.Calls, call => call.Path == "/api/usuarios/actual/permisos" && call.Token == "jwt-user");
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/usuarios");
    }

    [Fact]
    public async Task Api403WhileLoadingPermissionsRemainsAccessDenied()
    {
        var api = new FakeApi { PermissionStatusOverride = HttpStatusCode.Forbidden };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var denied = await client.GetAsync("/usuarios");

        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ValidationPreservesNonSensitiveFieldsAndConflictsOfferReload()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/usuarios");

        using var invalid = await client.PostAsync("/usuarios?handler=Crear", Form(antiforgery,
            ("Email", "invalid@example.test"), ("Password", "secret-that-must-not-render"), ("RolId", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        var invalidHtml = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("invalid@example.test", invalidHtml);
        Assert.Contains("Correo ya registrado", invalidHtml);
        Assert.DoesNotContain("secret-that-must-not-render", invalidHtml);

        using var forbidden = await client.PostAsync("/usuarios?handler=Crear", Form(antiforgery,
            ("Email", "forbidden@example.test"), ("Password", "secret-that-must-not-render"), ("RolId", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, forbidden.StatusCode);
        var forbiddenHtml = await forbidden.Content.ReadAsStringAsync();
        Assert.Contains("permiso", forbiddenHtml);
        Assert.Contains("forbidden@example.test", forbiddenHtml);
        Assert.DoesNotContain("secret-that-must-not-render", forbiddenHtml);

        using var created = await client.PostAsync("/usuarios?handler=Crear", Form(antiforgery,
            ("Email", "created@example.test"), ("Password", "secret-that-must-not-render"), ("RolId", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("Usuario y acceso organizacional creados", await created.Content.ReadAsStringAsync());

        using var assigned = await client.PostAsync("/usuarios?handler=Rol", Form(antiforgery,
            ("accesoId", TargetAccessId.ToString()), ("rolId", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);
        Assert.Contains("Rol asignado al acceso", await assigned.Content.ReadAsStringAsync());
        using var unassigned = await client.PostAsync("/usuarios?handler=Rol", Form(antiforgery,
            ("accesoId", TargetAccessId.ToString()), ("rolId", "")));
        Assert.Equal(HttpStatusCode.OK, unassigned.StatusCode);
        Assert.Contains("Rol retirado del acceso", await unassigned.Content.ReadAsStringAsync());

        using var activated = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "true")));
        Assert.Equal(HttpStatusCode.OK, activated.StatusCode);
        Assert.Contains("Acceso activado", await activated.Content.ReadAsStringAsync());

        using var conflict = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "false")));
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Conflicto:", conflictHtml);
        Assert.Contains("Recargar estado", conflictHtml);
        Assert.Contains("Activo", conflictHtml);
    }

    [Fact]
    public async Task RoleLifecycleUsesAuthenticatedExistingApiOperations()
    {
        var api = new FakeApi();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "admin@example.test");
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/roles");

        using var created = await client.PostAsync("/roles?handler=Crear", Form(antiforgery,
            ("Codigo", "support"), ("Nombre", "Support")));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        Assert.Contains("Rol creado", await created.Content.ReadAsStringAsync());
        using var renamed = await client.PostAsync("/roles?handler=Renombrar", Form(antiforgery,
            ("id", RoleId.ToString()), ("nombre", "Support updated")));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        using var grants = await client.PostAsync("/roles?handler=Permisos", Form(antiforgery,
            ("id", RoleId.ToString()), ("permisos", "envios.consultar")));
        Assert.Equal(HttpStatusCode.OK, grants.StatusCode);
        using var deleted = await client.PostAsync("/roles?handler=Eliminar", Form(antiforgery,
            ("id", RoleId.ToString())));
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Contains(api.Calls, call => call.Path == "/api/roles" && call.Method == HttpMethod.Post && call.Token == "jwt-admin");
        Assert.Contains(api.Calls, call => call.Path == $"/api/roles/{RoleId}" && call.Method == HttpMethod.Put);
        Assert.Contains(api.Calls, call => call.Path == $"/api/roles/{RoleId}/permisos" && call.Method == HttpMethod.Put);
        Assert.Contains(api.Calls, call => call.Path == $"/api/roles/{RoleId}" && call.Method == HttpMethod.Delete);
    }

    [Fact]
    public async Task BackofficeRendersOwnAccessAndAssignedOrProtectedRolesReadOnly()
    {
        var api = new FakeApi { IncludeOwnAccessRow = true };
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "admin@example.test");

        using var users = await client.GetAsync("/usuarios");
        var usersHtml = await users.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, users.StatusCode);
        Assert.Contains("Tu acceso no se puede modificar", usersHtml);
        Assert.DoesNotContain($"name=\"usuarioId\" value=\"{CurrentUserId}\"", usersHtml);
        Assert.DoesNotContain($"name=\"accesoId\" value=\"{OwnAccessId}\"", usersHtml);
        Assert.Contains($"name=\"usuarioId\" value=\"{TargetUserId}\"", usersHtml);

        using var roles = await client.GetAsync("/roles");
        var rolesHtml = await roles.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
        Assert.Contains("solo podés consultarlo", rolesHtml);
        Assert.DoesNotContain($"name=\"id\" value=\"{OwnRoleId}\"", rolesHtml);
        Assert.DoesNotContain($"name=\"id\" value=\"{ProtectedRoleId}\"", rolesHtml);
        Assert.Contains($"name=\"id\" value=\"{RoleId}\"", rolesHtml);

        using var commerceFactory = new CommerceFactory(api);
        using var commerceClient = commerceFactory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(commerceClient, "commerce@example.test");
        using var commerceUsers = await commerceClient.GetAsync("/usuarios");
        var commerceHtml = await commerceUsers.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, commerceUsers.StatusCode);
        Assert.Contains("Tu acceso no se puede modificar", commerceHtml);
        Assert.DoesNotContain($"name=\"usuarioId\" value=\"{CurrentUserId}\"", commerceHtml);
        Assert.DoesNotContain($"name=\"accesoId\" value=\"{OwnAccessId}\"", commerceHtml);
        Assert.Contains($"name=\"usuarioId\" value=\"{TargetUserId}\"", commerceHtml);
    }

    private static HttpClient Client(WebApplicationFactory<NexoRuta.Backoffice.Core.Security.ApiPermissionAuthorizationHandler> factory)
        => factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email)
    {
        var token = await GetAntiforgeryTokenAsync(client, "/ingresar");
        return await client.PostAsync("/ingresar", Form(token, ("Email", email), ("Password", "correct-password")));
    }

    private static async Task<string> GetAntiforgeryTokenAsync(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var inputs = Regex.Matches(html, "<input\\b[^>]*>", RegexOptions.IgnoreCase);
        var tokenInput = inputs
            .Select(input => input.Value)
            .FirstOrDefault(input => input.Contains("name=\"__RequestVerificationToken\"", StringComparison.OrdinalIgnoreCase));
        var match = tokenInput is null ? Match.Empty : Regex.Match(tokenInput, "value=\"([^\"]+)\"");
        var inputNames = string.Join(",", inputs.Select(input => Regex.Match(input.Value, "name=\"([^\"]+)\"", RegexOptions.IgnoreCase).Groups[1].Value));
        Assert.True(match.Success, $"The page must render its antiforgery token; found input names: {inputNames}.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static FormUrlEncodedContent Form(string token, params (string Key, string Value)[] values)
        => new(values.Append(("__RequestVerificationToken", token)).ToDictionary(pair => pair.Item1, pair => pair.Item2));

    private sealed class BackofficeFactory(FakeApi api) : WebApplicationFactory<NexoRuta.Backoffice.Core.Security.ApiPermissionAuthorizationHandler>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("NexoRuta", options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = api.CreateHandler())));
        }
    }

    private sealed class CommerceFactory(FakeApi api) : WebApplicationFactory<NexoRuta.Commerce.Components.App>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("NexoRuta", options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = api.CreateHandler())));
        }
    }

    private sealed class AdminFactory(FakeApi api) : WebApplicationFactory<NexoRuta.Admin.Core.Security.ApiPermissionAuthorizationHandler>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services => services.Configure<HttpClientFactoryOptions>("NexoRuta", options =>
                options.HttpMessageHandlerBuilderActions.Add(handlerBuilder => handlerBuilder.PrimaryHandler = api.CreateHandler())));
        }
    }

    private sealed class FakeApi
    {
        public ConcurrentQueue<ApiCall> Calls { get; } = new();
        public ConcurrentDictionary<string, string[]> PermissionOverrides { get; } = new(StringComparer.Ordinal);
        public HttpStatusCode? PermissionStatusOverride { get; init; }
        public Exception? PermissionFailure { get; init; }
        public bool IncludeOwnAccessRow { get; init; }
        public HttpStatusCode? RoleMutationStatusOverride { get; set; }
        public bool OrganizationHasOwner { get; init; } = true;
        public bool IsOrganizationOwner { get; init; } = true;
        public HttpStatusCode? OrganizationUpdateStatusOverride { get; init; }
        public bool CancellationRequestPending { get; set; }
        public string OrganizationState { get; set; } = "Activa";
        public HttpMessageHandler CreateHandler() => new ApiHandler(this);

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var token = request.Headers.Authorization?.Parameter;
            Calls.Enqueue(new ApiCall(path, request.Method, token));
            if (path == "/api/auth/login")
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                if (body.GetProperty("password").GetString() != "correct-password")
                    return Json(HttpStatusCode.Unauthorized, new { message = "El correo o la contraseña no son válidos." });
                var email = body.GetProperty("email").GetString()!;
                var accessToken = email.StartsWith("suspended-commerce", StringComparison.Ordinal) ? "jwt-suspended-commerce"
                    : email.StartsWith("suspended-operator", StringComparison.Ordinal) ? "jwt-suspended-operator"
                    : email.StartsWith("platform", StringComparison.Ordinal) ? "jwt-platform"
                    : email.StartsWith("commerce", StringComparison.Ordinal) ? "jwt-commerce"
                    : email.StartsWith("admin", StringComparison.Ordinal) ? "jwt-admin"
                    : email.StartsWith("roles", StringComparison.Ordinal) ? "jwt-roles"
                    : email.StartsWith("revoked", StringComparison.Ordinal) ? "jwt-revoked" : "jwt-user";
                var context = accessToken switch
                {
                    "jwt-platform" => "Plataforma",
                    "jwt-suspended-commerce" or "jwt-suspended-operator" => "OrganizacionSuspendida",
                    "jwt-commerce" => "Comercio",
                    _ => "Operador"
                };
                var organizationType = accessToken switch
                {
                    "jwt-suspended-commerce" => "Comercio",
                    "jwt-suspended-operator" => "Operador",
                    "jwt-commerce" => "Comercio",
                    "jwt-user" or "jwt-admin" or "jwt-roles" or "jwt-revoked" => "Operador",
                    _ => (string?)null
                };
                return Json(HttpStatusCode.OK, new { accessToken, tokenType = "Bearer", expiresAtUtc = DateTime.UtcNow.AddMinutes(5), contexto = context, tipoOrganizacion = organizationType });
            }

            if (token == "jwt-revoked" && path == "/api/usuarios/actual/permisos")
                return Json(HttpStatusCode.Unauthorized, new { message = "JWT revoked" });
            if (path == "/api/usuarios/actual/permisos")
            {
                if (PermissionFailure is { } failure) throw failure;
                if (PermissionStatusOverride is { } status)
                    return Json(status, new { message = "Permission service unavailable" });
                return Json(HttpStatusCode.OK, Permissions(token));
            }
            if (path == "/api/usuarios/actual") return Json(HttpStatusCode.OK, CurrentUser(token));
            if (path == "/api/organizacion/suspendida")
                return Json(HttpStatusCode.OK, new { organizacionId = Guid.NewGuid(), tipoOrganizacion = token == "jwt-suspended-commerce" ? "Comercio" : "Operador", nombre = "Suspended organization", estado = "Suspendida", mensaje = "Sign in again after reactivation." });
            if (path == "/api/organizacion/actual" && request.Method == HttpMethod.Get)
                return OrganizationResponse(token, token == "jwt-commerce" ? "Integration commerce" : "Operator", 0);
            if (path == "/api/organizacion/actual" && request.Method == HttpMethod.Put)
            {
                if (OrganizationUpdateStatusOverride is { } status)
                    return Json(status, new { message = "The organization changed since it was loaded." });
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                return OrganizationResponse(token, body.GetProperty("nombre").GetString()!, body.GetProperty("version").GetInt64() + 1);
            }
            if (path == "/api/organizacion/actual/solicitudes-baja" && request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, CancellationRequestPending ? new[] { CancellationRequest("Pendiente") } : []);
            if (path == "/api/organizacion/actual/solicitudes-baja" && request.Method == HttpMethod.Post)
            {
                CancellationRequestPending = true;
                return Json(HttpStatusCode.Created, CancellationRequest("Pendiente"));
            }
            if (path == "/api/admin/solicitudes-baja" && request.Method == HttpMethod.Get)
            {
                var requests = CancellationRequestPending ? new[] { CancellationRequest("Pendiente") } : [];
                return Json(HttpStatusCode.OK, new { items = requests, pagina = 1, tamanoPagina = 20, total = requests.Length });
            }
            if (path.StartsWith("/api/admin/solicitudes-baja/", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, CancellationRequest(CancellationRequestPending ? "Pendiente" : "Rechazada"));
            if (path.StartsWith("/api/admin/solicitudes-baja/", StringComparison.Ordinal) && path.EndsWith("/resolver", StringComparison.Ordinal))
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                var state = body.GetProperty("decision").GetString() == "Aprobar" ? "Aprobada" : "Rechazada";
                CancellationRequestPending = false;
                return Json(HttpStatusCode.OK, CancellationRequest(state));
            }
            if ((path == "/api/admin/comercios" || path == "/api/admin/operadores") && request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, new { items = new[] { OrganizationAdminResponse() }, pagina = 1, tamanoPagina = 20, total = 1 });
            if ((path == "/api/admin/comercios" || path == "/api/admin/operadores") && request.Method == HttpMethod.Post)
                return Json(HttpStatusCode.Created, new { organizacion = OrganizationAdminResponse(), usuarioId = CurrentUserId, accesoId = OwnAccessId, rolId = RoleId });
            if (path.StartsWith("/api/admin/comercios/", StringComparison.Ordinal) || path.StartsWith("/api/admin/operadores/", StringComparison.Ordinal))
            {
                if (path.EndsWith("/estado", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
                {
                    var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                    OrganizationState = body.GetProperty("estado").GetString()!;
                    return Json(HttpStatusCode.OK, OrganizationAdminResponse(body.GetProperty("version").GetInt64() + 1));
                }
                if (request.Method == HttpMethod.Get) return Json(HttpStatusCode.OK, OrganizationAdminResponse());
            }
            if (path == "/api/usuarios" && request.Method == HttpMethod.Get)
            {
                var users = new List<object> { new { usuarioId = TargetUserId, accesoId = TargetAccessId,
                    email = "target@example.test", rolId = RoleId, rolCodigo = "dynamic", rolNombre = "Dynamic role", activo = true } };
                if (IncludeOwnAccessRow)
                    users.Add(new { usuarioId = CurrentUserId, accesoId = OwnAccessId,
                        email = "admin@example.test", rolId = OwnRoleId, rolCodigo = "own", rolNombre = "Own role", activo = true });
                return Json(HttpStatusCode.OK, users);
            }
            if (path == "/api/usuarios" && request.Method == HttpMethod.Post)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                if (body.GetProperty("email").GetString() == "invalid@example.test")
                    return Json(HttpStatusCode.BadRequest, new { message = "Correo ya registrado." });
                if (body.GetProperty("email").GetString() == "forbidden@example.test")
                    return Json(HttpStatusCode.Forbidden, new { message = "Forbidden" });
                return Json(HttpStatusCode.Created, new { usuarioId = Guid.NewGuid(), accesoId = Guid.NewGuid(),
                    email = body.GetProperty("email").GetString(), rolId = RoleId, rolCodigo = "dynamic", rolNombre = "Dynamic role", activo = true });
            }
            if (path.EndsWith("/estado", StringComparison.Ordinal) && request.Method == HttpMethod.Patch)
            {
                var body = await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
                return body.GetProperty("activo").GetBoolean()
                    ? new HttpResponseMessage(HttpStatusCode.NoContent)
                    : Json(HttpStatusCode.Conflict, new { message = "El estado cambió durante la operación." });
            }
            if ((path == "/api/roles/asignables" || path == "/api/roles") && request.Method == HttpMethod.Get)
            {
                var roles = new List<object> { new { id = RoleId, codigo = "dynamic", nombre = "Dynamic role", permisos = new[] { "envios.consultar" }, esProtegidoSistema = false, esPropio = false } };
                if (IncludeOwnAccessRow && path == "/api/roles")
                {
                    roles.Add(new { id = OwnRoleId, codigo = "own", nombre = "Own role", permisos = new[] { "configuracion.gestionar" }, esProtegidoSistema = false, esPropio = true });
                    roles.Add(new { id = ProtectedRoleId, codigo = "system", nombre = "System role", permisos = new[] { "configuracion.gestionar" }, esProtegidoSistema = true, esPropio = false });
                }
                return Json(HttpStatusCode.OK, roles);
            }
            if (path == "/api/roles/permisos")
                return Json(HttpStatusCode.OK, new[]
                {
                    new { id = Guid.NewGuid(), codigo = "configuracion.gestionar", nombre = "Administrar configuración" },
                    new { id = Guid.NewGuid(), codigo = "envios.consultar", nombre = "Consultar envíos" },
                    new { id = Guid.NewGuid(), codigo = "envios.crear", nombre = "Crear envíos" },
                    new { id = Guid.NewGuid(), codigo = "usuarios.gestionar", nombre = "Administrar usuarios" }
                });
            if (path.StartsWith("/api/roles/", StringComparison.Ordinal) && request.Method != HttpMethod.Get)
            {
                if (RoleMutationStatusOverride is { } roleStatus)
                    return Json(roleStatus, new { message = "Invalid role input" });
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (path == "/api/roles" && request.Method == HttpMethod.Post)
            {
                if (RoleMutationStatusOverride is { } roleStatus)
                    return Json(roleStatus, new { message = "Invalid role input" });
                return Json(HttpStatusCode.Created, new { id = RoleId });
            }
            if (path == "/api/envios") return Json(HttpStatusCode.OK, Array.Empty<object>());
            return Json(HttpStatusCode.NotFound, new { message = "Not found" });
        }

        private string[] Permissions(string? token)
        {
            if (token is not null && PermissionOverrides.TryGetValue(token, out var permissions)) return permissions;
            return token switch
            {
                "jwt-roles" => ["envios.consultar", "configuracion.gestionar"],
                "jwt-admin" => ["envios.consultar", "usuarios.gestionar", "configuracion.gestionar"],
                "jwt-platform" => ["plataforma.organizaciones.gestionar"],
                _ => ["envios.consultar", "usuarios.gestionar"]
            };
        }

        private static object CurrentUser(string? token)
        {
            var commerce = token == "jwt-commerce";
            var platform = token == "jwt-platform";
            var roles = token == "jwt-roles";
            return new
            {
                accesoId = platform ? (Guid?)null : TargetAccessId,
                usuarioId = CurrentUserId,
                operadorId = commerce || platform ? (Guid?)null : Guid.Parse("00000000-0000-0000-0000-000000000202"),
                comercioId = commerce ? Guid.Parse("00000000-0000-0000-0000-000000000203") : (Guid?)null,
                usuarioEmail = commerce ? "commerce@example.test" : platform ? "platform@example.test" : roles ? "roles@example.test" : "user@example.test",
                operadorNombre = commerce || platform ? null : "Operator",
                comercioNombre = commerce ? "Commerce" : null,
                tipo = platform ? "Plataforma" : commerce ? "Comercio" : "Operador",
                rolId = platform ? (Guid?)null : RoleId,
                rolCodigo = platform ? null : "dynamic"
            };
        }

        private static object CancellationRequest(string state) => new
        {
            id = Guid.Parse("00000000-0000-0000-0000-000000000301"),
            tipoOrganizacion = "Comercio", organizacionId = Guid.Parse("00000000-0000-0000-0000-000000000203"),
            nombreOrganizacion = "Integration commerce", estado = state, motivo = "Cierre del comercio",
            solicitadaPorUsuarioId = CurrentUserId, solicitadaEnUtc = DateTimeOffset.UtcNow,
            resueltaPorUsuarioId = (Guid?)null, resueltaEnUtc = (DateTimeOffset?)null,
            motivoResolucion = (string?)null, version = 0L
        };

        private object OrganizationAdminResponse(long version = 0) => new
        {
            id = Guid.Parse("00000000-0000-0000-0000-000000000203"),
            nombre = "Integration commerce", estado = OrganizationState, version
        };

        private static HttpResponseMessage Json(HttpStatusCode status, object value)
            => new(status) { Content = JsonContent.Create(value) };

        private HttpResponseMessage OrganizationResponse(string? token, string name, long version)
            => Json(HttpStatusCode.OK, new
            {
                id = token == "jwt-commerce" ? Guid.Parse("00000000-0000-0000-0000-000000000203") : Guid.Parse("00000000-0000-0000-0000-000000000202"),
                nombre = name,
                version,
                estado = OrganizationState,
                esPropietario = IsOrganizationOwner,
                tieneDuenoRegistrado = OrganizationHasOwner
            });

        public sealed record ApiCall(string Path, HttpMethod Method, string? Token);

        private sealed class ApiHandler(FakeApi api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => api.SendAsync(request, cancellationToken);
        }
    }
}
