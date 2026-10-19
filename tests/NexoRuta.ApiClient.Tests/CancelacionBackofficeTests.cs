using System.Net;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task AdministradorCancelaConVersionMotivoYAntiforgery()
    {
        var api = new FakeApi { IncludeReparto = true, IsCancellationAdmin = true };
        api.PermissionOverrides["jwt-user"] = ["envios.consultar", "envios.gestionar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var lista = await client.GetAsync("/"); var html = await lista.Content.ReadAsStringAsync();
        Assert.Contains("Confirmar cancelación", WebUtility.HtmlDecode(html));
        Assert.Contains("name=\"Version\" value=\"8\"", html);
        var csrf = await GetAntiforgeryTokenAsync(client, "/");
        using var sinCsrf = await client.PostAsync("/?handler=Cancelar", new FormUrlEncodedContent(new Dictionary<string, string>
        { ["EnvioId"] = EnvioPrueba.ToString(), ["Version"] = "8", ["Motivo"] = "Solicitud del comercio." }));
        Assert.Equal(HttpStatusCode.BadRequest, sinCsrf.StatusCode); Assert.Empty(api.RepartoPayloads);
        using var confirmar = await client.PostAsync("/?handler=Cancelar", Form(csrf,
            ("EnvioId", EnvioPrueba.ToString()), ("Version", "8"), ("Motivo", "Solicitud del comercio.")));
        Assert.Equal(HttpStatusCode.Redirect, confirmar.StatusCode);
        var command = Assert.Single(api.RepartoPayloads);
        Assert.Equal($"/api/envios/{EnvioPrueba}/cancelacion", command.Path);
        Assert.Equal("jwt-user", command.Token);
        Assert.Equal(8, command.Body.GetProperty("version").GetInt64());
        Assert.Equal("Solicitud del comercio.", command.Body.GetProperty("motivo").GetString());
        using var final = await client.GetAsync("/");
        Assert.Contains("Cancelado", await final.Content.ReadAsStringAsync());
        Assert.DoesNotContain("Confirmar cancelaci", await final.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task DespachadorNoVeCancelacionYNoPuedeInvocarSuHandler()
    {
        var api = new FakeApi { IncludeReparto = true };
        api.PermissionOverrides["jwt-user"] = ["envios.consultar", "envios.gestionar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        using var lista = await client.GetAsync("/"); Assert.DoesNotContain("Cancelar envío", WebUtility.HtmlDecode(await lista.Content.ReadAsStringAsync()));
        var csrf = await GetAntiforgeryTokenAsync(client, "/");
        using var post = await client.PostAsync("/?handler=Cancelar", Form(csrf,
            ("EnvioId", EnvioPrueba.ToString()), ("Version", "8"), ("Motivo", "No autorizado.")));
        Assert.True(post.StatusCode == HttpStatusCode.Forbidden || post.StatusCode == HttpStatusCode.Redirect);
        Assert.Empty(api.RepartoPayloads);
    }
}
