using NexoRuta.Hosting;
using NexoRuta.Tracking.Components;
using NexoRuta.ApiClient.Features.Seguimiento.Clients;
var builder = WebApplication.CreateBuilder(args);
var commerceDemo = builder.Configuration.GetValue<bool>("CommerceDemo:Enabled");
if (commerceDemo && !(builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing")))
    throw new InvalidOperationException("CommerceDemo is restricted to Development/Testing.");
if (commerceDemo)
{
    if ((builder.Configuration["CommerceDemo:AdminPassword"]?.Length ?? 0) < 24 ||
        builder.Configuration.GetSection("CommerceDemo:SigningSecrets").Get<string[]>() is not {Length: >0})
        throw new InvalidOperationException("CommerceDemo requires a strong controls password and signing keys.");
    builder.Services.AddSingleton<NexoRuta.Tracking.CommerceDemo.ReceiverStore>();
    builder.Services.AddAuthentication("CommerceDemo").AddCookie("CommerceDemo", options =>
    {
        options.Cookie.Name = "NexoRuta.CommerceDemo";
        options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Strict;
        options.LoginPath = "/comercio/ingresar"; options.ExpireTimeSpan = TimeSpan.FromHours(4);
    });
    builder.Services.AddAuthorization();
    builder.Services.AddRazorPages();
}
builder.Host.UseNexoRutaLogging("NexoRuta.Tracking");
var sharedDataProtection = builder.Services.AddSharedDataProtection(builder.Configuration,
    builder.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, "tracking");
if (!sharedDataProtection && builder.Environment.IsProduction())
    throw new InvalidOperationException("Production Tracking requires shared Data Protection storage.");
builder.Services.AddHealthChecks().AddNexoRutaReadiness(sharedDataProtection);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
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
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/comercio"))
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!commerceDemo) { context.Response.StatusCode = 404; return; }
    }
    await next(context);
});
app.UseRouting();
if (commerceDemo) { app.UseAuthentication(); app.UseAuthorization(); }
app.UseNexoRutaRequestLogging();
app.UseAntiforgery();

app.MapNexoRutaHealthEndpoints();
if (commerceDemo)
{
    app.MapRazorPages();
    app.MapPost("/comercio/webhook", NexoRuta.Tracking.CommerceDemo.ReceiverEndpoint.Receive)
        .AllowAnonymous().DisableAntiforgery(); // Raw HTTP endpoint authenticates exact bytes with HMAC.
}
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
