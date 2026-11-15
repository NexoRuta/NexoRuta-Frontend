using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    private static readonly Guid InternalMessageId = Guid.Parse("00000000-0000-0000-0000-000000008101");
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InternalMessagesPreserveRequestOnUncertainResultAndRequireBothPermissions(bool malformed)
    {
        var api = new FakeApi { IncludeInternalMessages = true, FailInternalRetry = !malformed, MalformedInternalRetry = malformed, FailInternalListAfterPost = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var token = await GetAntiforgeryTokenAsync(client, "/mensajes-internos");
        var request = Guid.NewGuid();
        var fields = new (string,string)[] { ("MensajeId",InternalMessageId.ToString()),("SolicitudId",request.ToString()),("Intentos","3"),("Motivo","Servicio recuperado"),("Pagina","1") };
        using var failed = await client.PostAsync("/mensajes-internos", Form(token, fields));
        Assert.Equal(HttpStatusCode.OK, failed.StatusCode);
        var html = WebUtility.HtmlDecode(await failed.Content.ReadAsStringAsync());
        Assert.Contains("No se pudo confirmar el resultado", html);
        Assert.Contains(request.ToString(), html); Assert.Contains("Servicio recuperado", html);
        Assert.DoesNotContain("jwt-user", html);
        api.FailInternalRetry = false; api.MalformedInternalRetry = false; api.FailInternalListAfterPost = false;
        using var retried = await client.PostAsync("/mensajes-internos", Form(token, fields));
        Assert.Equal(HttpStatusCode.Redirect, retried.StatusCode);
        Assert.Equal(2, api.InternalRetryPayloads.Count);
        Assert.All(api.InternalRetryPayloads, payload => Assert.Equal(request, payload.GetProperty("solicitudId").GetGuid()));
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar"];
        using var denied = await client.PostAsync("/mensajes-internos", Form(token, fields));
        Assert.Equal(HttpStatusCode.Redirect, denied.StatusCode);
        Assert.Contains("acceso-denegado", denied.Headers.Location!.ToString());
        Assert.Equal(2, api.InternalRetryPayloads.Count);
    }

    [InternalMessageBrowserFact]
    public async Task InternalMessagesBrowserSupportsNarrowScreenKeyboardAndIndividualRetry()
    {
        var api = new FakeApi { IncludeInternalMessages = true };
        api.PermissionOverrides["jwt-user"] = ["configuracion.gestionar", "envios.consultar"];
        using var factory = new BackofficeFactory(api); factory.UseKestrel(0);
        using var client = factory.CreateClient();
        var root = FindFrontendRoot();
        var start = new ProcessStartInfo("node") { RedirectStandardOutput=true, RedirectStandardError=true, UseShellExecute=false };
        start.ArgumentList.Add(Path.Combine(root,"tests/browser/internal-messages.cjs"));
        start.Environment["INTERNAL_MESSAGES_URL"] = client.BaseAddress!.ToString();
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        Assert.True(process.ExitCode==0, await output+await error);
        var payload = Assert.Single(api.InternalRetryPayloads);
        Assert.Equal(InternalMessageId, payload.GetProperty("id").GetGuid());
    }
    private static string FindFrontendRoot()
    {
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);dir is not null;dir=dir.Parent)
            if(File.Exists(Path.Combine(dir.FullName,"tests/browser/internal-messages.cjs"))) return dir.FullName;
        throw new InvalidOperationException("Frontend source not found");
    }
    private sealed class InternalMessageBrowserFact : FactAttribute
    {
        public InternalMessageBrowserFact()
        { if(string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NEXORUTA_INTERNAL_MESSAGES_UI_DIR"))) Skip="Opt-in checkout browser test"; }
    }
    private sealed partial class FakeApi
    {
        public bool IncludeInternalMessages {get;set;}
        public bool FailInternalRetry {get;set;}
        public bool MalformedInternalRetry {get;set;}
        public bool FailInternalListAfterPost {get;set;}
        public ConcurrentQueue<JsonElement> InternalRetryPayloads {get;}=new();
        private async Task<HttpResponseMessage?> InternalMessages(HttpRequestMessage request,string path,CancellationToken ct)
        {
            if(!IncludeInternalMessages || !path.StartsWith("/api/admin/mensajes-internos")) return null;
            if(request.Method==HttpMethod.Get && FailInternalListAfterPost && !InternalRetryPayloads.IsEmpty) return new(HttpStatusCode.ServiceUnavailable);
            if(request.Method==HttpMethod.Get)
                return Json(HttpStatusCode.OK,InternalRetryPayloads.IsEmpty || FailInternalRetry ? new[]{new {Id=InternalMessageId,Tipo="GenerarSeguimiento",Destino="RabbitMq",Estado="Error",Intentos=3,Diagnostico="Publicación fallida. El Worker conserva el reintento automático.",ProximoIntentoEnUtc=DateTimeOffset.UtcNow}} : []);
            var payload=await request.Content!.ReadFromJsonAsync<JsonElement>(ct);
            using var enriched=JsonDocument.Parse(payload.GetRawText().TrimEnd('}')+",\"id\":\""+InternalMessageId+"\"}");
            InternalRetryPayloads.Enqueue(enriched.RootElement.Clone());
            if(MalformedInternalRetry) return new(HttpStatusCode.OK) { Content = new StringContent("invalid-json") };
            if(FailInternalRetry) throw new HttpRequestException("Synthetic uncertain delivery");
            return Json(HttpStatusCode.OK,new {SolicitudId=payload.GetProperty("solicitudId").GetGuid(),Resultado="Solicitado"});
        }
    }
}
