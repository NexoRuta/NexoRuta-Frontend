using System.Net;
using System.Text.RegularExpressions;
using NexoRuta.ApiClient.Features.Envios.Contracts.Responses;

namespace NexoRuta.ApiClient.Tests;

public sealed partial class BackofficeSessionAndPagesTests
{
    [Fact]
    public async Task OperatorCommerceFilterAndQuickCountsUseAuthorizedSearchScopeBeforeStateAndPagination()
    {
        var commerce=Guid.NewGuid();
        var shipments=Enumerable.Range(1,25).Select(i=>CreateShipment(i) with {
            ComercioId=i<=12?commerce:Guid.Parse("00000000-0000-0000-0000-000000000999"),
            ComercioNombre=i<=12?"Sender A":"Sender B", Estado=i%2==0?"Entregado":"EnDeposito"
        }).ToArray();
        var api=new FakeApi {EnviosResponse=shipments};
        using var factory=new BackofficeFactory(api); using var client=Client(factory);
        using var login=await LoginAsync(client,"admin@example.test");
        using var page=await client.GetAsync($"/?ComercioId={commerce:D}&Pagina=2");
        var html=WebUtility.HtmlDecode(await page.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK,page.StatusCode);
        Assert.Equal(2,Regex.Matches(html,"class=\"nr-shipment-row\"").Count);
        Assert.Contains("Mostrando 11–12 de 12 envíos",html);
        Assert.Contains("data-state-count=\"\">12</span>",html);
        Assert.Contains("data-state-count=\"EnDeposito\">6</span>",html);
        Assert.Contains("data-state-count=\"Entregado\">6</span>",html);
        Assert.Contains($"ComercioId={commerce:D}",html);
        using var filtered=await client.GetAsync($"/?ComercioId={commerce:D}&Estado=Entregado");
        var filteredHtml=WebUtility.HtmlDecode(await filtered.Content.ReadAsStringAsync());
        Assert.Equal(6,Regex.Matches(filteredHtml,"class=\"nr-shipment-row\"").Count);
        Assert.Contains("data-state-count=\"\">12</span>",filteredHtml);
        Assert.Contains("data-state-count=\"EnDeposito\">6</span>",filteredHtml);
        Assert.DoesNotContain(api.Calls,call=>call.Path.Contains("organizaciones")||call.Path.Contains("comercios"));
    }

    [Fact]
    public async Task OperatorParcelCodeSearchAndSelectedDetailsStayScopedWithoutChangingTheRequestedPage()
    {
        var shipments=Enumerable.Range(1,12).Select(CreateShipment).ToArray();
        shipments[10]=shipments[10] with {Bultos=[new BultoResponse("HUMAN-PARCEL",1500,30,20,10)]};
        var api=new FakeApi {EnviosResponse=shipments};
        using var factory=new BackofficeFactory(api);using var client=Client(factory);
        using var login=await LoginAsync(client,"admin@example.test");
        using var search=await client.GetAsync("/?Buscar=HUMAN-PARCEL");
        var html=WebUtility.HtmlDecode(await search.Content.ReadAsStringAsync());
        Assert.Equal(1,Regex.Matches(html,"class=\"nr-shipment-row\"").Count);
        using var selected=await client.GetAsync($"/?Pagina=1&Seleccion={shipments[11].Id:D}");
        var selectedHtml=WebUtility.HtmlDecode(await selected.Content.ReadAsStringAsync());
        Assert.Contains("Mostrando 1–10 de 12 envíos",selectedHtml);
        Assert.Contains($"id=\"shipment-detail-template-{shipments[11].Id}\"",selectedHtml);
        Assert.Contains("data-open-on-load=\"true\"",selectedHtml);
        using var foreign=await client.GetAsync($"/?Seleccion={Guid.NewGuid():D}");
        var foreignHtml=WebUtility.HtmlDecode(await foreign.Content.ReadAsStringAsync());
        Assert.DoesNotContain("data-open-on-load=\"true\"",foreignHtml);
        Assert.Contains("El envío seleccionado no está disponible",foreignHtml);
    }

    [Fact]
    public async Task OperatorInvalidOrUnavailableCommerceFilterNeverSilentlyBroadensTheList()
    {
        var api=new FakeApi {EnviosResponse=[CreateShipment(1)]};
        using var factory=new BackofficeFactory(api);using var client=Client(factory);
        using var login=await LoginAsync(client,"admin@example.test");
        using var invalid=await client.GetAsync("/?ComercioId=not-a-guid");
        var invalidHtml=WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync());
        Assert.Contains("El filtro de comercio no es válido",invalidHtml);
        Assert.DoesNotContain("class=\"nr-shipment-row\"",invalidHtml);
        Assert.DoesNotContain(api.Calls,call=>call.Path=="/api/envios");
        using var unavailable=await client.GetAsync($"/?ComercioId={Guid.NewGuid():D}");
        var unavailableHtml=WebUtility.HtmlDecode(await unavailable.Content.ReadAsStringAsync());
        Assert.Contains("No encontramos envíos con esos filtros",unavailableHtml);
        Assert.DoesNotContain("class=\"nr-shipment-row\"",unavailableHtml);
    }

    [Fact]
    public async Task OperatorCancellationKeepsCommerceSearchStatePageAndSelectionContext()
    {
        var commerce=Guid.NewGuid();
        var api=new FakeApi {IncludeReparto=true,IsCancellationAdmin=true};
        api.PermissionOverrides["jwt-user"]=["envios.consultar","envios.gestionar"];
        using var factory=new BackofficeFactory(api);using var client=Client(factory);
        using var login=await LoginAsync(client,"user@example.test");
        var csrf=await GetAntiforgeryTokenAsync(client,"/");
        using var response=await client.PostAsync("/?handler=Cancelar",Form(csrf,
            ("EnvioId",EnvioPrueba.ToString()),("Version","8"),("Motivo","Synthetic cancellation"),
            ("Buscar","Recipient"),("Estado","EnTransito"),("ComercioId",commerce.ToString("D")),("Pagina","3")));
        Assert.Equal(HttpStatusCode.Redirect,response.StatusCode);
        var location=response.Headers.Location!.OriginalString;
        Assert.Contains($"ComercioId={commerce:D}",location);
        Assert.Contains("Buscar=Recipient",location);Assert.Contains("Estado=EnTransito",location);Assert.Contains("Pagina=3",location);
        Assert.Contains($"Seleccion={EnvioPrueba:D}",location);
        var command=Assert.Single(api.RepartoPayloads);
        Assert.Equal(8,command.Body.GetProperty("version").GetInt64());
        Assert.Equal("Synthetic cancellation",command.Body.GetProperty("motivo").GetString());
    }

    [Fact]
    public async Task OperatorDetailNativeSummariesRemainKeyboardReachable()
    {
        var api = new FakeApi { EnviosResponse = [CreateShipment(1)] };
        api.PermissionOverrides["jwt-user"] = ["envios.consultar", "envios.gestionar"];
        using var factory = new BackofficeFactory(api); using var client = Client(factory);
        await LoginAsync(client, "user@example.test");
        var html = await client.GetStringAsync("/");
        var disclosures = System.Text.RegularExpressions.Regex.Matches(html, "<details[^>]*class=\"(?:nr-detail-technical|nr-cancel-shipment)\"[^>]*>");
        Assert.NotEmpty(disclosures);
        Assert.All(disclosures.Cast<System.Text.RegularExpressions.Match>(), d => Assert.DoesNotContain("tabindex=\"-1\"", d.Value));
    }
}
