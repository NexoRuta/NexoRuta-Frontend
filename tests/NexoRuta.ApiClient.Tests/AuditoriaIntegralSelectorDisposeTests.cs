using System.Reflection;
using Microsoft.JSInterop;
using NexoRuta.Commerce.Components.Features.Envios.Shared;
namespace NexoRuta.ApiClient.Tests;

// Review-only regression: dispose a component while JS map creation is pending.
public sealed class AuditoriaIntegralSelectorDisposeTests
{
    [Fact]
    public async Task AuditoriaIntegral_MapReturnedAfterDisposeMustBeReleased()
    {
        var js = new PendingMap();
        var picker = new SelectorDestino { Editable = false };
        var type = typeof(SelectorDestino);
        type.GetProperty("Js", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, js);
        type.GetField("token", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, "pk.synthetic");
        var render = (Task)type.GetMethod("OnAfterRenderAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(picker, [false])!;
        await js.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await picker.DisposeAsync(); // Navigation away before the map creation finishes.
        js.Continue.TrySetResult();
        await render.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, js.Map.ReleaseCalls);
    }
    private sealed class PendingMap : IJSRuntime, IJSObjectReference
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public MapReference Map { get; } = new();
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id, CancellationToken.None, args);
        public async ValueTask<T> InvokeAsync<T>(string id, CancellationToken ct, object?[]? args)
        {
            if (id == "crear") { Started.TrySetResult(); await Continue.Task.WaitAsync(ct); return (T)(object)Map; }
            return typeof(T)==typeof(IJSObjectReference) ? (T)(object)this : default!;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
    private sealed class MapReference : IJSObjectReference
    {
        public int ReleaseCalls;
        public ValueTask<T> InvokeAsync<T>(string id, object?[]? args) => InvokeAsync<T>(id,CancellationToken.None,args);
        public ValueTask<T> InvokeAsync<T>(string id,CancellationToken ct,object?[]? args)
        { if(id=="dispose")ReleaseCalls++;return ValueTask.FromResult(default(T)!); }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
