using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.ApiClient.Features.Operadores.Contracts.Responses;
using NexoRuta.ApiClient.Features.Auth.Contracts.Responses;
using NexoRuta.ApiClient.Features.Roles.Contracts.Responses;
using NexoRuta.ApiClient.Features.Usuarios.Contracts.Responses;

namespace NexoRuta.ApiClient.Tests;

public sealed class OpenApiContractTests
{

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
    [InlineData("/api/usuarios/actual", "get", "200", typeof(UsuarioActualResponse))]
    [InlineData("/api/usuarios/actual/permisos", "get", "200", typeof(IReadOnlyList<string>))]
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
    public async Task ProtectedAdministrativeMutationsDocumentForbiddenResponses(string path, string method)
    {
        using var document = await LoadOpenApiAsync();
        Assert.True(document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").TryGetProperty("403", out _));
    }

    private static void AssertSchema(Type type, JsonElement schema, JsonElement document)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        schema = Resolve(schema, document);
        if (type == typeof(string) || type == typeof(Guid) || type == typeof(decimal) || type == typeof(bool) ||
            type == typeof(DateTime) || type == typeof(DateTimeOffset))
        {
            var expected = type == typeof(decimal) ? "number" : type == typeof(bool) ? "boolean" : "string";
            var declared = schema.GetProperty("type");
            IEnumerable<string?> types = declared.ValueKind == JsonValueKind.Array
                ? declared.EnumerateArray().Select(x => x.GetString())
                : [declared.GetString()];
            Assert.Contains(expected, types);
            if (type == typeof(Guid))
                Assert.Equal("uuid", schema.GetProperty("format").GetString());
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
