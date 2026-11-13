using NexoRuta.Hosting;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Operadores.Clients;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Organizaciones.Clients;
using NexoRuta.Commerce.Components;
using NexoRuta.Commerce.Core.Security;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseNexoRutaLogging("NexoRuta.Commerce");
var sharedDataProtection = builder.Services.AddSharedDataProtection(builder.Configuration,
    builder.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, "commerce");
if (!sharedDataProtection && builder.Environment.IsProduction())
    throw new InvalidOperationException("Production Commerce requires shared Data Protection storage.");
builder.Services.AddTrustedProxyHeaders(builder.Configuration);
builder.Services.AddHostingTelemetry(builder.Configuration, "NexoRuta.Commerce");
builder.Services.AddHealthChecks().AddNexoRutaReadiness(sharedDataProtection);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRazorPages();
builder.Services.AddHttpContextAccessor();
builder.Services.AddAntiforgery(options => options.Cookie.Name = "NexoRuta.Commerce.Antiforgery");
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<CommerceAuthorizationState>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "NexoRuta.Commerce";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/ingresar";
    options.AccessDeniedPath = "/acceso-denegado";
    options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context =>
    {
        var cookiePresent=context.Request.Cookies.ContainsKey(options.Cookie.Name!);
        context.Response.Redirect(new Uri(new Uri(context.RedirectUri),RutaSesion.Ingreso(context.Request.Path,cookiePresent)).AbsoluteUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.HttpContext.User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            context.Response.Redirect("/organizacion-suspendida");
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ExpiredSessionItemKey))
        {
            context.Response.Redirect(RutaSesion.Ingreso(context.Request.Path,true,SesionUsuario.IngresoWeb(context.HttpContext.User)));
        }
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ApiUnavailableItemKey))
            context.Response.Redirect(RutaSesion.Indisponible(context.Request.Path));
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SesionUsuario.Comercio, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Comercio));
    options.AddPolicy(ApiPermissionAuthorizationHandler.UsersManagePolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Comercio)
        .AddRequirements(new ApiPermissionRequirement("usuarios.gestionar")));
    options.AddPolicy(ApiPermissionAuthorizationHandler.ConfigManagePolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Comercio)
        .AddRequirements(new ApiPermissionRequirement("configuracion.gestionar")));
    options.AddPolicy(ApiPermissionAuthorizationHandler.ShipmentsCreatePolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Comercio)
        .AddRequirements(new ApiPermissionRequirement("envios.crear")));
    options.AddPolicy(ApiPermissionAuthorizationHandler.ShipmentsReadPolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Comercio)
        .AddRequirements(new ApiPermissionRequirement("envios.consultar")));
    options.AddPolicy(ApiPermissionAuthorizationHandler.ReturnsManagePolicy, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Comercio)
        .AddRequirements(new ApiPermissionRequirement("devoluciones.gestionar")));
});
builder.Services.AddScoped<IAuthorizationHandler, ApiPermissionAuthorizationHandler>();
builder.Services.AddHttpClient("NexoRuta", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5000/"))
    .AddTypedClient<NexoRuta.ApiClient.Features.Gestion.GestionApiClient>()
    .AddTypedClient<NexoRuta.ApiClient.Features.Webhooks.WebhooksApiClient>()
    .AddTypedClient<EnviosApiClient>()
    .AddTypedClient<NexoRuta.ApiClient.Features.Devoluciones.Clients.DevolucionesApiClient>()
    .AddTypedClient<DestinosEnvioApiClient>()
    .AddTypedClient<AuthApiClient>()
    .AddTypedClient<UsuariosApiClient>()
    .AddTypedClient<RolesApiClient>()
    .AddTypedClient<OrganizacionPropiaApiClient>()
    .AddTypedClient<OrganizacionSuspendidaApiClient>()
    .AddTypedClient<OperadoresApiClient>();

var app = builder.Build();
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.Use(async (context, next) =>
{
    // Do not re-execute identity-only form errors through a commerce-only page.
    if (HttpMethods.IsPost(context.Request.Method) && context.Request.Path is var path &&
        (path == "/registrarse" || path == "/organizacion") &&
        context.Features.Get<Microsoft.AspNetCore.Diagnostics.IStatusCodePagesFeature>() is { } statusPages)
        statusPages.Enabled = false;
    await next(context);
});
app.UseRouting();
app.UseNexoRutaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapNexoRutaHealthEndpoints();
app.MapRazorPages();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(SesionUsuario.Comercio);

app.Run();
