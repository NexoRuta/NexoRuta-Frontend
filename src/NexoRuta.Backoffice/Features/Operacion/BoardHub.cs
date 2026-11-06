using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using NexoRuta.ApiClient.Core.Session;
namespace NexoRuta.Backoffice.Features.Operacion;

// The browser uses its existing protected same-origin cookie. The API bearer
// credential stays on the server and is never embedded in HTML or a browser URL.
[Authorize(Policy = SesionUsuario.Operador)]
public sealed class BoardHub(IConfiguration configuration) : Hub
{
    public async IAsyncEnumerable<JsonElement> Fotografias([EnumeratorCancellation] CancellationToken ct)
    {
        var token = SesionUsuario.ObtenerToken(Context.User!);
        var baseUri = new Uri(configuration["Api:BaseAddress"] ?? "http://localhost:5000/");
        await using var api = new HubConnectionBuilder().WithUrl(new Uri(baseUri, "hubs/operacion"),
            options => { options.AccessTokenProvider = () => Task.FromResult<string?>(token); })
            .Build();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, Context.ConnectionAborted);
        await api.StartAsync(linked.Token);
        await foreach (var snapshot in api.StreamAsync<JsonElement>("Fotografias", linked.Token))
            yield return snapshot;
    }
}
