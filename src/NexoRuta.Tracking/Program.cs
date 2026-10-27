using NexoRuta.Hosting;
using NexoRuta.Tracking.Components;
using NexoRuta.ApiClient.Features.Seguimiento.Clients;
var builder = WebApplication.CreateBuilder(args.FirstOrDefault() == "keys-import" ? [] : args);
builder.Services.AddHealthChecks();
builder.Services.AddSharedDataProtection(builder.Configuration, builder.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, "tracking");
builder.Services.AddTrustedProxyHeaders(builder.Configuration);
builder.Services.AddHostingTelemetry(builder.Configuration, "NexoRuta.Tracking");

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
if (args.Length == 2 && args[0] == "keys-import")
{
    await SharedDataProtection.ImportAsync(app.Services, args[1]);
    return;
}
app.UseForwardedHeaders();

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

app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
