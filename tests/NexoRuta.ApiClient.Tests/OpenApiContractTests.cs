using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;
using NexoRuta.ApiClient.Features.Auth.Contracts.Responses;
using NexoRuta.ApiClient.Features.Auth.Contracts.Requests;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses;
using NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses;
using NexoRuta.ApiClient.Features.Organizaciones.Contracts.Requests;
using NexoRuta.ApiClient.Features.Tarifas.Contracts;

namespace NexoRuta.ApiClient.Tests;

public sealed class OpenApiContractTests
{
    [Fact]
    public async Task OperationalRulesContractContainsVersionedDraftPublicationAndValidationSchemas()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var operations = new Dictionary<string, string[]>
        {
            ["/api/configuracion/reglas-operativas"] = ["get"],
            ["/api/configuracion/reglas-operativas/borrador"] = ["get", "post", "put"],
            ["/api/configuracion/reglas-operativas/borrador/publicar"] = ["post"],
            ["/api/configuracion/reglas-operativas/publicaciones"] = ["get"],
            ["/api/configuracion/reglas-operativas/publicaciones/{versionId}"] = ["get"]
        };
        foreach (var (path, methods) in operations)
        {
            Assert.True(paths.TryGetProperty(path, out var item), $"OpenAPI path {path} is missing.");
            foreach (var method in methods)
                Assert.True(item.TryGetProperty(method, out _), $"OpenAPI operation {method} {path} is missing.");
        }

        var replace = paths.GetProperty("/api/configuracion/reglas-operativas/borrador").GetProperty("put");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.Contains(status, replace.GetProperty("responses").EnumerateObject().Select(item => item.Name));
        var request = Resolve(replace.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        var required = request.GetProperty("required").EnumerateArray().Select(item => item.GetString()).ToHashSet();
        foreach (var field in new[] { "versionConcurrencia", "maxIntentosEntrega", "intervaloEntreIntentosMinutos",
                     "politicaDevolucionAlAgotarIntentos", "plazoEstandarMinutos", "plazoUrgenteMinutos",
                     "requisitosPrueba", "motivos" })
            Assert.Contains(field, required);
        Assert.Equal("int64", request.GetProperty("properties").GetProperty("versionConcurrencia")
            .GetProperty("format").GetString());
        Assert.Equal(0, request.GetProperty("properties").GetProperty("versionConcurrencia")
            .GetProperty("minimum").GetInt32());
        Assert.Contains("null", request.GetProperty("properties").GetProperty("maxIntentosEntrega")
            .GetProperty("type").EnumerateArray().Select(item => item.GetString()));

        var proofItems = Resolve(request.GetProperty("properties").GetProperty("requisitosPrueba")
            .GetProperty("items"), root);
        Assert.Equal(new[] { "FirmaEnPantalla", "Fotografia", "NombreReceptor", "DocumentoReceptor" },
            proofItems.GetProperty("enum").EnumerateArray().Select(item => item.GetString()).ToArray());
        var motives = Resolve(request.GetProperty("properties").GetProperty("motivos").GetProperty("items"), root);
        Assert.False(motives.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(40, motives.GetProperty("properties").GetProperty("codigo").GetProperty("maxLength").GetInt32());
        Assert.Equal(120, motives.GetProperty("properties").GetProperty("descripcion").GetProperty("maxLength").GetInt32());

        var publish = paths.GetProperty("/api/configuracion/reglas-operativas/borrador/publicar").GetProperty("post");
        var publishRequest = Resolve(publish.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("versionConcurrencia", publishRequest.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString()));

        var detail = Resolve(publish.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("versionConcurrencia", detail.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("numeroVersionPublicada", detail.GetProperty("required").EnumerateArray()
            .Select(item => item.GetString()));
        Assert.Contains("null", detail.GetProperty("properties").GetProperty("numeroVersionPublicada")
            .GetProperty("type").EnumerateArray().Select(item => item.GetString()));

        var history = paths.GetProperty("/api/configuracion/reglas-operativas/publicaciones").GetProperty("get");
        Assert.Contains("400", history.GetProperty("responses").EnumerateObject().Select(item => item.Name));
        Assert.Equal(100, history.GetProperty("parameters").EnumerateArray()
            .Single(item => item.GetProperty("name").GetString() == "tamanoPagina")
            .GetProperty("schema").GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task CoverageEndpointsArePublishedInTheVersionedOpenApiContract()
    {
        using var document = await LoadOpenApiAsync();
        var paths = document.RootElement.GetProperty("paths");
        var operations = new Dictionary<string, string[]>
        {
            ["/api/configuracion/zonas"] = ["get", "post"],
            ["/api/configuracion/zonas/{zonaId}"] = ["get", "put"],
            ["/api/configuracion/zonas/{zonaId}/estado"] = ["put"],
            ["/api/configuracion/zonas/{zonaId}/franjas"] = ["get", "post"],
            ["/api/configuracion/zonas/{zonaId}/franjas/{franjaId}"] = ["put"],
            ["/api/configuracion/zonas/departamentos"] = ["get"],
            ["/api/configuracion/zonas/departamentos/territorio"] = ["get"],
            ["/api/configuracion/zonas/departamentos/{codigo}"] = ["get"],
            ["/api/configuracion/zonas/cobertura"] = ["post"]
        };

        foreach (var (path, methods) in operations)
        {
            Assert.True(paths.TryGetProperty(path, out var item), $"OpenAPI path {path} is missing.");
            foreach (var method in methods)
                Assert.True(item.TryGetProperty(method, out _), $"OpenAPI operation {method.ToUpperInvariant()} {path} is missing.");
        }
    }

    [Fact]
    public async Task CoverageOpenApiDocumentsGeoJsonSlotPrecisionVersionAndErrors()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var create = paths.GetProperty("/api/configuracion/zonas").GetProperty("post");
        var createSchema = Resolve(create.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.Equal(new[] { "nombre" }, createSchema.GetProperty("required").EnumerateArray()
            .Select(x => x.GetString()).Order());
        Assert.Contains("geometria", createSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name));
        Assert.Contains("codigoDepartamento", createSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name));
        Assert.False(createSchema.GetProperty("additionalProperties").GetBoolean());
        var geometryUnion = createSchema.GetProperty("properties").GetProperty("geometria");
        var geometryReference = geometryUnion.GetProperty("oneOf").EnumerateArray()
            .Single(x => x.TryGetProperty("$ref", out _));
        var geoJson = Resolve(geometryReference, root);
        var geoJsonType = geoJson.GetProperty("properties").GetProperty("type");
        Assert.Equal("^(Polygon|MultiPolygon)$", geoJsonType.GetProperty("pattern").GetString());
        Assert.DoesNotContain("string", geoJson.GetProperty("properties").GetProperty("coordinates").GetRawText(),
            StringComparison.OrdinalIgnoreCase);

        var detail = paths.GetProperty("/api/configuracion/zonas/{zonaId}").GetProperty("get");
        var detailSchema = Resolve(detail.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        var geometrySchema = detailSchema.GetProperty("properties").GetProperty("geometria");
        Assert.DoesNotContain("string", geometrySchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var geoJsonResponse = Resolve(geometrySchema, root);
        Assert.Equal("object", geoJsonResponse.GetProperty("type").GetString());
        Assert.Contains("coordinates", geoJsonResponse.GetProperty("properties").EnumerateObject().Select(x => x.Name));
        var updateResponses = paths.GetProperty("/api/configuracion/zonas/{zonaId}").GetProperty("put").GetProperty("responses");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.Contains(status, updateResponses.EnumerateObject().Select(x => x.Name));

        var slot = Resolve(paths.GetProperty("/api/configuracion/zonas/{zonaId}/franjas").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Equal("time", slot.GetProperty("properties").GetProperty("horaInicio").GetProperty("format").GetString());
        Assert.Equal(0, slot.GetProperty("properties").GetProperty("versionZona").GetProperty("minimum").GetInt32());
        Assert.Contains("versionZona", slot.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task TariffEndpointsArePublishedInTheVersionedOpenApiContract()
    {
        using var document = await LoadOpenApiAsync();
        var paths = document.RootElement.GetProperty("paths");
        var operations = new Dictionary<string, string[]>
        {
            ["/api/configuracion/tarifas"] = ["get"],
            ["/api/configuracion/tarifas/publicaciones"] = ["get"],
            ["/api/configuracion/tarifas/publicaciones/{versionId}"] = ["get"],
            ["/api/configuracion/tarifas/borrador"] = ["get", "post"],
            ["/api/configuracion/tarifas/borrador/tarifas"] = ["put"],
            ["/api/configuracion/tarifas/borrador/publicar"] = ["post"],
            ["/api/configuracion/tarifas/simulaciones"] = ["post"]
        };

        foreach (var (path, methods) in operations)
        {
            Assert.True(paths.TryGetProperty(path, out var item), $"OpenAPI path {path} is missing.");
            foreach (var method in methods)
                Assert.True(item.TryGetProperty(method, out _), $"OpenAPI operation {method.ToUpperInvariant()} {path} is missing.");
        }
    }

    [Fact]
    public async Task TariffOpenApiDocumentsTypedEnumsDecimalPrecisionVersionsAndErrors()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var replace = paths.GetProperty("/api/configuracion/tarifas/borrador/tarifas").GetProperty("put");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.Contains(status, replace.GetProperty("responses").EnumerateObject().Select(x => x.Name));
        var request = Resolve(replace.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.Equal(new[] { "tarifas", "versionConcurrencia" }, request.GetProperty("required").EnumerateArray()
            .Select(x => x.GetString()).Order());
        Assert.False(request.GetProperty("additionalProperties").GetBoolean());
        var version = request.GetProperty("properties").GetProperty("versionConcurrencia");
        Assert.Equal("int64", version.GetProperty("format").GetString());
        Assert.Equal(0, version.GetProperty("minimum").GetInt32());

        var rate = Resolve(request.GetProperty("properties").GetProperty("tarifas")
            .GetProperty("items"), root);
        var amount = rate.GetProperty("properties").GetProperty("importeBaseUyu");
        Assert.Equal("decimal", amount.GetProperty("format").GetString());
        var modality = Resolve(rate.GetProperty("properties").GetProperty("modalidad"), root);
        Assert.Equal(new[] { "Estandar", "Urgente" }, modality.GetProperty("enum").EnumerateArray()
            .Select(x => x.GetString()).ToArray());
        var adjustment = Resolve(rate.GetProperty("properties").GetProperty("ajustes").GetProperty("items"), root);
        var adjustmentType = Resolve(adjustment.GetProperty("properties").GetProperty("tipo"), root);
        Assert.Equal(new[] { "RecargoFijo", "RecargoPorcentual", "BonificacionFija", "BonificacionPorcentual" },
            adjustmentType.GetProperty("enum").EnumerateArray().Select(x => x.GetString()).ToArray());

        var simulate = paths.GetProperty("/api/configuracion/tarifas/simulaciones").GetProperty("post");
        foreach (var status in new[] { "200", "400", "401", "403", "404", "409" })
            Assert.Contains(status, simulate.GetProperty("responses").EnumerateObject().Select(x => x.Name));
        var simulateRequest = Resolve(simulate.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.Equal(new[] { "bultos", "modalidad", "versionTarifarioId", "zonaId" },
            simulateRequest.GetProperty("required").EnumerateArray().Select(x => x.GetString()).Order());
        Assert.False(simulateRequest.GetProperty("properties").TryGetProperty("precios", out _));
        Assert.False(simulateRequest.GetProperty("additionalProperties").GetBoolean());
        var result = Resolve(simulate.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("versionConcurrencia", result.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("numeroVersionPublicada", result.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        foreach (var field in new[] { "pesoTotalKg", "volumenTotalM3", "totalPrevioPisoUyu", "totalFinalUyu" })
            Assert.Equal("decimal", result.GetProperty("properties").GetProperty(field).GetProperty("format").GetString());
        Assert.Contains("null", result.GetProperty("properties").GetProperty("numeroVersionPublicada")
            .GetProperty("type").EnumerateArray().Select(x => x.GetString()));
    }

    [Fact]
    public async Task PasswordRecoveryIsPublicWithoutAdministrativePasswordRoutes()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var request = paths.GetProperty("/api/auth/password-recovery/request").GetProperty("post");
        var requestSchema = Resolve(request.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        AssertSchema(typeof(SolicitarRecuperacionPasswordRequest), requestSchema, root);
        var responseSchema = Resolve(request.GetProperty("responses").GetProperty("200").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        AssertSchema(typeof(SolicitarRecuperacionPasswordResponse), responseSchema, root);
        Assert.Equal(new[] { "message" }, responseSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain("token", responseSchema.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.True(request.GetProperty("responses").TryGetProperty("429", out _));

        var reset = paths.GetProperty("/api/auth/password-recovery/reset").GetProperty("post");
        var resetSchema = Resolve(reset.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), root);
        AssertSchema(typeof(RestablecerPasswordRequest), resetSchema, root);
        Assert.Equal(new[] { "nuevaPassword", "token", "usuarioId" },
            resetSchema.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.True(reset.GetProperty("responses").TryGetProperty("204", out _));
        Assert.True(reset.GetProperty("responses").TryGetProperty("400", out _));
        Assert.True(reset.GetProperty("responses").TryGetProperty("429", out _));

        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
            && !path.Name.StartsWith("/api/auth/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EmailEditUsesOnlyEmailAndVersionAndPublishesControlledResponses()
    {
        using var document = await LoadOpenApiAsync();
        var operation = document.RootElement.GetProperty("paths")
            .GetProperty("/api/usuarios/{usuarioId}/email").GetProperty("patch");
        var request = Resolve(operation.GetProperty("requestBody").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema"), document.RootElement);
        Assert.Equal(new[] { "email", "version" }, request.GetProperty("properties")
            .EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(254, request.GetProperty("properties").GetProperty("email").GetProperty("maxLength").GetInt32());
        foreach (var status in new[] { "204", "400", "401", "403", "404", "409" })
            Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
    }

    [Fact]
    public async Task SelfServiceContractsExposeIdentityOnlyRegistrationAndNameOnlyOnboarding()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var registration = paths.GetProperty("/api/auth/register").GetProperty("post");
        var schema = Resolve(registration.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Equal(new[] { "email", "password" }, schema.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.Equal(12, schema.GetProperty("properties").GetProperty("password").GetProperty("minLength").GetInt32());
        foreach (var path in new[] { "/api/alta-propia/comercios", "/api/alta-propia/operadores" })
        {
            var operation = paths.GetProperty(path).GetProperty("post");
            var request = Resolve(operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
            Assert.Equal(new[] { "nombre" }, request.GetProperty("properties").EnumerateObject().Select(x => x.Name));
            foreach (var status in new[] { "201", "400", "401", "403", "409" })
                Assert.True(operation.GetProperty("responses").TryGetProperty(status, out _));
        }
        var context = Resolve(paths.GetProperty("/api/usuarios/actual").GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("null", context.GetProperty("properties").GetProperty("accesoId").GetProperty("type").EnumerateArray().Select(x => x.GetString()));
    }

    [Theory]
    [InlineData("operadores")]
    [InlineData("comercios")]
    public async Task PlatformOrganizationEndpointsExposeAdministrativeContractsWithoutCredentials(string kind)
    {
        using var document = await LoadOpenApiAsync();
        var paths = document.RootElement.GetProperty("paths");
        var collection = paths.GetProperty($"/api/admin/{kind}");
        foreach (var operation in new[] { collection.GetProperty("get"), collection.GetProperty("post"),
                     paths.GetProperty($"/api/admin/{kind}/{{id}}").GetProperty("get") })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty("401", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("403", out _));
        }
        var create = collection.GetProperty("post");
        Assert.True(create.GetProperty("responses").TryGetProperty("409", out _));
        var response = create.GetProperty("responses").GetProperty("201").GetProperty("content")
            .GetProperty("application/json").GetProperty("schema");
        var properties = Resolve(response, document.RootElement).GetProperty("properties");
        Assert.Equal(new[] { "accesoId", "organizacion", "rolId", "usuarioId" }, properties.EnumerateObject().Select(x => x.Name).Order());
        var listing = Resolve(collection.GetProperty("get").GetProperty("responses").GetProperty("200")
            .GetProperty("content").GetProperty("application/json").GetProperty("schema"), document.RootElement);
        Assert.Equal(new[] { "items", "pagina", "tamanoPagina", "total" }, listing.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
    }

    [Fact]
    public async Task CatalogoOperadores_PublicaLaRutaQueConsumeCommerce()
    {
        using var document = await LoadOpenApiAsync();

        var paths = document.RootElement.GetProperty("paths");

        Assert.True(
            paths.TryGetProperty("/api/comercio/operadores", out var endpoint));

        Assert.True(
            endpoint.TryGetProperty("get", out _));
    }

    [Fact]
    public async Task CatalogoGlobalDeAccesosFueRetirado()
    {
        using var document = await LoadOpenApiAsync();
        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty("/api/accesos", out _));
    }


    [Fact]
    public async Task CrearEnvioRequest_ConservaPropiedadesYValidacionesDelContratoPublicado()
    {
        using var document = await LoadOpenApiAsync();
        var schema = document.RootElement.GetProperty("paths").GetProperty("/api/envios").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        AssertSchema(typeof(CrearEnvioRequest), schema, document.RootElement);
        schema = Resolve(schema, document.RootElement);
        var properties = schema.GetProperty("properties");
        Assert.Equal(typeof(CrearEnvioRequest).GetProperties().Length, properties.EnumerateObject().Count());

        foreach (var property in typeof(CrearEnvioRequest).GetProperties())
        {
            var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
            var published = properties.GetProperty(name);
            if (property.GetCustomAttribute<StringLengthAttribute>() is { } length)
                Assert.Equal(length.MaximumLength, published.GetProperty("maxLength").GetInt32());
            if (property.GetCustomAttribute<RequiredAttribute>() is not null)
                Assert.Contains(name, schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
            if (property.GetCustomAttribute<RangeAttribute>() is { } range)
            {
                Assert.Equal(Convert.ToDecimal(range.Minimum, CultureInfo.InvariantCulture), published.GetProperty("minimum").GetDecimal());
                Assert.Equal(Convert.ToDecimal(range.Maximum, CultureInfo.InvariantCulture), published.GetProperty("maximum").GetDecimal());
            }
        }
    }

    [Theory]
    [InlineData("/api/admin/operadores", "get", "200", typeof(PaginaOrganizacionesResponse))]
    [InlineData("/api/admin/comercios", "get", "200", typeof(PaginaOrganizacionesResponse))]
    [InlineData("/api/admin/operadores", "post", "201", typeof(AltaOrganizacionResponse))]
    [InlineData("/api/admin/comercios", "post", "201", typeof(AltaOrganizacionResponse))]
    [InlineData("/api/admin/operadores/{id}", "get", "200", typeof(OrganizacionResponse))]
    [InlineData("/api/admin/comercios/{id}", "get", "200", typeof(OrganizacionResponse))]
    [InlineData("/api/usuarios/actual", "get", "200", typeof(UsuarioActualResponse))]
    [InlineData("/api/usuarios/actual/permisos", "get", "200", typeof(IReadOnlyList<string>))]
    [InlineData("/api/organizacion/actual", "get", "200", typeof(OrganizacionPropiaResponse))]
    [InlineData("/api/organizacion/actual", "put", "200", typeof(OrganizacionPropiaResponse))]
    [InlineData("/api/usuarios", "get", "200", typeof(IReadOnlyList<UsuarioOrganizacionResponse>))]
    [InlineData("/api/usuarios", "post", "201", typeof(UsuarioOrganizacionResponse))]
    [InlineData("/api/auth/login", "post", "200", typeof(LoginResponse))]
    [InlineData("/api/roles", "get", "200", typeof(IReadOnlyList<RolResponse>))]
    [InlineData("/api/roles/asignables", "get", "200", typeof(IReadOnlyList<RolResponse>))]
    [InlineData("/api/roles/permisos", "get", "200", typeof(IReadOnlyList<PermisoResponse>))]
    [InlineData("/api/roles", "post", "201", typeof(CrearRolResponse))]
    [InlineData("/api/comercio/operadores", "get", "200", typeof(IReadOnlyList<OperadorDisponibleResponse>))]
    [InlineData("/api/envios", "post", "201", typeof(EnvioCreadoResponse))]
    [InlineData("/api/envios", "get", "200", typeof(IReadOnlyList<EnvioResponse>))]
    public async Task Respuestas_CoincidenConLasPropiedadesYTiposDelCliente(string path, string method, string status, Type type)
    {
        using var document = await LoadOpenApiAsync();
        var schema = document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status).GetProperty("content").GetProperty("application/json").GetProperty("schema");
        AssertSchema(type, schema, document.RootElement);
    }

    [Theory]
    [InlineData("/api/usuarios", "post")]
    [InlineData("/api/usuarios/{usuarioId}/estado", "patch")]
    [InlineData("/api/roles/{id}", "put")]
    [InlineData("/api/roles/{id}", "delete")]
    [InlineData("/api/roles/{id}/permisos", "put")]
    [InlineData("/api/roles/accesos/{accesoId}", "put")]
    [InlineData("/api/organizacion/actual", "put")]
    public async Task ProtectedAdministrativeMutationsDocumentForbiddenResponses(string path, string method)
    {
        using var document = await LoadOpenApiAsync();
        Assert.True(document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").TryGetProperty("403", out _));
    }

    [Theory]
    [InlineData("operadores")]
    [InlineData("comercios")]
    public async Task PlatformCreationRequestMatchesTheApiAndRequiresTwelveCharacterPassword(string kind)
    {
        using var document = await LoadOpenApiAsync();
        var schema = document.RootElement.GetProperty("paths").GetProperty($"/api/admin/{kind}").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        AssertSchema(typeof(CrearOrganizacionRequest), schema, document.RootElement);
        var fields = Resolve(schema, document.RootElement).GetProperty("properties");
        Assert.Equal(160, fields.GetProperty("nombre").GetProperty("maxLength").GetInt32());
        var admin = Resolve(fields.GetProperty("administrador"), document.RootElement).GetProperty("properties");
        Assert.Equal(254, admin.GetProperty("email").GetProperty("maxLength").GetInt32());
        Assert.Equal(12, admin.GetProperty("password").GetProperty("minLength").GetInt32());
    }

    [Fact]
    public async Task OrganizationUpdateContractOnlyAcceptsNameAndConcurrencyVersion()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var update = root.GetProperty("paths").GetProperty("/api/organizacion/actual").GetProperty("put");
        var schema = Resolve(update.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Equal(new[] { "nombre", "version" }, schema.GetProperty("properties").EnumerateObject().Select(x => x.Name).Order());
        Assert.Contains("nombre", schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("version", schema.GetProperty("required").EnumerateArray().Select(x => x.GetString()));
        Assert.Equal(160, schema.GetProperty("properties").GetProperty("nombre").GetProperty("maxLength").GetInt32());
        Assert.Equal(0, schema.GetProperty("properties").GetProperty("version").GetProperty("minimum").GetInt64());
        Assert.Contains("409", update.GetProperty("responses").EnumerateObject().Select(x => x.Name));
    }

    [Fact]
    public async Task CancellationAndSuspensionEndpointsMatchSharedContractsAndPublishControlledFailures()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");

        var ownRequests = paths.GetProperty("/api/organizacion/actual/solicitudes-baja");
        AssertSchema(typeof(IReadOnlyList<NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses.SolicitudBajaResponse>),
            ownRequests.GetProperty("get").GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        var submit = ownRequests.GetProperty("post");
        AssertSchema(typeof(NexoRuta.ApiClient.Features.Organizaciones.Contracts.Requests.SolicitarBajaRequest),
            submit.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        AssertSchema(typeof(NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses.SolicitudBajaResponse),
            submit.GetProperty("responses").GetProperty("201").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("409", submit.GetProperty("responses").EnumerateObject().Select(x => x.Name));

        var list = paths.GetProperty("/api/admin/solicitudes-baja").GetProperty("get");
        AssertSchema(typeof(NexoRuta.ApiClient.Features.Plataforma.Contracts.Responses.PaginaSolicitudesBajaResponse),
            list.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        var resolve = paths.GetProperty("/api/admin/solicitudes-baja/{id}/resolver").GetProperty("post");
        AssertSchema(typeof(NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests.ResolverSolicitudBajaRequest),
            resolve.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("409", resolve.GetProperty("responses").EnumerateObject().Select(x => x.Name));

        var state = paths.GetProperty("/api/admin/{tipo}/{id}/estado").GetProperty("put");
        AssertSchema(typeof(NexoRuta.ApiClient.Features.Plataforma.Contracts.Requests.CambiarEstadoOrganizacionRequest),
            state.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("409", state.GetProperty("responses").EnumerateObject().Select(x => x.Name));
        AssertSchema(typeof(NexoRuta.ApiClient.Features.Organizaciones.Contracts.Responses.OrganizacionSuspendidaResponse),
            paths.GetProperty("/api/organizacion/suspendida").GetProperty("get").GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
    }

    [Fact]
    public async Task ReactivationContractsSeparateRestrictedOwnerRequestsFromPlatformResolution()
    {
        using var document = await LoadOpenApiAsync();
        var root = document.RootElement;
        var paths = root.GetProperty("paths");
        var own = paths.GetProperty("/api/organizacion/suspendida/solicitudes-reactivacion");
        AssertSchema(typeof(IReadOnlyList<SolicitudReactivacionResponse>),
            own.GetProperty("get").GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        AssertSchema(typeof(SolicitudReactivacionResponse),
            own.GetProperty("post").GetProperty("responses").GetProperty("201")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("409", own.GetProperty("post").GetProperty("responses").EnumerateObject().Select(x => x.Name));
        AssertSchema(typeof(PaginaSolicitudesReactivacionResponse),
            paths.GetProperty("/api/admin/solicitudes-reactivacion").GetProperty("get").GetProperty("responses").GetProperty("200")
                .GetProperty("content").GetProperty("application/json").GetProperty("schema"), root);
        Assert.Contains("409", paths.GetProperty("/api/admin/solicitudes-reactivacion/{id}/resolver")
            .GetProperty("post").GetProperty("responses").EnumerateObject().Select(x => x.Name));
    }

    private static void AssertSchema(Type type, JsonElement schema, JsonElement document)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        schema = Resolve(schema, document);
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(decimal) || type == typeof(bool) || type == typeof(int) || type == typeof(long) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            var expected = type == typeof(decimal) ? "number" : type == typeof(bool) ? "boolean" : type == typeof(int) || type == typeof(long) ? "integer" : "string";
            var declared = schema.GetProperty("type");
            IEnumerable<string?> types = declared.ValueKind == JsonValueKind.Array
                ? declared.EnumerateArray().Select(x => x.GetString())
                : [declared.GetString()];
            Assert.Contains(expected, types);
            if (type == typeof(Guid))
                Assert.Equal("uuid", schema.GetProperty("format").GetString());
            if (type == typeof(long))
                Assert.Equal("int64", schema.GetProperty("format").GetString());
            if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
                Assert.Equal("date-time", schema.GetProperty("format").GetString());
        }
        else if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            Assert.Equal("array", schema.GetProperty("type").GetString());
            AssertSchema(type.GenericTypeArguments[0], schema.GetProperty("items"), document);
        }
        else
        {
            var properties = schema.GetProperty("properties");
            Assert.Equal(type.GetProperties().Length, properties.EnumerateObject().Count());
            foreach (var property in type.GetProperties())
            {
                var name = JsonNamingPolicy.CamelCase.ConvertName(property.Name);
                Assert.True(properties.TryGetProperty(name, out var published), $"Falta {type.Name}.{name} en OpenAPI.");
                AssertSchema(property.PropertyType, published, document);
            }
        }
    }

    private static JsonElement Resolve(JsonElement schema, JsonElement document)
        => schema.TryGetProperty("$ref", out var reference)
            ? document.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1])
            : schema;

    private static async Task<JsonDocument> LoadOpenApiAsync()
    {
        var url = Environment.GetEnvironmentVariable("NEXORUTA_OPENAPI_URL");
        if (string.IsNullOrWhiteSpace(url))
            return JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Contracts", "nexoruta.openapi.json")));

        using var http = new HttpClient();
        return JsonDocument.Parse(await http.GetStringAsync(url));
    }
}
