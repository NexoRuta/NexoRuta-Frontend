using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
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
    private static readonly Guid CoverageZoneId = Guid.Parse("00000000-0000-0000-0000-000000000401");
    private static readonly Guid CoverageSlotId = Guid.Parse("00000000-0000-0000-0000-000000000402");
    private static readonly Guid TariffDraftId = Guid.Parse("00000000-0000-0000-0000-000000000501");
    private static readonly Guid TariffPublishedId = Guid.Parse("00000000-0000-0000-0000-000000000502");
    private static readonly Guid TariffActiveZoneId = Guid.Parse("00000000-0000-0000-0000-000000000503");
    private static readonly Guid TariffInactiveZoneId = Guid.Parse("00000000-0000-0000-0000-000000000504");
    private static readonly Guid RulesDraftId = Guid.Parse("00000000-0000-0000-0000-000000000701");
    private static readonly Guid RulesPublishedId = Guid.Parse("00000000-0000-0000-0000-000000000702");

    [Fact]
    public async Task ZoneNavigationAndPagesRequireCurrentOperatorPermission()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var zones = await client.GetAsync("/zonas");
        Assert.Equal(HttpStatusCode.OK, zones.StatusCode);
        var html = await zones.Content.ReadAsStringAsync();
        Assert.Contains("Zonas y franjas", html);
        Assert.Contains("Todas", html);
        Assert.Contains("Consultar / editar", html);
        Assert.DoesNotContain("jwt-user", html);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/zonas" && call.Token == "jwt-user");
        using var independentPage = await client.GetAsync("/ingresar");
        var independentHtml = await independentPage.Content.ReadAsStringAsync();
        Assert.Contains("href=\"/zonas\"", independentHtml);
        Assert.Contains("href=\"/tarifas\"", independentHtml);

        api.PermissionOverrides["jwt-user"] = [];
        using var denied = await client.GetAsync("/zonas/crear");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TariffHomeShowsUnconfiguredStateOnlyToAuthorizedOperators()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var home = await client.GetAsync("/tarifas");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Sin publicación", html);
        Assert.Contains("Crear borrador inicial", html);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/tarifas" && call.Token == "jwt-user");

        api.PermissionOverrides["jwt-user"] = [];
        using var denied = await client.GetAsync("/tarifas/borrador");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationalRulesHomeShowsUnconfiguredStateAndRequiresDynamicPermission()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var home = await client.GetAsync("/reglas-operativas");
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        var html = await home.Content.ReadAsStringAsync();
        Assert.Contains("Configurar reglas", html);
        Assert.Contains("Guardar conserva los cambios en el borrador. Publicar los convierte en la configuración vigente.", html);
        Assert.DoesNotContain("versionConcurrencia", html);
        Assert.Contains("href=\"/reglas-operativas\"", html);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/reglas-operativas" && call.Token == "jwt-user");

        using var standalone = await client.GetAsync("/ingresar");
        Assert.Contains("href=\"/reglas-operativas\"", await standalone.Content.ReadAsStringAsync());

        api.PermissionOverrides["jwt-user"] = [];
        using var denied = await client.GetAsync("/reglas-operativas/borrador");
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.StartsWith("/acceso-denegado", denied.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OperationalRulesHomeCreatesInitialDraftOrCopyOnlyAfterExplicitPost()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas");
        using var initial = await client.PostAsync("/reglas-operativas?handler=CrearBorrador", Form(token));
        Assert.Equal(HttpStatusCode.Redirect, initial.StatusCode);
        Assert.Equal("/reglas-operativas/borrador", new Uri(client.BaseAddress!, initial.Headers.Location!).AbsolutePath);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/reglas-operativas/borrador" && call.Method == HttpMethod.Post);

        api.HasRulesDraft = false;
        api.HasRulesPublication = true;
        using var publishedState = await client.GetAsync("/reglas-operativas");
        var html = await publishedState.Content.ReadAsStringAsync();
        Assert.Contains("Crear copia para editar", html);
        Assert.DoesNotContain("Configurar reglas", html);
        token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas");
        using var copy = await client.PostAsync("/reglas-operativas?handler=CrearBorrador", Form(token));
        Assert.Equal(HttpStatusCode.Redirect, copy.StatusCode);
        Assert.True(api.HasRulesDraft);
    }

    [Fact]
    public async Task OperationalRulesHomeOffersToContinueAnExistingDraftSeparatelyFromThePublication()
    {
        var api = new FakeApi { HasRulesDraft = true, HasRulesPublication = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/reglas-operativas");
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Reglas publicadas vigentes", html);
        Assert.Contains("Cambios en preparación", html);
        Assert.Contains("Editar borrador", html);
        Assert.DoesNotContain("Crear copia para editar", html);
    }

    [Fact]
    public async Task OperationalRulesDraftKeepsIncompleteValuesAndDisplaysDurationPartsAndUnavailableMotives()
    {
        var api = new FakeApi { HasRulesDraft = true, RulesConcurrencyVersion = 0,
            RulesIntervalMinutes = 1_501, RulesUrgentMinutes = 525_600,
            RulesProofs = ["Fotografia", "DocumentoReceptor"],
            RulesMotives = [new("DOMICILIO_CERRADO", "Domicilio cerrado", true), new("SIN_ACCESO", "Sin acceso", false)] };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/reglas-operativas/borrador");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"VersionConcurrencia\" value=\"0\"", html);
        Assert.Contains("name=\"IntervaloEntreIntentos.Dias\"", html);
        Assert.Contains("IntervaloEntreIntentos.Horas", html);
        Assert.Contains("IntervaloEntreIntentos.Minutos", html);
        Assert.Contains(Regex.Matches(html, "<option[^>]*selected[^>]*>").Select(match => match.Value), option => option.Contains("value=\"01\"", StringComparison.Ordinal));
        Assert.Contains("DOMICILIO_CERRADO", html);
        Assert.Contains("SIN_ACCESO", html);
        Assert.Contains("value=\"false\"", html);
        Assert.Contains("Se exigirán todas las opciones seleccionadas.", html);
        Assert.Contains("Espera mínima después de un intento fallido", html);
        Assert.Contains("Plazo Urgente", html);
        Assert.Contains("name=\"PlazoEstandar.Horas\"", html);
        Assert.Contains("name=\"PlazoEstandar.Minutos\"", html);
        Assert.Contains("value=\"\">—</option>", html);
        Assert.DoesNotContain("jwt-user", html);
    }

    [Fact]
    public async Task OperationalRulesBlankDraftKeepsNullDurationsAndSendsExplicitEmptyCollections()
    {
        var api = new FakeApi { HasRulesDraft = true, RulesConcurrencyVersion = 0 };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        using var response = await client.PostAsync("/reglas-operativas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("MaxIntentosEntrega", ""),
            ("IntervaloEntreIntentos.Dias", ""), ("IntervaloEntreIntentos.Horas", ""), ("IntervaloEntreIntentos.Minutos", ""),
            ("PoliticaDevolucionAlAgotarIntentos", ""), ("PlazoEstandar.Dias", ""), ("PlazoEstandar.Horas", ""), ("PlazoEstandar.Minutos", ""),
            ("PlazoUrgente.Dias", ""), ("PlazoUrgente.Horas", ""), ("PlazoUrgente.Minutos", "")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var payload = Assert.Single(api.RulePayloads).Body;
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("maxIntentosEntrega").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("intervaloEntreIntentosMinutos").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("plazoEstandarMinutos").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("plazoUrgenteMinutos").ValueKind);
        Assert.Equal(0, payload.GetProperty("requisitosPrueba").GetArrayLength());
        Assert.Equal(0, payload.GetProperty("motivos").GetArrayLength());
    }

    [Fact]
    public async Task OperationalRulesRejectsAllZeroDurationInsteadOfSavingItAsAFullDay()
    {
        var api = new FakeApi { HasRulesDraft = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        using var response = await client.PostAsync("/reglas-operativas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("IntervaloEntreIntentos.Dias", "0"),
            ("IntervaloEntreIntentos.Horas", "00"), ("IntervaloEntreIntentos.Minutos", "00"),
            ("PlazoEstandar.Dias", ""), ("PlazoEstandar.Horas", ""), ("PlazoEstandar.Minutos", ""),
            ("PlazoUrgente.Dias", ""), ("PlazoUrgente.Horas", ""), ("PlazoUrgente.Minutos", "")));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La duración debe ser mayor que cero.", html);
        Assert.Empty(api.RulePayloads);
    }

    [Fact]
    public async Task OperationalRulesRejectsDurationBeyondTheContractMaximum()
    {
        var api = new FakeApi { HasRulesDraft = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        using var response = await client.PostAsync("/reglas-operativas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("IntervaloEntreIntentos.Dias", "365"),
            ("IntervaloEntreIntentos.Horas", "00"), ("IntervaloEntreIntentos.Minutos", "01"),
            ("PlazoEstandar.Dias", ""), ("PlazoEstandar.Horas", ""), ("PlazoEstandar.Minutos", ""),
            ("PlazoUrgente.Dias", ""), ("PlazoUrgente.Horas", ""), ("PlazoUrgente.Minutos", "")));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("La duración no puede superar 365 días.", html);
        Assert.Empty(api.RulePayloads);
    }

    [Fact]
    public async Task OperationalRulesDraftSendsCompleteCollectionsAndConvertsDurationsWithoutLoss()
    {
        var api = new FakeApi { HasRulesDraft = true, RulesConcurrencyVersion = 0,
            RulesMotives = [new("DOMICILIO_CERRADO", "Domicilio cerrado", true), new("SIN_ACCESO", "Sin acceso", false)] };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        var fields = new List<KeyValuePair<string, string>>
        {
            new("VersionConcurrencia", "0"), new("ColeccionesEnviadas", "true"),
            new("MaxIntentosEntrega", "3"), new("IntervaloEntreIntentos.Dias", "1"),
            new("IntervaloEntreIntentos.Horas", "01"), new("IntervaloEntreIntentos.Minutos", "01"),
            new("PoliticaDevolucionAlAgotarIntentos", "IniciarAutomaticamente"),
            new("PlazoEstandar.Dias", ""), new("PlazoEstandar.Horas", "00"), new("PlazoEstandar.Minutos", "01"),
            new("PlazoUrgente.Dias", "365"), new("PlazoUrgente.Horas", "00"), new("PlazoUrgente.Minutos", "00"),
            new("RequisitosPrueba", "Fotografia"), new("RequisitosPrueba", "DocumentoReceptor"),
            new("Motivos[0].Codigo", "DOMICILIO_CERRADO"), new("Motivos[0].Descripcion", "Domicilio cerrado actualizado"),
            new("Motivos[0].EsNuevo", "false"), new("Motivos[0].Disponible", "true"),
            new("Motivos[1].Codigo", "SIN_ACCESO"), new("Motivos[1].Descripcion", "Sin acceso"),
            new("Motivos[1].EsNuevo", "false"), new("Motivos[1].Disponible", "false"),
            new("__RequestVerificationToken", token)
        };

        using var response = await client.PostAsync("/reglas-operativas/borrador?handler=Guardar", new FormUrlEncodedContent(fields));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var payload = Assert.Single(api.RulePayloads).Body;
        Assert.Equal(0, payload.GetProperty("versionConcurrencia").GetInt64());
        Assert.Equal(1_501, payload.GetProperty("intervaloEntreIntentosMinutos").GetInt32());
        Assert.Equal(1, payload.GetProperty("plazoEstandarMinutos").GetInt32());
        Assert.Equal(525_600, payload.GetProperty("plazoUrgenteMinutos").GetInt32());
        Assert.Equal(2, payload.GetProperty("requisitosPrueba").GetArrayLength());
        Assert.Equal(2, payload.GetProperty("motivos").GetArrayLength());
        Assert.False(payload.GetProperty("motivos")[1].GetProperty("disponible").GetBoolean());
        Assert.Equal("SIN_ACCESO", payload.GetProperty("motivos")[1].GetProperty("codigo").GetString());
        Assert.Equal("DOMICILIO_CERRADO", payload.GetProperty("motivos")[0].GetProperty("codigo").GetString());
        Assert.Equal("Domicilio cerrado actualizado", payload.GetProperty("motivos")[0].GetProperty("descripcion").GetString());
    }

    [Fact]
    public async Task OperationalRulesAddingAndRemovingNewMotiveIsLocalAndDoesNotSavePartialDraft()
    {
        var api = new FakeApi { HasRulesDraft = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");

        using var added = await client.PostAsync("/reglas-operativas/borrador?handler=AgregarMotivo", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("Motivos[0].Codigo", "TEMPORAL"),
            ("Motivos[0].Descripcion", "Temporal"), ("Motivos[0].EsNuevo", "true"), ("Motivos[0].Disponible", "true")));
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        Assert.Contains("Motivo nuevo", await added.Content.ReadAsStringAsync());
        Assert.Empty(api.RulePayloads);

        token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        using var removed = await client.PostAsync("/reglas-operativas/borrador?handler=QuitarMotivoNuevo&indice=0", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("Motivos[0].Codigo", "TEMPORAL"),
            ("Motivos[0].Descripcion", "Temporal"), ("Motivos[0].EsNuevo", "true"), ("Motivos[0].Disponible", "true")));
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.DoesNotContain("TEMPORAL", await removed.Content.ReadAsStringAsync());
        Assert.Empty(api.RulePayloads);

        using var missingAntiforgery = await client.PostAsync("/reglas-operativas/borrador?handler=AgregarMotivo",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["VersionConcurrencia"] = "0" }));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
    }

    [Fact]
    public async Task OperationalRulesDraftConflictKeepsPostedValuesAndOffersReloadWithoutChangingVersion()
    {
        var api = new FakeApi { HasRulesDraft = true, RulesConcurrencyVersion = 0, RulesSaveStatusOverride = HttpStatusCode.Conflict };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        using var response = await client.PostAsync("/reglas-operativas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("MaxIntentosEntrega", "6"),
            ("IntervaloEntreIntentos.Dias", ""), ("IntervaloEntreIntentos.Horas", "00"), ("IntervaloEntreIntentos.Minutos", "30"),
            ("PoliticaDevolucionAlAgotarIntentos", "DecisionOperativa"), ("PlazoEstandar.Dias", "1"),
            ("PlazoEstandar.Horas", "00"), ("PlazoEstandar.Minutos", "00"), ("PlazoUrgente.Dias", "0"),
            ("PlazoUrgente.Horas", "01"), ("PlazoUrgente.Minutos", "00"), ("RequisitosPrueba", "FirmaEnPantalla")));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("name=\"VersionConcurrencia\" value=\"0\"", html);
        Assert.Contains("value=\"6\"", html);
        Assert.Contains("Recargar borrador actual", html);
        Assert.Contains("no se aplicaron tus cambios", html);
        Assert.True(api.RulePayloads.All(call => call.Body.GetProperty("versionConcurrencia").GetInt64() == 0));
    }

    [Fact]
    public async Task OperationalRulesPublishRequiresConfirmationAndDoesNotSaveDraft()
    {
        var api = new FakeApi { HasRulesDraft = true, RulesMaxAttempts = 3, RulesIntervalMinutes = 30,
            RulesPolicy = "DecisionOperativa", RulesStandardMinutes = 1440, RulesUrgentMinutes = 60,
            RulesProofs = ["FirmaEnPantalla"], RulesMotives = [new("DOMICILIO_CERRADO", "Domicilio cerrado", true)] };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/publicar");

        using var unconfirmed = await client.PostAsync("/reglas-operativas/publicar", Form(token, ("VersionConcurrencia", "0")));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Empty(api.RulePayloads);

        token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/publicar");
        using var published = await client.PostAsync("/reglas-operativas/publicar", Form(token,
            ("VersionConcurrencia", "0"), ("ConfirmarPublicacion", "true")));
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.StartsWith("/reglas-operativas/publicaciones/", published.Headers.Location!.OriginalString, StringComparison.Ordinal);
        var payload = Assert.Single(api.RulePayloads);
        Assert.Equal(HttpMethod.Post, payload.Method);
        Assert.EndsWith("/borrador/publicar", payload.Path, StringComparison.Ordinal);
        Assert.DoesNotContain(api.RulePayloads, item => item.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task OperationalRulesPublishExplainsIncompleteSectionsBeforeConfirmation()
    {
        var api = new FakeApi { HasRulesDraft = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/reglas-operativas/publicar");
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Intentos de entrega", html);
        Assert.Contains("Prueba requerida para entregar", html);
        Assert.Contains("Política cuando se agotan los intentos", html);
        Assert.Contains("Plazos de entrega", html);
        Assert.Contains("Motivos de no entrega disponibles", html);
    }

    [Fact]
    public async Task OperationalRulesDraftApi400PreservesFormAndShowsControlledValidation()
    {
        var api = new FakeApi { HasRulesDraft = true, RulesSaveStatusOverride = HttpStatusCode.BadRequest };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/reglas-operativas/borrador");
        using var response = await client.PostAsync("/reglas-operativas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "0"), ("ColeccionesEnviadas", "true"), ("MaxIntentosEntrega", "7"),
            ("IntervaloEntreIntentos.Dias", ""), ("IntervaloEntreIntentos.Horas", "00"), ("IntervaloEntreIntentos.Minutos", "30"),
            ("PoliticaDevolucionAlAgotarIntentos", "DecisionOperativa"), ("PlazoEstandar.Dias", "1"),
            ("PlazoEstandar.Horas", "00"), ("PlazoEstandar.Minutos", "00"), ("PlazoUrgente.Dias", "0"),
            ("PlazoUrgente.Horas", "01"), ("PlazoUrgente.Minutos", "00"), ("RequisitosPrueba", "FirmaEnPantalla")));
        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("value=\"7\"", html);
        Assert.Contains("El borrador cambió durante la edición", WebUtility.HtmlDecode(html));
        Assert.Single(api.RulePayloads);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "/ingresar")]
    [InlineData(HttpStatusCode.Forbidden, "/acceso-denegado")]
    public async Task OperationalRulesDraftHandlesApiAuthenticationAndPermissionFailures(HttpStatusCode status, string expectedPath)
    {
        var api = new FakeApi { HasRulesDraft = true, RulesDraftStatusOverride = status };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/reglas-operativas/borrador");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith(expectedPath, new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OperationalRulesMissingDraftRedirectsToStateWithoutCreatingConfiguration()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/reglas-operativas/borrador");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/reglas-operativas", new Uri(client.BaseAddress!, response.Headers.Location!).AbsolutePath);
        Assert.DoesNotContain(api.RulePayloads, call => call.Method == HttpMethod.Post);
    }

    [Fact]
    public async Task OperationalRulesUnknownPublicationReturnsNotFound()
    {
        var api = new FakeApi { HasRulesPublication = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/reglas-operativas/publicaciones/00000000-0000-0000-0000-000000000799");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task OperationalRulesHistoryIsPagedAndPublicationDetailIsReadonly()
    {
        var api = new FakeApi { HasRulesPublication = true, RulesPublishedNumber = 21, RulesMaxAttempts = 3,
            RulesProofs = ["Fotografia"], RulesMotives = [new("DOMICILIO_CERRADO", "Domicilio cerrado", true)] };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var history = await client.GetAsync("/reglas-operativas/publicaciones?pagina=2");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/reglas-operativas/publicaciones" && call.Query.Contains("pagina=2", StringComparison.Ordinal));
        using var detail = await client.GetAsync($"/reglas-operativas/publicaciones/{RulesPublishedId:D}");
        var html = WebUtility.HtmlDecode(await detail.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("Publicación", html);
        Assert.Contains("Fotografía", html);
        Assert.Contains("Crear copia para editar", html);
        Assert.DoesNotContain("name=\"MaxIntentosEntrega\"", html);
    }

    [Fact]
    public async Task TariffDraftReplacesFullCollectionAndIncludesInactiveZoneReferencesFromEveryCatalogPage()
    {
        var api = new FakeApi { HasTariffDraft = true, UseTariffZoneCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        api.TariffDraftRates =
        [
            new(TariffActiveZoneId, "Estandar", 10m, 1.25m, 2.5m,
                [new("Protección", "RecargoFijo", 3.75m, null)]),
            new(TariffInactiveZoneId, "Urgente", 0m, 0m, 0m, [])
        ];
        api.TariffZones = Enumerable.Range(0, 52)
            .Select(i => new TariffZoneFixture(Guid.Parse($"00000000-0000-0000-0000-{600 + i:D12}"), $"Zona activa {i + 1}", "Activa"))
            .Append(new TariffZoneFixture(TariffActiveZoneId, "Centro", "Activa"))
            .Append(new TariffZoneFixture(TariffInactiveZoneId, "Norte", "Inactiva"))
            .ToList();
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        var token = await GetAntiforgeryTokenAsync(client, "/tarifas/borrador");
        using var page = await client.GetAsync("/tarifas/borrador");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/zonas" && call.Query.Contains("pagina=2", StringComparison.Ordinal));
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/zonas" && call.Query.Contains("estado=Inactiva", StringComparison.Ordinal));
        Assert.Contains("Norte", html);
        Assert.Contains("Inactiva", html);
        Assert.Contains("tarifa conservada", html);
        Assert.Contains("Protecci&#xF3;n", html);
        Assert.Contains("Hay cambios sin guardar", html);
        var decoded = WebUtility.HtmlDecode(html);
        Assert.Contains("¿Cómo modifica el precio?", decoded);
        Assert.Contains("Sumar un importe fijo ($)", decoded);
        Assert.Contains("Sumar un porcentaje (%)", decoded);
        Assert.Contains("Descontar un importe fijo ($)", decoded);
        Assert.Contains("Descontar un porcentaje (%)", decoded);
        Assert.Contains("Importe a sumar (UYU)", decoded);
        Assert.Contains("Porcentaje a sumar (%)", decoded);
        Assert.Contains("Importe a descontar (UYU)", decoded);
        Assert.Contains("Porcentaje a descontar (%)", decoded);
        Assert.Contains("Se aplica una vez por envío.", decoded);
        Assert.Contains("Se calcula sobre el subtotal de base, peso y volumen.", decoded);

        using var saved = await client.PostAsync("/tarifas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "4"),
            ("Tarifas[0].ZonaId", TariffActiveZoneId.ToString("D")), ("Tarifas[0].Modalidad", "Estandar"),
            ("Tarifas[0].ImporteBaseUyu", "12,34"), ("Tarifas[0].PrecioPorKilogramoUyu", "1,123456"),
            ("Tarifas[0].PrecioPorMetroCubicoUyu", "7,654321"),
            ("Tarifas[0].Ajustes[0].Nombre", "Protección"), ("Tarifas[0].Ajustes[0].Tipo", "RecargoFijo"), ("Tarifas[0].Ajustes[0].Valor", "4,50"),
            ("Tarifas[1].ZonaId", TariffInactiveZoneId.ToString("D")), ("Tarifas[1].Modalidad", "Urgente"),
            ("Tarifas[1].ImporteBaseUyu", "0"), ("Tarifas[1].PrecioPorKilogramoUyu", "0"),
            ("Tarifas[1].PrecioPorMetroCubicoUyu", "0")));

        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        Assert.Equal("/tarifas/borrador", saved.Headers.Location!.OriginalString);
        var call = Assert.Single(api.TariffPayloads, item => item.Path == "/api/configuracion/tarifas/borrador/tarifas");
        Assert.Equal("jwt-user", call.Token);
        Assert.Equal(4, call.Body.GetProperty("versionConcurrencia").GetInt64());
        var rates = call.Body.GetProperty("tarifas").EnumerateArray().ToArray();
        Assert.Equal(2, rates.Length);
        Assert.Equal(TariffActiveZoneId, rates[0].GetProperty("zonaId").GetGuid());
        Assert.Equal(1.123456m, rates[0].GetProperty("precioPorKilogramoUyu").GetDecimal());
        Assert.Equal(4.50m, rates[0].GetProperty("ajustes")[0].GetProperty("importeFijoUyu").GetDecimal());
        Assert.Equal(TariffInactiveZoneId, rates[1].GetProperty("zonaId").GetGuid());
        Assert.Equal(0m, rates[1].GetProperty("importeBaseUyu").GetDecimal());
    }

    [Fact]
    public async Task TariffStaleDraftKeepsFormValuesAndDoesNotRetryReplacement()
    {
        var api = new FakeApi { HasTariffDraft = true, UseTariffZoneCatalog = true,
            TariffSaveStatusOverride = HttpStatusCode.Conflict };
        api.TariffZones = [new(TariffActiveZoneId, "Centro", "Activa")];
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/tarifas/borrador");

        using var response = await client.PostAsync("/tarifas/borrador?handler=Guardar", Form(token,
            ("VersionConcurrencia", "4"),
            ("Tarifas[0].ZonaId", TariffActiveZoneId.ToString("D")), ("Tarifas[0].Modalidad", "Estandar"),
            ("Tarifas[0].ImporteBaseUyu", "555,25"), ("Tarifas[0].PrecioPorKilogramoUyu", "1"),
            ("Tarifas[0].PrecioPorMetroCubicoUyu", "2")));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no se sobrescribieron cambios", html);
        Assert.Contains("555,25", html);
        Assert.Contains("Recargar borrador actual", html);
        Assert.Single(api.TariffPayloads, item => item.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task TariffPublicationRequiresConfirmationAndNeverSavesDraftAsASideEffect()
    {
        var api = new FakeApi { HasTariffDraft = true, UseTariffZoneCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        api.TariffDraftRates = [new(TariffActiveZoneId, "Estandar", 10m, 1m, 2m,
            [new("Protección", "RecargoFijo", 3.75m, null)])];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        var token = await GetAntiforgeryTokenAsync(client, "/tarifas/publicar");
        using var page = await client.GetAsync("/tarifas/publicar");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("publicación inmutable", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Sumar un importe fijo ($)", WebUtility.HtmlDecode(html));
        Assert.Contains("ConfirmarPublicacion", html);

        using var unconfirmed = await client.PostAsync("/tarifas/publicar", Form(token, ("VersionConcurrencia", "4")));
        Assert.Equal(HttpStatusCode.OK, unconfirmed.StatusCode);
        Assert.Empty(api.TariffPayloads);

        token = await GetAntiforgeryTokenAsync(client, "/tarifas/publicar");
        using var published = await client.PostAsync("/tarifas/publicar", Form(token,
            ("VersionConcurrencia", "4"), ("ConfirmarPublicacion", "true")));
        Assert.Equal(HttpStatusCode.Redirect, published.StatusCode);
        Assert.StartsWith("/tarifas/publicaciones/", published.Headers.Location!.OriginalString, StringComparison.Ordinal);
        Assert.Single(api.TariffPayloads, item => item.Method == HttpMethod.Post && item.Path.EndsWith("/borrador/publicar", StringComparison.Ordinal));
        Assert.DoesNotContain(api.TariffPayloads, item => item.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task TariffSimulatorNavigationAndParcelEditingButtonsBypassUnrelatedRequiredFields()
    {
        var api = new FakeApi { HasTariffDraft = true, UseTariffZoneCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var page = await client.GetAsync($"/tarifas/simular?versionTarifarioId={TariffDraftId:D}");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        static string ButtonTag(string markup, string handler) => Regex.Match(markup,
            $"<button\\b(?=[^>]*formaction=\\\"[^\\\"]*handler={handler})[^>]*>", RegexOptions.Singleline).Value;

        foreach (var handler in new[] { "CargarVersion", "CargarZona", "AgregarBulto" })
            Assert.Contains("formnovalidate", ButtonTag(html, handler), StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain("formnovalidate", ButtonTag(html, "Simular"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TariffSimulatorSendsEveryParcelAndRendersThePersistedApiBreakdown()
    {
        var api = new FakeApi { HasTariffDraft = true, UseTariffZoneCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var page = await client.GetAsync($"/tarifas/simular?versionTarifarioId={TariffDraftId:D}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var token = await GetAntiforgeryTokenAsync(client, $"/tarifas/simular?versionTarifarioId={TariffDraftId:D}");
        using var response = await client.PostAsync("/tarifas/simular?handler=Simular", Form(token,
            ("VersionTarifarioId", TariffDraftId.ToString("D")), ("ZonaId", TariffActiveZoneId.ToString("D")),
            ("Modalidad", "Estandar"),
            ("Bultos[0].PesoGramos", "1000"), ("Bultos[0].LargoCentimetros", "20"),
            ("Bultos[0].AnchoCentimetros", "10"), ("Bultos[0].AltoCentimetros", "5"),
            ("Bultos[1].PesoGramos", "500"), ("Bultos[1].LargoCentimetros", "10"),
            ("Bultos[1].AnchoCentimetros", "10"), ("Bultos[1].AltoCentimetros", "10")));

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("UYU 265,84", html);
        Assert.Contains("Total antes del piso cero", html);
        Assert.Contains("Sumar un porcentaje (%)", WebUtility.HtmlDecode(html));
        var simulation = Assert.Single(api.TariffPayloads, item => item.Path == "/api/configuracion/tarifas/simulaciones");
        Assert.Equal("jwt-user", simulation.Token);
        Assert.Equal(2, simulation.Body.GetProperty("bultos").GetArrayLength());
        Assert.False(simulation.Body.TryGetProperty("importeBaseUyu", out _));
    }

    [Fact]
    public async Task TariffDraftCreationIsAntiforgeryProtectedAndCopiesFromCurrentPublication()
    {
        var api = new FakeApi { HasCurrentTariffPublication = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var home = await client.GetAsync("/tarifas");
        var homeHtml = await home.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, home.StatusCode);
        Assert.Contains("Crear copia como borrador", homeHtml);
        var token = await GetAntiforgeryTokenAsync(client, "/tarifas");
        using var rejected = await client.PostAsync("/tarifas?handler=CrearBorrador",
            new FormUrlEncodedContent(new Dictionary<string, string>()));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/configuracion/tarifas/borrador" && call.Method == HttpMethod.Post);

        using var created = await client.PostAsync("/tarifas?handler=CrearBorrador", Form(token));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal("/tarifas/borrador", created.Headers.Location!.OriginalString);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/tarifas/borrador" &&
            call.Method == HttpMethod.Post && call.Token == "jwt-user");
    }

    [Fact]
    public async Task TariffRemovalStaysLocalUntilFullDraftReplacement()
    {
        var api = new FakeApi { HasTariffDraft = true, UseTariffZoneCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        api.TariffZones = [new(TariffActiveZoneId, "Centro", "Activa")];
        api.TariffDraftRates = [new(TariffActiveZoneId, "Estandar", 10m, 1m, 2m, [])];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/tarifas/borrador");

        using var removed = await client.PostAsync("/tarifas/borrador?indice=0&handler=QuitarTarifa", Form(token,
            ("VersionConcurrencia", "4"), ("Tarifas[0].ZonaId", TariffActiveZoneId.ToString("D")),
            ("Tarifas[0].Modalidad", "Estandar"), ("Tarifas[0].ImporteBaseUyu", "10"),
            ("Tarifas[0].PrecioPorKilogramoUyu", "1"), ("Tarifas[0].PrecioPorMetroCubicoUyu", "2")));

        var html = await removed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        Assert.Contains("Hay cambios sin guardar", html);
        Assert.DoesNotContain("Tarifa 1", html);
        Assert.Empty(api.TariffPayloads);
    }

    [Fact]
    public async Task TariffPublishedHistoryAndDetailAreReadOnlyAndUsePagedApiContract()
    {
        var api = new FakeApi { HasCurrentTariffPublication = true, UseTariffZoneCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        api.TariffZones = [new(TariffActiveZoneId, "Centro", "Activa")];
        api.TariffPublishedRates = [new(TariffActiveZoneId, "Urgente", 0m, 2m, 3m,
            [new("Descuento", "BonificacionPorcentual", null, 5m)])];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var history = await client.GetAsync("/tarifas/publicaciones?pagina=1&tamanoPagina=10");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains(api.Calls, call => call.Path == "/api/configuracion/tarifas/publicaciones" &&
            call.Query.Contains("pagina=1", StringComparison.Ordinal) && call.Query.Contains("tamanoPagina=10", StringComparison.Ordinal));

        using var detail = await client.GetAsync($"/tarifas/publicaciones/{TariffPublishedId:D}");
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        Assert.Contains("UYU 0", html);
        Assert.Contains("Descontar un porcentaje (%)", WebUtility.HtmlDecode(html));
        Assert.DoesNotContain("name=\"Tarifas[0]", html);
        Assert.DoesNotContain("Guardar borrador", html);
        Assert.Contains("Simular esta publicaci", html);
    }

    [Fact]
    public async Task TariffPermissionDoesNotGrantCommerceAdministrativeScope()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-commerce"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "commerce@example.test");

        using var response = await client.GetAsync("/tarifas");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/ingresar", response.Headers.Location!.AbsolutePath);
        Assert.DoesNotContain(api.Calls, call => call.Path == "/api/configuracion/tarifas");
    }

    [Fact]
    public async Task TariffPagesKeepExpiredForbiddenAndMissingResourceResponsesControlled()
    {
        var expiredApi = new FakeApi { TariffStateStatusOverride = HttpStatusCode.Unauthorized };
        expiredApi.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using (var factory = new BackofficeFactory(expiredApi))
        using (var client = Client(factory))
        {
            await LoginAsync(client, "user@example.test");
            using var expired = await client.GetAsync("/tarifas");
            Assert.Equal(HttpStatusCode.Redirect, expired.StatusCode);
            Assert.Equal("/ingresar?expirada=true", expired.Headers.Location!.OriginalString);
        }

        var forbiddenApi = new FakeApi { TariffStateStatusOverride = HttpStatusCode.Forbidden };
        forbiddenApi.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using (var factory = new BackofficeFactory(forbiddenApi))
        using (var client = Client(factory))
        {
            await LoginAsync(client, "user@example.test");
            using var forbidden = await client.GetAsync("/tarifas");
            Assert.Equal(HttpStatusCode.OK, forbidden.StatusCode);
            Assert.Contains("No ten", await forbidden.Content.ReadAsStringAsync());
        }

        var missingApi = new FakeApi();
        missingApi.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using (var factory = new BackofficeFactory(missingApi))
        using (var client = Client(factory))
        {
            await LoginAsync(client, "user@example.test");
            using var missing = await client.GetAsync("/tarifas/publicaciones/00000000-0000-0000-0000-000000000999");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
    }

    [Fact]
    public async Task ZoneCreateWithCatalogPostsGeoJsonObjectThenRedirectsToInactiveZone()
    {
        var api = new FakeApi { EmptyDepartmentCatalog = false };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api, mapboxConfigured: true);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        var token = await GetAntiforgeryTokenAsync(client, "/zonas/crear");
        using var page = await client.GetAsync("/zonas/crear");
        var html = await page.Content.ReadAsStringAsync();
        Assert.Contains("Artigas (01)", html);
        Assert.Contains("Dibujo propio en el mapa", html);
        Assert.Contains("mapbox-gl-js/v3.33.0/mapbox-gl.js", html);
        Assert.Contains("plugins/mapbox-gl-draw/v1.5.1/mapbox-gl-draw.js", html);

        const string geometry = "{\"type\":\"Polygon\",\"coordinates\":[[[-56,-34],[-55,-34],[-55,-33],[-56,-34]]]}";
        using var created = await client.PostAsync("/zonas/crear", Form(token,
            ("Nombre", "Zona Centro"), ("Fuente", "Dibujo"), ("GeometriaJson", geometry)));

        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        Assert.Equal($"/zonas/{CoverageZoneId}", created.Headers.Location!.OriginalString);
        var request = Assert.Single(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path == "/api/configuracion/zonas");
        var body = request.Body;
        Assert.Equal("Zona Centro", body.GetProperty("nombre").GetString());
        Assert.Equal("Polygon", body.GetProperty("geometria").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Array, body.GetProperty("geometria").GetProperty("coordinates").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("codigoDepartamento").ValueKind);
        Assert.Equal("jwt-user", request.Token);
    }

    [Fact]
    public async Task ZoneCreateExplainsAnEmptyCatalogSeparatelyFromMapboxConfiguration()
    {
        var api = new FakeApi { EmptyDepartmentCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api, mapboxConfigured: true);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var page = await client.GetAsync("/zonas/crear");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("Las referencias departamentales todavía no están cargadas", html);
        Assert.DoesNotContain("falta configurar un token público de Mapbox", html);
        Assert.Contains("data-territory-url=", html);
    }

    [Fact]
    public async Task ZoneCreateReportsCatalogTransportFailureInsteadOfCallingItEmpty()
    {
        var api = new FakeApi { DepartmentCatalogStatusOverride = HttpStatusCode.ServiceUnavailable };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api, mapboxConfigured: true);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var page = await client.GetAsync("/zonas/crear");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("No se pudo consultar el catálogo departamental. Revisá la disponibilidad de la API", html);
        Assert.DoesNotContain("Las referencias departamentales todavía no están cargadas", html);
        Assert.DoesNotContain("falta configurar un token público de Mapbox", html);
    }

    [Fact]
    public async Task MissingMapboxConfigurationDoesNotPretendToSaveDrawnGeometry()
    {
        var api = new FakeApi { EmptyDepartmentCatalog = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/zonas/crear");

        const string geometry = "{\"type\":\"Polygon\",\"coordinates\":[[[-56,-34],[-55,-34],[-55,-33],[-56,-34]]]}";
        using var response = await client.PostAsync("/zonas/crear", Form(token,
            ("Nombre", "No guardada"), ("Fuente", "Dibujo"), ("GeometriaJson", geometry)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("falta configurar un token público de Mapbox", html);
        Assert.DoesNotContain("id=\"coverage-map\"", html);
        Assert.DoesNotContain(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path == "/api/configuracion/zonas");
    }

    [Fact]
    public async Task ZoneCreateTurnsApiBadRequestIntoControlledValidationMessage()
    {
        var api = new FakeApi { ZoneCreateStatusOverride = HttpStatusCode.BadRequest };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api, mapboxConfigured: true);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/zonas/crear");
        const string geometry = "{\"type\":\"Polygon\",\"coordinates\":[[[-56,-34],[-55,-34],[-55,-33],[-56,-34]]]}";

        using var response = await client.PostAsync("/zonas/crear", Form(token,
            ("Nombre", "Zona Centro"), ("Fuente", "Dibujo"), ("GeometriaJson", geometry)));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        var alert = Regex.Match(html, "<p class=\\\"alert [^\\\"]+\\\" role=\\\"alert\\\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;
        Assert.True(WebUtility.HtmlDecode(alert).Contains("La API rechazó los datos", StringComparison.Ordinal),
            $"Alert: {WebUtility.HtmlDecode(alert)}; API calls: {string.Join(", ", api.Calls.Select(x => $"{x.Method} {x.Path}"))}");
        Assert.DoesNotContain("internal geometry exception", WebUtility.HtmlDecode(alert));
        Assert.DoesNotContain("coordinates", WebUtility.HtmlDecode(alert));
    }

    [Fact]
    public async Task ZoneDetailNotFoundIsScopedAndUsesNotFoundStatus()
    {
        var api = new FakeApi { ZoneDetailStatusOverride = HttpStatusCode.NotFound };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync($"/zonas/{CoverageZoneId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("La zona, franja o referencia ya no existe para este operador", html);
        Assert.DoesNotContain(CoverageZoneId.ToString(), html);
    }

    [Fact]
    public async Task ExpiredZoneApiSessionUsesExistingSignInRedirect()
    {
        var api = new FakeApi { ZoneListStatusOverride = HttpStatusCode.Unauthorized };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync("/zonas");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/ingresar?expirada=true", response.Headers.Location!.OriginalString, StringComparison.Ordinal);
        using var afterExpiry = await client.GetAsync("/zonas");
        Assert.StartsWith("/ingresar", afterExpiry.Headers.Location!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ZoneSlotTimeSelectsUseInvariantValuesAndPreserveUnchangedPrecision()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        var pageToken = await GetAntiforgeryTokenAsync(client, $"/zonas/{CoverageZoneId}");
        using var detail = await client.GetAsync($"/zonas/{CoverageZoneId}");
        var html = await detail.Content.ReadAsStringAsync();
        Assert.Contains("Versión del agregado: <strong>4</strong>", html);
        Assert.Contains("Revisión vigente: <strong>7</strong>", html);
        Assert.Contains("23:59:59.999999", html);
        Assert.Contains("00:00:00.000001", html);
        Assert.Contains("<legend class=\"h6\">Inicio</legend>", html);
        Assert.Contains("<legend class=\"h6\">Fin</legend>", html);
        Assert.Contains("name=\"HoraInicioHora\"", html);
        Assert.Contains("name=\"HoraInicioMinuto\"", html);
        Assert.Contains("value=\"00\">00</option>", html);
        Assert.Contains("value=\"23\">23</option>", html);
        Assert.Contains("value=\"59\">59</option>", html);
        Assert.DoesNotContain("name=\"HoraInicio\"", html); // no longer posted as a free-form time value

        using var response = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(pageToken,
            ("DiaSemanaIso", "7"), ("HoraInicioHora", "00"), ("HoraInicioMinuto", "00"),
            ("HoraFinHora", "23"), ("HoraFinMinuto", "59"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var request = Assert.Single(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path.EndsWith("/franjas", StringComparison.Ordinal));
        Assert.Equal("00:00:00.000000", request.Body.GetProperty("horaInicio").GetString());
        Assert.Equal("23:59:00.000000", request.Body.GetProperty("horaFin").GetString());
        Assert.Equal((short)7, request.Body.GetProperty("diaSemanaIso").GetInt16());
        Assert.Equal(4, request.Body.GetProperty("versionZona").GetInt64());

        using var updated = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=EditarFranja", Form(pageToken,
            ("franjaId", CoverageSlotId.ToString()), ("DiaSemanaIso", "7"),
            ("HoraInicioHora", "23"), ("HoraInicioMinuto", "59"),
            ("HoraFinHora", "00"), ("HoraFinMinuto", "00"), ("EstadoFranja", "Activa"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.Redirect, updated.StatusCode);
        var update = Assert.Single(api.ZonePayloads, x => x.Method == HttpMethod.Put && x.Path.EndsWith($"/franjas/{CoverageSlotId}", StringComparison.Ordinal));
        Assert.Equal("23:59:59.999999", update.Body.GetProperty("horaInicio").GetString());
        Assert.Equal("00:00:00.000001", update.Body.GetProperty("horaFin").GetString());

        using var changed = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=EditarFranja", Form(pageToken,
            ("franjaId", CoverageSlotId.ToString()), ("DiaSemanaIso", "7"),
            ("HoraInicioHora", "23"), ("HoraInicioMinuto", "58"),
            ("HoraFinHora", "00"), ("HoraFinMinuto", "01"), ("EstadoFranja", "Activa"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.Redirect, changed.StatusCode);
        var changedUpdate = api.ZonePayloads.Last(x => x.Method == HttpMethod.Put && x.Path.EndsWith($"/franjas/{CoverageSlotId}", StringComparison.Ordinal));
        Assert.Equal("23:58:00.000000", changedUpdate.Body.GetProperty("horaInicio").GetString());
        Assert.Equal("00:01:00.000000", changedUpdate.Body.GetProperty("horaFin").GetString());
    }

    [Fact]
    public async Task SlotTimeValidationAndApiErrorsKeepSelectionsWithoutDefaultingMissingTimes()
    {
        var api = new FakeApi { SlotCreateStatusOverride = HttpStatusCode.BadRequest, SlotUpdateStatusOverride = HttpStatusCode.BadRequest };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, $"/zonas/{CoverageZoneId}");

        using var missing = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(token,
            ("DiaSemanaIso", "3"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        var missingHtml = await missing.Content.ReadAsStringAsync();
        var missingAlert = Regex.Match(missingHtml, "<p class=\\\"alert [^\\\"]+\\\" role=\\\"alert\\\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Contains("Elegí un día ISO y hora y minuto para el inicio y el fin", WebUtility.HtmlDecode(missingAlert));
        Assert.Contains("id=\"new-start-hour\"", missingHtml);
        Assert.Contains("name=\"HoraInicioHora\"", missingHtml);
        var missingCreateForm = Regex.Match(missingHtml, "<form id=\\\"new-slot-form\\\".*?</form>", RegexOptions.Singleline).Value;
        Assert.DoesNotContain("value=\"00\" selected=\"selected\"", missingCreateForm);
        Assert.DoesNotContain(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path.EndsWith("/franjas", StringComparison.Ordinal));

        using var equal = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(token,
            ("DiaSemanaIso", "3"), ("HoraInicioHora", "08"), ("HoraInicioMinuto", "30"),
            ("HoraFinHora", "08"), ("HoraFinMinuto", "30"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.OK, equal.StatusCode);
        Assert.Contains("Los horarios de inicio y fin deben ser distintos", await equal.Content.ReadAsStringAsync());
        Assert.DoesNotContain(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path.EndsWith("/franjas", StringComparison.Ordinal));

        using var createRejected = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(token,
            ("DiaSemanaIso", "3"), ("HoraInicioHora", "23"), ("HoraInicioMinuto", "59"),
            ("HoraFinHora", "00"), ("HoraFinMinuto", "00"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.OK, createRejected.StatusCode);
        var createHtml = await createRejected.Content.ReadAsStringAsync();
        var createForm = Regex.Match(createHtml, "<form id=\\\"new-slot-form\\\".*?</form>", RegexOptions.Singleline).Value;
        Assert.Contains("value=\"23\" selected=\"selected\"", createForm);
        Assert.Contains("value=\"59\" selected=\"selected\"", createForm);
        Assert.Contains("value=\"00\" selected=\"selected\"", createForm);
        Assert.Contains("value=\"3\" selected=\"selected\"", createForm);

        using var editRejected = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=EditarFranja", Form(token,
            ("franjaId", CoverageSlotId.ToString()), ("DiaSemanaIso", "6"),
            ("HoraInicioHora", "22"), ("HoraInicioMinuto", "41"),
            ("HoraFinHora", "06"), ("HoraFinMinuto", "12"), ("EstadoFranja", "Inactiva"), ("VersionZona", "4")));
        Assert.Equal(HttpStatusCode.OK, editRejected.StatusCode);
        var editHtml = await editRejected.Content.ReadAsStringAsync();
        var editForm = Regex.Matches(editHtml, "<form[^>]*data-slot-time-form[^>]*>.*?</form>", RegexOptions.Singleline)
            .Cast<Match>().Select(x => x.Value).FirstOrDefault(x => x.Contains(CoverageSlotId.ToString(), StringComparison.Ordinal));
        Assert.NotNull(editForm);
        Assert.Contains("value=\"22\" selected=\"selected\"", editForm);
        Assert.Contains("value=\"41\" selected=\"selected\"", editForm);
        Assert.Contains("value=\"06\" selected=\"selected\"", editForm);
        Assert.Contains("value=\"12\" selected=\"selected\"", editForm);
        Assert.Contains("value=\"6\" selected=\"selected\"", editForm);
        Assert.Contains("value=\"Inactiva\" selected=\"selected\"", editForm);
    }

    [Fact]
    public async Task ZeroIsValidAggregateVersionButMissingVersionIsRejected()
    {
        var api = new FakeApi { ZoneVersion = 0 };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, $"/zonas/{CoverageZoneId}");

        using var missing = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(token,
            ("DiaSemanaIso", "1"), ("HoraInicioHora", "08"), ("HoraInicioMinuto", "00"),
            ("HoraFinHora", "09"), ("HoraFinMinuto", "00")));
        Assert.Equal(HttpStatusCode.OK, missing.StatusCode);
        Assert.Contains("Recargá la zona para obtener una versión válida", WebUtility.HtmlDecode(await missing.Content.ReadAsStringAsync()));
        Assert.DoesNotContain(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path.EndsWith("/franjas", StringComparison.Ordinal));

        using var zeroVersion = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(token,
            ("DiaSemanaIso", "1"), ("HoraInicioHora", "08"), ("HoraInicioMinuto", "00"),
            ("HoraFinHora", "09"), ("HoraFinMinuto", "00"), ("VersionZona", "0")));
        Assert.Equal(HttpStatusCode.Redirect, zeroVersion.StatusCode);
        var request = Assert.Single(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path.EndsWith("/franjas", StringComparison.Ordinal));
        Assert.Equal(0, request.Body.GetProperty("versionZona").GetInt64());
    }

    [Fact]
    public async Task InactiveZoneCannotBeActivatedWithoutActiveSlot()
    {
        var api = new FakeApi { ZoneHasActiveSlot = false };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");

        using var response = await client.GetAsync($"/zonas/{CoverageZoneId}");

        var html = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Agregá al menos una franja activa para habilitar la zona", html);
        Assert.Contains("Activar zona", html);
        Assert.Contains("disabled=\"disabled\"", html);
    }

    [Fact]
    public async Task ZoneEditConflictKeepsUserInputAndDoesNotRetryWithNewVersion()
    {
        var api = new FakeApi { ZoneUpdateStatusOverride = HttpStatusCode.Conflict };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, $"/zonas/{CoverageZoneId}");

        const string geometry = "{\"type\":\"MultiPolygon\",\"coordinates\":[[[[-56,-34],[-55,-34],[-55,-33],[-56,-34]]]]}";
        using var conflict = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=Editar", Form(token,
            ("Nombre", "Nombre que estaba editando"), ("VersionEditada", "4"), ("GeometriaJson", geometry)));

        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var html = await conflict.Content.ReadAsStringAsync();
        var alert = Regex.Match(html, "<p class=\\\"alert [^\\\"]+\\\" role=\\\"alert\\\">(.*?)</p>", RegexOptions.Singleline).Groups[1].Value;
        Assert.True(WebUtility.HtmlDecode(alert).Contains("La zona cambió mientras la editabas", StringComparison.Ordinal),
            $"Alert: {WebUtility.HtmlDecode(alert)}; API calls: {string.Join(", ", api.Calls.Select(x => $"{x.Method} {x.Path}"))}");
        Assert.Contains("value=\"Nombre que estaba editando\"", html);
        Assert.Contains("Recargar estado actual", html);
        Assert.Contains("disabled=\"disabled\"", html);
        Assert.Single(api.ZonePayloads, x => x.Method == HttpMethod.Put && x.Path == $"/api/configuracion/zonas/{CoverageZoneId}");
    }

    [Fact]
    public async Task SlotConflictExplainsOverlapWithoutClaimingVersionChanged()
    {
        var api = new FakeApi { SlotCreateStatusOverride = HttpStatusCode.Conflict };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, $"/zonas/{CoverageZoneId}");

        using var response = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=CrearFranja", Form(token,
            ("DiaSemanaIso", "7"), ("HoraInicioHora", "23"), ("HoraInicioMinuto", "59"),
            ("HoraFinHora", "00"), ("HoraFinMinuto", "00"), ("VersionZona", "4")));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Las franjas activas se superponen", html);
        Assert.DoesNotContain("La zona cambió mientras la editabas", html);
        Assert.Contains("Recargar estado actual", html);
        var request = Assert.Single(api.ZonePayloads, x => x.Method == HttpMethod.Post && x.Path.EndsWith("/franjas", StringComparison.Ordinal));
        Assert.Equal("23:59:00.000000", request.Body.GetProperty("horaInicio").GetString());
        Assert.Equal("00:00:00.000000", request.Body.GetProperty("horaFin").GetString());
    }

    [Fact]
    public async Task CoverageQueryRendersEveryOverlapWithoutSelectingOne()
    {
        var api = new FakeApi();
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var factory = new BackofficeFactory(api, mapboxConfigured: true);
        using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, $"/zonas/{CoverageZoneId}");

        using var response = await client.PostAsync($"/zonas/{CoverageZoneId}?handler=ConsultarCobertura", Form(token,
            ("Longitud", "-56.1645"), ("Latitud", "-34.9011")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("El punto coincide con varias zonas", html);
        Assert.Contains("Centro", html);
        Assert.Contains("Rambla", html);
        Assert.DoesNotContain("Seleccionada automáticamente", html);
        Assert.DoesNotContain("jwt-user", html);
        var coverageRequest = Assert.Single(api.ZonePayloads, x => x.Path == "/api/configuracion/zonas/cobertura");
        Assert.Equal(-56.1645, coverageRequest.Body.GetProperty("longitud").GetDouble());
        Assert.Equal(-34.9011, coverageRequest.Body.GetProperty("latitud").GetDouble());
    }

    [Fact]
    public async Task RecoveryPageIsPublicAndLinkedFromBackofficeLogin()
    {
        using var factory = new BackofficeFactory(new FakeApi());
        using var client = Client(factory);
        using var login = await client.GetAsync("/ingresar");
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.Contains("href=\"/recuperar-contrasena\"", await login.Content.ReadAsStringAsync());
        using var recovery = await client.GetAsync("/recuperar-contrasena");
        Assert.Equal(HttpStatusCode.OK, recovery.StatusCode);
        var html = await recovery.Content.ReadAsStringAsync();
        Assert.Contains("correo registrado", html, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("no-referrer", recovery.Headers.GetValues("Referrer-Policy").Single());
    }

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

    [Theory]
    [InlineData(false, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public async Task StateFormsPostRequestedStateAndReloadWithoutCreationValidation(bool commerce, bool desiredActive)
    {
        var api = new FakeApi { TargetActive = !desiredActive };
        if (commerce)
        {
            using var factory = new CommerceFactory(api);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            await LoginAsync(client, "commerce@example.test");
            await AssertStateFormAsync(client, api, desiredActive, "jwt-commerce");
        }
        else
        {
            using var factory = new BackofficeFactory(api);
            using var client = Client(factory);
            await LoginAsync(client, "user@example.test");
            await AssertStateFormAsync(client, api, desiredActive, "jwt-user");
        }
    }

    private static async Task AssertStateFormAsync(HttpClient client, FakeApi api, bool desiredActive, string token)
    {
        using var listing = await client.GetAsync("/usuarios");
        Assert.Equal(HttpStatusCode.OK, listing.StatusCode);
        var listingHtml = WebUtility.HtmlDecode(await listing.Content.ReadAsStringAsync());
        var inputs = Regex.Matches(listingHtml, "<input\\b[^>]*>", RegexOptions.IgnoreCase).Select(input => input.Value).ToArray();
        var userInput = inputs.FirstOrDefault(input => Regex.IsMatch(input, "name=\\\"usuarioId\\\"", RegexOptions.IgnoreCase));
        var userIdValue = userInput is null ? Match.Empty : Regex.Match(userInput, "value=\\\"([^\\\"]+)\\\"");
        Assert.True(userIdValue.Success, $"The rendered state form must include its target id; found '{userInput}'.");
        var formUserId = Guid.Parse(userIdValue.Groups[1].Value);
        Assert.Equal(TargetUserId, formUserId);

        var stateInput = inputs.FirstOrDefault(input => Regex.IsMatch(input, "name=\\\"activo\\\"", RegexOptions.IgnoreCase));
        var stateValue = stateInput is null ? Match.Empty : Regex.Match(stateInput, "value=\\\"([^\\\"]+)\\\"");
        Assert.True(stateValue.Success && bool.TryParse(stateValue.Groups[1].Value, out var formActive) && formActive == desiredActive,
            $"Expected requested state {desiredActive} in hidden form input; found '{stateInput}'.");
        Assert.Contains(desiredActive ? "Activar" : "Desactivar", listingHtml);

        var antiforgery = await GetAntiforgeryTokenAsync(client, "/usuarios");
        using var missingAntiforgery = await client.PostAsync("/usuarios?handler=Estado",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["usuarioId"] = TargetUserId.ToString(),
                ["activo"] = desiredActive.ToString()
            }));
        Assert.Equal(HttpStatusCode.BadRequest, missingAntiforgery.StatusCode);
        Assert.Empty(api.StateRequests);

        using var response = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", formUserId.ToString()), ("activo", stateValue.Groups[1].Value)));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
        Assert.Contains(desiredActive ? "Acceso activado." : "Acceso desactivado; la identidad del usuario se conserva.", html);
        Assert.Contains(desiredActive ? ">Activo<" : ">Inactivo<", html);

        var renderedText = Regex.Replace(html, "<[^>]+>", " ");
        Assert.DoesNotContain("The Email field is required.", renderedText);
        Assert.DoesNotContain("The Password field is required.", renderedText);
        Assert.DoesNotContain("The RolId field is required.", renderedText);

        var stateCall = Assert.Single(api.StateRequests);
        Assert.Equal($"/api/usuarios/{TargetUserId}/estado", stateCall.Path);
        Assert.Equal(desiredActive, stateCall.Activo);
        Assert.Equal(token, stateCall.Token);
        Assert.Contains(api.Calls, call => call.Path == stateCall.Path && call.Method == HttpMethod.Patch && call.Token == token);
        Assert.Equal(desiredActive, api.TargetActive);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task StateApiFailureNeverRendersSuccess(bool commerce)
    {
        var api = new FakeApi { TargetActive = false, StateMutationStatusOverride = HttpStatusCode.Conflict };
        if (commerce)
        {
            using var factory = new CommerceFactory(api);
            using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false, HandleCookies = true });
            await LoginAsync(client, "commerce@example.test");
            await AssertStateFailureAsync(client, api);
        }
        else
        {
            using var factory = new BackofficeFactory(api);
            using var client = Client(factory);
            await LoginAsync(client, "user@example.test");
            await AssertStateFailureAsync(client, api);
        }
    }

    private static async Task AssertStateFailureAsync(HttpClient client, FakeApi api)
    {
        var antiforgery = await GetAntiforgeryTokenAsync(client, "/usuarios");
        using var response = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "true")));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Conflicto", html);
        Assert.DoesNotContain("Acceso activado", html);
        Assert.DoesNotContain("Acceso desactivado", html);
        Assert.False(api.TargetActive);
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
    public async Task OwnerAccessAndRoleAreReadOnlyInBackofficeAndCommerce()
    {
        var api = new FakeApi { IncludeOwnerRows = true };
        api.PermissionOverrides["jwt-user"] = ["usuarios.gestionar", "configuracion.gestionar"];
        api.PermissionOverrides["jwt-commerce"] = ["usuarios.gestionar", "configuracion.gestionar"];

        using var backofficeFactory = new BackofficeFactory(api);
        using var backoffice = Client(backofficeFactory);
        await LoginAsync(backoffice, "user@example.test");
        using var backofficeUsers = await backoffice.GetAsync("/usuarios");
        var backofficeUsersHtml = WebUtility.HtmlDecode(await backofficeUsers.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, backofficeUsers.StatusCode);
        Assert.Contains("cuenta propietaria de la organización", backofficeUsersHtml);
        Assert.DoesNotContain($"name=\"usuarioId\" value=\"{TargetUserId}\"", backofficeUsersHtml);
        Assert.DoesNotContain($"name=\"accesoId\" value=\"{TargetAccessId}\"", backofficeUsersHtml);
        using var backofficeRoles = await backoffice.GetAsync("/roles");
        var backofficeRolesHtml = WebUtility.HtmlDecode(await backofficeRoles.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, backofficeRoles.StatusCode);
        Assert.Contains("asignado al propietario de la organización", backofficeRolesHtml);
        Assert.DoesNotContain($"name=\"id\" value=\"{RoleId}\"", backofficeRolesHtml);

        using var commerceFactory = new CommerceFactory(api);
        using var commerce = commerceFactory.CreateClient(new WebApplicationFactoryClientOptions
        { AllowAutoRedirect = false, HandleCookies = true });
        await LoginAsync(commerce, "commerce@example.test");
        using var commerceUsers = await commerce.GetAsync("/usuarios");
        var commerceUsersHtml = WebUtility.HtmlDecode(await commerceUsers.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, commerceUsers.StatusCode);
        Assert.Contains("cuenta propietaria del comercio", commerceUsersHtml);
        Assert.DoesNotContain($"name=\"usuarioId\" value=\"{TargetUserId}\"", commerceUsersHtml);
        Assert.DoesNotContain($"name=\"accesoId\" value=\"{TargetAccessId}\"", commerceUsersHtml);
        using var commerceRoles = await commerce.GetAsync("/roles");
        var commerceRolesHtml = WebUtility.HtmlDecode(await commerceRoles.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, commerceRoles.StatusCode);
        Assert.Contains("asignado al propietario del comercio", commerceRolesHtml);
        Assert.DoesNotContain($"name=\"id\" value=\"{RoleId}\"", commerceRolesHtml);
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
        api.StateMutationStatusOverride = HttpStatusCode.Conflict;
        using var conflict = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "false")));
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Conflicto", conflictHtml);
        Assert.Contains("Recargar estado", conflictHtml);
        Assert.DoesNotContain("Acceso desactivado", conflictHtml);
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

        api.StateMutationStatusOverride = HttpStatusCode.Conflict;
        using var conflict = await client.PostAsync("/usuarios?handler=Estado", Form(antiforgery,
            ("usuarioId", TargetUserId.ToString()), ("activo", "false")));
        Assert.Equal(HttpStatusCode.OK, conflict.StatusCode);
        var conflictHtml = await conflict.Content.ReadAsStringAsync();
        Assert.Contains("Conflicto:", conflictHtml);
        Assert.Contains("Recargar estado", conflictHtml);
        Assert.DoesNotContain("Acceso desactivado", conflictHtml);
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

    private sealed class BackofficeFactory(FakeApi api, bool mapboxConfigured = false) : WebApplicationFactory<NexoRuta.Backoffice.Core.Security.ApiPermissionAuthorizationHandler>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                if (mapboxConfigured)
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Mapbox:PublicAccessToken"] = "pk.test-public-token",
                        ["Mapbox:StyleUrl"] = "mapbox://styles/test/style"
                    });
            });
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
        public ConcurrentQueue<TariffPayloadCall> TariffPayloads { get; } = new();
        public ConcurrentQueue<RulesPayloadCall> RulePayloads { get; } = new();
        public ConcurrentDictionary<string, string[]> PermissionOverrides { get; } = new(StringComparer.Ordinal);
        public HttpStatusCode? PermissionStatusOverride { get; init; }
        public Exception? PermissionFailure { get; init; }
        public bool IncludeOwnAccessRow { get; init; }
        public bool IncludeOwnerRows { get; init; }
        public HttpStatusCode? RoleMutationStatusOverride { get; set; }
        public HttpStatusCode? StateMutationStatusOverride { get; set; }
        public bool TargetActive { get; set; } = true;
        public ConcurrentQueue<StateCall> StateRequests { get; } = new();
        public bool OrganizationHasOwner { get; init; } = true;
        public bool IsOrganizationOwner { get; init; } = true;
        public HttpStatusCode? OrganizationUpdateStatusOverride { get; init; }
        public bool CancellationRequestPending { get; set; }
        public string OrganizationState { get; set; } = "Activa";
        public bool EmptyDepartmentCatalog { get; init; } = true;
        public HttpStatusCode? DepartmentCatalogStatusOverride { get; init; }
        public long ZoneVersion { get; init; } = 4;
        public bool ZoneHasActiveSlot { get; init; } = true;
        public HttpStatusCode? ZoneListStatusOverride { get; init; }
        public HttpStatusCode? ZoneCreateStatusOverride { get; init; }
        public HttpStatusCode? ZoneDetailStatusOverride { get; init; }
        public HttpStatusCode? ZoneUpdateStatusOverride { get; init; }
        public HttpStatusCode? SlotCreateStatusOverride { get; init; }
        public HttpStatusCode? SlotUpdateStatusOverride { get; init; }
        public bool HasTariffDraft { get; set; }
        public bool HasCurrentTariffPublication { get; set; }
        public bool UseTariffZoneCatalog { get; set; }
        public long TariffConcurrencyVersion { get; set; } = 4;
        public int TariffPublishedNumber { get; set; } = 1;
        public HttpStatusCode? TariffSaveStatusOverride { get; set; }
        public HttpStatusCode? TariffPublishStatusOverride { get; set; }
        public HttpStatusCode? TariffStateStatusOverride { get; set; }
        public List<TariffRateFixture> TariffDraftRates { get; set; } = [new(TariffActiveZoneId, "Estandar", 10m, 1m, 2m, [])];
        public List<TariffRateFixture> TariffPublishedRates { get; set; } = [];
        public List<TariffZoneFixture> TariffZones { get; set; } = [];
        public bool HasRulesDraft { get; set; }
        public bool HasRulesPublication { get; set; }
        public long RulesConcurrencyVersion { get; set; }
        public int RulesPublishedNumber { get; set; } = 1;
        public int? RulesMaxAttempts { get; set; }
        public int? RulesIntervalMinutes { get; set; }
        public string? RulesPolicy { get; set; }
        public int? RulesStandardMinutes { get; set; }
        public int? RulesUrgentMinutes { get; set; }
        public List<string> RulesProofs { get; set; } = [];
        public List<RuleMotiveFixture> RulesMotives { get; set; } = [];
        public HttpStatusCode? RulesSaveStatusOverride { get; set; }
        public HttpStatusCode? RulesPublishStatusOverride { get; set; }
        public HttpStatusCode? RulesDraftStatusOverride { get; set; }
        public ConcurrentQueue<ZonePayloadCall> ZonePayloads { get; } = new();
        public HttpMessageHandler CreateHandler() => new ApiHandler(this);

        private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            var token = request.Headers.Authorization?.Parameter;
            Calls.Enqueue(new ApiCall(path, request.Method, token, request.RequestUri.Query));
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
            if (path == "/api/configuracion/reglas-operativas" && request.Method == HttpMethod.Get)
                return Json(HttpStatusCode.OK, RulesStateResponse());
            if (path == "/api/configuracion/reglas-operativas/borrador" && request.Method == HttpMethod.Post)
            {
                HasRulesDraft = true;
                return Json(HttpStatusCode.Created, RulesVersionResponse(RulesDraftId, "Borrador", null));
            }
            if (path == "/api/configuracion/reglas-operativas/borrador" && request.Method == HttpMethod.Get)
            {
                if (RulesDraftStatusOverride is { } status)
                    return Json(status, new { message = "rules draft request failed" });
                if (!HasRulesDraft) return Json(HttpStatusCode.NotFound, new { message = "Not found" });
                return Json(HttpStatusCode.OK, RulesVersionResponse(RulesDraftId, "Borrador", null));
            }
            if (path == "/api/configuracion/reglas-operativas/borrador" && request.Method == HttpMethod.Put)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                RulePayloads.Enqueue(new(path, request.Method, token, body));
                if (RulesSaveStatusOverride is { } status)
                    return Json(status, new { message = "El borrador cambió durante la edición." });
                RulesConcurrencyVersion++;
                RulesMaxAttempts = ReadNullableInt(body, "maxIntentosEntrega");
                RulesIntervalMinutes = ReadNullableInt(body, "intervaloEntreIntentosMinutos");
                RulesPolicy = body.GetProperty("politicaDevolucionAlAgotarIntentos").ValueKind == JsonValueKind.Null
                    ? null : body.GetProperty("politicaDevolucionAlAgotarIntentos").GetString();
                RulesStandardMinutes = ReadNullableInt(body, "plazoEstandarMinutos");
                RulesUrgentMinutes = ReadNullableInt(body, "plazoUrgenteMinutos");
                RulesProofs = body.GetProperty("requisitosPrueba").EnumerateArray().Select(item => item.GetString()!).ToList();
                RulesMotives = body.GetProperty("motivos").EnumerateArray().Select(item => new RuleMotiveFixture(
                    item.GetProperty("codigo").GetString()!, item.GetProperty("descripcion").GetString()!, item.GetProperty("disponible").GetBoolean())).ToList();
                return Json(HttpStatusCode.OK, RulesVersionResponse(RulesDraftId, "Borrador", null));
            }
            if (path == "/api/configuracion/reglas-operativas/borrador/publicar" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                RulePayloads.Enqueue(new(path, request.Method, token, body));
                if (RulesPublishStatusOverride is { } status)
                    return Json(status, new { message = "El borrador requiere correcciones antes de publicar." });
                HasRulesDraft = false;
                HasRulesPublication = true;
                RulesPublishedNumber++;
                return Json(HttpStatusCode.OK, RulesVersionResponse(RulesPublishedId, "Publicada", RulesPublishedNumber));
            }
            if (path == "/api/configuracion/reglas-operativas/publicaciones" && request.Method == HttpMethod.Get)
            {
                var page = QueryInteger(request.RequestUri!.Query, "pagina", 1);
                var pageSize = QueryInteger(request.RequestUri.Query, "tamanoPagina", 20);
                var allItems = HasRulesPublication
                    ? Enumerable.Range(1, RulesPublishedNumber).Select(number => (object)RulesVersionSummary(
                        number == RulesPublishedNumber ? RulesPublishedId : RulesHistoryId(number), number)).Reverse().ToArray()
                    : Array.Empty<object>();
                var items = allItems.Skip((page - 1) * pageSize).Take(pageSize);
                return Json(HttpStatusCode.OK, new { items, pagina = page, tamanoPagina = pageSize, total = allItems.Length });
            }
            if (path.StartsWith("/api/configuracion/reglas-operativas/publicaciones/", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                if (!HasRulesPublication || !Guid.TryParse(path["/api/configuracion/reglas-operativas/publicaciones/".Length..], out var versionId))
                    return Json(HttpStatusCode.NotFound, new { message = "Not found" });
                if (versionId != RulesPublishedId && !Enumerable.Range(1, RulesPublishedNumber).Any(number => RulesHistoryId(number) == versionId))
                    return Json(HttpStatusCode.NotFound, new { message = "Not found" });
                return Json(HttpStatusCode.OK, RulesVersionResponse(versionId, "Publicada", RulesPublishedNumber));
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
                    email = "target@example.test", rolId = RoleId, rolCodigo = "dynamic", rolNombre = "Dynamic role",
                    activo = TargetActive, version = 0L, rolProtegido = false, esPropietario = IncludeOwnerRows } };
                if (IncludeOwnAccessRow)
                    users.Add(new { usuarioId = CurrentUserId, accesoId = OwnAccessId,
                        email = "admin@example.test", rolId = OwnRoleId, rolCodigo = "own", rolNombre = "Own role",
                        activo = true, version = 0L, rolProtegido = false, esPropietario = false });
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
                var activo = body.GetProperty("activo").GetBoolean();
                StateRequests.Enqueue(new(path, activo, token));
                if (StateMutationStatusOverride is { } status)
                    return Json(status, new { message = "El estado cambió durante la operación." });
                TargetActive = activo;
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if ((path == "/api/roles/asignables" || path == "/api/roles") && request.Method == HttpMethod.Get)
            {
                var roles = new List<object> { new { id = RoleId, codigo = "dynamic", nombre = "Dynamic role",
                    permisos = new[] { "envios.consultar" }, esProtegidoSistema = false, esPropio = false,
                    esRolPropietario = IncludeOwnerRows } };
                if (IncludeOwnAccessRow && path == "/api/roles")
                {
                    roles.Add(new { id = OwnRoleId, codigo = "own", nombre = "Own role", permisos = new[] { "configuracion.gestionar" }, esProtegidoSistema = false, esPropio = true, esRolPropietario = false });
                    roles.Add(new { id = ProtectedRoleId, codigo = "system", nombre = "System role", permisos = new[] { "configuracion.gestionar" }, esProtegidoSistema = true, esPropio = false, esRolPropietario = false });
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
            if (path == "/api/configuracion/tarifas" && request.Method == HttpMethod.Get)
            {
                if (TariffStateStatusOverride is { } stateStatus)
                    return Json(stateStatus, new { message = "tariff state unavailable" });
                return Json(HttpStatusCode.OK, TariffStateResponse());
            }
            if (path == "/api/configuracion/tarifas/borrador" && request.Method == HttpMethod.Post)
            {
                HasTariffDraft = true;
                return Json(HttpStatusCode.Created, TariffVersionResponse(TariffDraftId, "Borrador", null, TariffConcurrencyVersion, TariffDraftRates));
            }
            if (path == "/api/configuracion/tarifas/borrador" && request.Method == HttpMethod.Get)
            {
                if (!HasTariffDraft) return Json(HttpStatusCode.NotFound, new { message = "Not found" });
                return Json(HttpStatusCode.OK, TariffVersionResponse(TariffDraftId, "Borrador", null, TariffConcurrencyVersion, TariffDraftRates));
            }
            if (path == "/api/configuracion/tarifas/borrador/tarifas" && request.Method == HttpMethod.Put)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                TariffPayloads.Enqueue(new(path, request.Method, token, body));
                if (TariffSaveStatusOverride is { } saveStatus)
                    return Json(saveStatus, new { message = "The draft changed." });
                TariffConcurrencyVersion++;
                TariffDraftRates = body.GetProperty("tarifas").EnumerateArray().Select(rate => new TariffRateFixture(
                    rate.GetProperty("zonaId").GetGuid(), rate.GetProperty("modalidad").GetString()!,
                    rate.GetProperty("importeBaseUyu").GetDecimal(), rate.GetProperty("precioPorKilogramoUyu").GetDecimal(),
                    rate.GetProperty("precioPorMetroCubicoUyu").GetDecimal(), rate.GetProperty("ajustes").EnumerateArray()
                        .Select(adjustment => new TariffAdjustmentFixture(adjustment.GetProperty("nombre").GetString()!,
                            adjustment.GetProperty("tipo").GetString()!, ReadNullableDecimal(adjustment, "importeFijoUyu"),
                            ReadNullableDecimal(adjustment, "porcentaje"))).ToList())).ToList();
                return Json(HttpStatusCode.OK, TariffVersionResponse(TariffDraftId, "Borrador", null, TariffConcurrencyVersion, TariffDraftRates));
            }
            if (path == "/api/configuracion/tarifas/borrador/publicar" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                TariffPayloads.Enqueue(new(path, request.Method, token, body));
                if (TariffPublishStatusOverride is { } publishStatus)
                    return Json(publishStatus, new { message = "The draft cannot be published." });
                HasCurrentTariffPublication = true;
                HasTariffDraft = false;
                TariffPublishedRates = [.. TariffDraftRates];
                return Json(HttpStatusCode.OK, TariffVersionResponse(TariffPublishedId, "Publicada", TariffPublishedNumber, TariffConcurrencyVersion, TariffPublishedRates));
            }
            if (path == "/api/configuracion/tarifas/publicaciones" && request.Method == HttpMethod.Get)
            {
                var query = request.RequestUri!.Query;
                var page = QueryInteger(query, "pagina", 1);
                var pageSize = QueryInteger(query, "tamanoPagina", 20);
                var allItems = HasCurrentTariffPublication
                    ? new[] { TariffVersionSummary(TariffPublishedId, "Publicada", TariffPublishedNumber, TariffConcurrencyVersion) }
                    : Array.Empty<object>();
                var items = allItems.Skip((page - 1) * pageSize).Take(pageSize);
                return Json(HttpStatusCode.OK, new { items, pagina = page, tamanoPagina = pageSize, total = allItems.Length });
            }
            if (path.StartsWith("/api/configuracion/tarifas/publicaciones/", StringComparison.Ordinal) && request.Method == HttpMethod.Get)
            {
                if (!Guid.TryParse(path[("/api/configuracion/tarifas/publicaciones/".Length)..], out var versionId) ||
                    versionId != TariffPublishedId || !HasCurrentTariffPublication)
                    return Json(HttpStatusCode.NotFound, new { message = "Not found" });
                return Json(HttpStatusCode.OK, TariffVersionResponse(TariffPublishedId, "Publicada", TariffPublishedNumber, TariffConcurrencyVersion,
                    TariffPublishedRates.Count > 0 ? TariffPublishedRates : TariffDraftRates));
            }
            if (path == "/api/configuracion/tarifas/simulaciones" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                TariffPayloads.Enqueue(new(path, request.Method, token, body));
                return Json(HttpStatusCode.OK, new
                {
                    origen = "Borrador", versionTarifarioId = TariffDraftId, numeroVersionPublicada = (int?)null,
                    versionConcurrencia = TariffConcurrencyVersion, zonaId = body.GetProperty("zonaId").GetGuid(),
                    modalidad = body.GetProperty("modalidad").GetString(), moneda = "UYU",
                    pesoTotalKg = 1.5m, volumenTotalM3 = 0.003m, importeBaseUyu = 250m,
                    componentePesoUyu = 7.5m, componenteVolumenUyu = 8.34m,
                    ajustes = new[] { new { nombre = "Recargo demo", tipo = "RecargoPorcentual", valorConfigurado = 15m,
                        @base = "SubtotalOriginal", importeUyu = 35.9775m } },
                    subtotalUyu = 265.84m, recargosUyu = 0m, bonificacionesUyu = 0m,
                    totalPrevioPisoUyu = 265.84m, pisoCeroAplicado = false, totalFinalUyu = 265.84m
                });
            }
            if (path == "/api/configuracion/zonas/departamentos" && request.Method == HttpMethod.Get)
            {
                if (DepartmentCatalogStatusOverride is { } catalogStatus)
                    return Json(catalogStatus, new { message = "catalog unavailable" });
                return Json(HttpStatusCode.OK, EmptyDepartmentCatalog ? Array.Empty<object>() : new object[]
                {
                    new { codigo = "01", versionFuente = "test-v1", nombre = "Artigas",
                        urlFuente = "https://example.test", atribucion = "test", licencia = "test", importadaUtc = DateTimeOffset.UtcNow }
                });
            }
            if (path == "/api/configuracion/zonas" && request.Method == HttpMethod.Get)
            {
                if (ZoneListStatusOverride is { } listStatus)
                    return Json(listStatus, new { message = "internal list exception" });
                if (UseTariffZoneCatalog)
                {
                    var query = request.RequestUri!.Query;
                    var stateFilter = query.Contains("estado=Inactiva", StringComparison.Ordinal) ? "Inactiva" : "Activa";
                    var page = QueryInteger(query, "pagina", 1);
                    var pageSize = QueryInteger(query, "tamanoPagina", 50);
                    var matches = TariffZones.Where(zone => zone.Estado == stateFilter).ToArray();
                    var items = matches.Skip((page - 1) * pageSize).Take(pageSize).Select(zone => (object)new
                    {
                        id = zone.Id, nombre = zone.Nombre, estado = zone.Estado, franjasActivas = 1,
                        version = ZoneVersion, revisionActual = 1, actualizadaUtc = DateTimeOffset.UtcNow
                    });
                    return Json(HttpStatusCode.OK, new { items, pagina = page, tamanoPagina = pageSize, total = matches.Length });
                }
                var state = request.RequestUri!.Query.Contains("Inactiva", StringComparison.Ordinal) ? "Inactiva" : "Activa";
                return Json(HttpStatusCode.OK, new
                {
                    items = new[] { new { id = CoverageZoneId, nombre = "Zona Centro", estado = state, franjasActivas = ZoneHasActiveSlot ? 1 : 0,
                        version = ZoneVersion, revisionActual = 7, actualizadaUtc = DateTimeOffset.UtcNow } },
                    pagina = 1, tamanoPagina = 20, total = 1
                });
            }
            if (path == "/api/configuracion/zonas" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                ZonePayloads.Enqueue(new(path, request.Method, token, body));
                if (ZoneCreateStatusOverride is { } createStatus)
                    return Json(createStatus, new { message = "internal geometry exception with coordinates" });
                return Json(HttpStatusCode.Created, ZoneDetailResponse());
            }
            if (path == "/api/configuracion/zonas/cobertura" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                ZonePayloads.Enqueue(new(path, request.Method, token, body));
                return Json(HttpStatusCode.OK, new
                {
                    resultado = "Cubierta", requiereSeleccionDeZona = true,
                    coincidencias = new[]
                    {
                        new { zonaId = CoverageZoneId, nombre = "Centro", revisionActual = 7,
                            franjasActivas = new[] { new { id = CoverageSlotId, diaSemanaIso = (short)1, horaInicio = "08:00:00.000000", horaFin = "09:00:00.000000", estado = "Activa" } } },
                        new { zonaId = Guid.Parse("00000000-0000-0000-0000-000000000403"), nombre = "Rambla", revisionActual = 2,
                            franjasActivas = new[] { new { id = Guid.Parse("00000000-0000-0000-0000-000000000404"), diaSemanaIso = (short)2, horaInicio = "10:00:00.000000", horaFin = "11:00:00.000000", estado = "Activa" } } }
                    }
                });
            }
            if (path == $"/api/configuracion/zonas/{CoverageZoneId}" && request.Method == HttpMethod.Get)
            {
                if (ZoneDetailStatusOverride is { } detailStatus)
                    return Json(detailStatus, new { message = "internal not found details" });
                return Json(HttpStatusCode.OK, ZoneDetailResponse());
            }
            if (path == $"/api/configuracion/zonas/{CoverageZoneId}" && request.Method == HttpMethod.Put)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                ZonePayloads.Enqueue(new(path, request.Method, token, body));
                if (ZoneUpdateStatusOverride is { } updateStatus)
                    return Json(updateStatus, new { message = "La configuración de la zona cambió; recargá antes de volver a guardar." });
                return Json(HttpStatusCode.OK, ZoneDetailResponse());
            }
            if (path == $"/api/configuracion/zonas/{CoverageZoneId}/franjas" && request.Method == HttpMethod.Post)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                ZonePayloads.Enqueue(new(path, request.Method, token, body));
                if (SlotCreateStatusOverride is { } slotStatus)
                    return Json(slotStatus, new { message = "Las franjas activas de una zona no pueden superponerse." });
                return Json(HttpStatusCode.Created, new { franja = new { id = Guid.NewGuid(), diaSemanaIso = (short)7,
                    horaInicio = "23:59:59.999999", horaFin = "00:00:00.000001", estado = "Activa" }, versionZona = 5L, revisionActual = 8 });
            }
            if (path.StartsWith($"/api/configuracion/zonas/{CoverageZoneId}/franjas/", StringComparison.Ordinal) && request.Method == HttpMethod.Put)
            {
                var body = (await request.Content!.ReadFromJsonAsync<JsonElement>(ct)).Clone();
                ZonePayloads.Enqueue(new(path, request.Method, token, body));
                if (SlotUpdateStatusOverride is { } slotStatus)
                    return Json(slotStatus, new { message = "The slot input was rejected." });
                return Json(HttpStatusCode.OK, new { franja = new { id = CoverageSlotId, diaSemanaIso = (short)7,
                    horaInicio = body.GetProperty("horaInicio").GetString(), horaFin = body.GetProperty("horaFin").GetString(),
                    estado = body.GetProperty("estado").GetString() }, versionZona = 5L, revisionActual = 8 });
            }
            if (path == $"/api/configuracion/zonas/{CoverageZoneId}/estado" && request.Method == HttpMethod.Put)
                return Json(HttpStatusCode.OK, ZoneDetailResponse());
            if (path == "/api/envios") return Json(HttpStatusCode.OK, Array.Empty<object>());
            return Json(HttpStatusCode.NotFound, new { message = "Not found" });
        }

        private object ZoneDetailResponse() => new
        {
            id = CoverageZoneId, nombre = "Zona Centro", estado = "Inactiva",
            geometria = new { type = "Polygon", coordinates = new[] { new[] { new[] { -56.2, -34.9 }, new[] { -56.1, -34.9 }, new[] { -56.1, -34.8 }, new[] { -56.2, -34.9 } } } },
            zonaHoraria = "America/Montevideo",
            franjas = new[] { new { id = CoverageSlotId, diaSemanaIso = (short)7, horaInicio = "23:59:59.999999", horaFin = "00:00:00.000001", estado = ZoneHasActiveSlot ? "Activa" : "Inactiva" } },
            version = ZoneVersion, revisionActual = 7, origenGeometria = "Dibujada", codigoDepartamento = (string?)null,
            versionFuente = (string?)null, creadaUtc = DateTimeOffset.UtcNow, actualizadaUtc = DateTimeOffset.UtcNow
        };

        private object TariffStateResponse()
        {
            var current = HasCurrentTariffPublication
                ? TariffVersionSummary(TariffPublishedId, "Publicada", TariffPublishedNumber, TariffConcurrencyVersion)
                : null;
            var draft = HasTariffDraft
                ? TariffVersionSummary(TariffDraftId, "Borrador", null, TariffConcurrencyVersion)
                : null;
            return new { configurado = HasCurrentTariffPublication || HasTariffDraft, publicacionVigente = current, borrador = draft };
        }

        private object RulesStateResponse()
        {
            var current = HasRulesPublication ? RulesVersionSummary(RulesPublishedId, RulesPublishedNumber) : null;
            var draft = HasRulesDraft ? RulesVersionSummary(RulesDraftId, null) : null;
            return new { configurado = HasRulesPublication || HasRulesDraft, publicacionVigente = current, borrador = draft };
        }

        private object RulesVersionSummary(Guid id, int? number)
            => new { versionReglasOperativasId = id, estado = number is null ? "Borrador" : "Publicada",
                numeroVersionPublicada = number, versionConcurrencia = RulesConcurrencyVersion,
                creadaUtc = DateTimeOffset.UtcNow, publicadaUtc = number is null ? (DateTimeOffset?)null : DateTimeOffset.UtcNow };

        private object RulesVersionResponse(Guid id, string state, int? number)
            => new { versionReglasOperativasId = id, estado = state, numeroVersionPublicada = number,
                versionConcurrencia = RulesConcurrencyVersion, maxIntentosEntrega = RulesMaxAttempts,
                intervaloEntreIntentosMinutos = RulesIntervalMinutes,
                politicaDevolucionAlAgotarIntentos = RulesPolicy, plazoEstandarMinutos = RulesStandardMinutes,
                plazoUrgenteMinutos = RulesUrgentMinutes, requisitosPrueba = RulesProofs,
                motivos = RulesMotives.Select(motive => new { codigo = motive.Code, descripcion = motive.Description, disponible = motive.Available }),
                creadaUtc = DateTimeOffset.UtcNow, publicadaUtc = state == "Publicada" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null };

        private static Guid RulesHistoryId(int number)
            => Guid.Parse($"00000000-0000-0000-0000-{700 + number:D12}");

        private static object TariffVersionSummary(Guid id, string state, int? number, long concurrencyVersion)
            => new { versionTarifarioId = id, estado = state, numeroVersionPublicada = number,
                versionConcurrencia = concurrencyVersion, moneda = "UYU", creadaUtc = DateTimeOffset.UtcNow,
                publicadaUtc = state == "Publicada" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null };

        private static object TariffVersionResponse(Guid id, string state, int? number, long concurrencyVersion,
            IReadOnlyList<TariffRateFixture> rates)
            => new { versionTarifarioId = id, estado = state, numeroVersionPublicada = number,
                versionConcurrencia = concurrencyVersion, moneda = "UYU", creadaUtc = DateTimeOffset.UtcNow,
                publicadaUtc = state == "Publicada" ? DateTimeOffset.UtcNow : (DateTimeOffset?)null,
                tarifas = rates.Select(rate => new { zonaId = rate.ZoneId, modalidad = rate.Modality,
                    importeBaseUyu = rate.BaseUyu, precioPorKilogramoUyu = rate.PerKilogramUyu,
                    precioPorMetroCubicoUyu = rate.PerCubicMeterUyu,
                    ajustes = rate.Adjustments.Select(adjustment => new { nombre = adjustment.Name, tipo = adjustment.Type,
                        importeFijoUyu = adjustment.FixedAmountUyu, porcentaje = adjustment.Percentage }) }) };

        private static decimal? ReadNullableDecimal(JsonElement parent, string property)
        {
            var value = parent.GetProperty(property);
            return value.ValueKind == JsonValueKind.Null ? null : value.GetDecimal();
        }

        private static int? ReadNullableInt(JsonElement parent, string property)
        {
            var value = parent.GetProperty(property);
            return value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
        }

        private static int QueryInteger(string query, string key, int fallback)
        {
            foreach (var segment in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var pair = segment.Split('=', 2);
                if (pair.Length == 2 && pair[0] == key && int.TryParse(pair[1], out var value)) return value;
            }
            return fallback;
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

        public sealed record ApiCall(string Path, HttpMethod Method, string? Token, string Query);
        public sealed record StateCall(string Path, bool Activo, string? Token);
        public sealed record ZonePayloadCall(string Path, HttpMethod Method, string? Token, JsonElement Body);
        public sealed record TariffPayloadCall(string Path, HttpMethod Method, string? Token, JsonElement Body);
        public sealed record RulesPayloadCall(string Path, HttpMethod Method, string? Token, JsonElement Body);

        private sealed class ApiHandler(FakeApi api) : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
                => api.SendAsync(request, cancellationToken);
        }
    }

    private sealed record TariffZoneFixture(Guid Id, string Nombre, string Estado);
    private sealed record TariffRateFixture(Guid ZoneId, string Modality, decimal BaseUyu, decimal PerKilogramUyu,
        decimal PerCubicMeterUyu, IReadOnlyList<TariffAdjustmentFixture> Adjustments);
    private sealed record TariffAdjustmentFixture(string Name, string Type, decimal? FixedAmountUyu, decimal? Percentage);
    private sealed record RuleMotiveFixture(string Code, string Description, bool Available);
}
