using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Components;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Envios.Contracts.Requests;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.Commerce.Components.Features.Envios.Pages;

namespace NexoRuta.ApiClient.Tests;

public sealed class AlternativasOperadorComponentTests
{
    [Fact]
    public async Task MissingOperationalPublicationKeepsReasonAndOffersEligibleAlternatives()
    {
        using var setup = new Scenario(); setup.Handler.MissingRules = true;
        await setup.Call("ConsultarServiciosAsync");
        Assert.Contains("reglas operativas", setup.Value<string>("errorServicio"));
        Assert.True(setup.Value<bool>("sinServicio"));
        Assert.Equal(1, setup.Count("alternativas"));
        await setup.Call("ElegirAlternativaAsync", setup.Handler.Other);
        Assert.Equal(setup.Handler.Other, setup.Form.OperadorId);
        Assert.Equal("Saved address", setup.Form.Direccion);
        Assert.Equal(2, setup.Form.Bultos.Count);
        Assert.Equal(1, setup.Handler.Quotes);
    }

    [Fact]
    public async Task NoCoverageLoadsAlternativesWithoutChangingTheOperatorOrDraft()
    {
        using var setup = new Scenario();
        var original = setup.Form.OperadorId;
        await setup.Call("ConsultarServiciosAsync");
        Assert.Equal(original, setup.Form.OperadorId);
        Assert.Contains("no ofrece cobertura", setup.Value<string>("errorServicio"));
        Assert.Equal(1, setup.Count("alternativas"));
        Assert.Equal(0, setup.Handler.Quotes);
    }

    [Fact]
    public async Task ExplicitChoiceKeepsEveryDraftValueAndQuotesWithoutCreating()
    {
        using var setup = new Scenario();
        await setup.Call("ConsultarServiciosAsync");
        Assert.Equal(0, setup.Count("operadores")); // The initial catalog predates this alternative.
        var parcels = setup.Form.Bultos.ToArray();
        await setup.Call("ElegirAlternativaAsync", setup.Handler.Other);
        Assert.Equal(setup.Handler.Other, setup.Form.OperadorId);
        Assert.Equal(1, setup.Count("operadores"));
        Assert.Equal("Recipient", setup.Form.DestinatarioNombre);
        Assert.Equal("recipient@example.test", setup.Form.DestinatarioEmail);
        Assert.Equal("Saved address", setup.Form.Direccion);
        Assert.Equal(-34.9, setup.Form.Servicio.Latitud);
        Assert.Equal(-56.2, setup.Form.Servicio.Longitud);
        Assert.Equal(parcels, setup.Form.Bultos);
        Assert.Equal(1, setup.Handler.Quotes);
        Assert.NotNull(setup.Value<object>("cotizacion"));
        Assert.Equal(0, setup.Handler.Creates);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyAndQueryFailureAreDistinctAndRetryable(bool failure)
    {
        using var setup = new Scenario();
        setup.Handler.EmptyAlternatives = !failure; setup.Handler.FailAlternatives = failure;
        await setup.Call("ConsultarServiciosAsync");
        Assert.Equal(0, setup.Count("alternativas"));
        Assert.Equal(failure, setup.Value<string>("errorAlternativas") is not null);
        Assert.Equal(!failure, setup.Value<bool>("alternativasConsultadas"));
        setup.Handler.EmptyAlternatives = setup.Handler.FailAlternatives = false;
        await setup.Call("ConsultarAlternativasAsync");
        Assert.Equal(1,setup.Count("alternativas"));
        Assert.Null(setup.Value<string>("errorAlternativas"));
    }

    [Fact]
    public async Task MultipleModalitiesRequireExplicitServiceSelectionAndInvalidDraftHasNoQuote()
    {
        using var setup = new Scenario(); setup.Handler.MultipleServices = true;
        await setup.Call("ConsultarServiciosAsync");
        await setup.Call("ElegirAlternativaAsync",setup.Handler.Other);
        Assert.Equal(0,setup.Handler.Quotes);
        var services = setup.Value<IReadOnlyList<NexoRuta.ApiClient.Features.Envios.Contracts.Responses.ServicioEnvioDisponibleResponse>>("servicios")!;
        setup.Form.DestinatarioNombre = "";
        await setup.Call("ElegirServicioAsync",new ChangeEventArgs { Value=$"{services[0].ZonaId:D}|{services[0].Modalidad}" });
        Assert.Equal(0,setup.Handler.Quotes);
        setup.Form.DestinatarioNombre = "Recipient";
        await setup.Call("ElegirServicioAsync",new ChangeEventArgs { Value=$"{services[1].ZonaId:D}|{services[1].Modalidad}" });
        Assert.Equal(1,setup.Handler.Quotes); Assert.Equal(0,setup.Handler.Creates);
    }

    [Fact]
    public async Task LateAlternativeResponseCannotReplaceNewPointResults()
    {
        using var setup = new Scenario(); setup.Handler.HoldFirstAlternatives = true;
        var previous = setup.Call("ConsultarServiciosAsync");
        await setup.Handler.AlternativesStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        setup.Form.Servicio.Latitud = -33;
        setup.Handler.EmptyAlternatives = true;
        await setup.Call("ConsultarServiciosAsync");
        setup.Handler.ReleaseAlternatives.TrySetResult(); await previous;
        Assert.Equal(0,setup.Count("alternativas"));
        Assert.True(setup.Value<bool>("alternativasConsultadas"));
        Assert.False(setup.Value<bool>("consultandoAlternativas"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteServicesAndErrorsCannotReplaceTheNewOperator(bool failure)
    {
        using var setup = new Scenario(); setup.Handler.HoldServices = true; setup.Handler.FailHeldServices = failure;
        var previous = setup.Call("ConsultarServiciosAsync");
        await setup.Handler.ServicesStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        setup.Form.OperadorId = setup.Handler.Other;
        await setup.Call("PuntoManualAsync");
        setup.Handler.ReleaseServices.TrySetResult(); await previous;
        Assert.Null(setup.Value<string>("errorServicio"));
        Assert.False(setup.Value<bool>("sinServicio"));
        Assert.False(setup.Value<bool>("consultandoServicios"));
        Assert.Equal(1,setup.Count("servicios"));
        Assert.Equal(0,setup.Count("alternativas"));
    }

    [Fact]
    public async Task LateQuoteAndWithdrawalDoNotLeaveAnOldPriceOrCreateShipment()
    {
        using var setup = new Scenario(); setup.Handler.HoldQuote = true;
        await setup.Call("ConsultarServiciosAsync");
        var selection = setup.Call("ElegirAlternativaAsync",setup.Handler.Other);
        await setup.Handler.QuoteStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        setup.Form.Servicio.Latitud = -33;
        await setup.Call("PuntoManualAsync");
        setup.Handler.ReleaseQuote.TrySetResult(); await selection;
        Assert.Null(setup.Value<object>("cotizacion"));
        Assert.False(setup.Value<bool>("cotizando"));
        Assert.Equal(0,setup.Handler.Creates);
        setup.Handler.HoldQuote = false;
        await setup.Call("CotizarAsync");
        Assert.NotNull(setup.Value<object>("cotizacion"));
        setup.Handler.WithdrawOnCreate = true;
        await setup.Call("CrearAsync");
        Assert.Null(setup.Value<object>("cotizacion"));
        Assert.Null(setup.Form.Servicio.VersionTarifarioEsperada);
        Assert.Equal("Saved address",setup.Form.Direccion);
        Assert.Equal(2,setup.Form.Bultos.Count);
        Assert.Contains("unavailable",setup.Value<string>("error"));
    }

    private sealed class Scenario : IDisposable
    {
        public CrearEnvio Page { get; } = new();
        public AlternativeHandler Handler { get; } = new();
        public HttpClient Http { get; }
        public CrearEnvioRequest Form => Value<CrearEnvioRequest>("formulario")!;
        public Scenario()
        {
            Http = new(Handler) { BaseAddress = new Uri("http://api.test/") };
            typeof(CrearEnvio).GetField("token",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Page,"synthetic-token");
            typeof(CrearEnvio).GetProperty("ApiClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Page, new EnviosApiClient(Http));
            typeof(CrearEnvio).GetProperty("OperadoresClient", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Page, new OperadoresApiClient(Http));
            typeof(CrearEnvio).GetProperty("Navigation", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Page, new Navigation());
            Form.OperadorId = Handler.Current; Form.DestinatarioNombre = "Recipient";
            Form.DestinatarioEmail = "recipient@example.test"; Form.Direccion = "Saved address";
            Form.Servicio.Latitud = -34.9; Form.Servicio.Longitud = -56.2;
            Form.Bultos = [new() { Codigo="A",PesoGramos=1500,LargoCentimetros=30,AnchoCentimetros=20,AltoCentimetros=10 },
                new() { Codigo="B",PesoGramos=1000,LargoCentimetros=20,AnchoCentimetros=20,AltoCentimetros=10 }];
        }
        public T? Value<T>(string name) => (T?)typeof(CrearEnvio).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(Page);
        public int Count(string name) => ((System.Collections.IEnumerable)Value<object>(name)!).Cast<object>().Count();
        public Task Call(string name, params object[] args) => (Task)typeof(CrearEnvio).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(Page,args)!;
        public void Dispose() { Page.Dispose(); Http.Dispose(); }
    }

    private sealed class Navigation : NavigationManager
    {
        public Navigation() => Initialize("http://commerce.test/", "http://commerce.test/envios/crear");
        protected override void NavigateToCore(string uri, bool forceLoad) => throw new NotSupportedException();
    }

    private sealed class AlternativeHandler : HttpMessageHandler
    {
        public Guid Current { get; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
        public Guid Other { get; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
        public int Quotes, Creates;
        public bool MissingRules;
        public bool EmptyAlternatives,FailAlternatives,MultipleServices,HoldFirstAlternatives,HoldQuote,WithdrawOnCreate,HoldServices,FailHeldServices;
        public TaskCompletionSource ServicesStarted { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseServices { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AlternativesStarted { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseAlternatives { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource QuoteStarted { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseQuote { get; }=new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int alternativeCalls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (MissingRules && path.Contains(Current.ToString()) && path.EndsWith("servicios-envio"))
                return new(HttpStatusCode.Conflict) { Content = JsonContent.Create(new { message = "El operador debe publicar sus reglas operativas antes de admitir env?os." }) };
            if(path.Contains(Current.ToString())&&path.EndsWith("servicios-envio")&&HoldServices)
            {
                ServicesStarted.TrySetResult(); await ReleaseServices.Task;
                if(FailHeldServices) throw new HttpRequestException("Controlled stale service error");
            }
            if(path=="/api/operadores/alternativas-envio")
            {
                var first=Interlocked.Increment(ref alternativeCalls)==1;
                var empty=EmptyAlternatives;
                if(first&&HoldFirstAlternatives) { AlternativesStarted.TrySetResult();await ReleaseAlternatives.Task; }
                if(FailAlternatives) throw new HttpRequestException("Controlled alternatives failure");
                return new(HttpStatusCode.OK) {Content=JsonContent.Create(empty?Array.Empty<object>():new object[]{new {operadorId=Other,nombre="Alternative",modalidades=new[]{"Urgente"}}})};
            }
            if(path=="/api/envios/cotizacion"&&HoldQuote) { QuoteStarted.TrySetResult();await ReleaseQuote.Task; }
            if(path=="/api/envios"&&WithdrawOnCreate) { Creates++;return new(HttpStatusCode.BadRequest){Content=JsonContent.Create(new {message="Service unavailable"})}; }
            object body = path switch {
                "/api/operadores/alternativas-envio" => new[] { new { operadorId=Other,nombre="Alternative",modalidades=new[]{"Urgente"} } },
                var x when x.EndsWith("servicios-envio") => x.Contains(Current.ToString()) ? Array.Empty<object>() : MultipleServices ? new object[] { new {zonaId=Guid.Parse("00000000-0000-0000-0000-000000000003"),zonaNombre="Zone",modalidad="Urgente"},new {zonaId=Guid.Parse("00000000-0000-0000-0000-000000000003"),zonaNombre="Zone",modalidad="Estandar"} } : new object[] { new { zonaId=Guid.Parse("00000000-0000-0000-0000-000000000003"),zonaNombre="Zone",modalidad="Urgente" } },
                var x when x.EndsWith("identidad-visual") => new { nombre="Alternative" },
                "/api/envios/cotizacion" => Quote(),
                "/api/envios" => throw new InvalidOperationException("Choosing an alternative must never create a shipment."),
                _ => throw new InvalidOperationException(path)
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content=JsonContent.Create(body) };
        }
        private object Quote() { Quotes++; return new { versionTarifarioId=Guid.NewGuid(),zonaId=Guid.NewGuid(),modalidad="Urgente",moneda="UYU",totalFinalUyu=1751,importeBaseUyu=1500,componentePesoUyu=250,componenteVolumenUyu=1 }; }
    }
}
