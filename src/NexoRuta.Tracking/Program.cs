using NexoRuta.Tracking.Components;
using NexoRuta.ApiClient.Features.Seguimiento.Clients;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services
    .AddHttpClient("NexoRuta.Seguimiento", client =>
    {
        client.BaseAddress = new Uri(
            builder.Configuration["Api:BaseAddress"]
            ?? "http://localhost:5000/"
        );
    })
    .AddTypedClient<SeguimientoApiClient>();

builder.Logging.AddFilter(
    "System.Net.Http.HttpClient.NexoRuta.Seguimiento",
    LogLevel.Warning
);

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/seguimiento"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
    }
    await next(context);
});

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
