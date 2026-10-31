using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.Web.HtmlRendering;
using Microsoft.AspNetCore.Components.HtmlRendering.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;
using NexoRuta.Commerce.Components.Features.Envios.Shared;

namespace NexoRuta.ApiClient.Tests;

public sealed class ImportacionEnviosComponentTests
{
    [Fact]
    public async Task AtomicRejectionShowsNoGlobalSuccessAndCorrelatesRowsForCorrection()
    {
        var result = new ImportacionEnviosApiResult(HttpStatusCode.Conflict, new ImportacionEnviosResponse(
            "RechazadaAtomica", new(0, 1, 1, 1, 0, 1),
            [
                new(1, "PO-1", "NoProcesado", null, null, [new("AtomicBatchRejected", null, "No se creó porque otra fila rechazó el archivo completo.")]),
                new(2, "PO-1", "DuplicadoDe", null, 1, [new("AtomicBatchRejected", null, "No se creó porque otra fila rechazó el archivo completo.")]),
                new(3, "PO-2", "Conflicto", null, null, [new("ExternalConflict", null, "La referencia ya existe con datos u operador distintos.")]),
                new(4, "PO-3", "YaExistente", Guid.NewGuid(), null, [])
            ]));
        var html = await RenderAsync(result);

        Assert.Contains("No se creó ningún envío nuevo", html);
        Assert.Contains("Conflicto", html);
        Assert.Contains("No procesado", html);
        Assert.Contains("Duplicado en el archivo", html);
        Assert.Contains("Duplicado de la fila 1", html);
        Assert.DoesNotContain("se realizó una sola operación", html);
        Assert.Contains("Ya existente", html);
        Assert.DoesNotContain("Importación completada", html);
        Assert.Contains("Creados", html);
    }

    [Fact]
    public async Task CompletedImportExplainsThatExistingShipmentsWereNotModified()
    {
        var result = new ImportacionEnviosApiResult(HttpStatusCode.OK, new ImportacionEnviosResponse(
            "Completada", new(1, 1, 0, 0, 0, 0),
            [
                new(1, "PO-1", "Creado", Guid.NewGuid(), null, []),
                new(2, "PO-2", "YaExistente", Guid.NewGuid(), null, [])
            ]));
        var html = await RenderAsync(result);

        Assert.Contains("Importación completada", html);
        Assert.Contains("Los envíos existentes no se modificaron", html);
        Assert.Contains("Creado", html);
        Assert.Contains("Ya existente", html);
    }

    [Fact]
    public void TemplateHeaderExactlyMatchesBackendParserContract()
    {
        var template = File.ReadAllLines(Path.Combine(FindRepositoryRoot(), "frontend", "src", "NexoRuta.Commerce", "wwwroot", "plantillas", "alta-envios.csv"));
        Assert.Equal("referenciaExterna,nombreDestinatario,emailDestinatario,direccion,codigoBulto,pesoGramos,largoCentimetros,anchoCentimetros,altoCentimetros", template[0]);
        Assert.Contains("\"Av. 18 de Julio 1234, apto 2\"", template[1]);
    }

    private static async Task<string> RenderAsync(ImportacionEnviosApiResult result)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        using var provider = services.BuildServiceProvider();
        await using var renderer = new TestHtmlRenderer(provider, provider.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var root = await renderer.RenderAsync<ResultadoImportacionEnvios>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { ["Resultado"] = result }));
            return WebUtility.HtmlDecode(root.ToHtmlString());
        });
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "frontend", "src", "NexoRuta.Commerce", "NexoRuta.Commerce.csproj")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not locate the NexoRuta repository root from the test output directory.");
    }

    private sealed class TestHtmlRenderer(IServiceProvider services, ILoggerFactory loggerFactory)
        : StaticHtmlRenderer(services, loggerFactory)
    {
        protected override IComponent ResolveComponentForRenderMode(Type type, int? parentId,
            IComponentActivator activator, IComponentRenderMode mode) => activator.CreateInstance(type);

        public async Task<HtmlRootComponent> RenderAsync<T>(ParameterView? parameters = null) where T : IComponent
        {
            var root = BeginRenderingComponent(typeof(T), parameters ?? ParameterView.Empty);
            await root.QuiescenceTask;
            return root;
        }
    }
}
