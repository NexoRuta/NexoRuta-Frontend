using System.Reflection;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.Commerce.Components.Features.Envios.Shared;

namespace NexoRuta.ApiClient.Tests;

public sealed class SelectorDestinoLifecycleTests
{
    [Fact]
    public async Task CoverageCompletingAfterDisposalDoesNotCancelADisposedSearchAgain()
    {
        var pending = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var http = new HttpClient(new PendingCoverage(pending.Task)) { BaseAddress = new Uri("http://api.test/") };
        var picker = new SelectorDestino { Editable = true, OperadorId = Guid.NewGuid(), ApiToken = "synthetic-session" };
        Set(picker, "Envios", new EnviosApiClient(http));
        Set(picker, "busquedaCancelacion", new CancellationTokenSource());
        var parameters = (Task)typeof(SelectorDestino).GetMethod("OnParametersSetAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, null)!;
        await picker.DisposeAsync();
        pending.SetResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") });
        await parameters.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task BecomingEditableDuringCreationStillConnectsTheKeyboardOnce()
    {
        var js = new PendingInitialization("crear");
        var picker = new SelectorDestino { Editable = false };
        Set(picker, "Js", js);
        Set(picker, "token", "pk.synthetic");
        var render = Render(picker);
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        picker.Editable = true;
        await Render(picker);
        js.Continue.SetResult();
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, js.KeyboardCreations);
        Assert.Equal(1, js.MapCreations);
        await picker.DisposeAsync();
    }

    [Theory]
    [InlineData("import")]
    [InlineData("conectarTeclado")]
    [InlineData("crear")]
    public async Task AbandoningInitializationReleasesEveryAcquiredReferenceExactlyOnce(string pending)
    {
        var js = new PendingInitialization(pending);
        var picker = new SelectorDestino { Editable = true };
        Set(picker, "Js", js);
        Set(picker, "token", "pk.synthetic");
        var render = Render(picker);
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await picker.DisposeAsync();
        js.Continue.SetResult();
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        await picker.DisposeAsync();
        await Render(picker);

        Assert.Equal(1, js.Module.ReleaseCalls);
        Assert.Equal(pending == "import" ? 0 : 1, js.Keyboard.DisposeCalls);
        Assert.Equal(pending == "import" ? 0 : 1, js.Keyboard.ReleaseCalls);
        Assert.Equal(pending == "crear" ? 1 : 0, js.Map.DisposeCalls);
        Assert.Equal(pending == "crear" ? 1 : 0, js.Map.ReleaseCalls);
        Assert.Equal(0, js.Map.UpdateCalls);
        Assert.Equal(pending == "import" ? 0 : 1, js.KeyboardCreations);
        Assert.Equal(pending == "crear" ? 1 : 0, js.MapCreations);
        if (js.Receiver is not null)
            Assert.Throws<ObjectDisposedException>(() => js.Receiver.Value);
    }

    [Theory]
    [InlineData("Seleccionar")]
    [InlineData("EstadoMapa")]
    [InlineData("EstadoCobertura")]
    [InlineData("MoverSugerencia")]
    [InlineData("ElegirSugerenciaActiva")]
    [InlineData("CerrarSugerencias")]
    public async Task CallbacksAfterDisposalDoNotDispatchOrChangeTheParent(string callback)
    {
        var picker = new SelectorDestino { Editable = true };
        var changes = 0;
        picker.PuntoCambiado = EventCallback.Factory.Create<(double?, double?, string?)>(this,
            (Action<(double?, double?, string?)>)(_ => changes++));
        await picker.DisposeAsync();
        // No renderer is attached: dispatching after disposal would itself throw.
        await (callback switch
        {
            "Seleccionar" => picker.Seleccionar(-34.9, -56.2, "Late selection"),
            "EstadoMapa" => picker.EstadoMapa("listo"),
            "EstadoCobertura" => picker.EstadoCobertura("error"),
            "MoverSugerencia" => picker.MoverSugerencia(1),
            "ElegirSugerenciaActiva" => picker.ElegirSugerenciaActiva(),
            _ => picker.CerrarSugerencias()
        });
        Assert.Equal(0, changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ParentDisposingDuringPointNotificationStopsTheContinuation(bool suggestion)
    {
        var js = new PendingInitialization("");
        var picker = new SelectorDestino { Editable = true, ParaAlta = true, ApiToken = "synthetic-session" };
        Set(picker, "modulo", js.Module);
        var changes = 0;
        picker.PuntoCambiado = EventCallback.Factory.Create<(double?, double?, string?)>(this,
            (Func<(double?, double?, string?), Task>)(async _ => { changes++; await picker.DisposeAsync(); }));
        var method = typeof(SelectorDestino).GetMethod(suggestion ? "ElegirAsync" : "CambiarBusqueda", BindingFlags.Instance | BindingFlags.NonPublic)!;
        object argument = suggestion
            ? new SelectorDestino.Sugerencia { Id = "1", Descripcion = "Synthetic address", Latitud = -34.9, Longitud = -56.2 }
            : new ChangeEventArgs { Value = "Synthetic address" };
        await (Task)method.Invoke(picker, [argument])!;
        Assert.Equal(1, changes);
        Assert.Equal(1, js.Module.ReleaseCalls);
        Assert.False((bool)typeof(SelectorDestino).GetField("buscando", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(picker)!);
    }

    private static Task Render(SelectorDestino picker) => (Task)typeof(SelectorDestino)
        .GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, [false])!;

    private static void Set(SelectorDestino picker, string name, object value)
    {
        var type = typeof(SelectorDestino);
        if (type.GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } property)
            property.SetValue(picker, value);
        else type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, value);
    }

    private sealed class PendingInitialization : IJSRuntime
    {
        private readonly string pending;
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Reference Module { get; }
        public Reference Keyboard { get; } = new();
        public Reference Map { get; } = new();
        public DotNetObjectReference<SelectorDestino>? Receiver;
        public int KeyboardCreations, MapCreations;

        public PendingInitialization(string pending)
        {
            this.pending = pending;
            Module = new Reference(this);
        }

        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            if (id == "conectarTeclado") { KeyboardCreations++; Receiver = (DotNetObjectReference<SelectorDestino>)args![1]!; }
            if (id == "crear") { MapCreations++; Receiver = (DotNetObjectReference<SelectorDestino>)args![3]!; }
            if (id == pending) { Started.TrySetResult(); await Continue.Task.WaitAsync(ct); }
            return (T)(object)(id switch { "import" => Module, "conectarTeclado" => Keyboard, "crear" => Map, _ => throw new InvalidOperationException(id) });
        }
    }

    private sealed class Reference(PendingInitialization? owner = null) : IJSObjectReference
    {
        public int DisposeCalls, ReleaseCalls, UpdateCalls;
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            if (owner is not null) return owner.InvokeAsync<T>(id, ct, args);
            if (id == "dispose") DisposeCalls++; else UpdateCalls++;
            return ValueTask.FromResult(default(T)!);
        }
        public ValueTask DisposeAsync() { ReleaseCalls++; return ValueTask.CompletedTask; }
    }

    private sealed class PendingCoverage(Task<HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => response;
    }
}
