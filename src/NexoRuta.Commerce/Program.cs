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
builder.Services.AddHealthChecks();

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
    options.Events.OnRedirectToAccessDenied = async context =>
    {
        if (context.HttpContext.User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            context.Response.Redirect("/organizacion-suspendida");
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ExpiredSessionItemKey))
        {
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.Redirect("/ingresar?expirada=true");
        }
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ApiUnavailableItemKey))
            context.Response.Redirect("/api-indisponible");
        else context.Response.Redirect(context.RedirectUri);
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
});
builder.Services.AddScoped<IAuthorizationHandler, ApiPermissionAuthorizationHandler>();
builder.Services.AddHttpClient("NexoRuta", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5000/"))
    .AddTypedClient<EnviosApiClient>()
    .AddTypedClient<DestinosEnvioApiClient>()
    .AddTypedClient<AuthApiClient>()
    .AddTypedClient<UsuariosApiClient>()
    .AddTypedClient<RolesApiClient>()
    .AddTypedClient<OrganizacionPropiaApiClient>()
    .AddTypedClient<OrganizacionSuspendidaApiClient>()
    .AddTypedClient<OperadoresApiClient>();

var app = builder.Build();

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
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapRazorPages();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization(SesionUsuario.Comercio);

app.Run();
