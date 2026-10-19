using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Organizaciones.Clients;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.Backoffice.Core.Security;
using NexoRuta.Backoffice.Features.ZonasCobertura;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", SesionUsuario.Operador);
    options.Conventions.AuthorizePage("/Index", "envios.consultar");
    options.Conventions.AuthorizePage("/Usuarios", "usuarios.gestionar");
    options.Conventions.AuthorizePage("/Roles", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/ZonasCobertura/Index", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/ZonasCobertura/Crear", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/ZonasCobertura/Detalle", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AllowAnonymousToPage("/Ingresar");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/AccesoDenegado");
    options.Conventions.AllowAnonymousToPage("/ApiIndisponible");
    options.Conventions.AllowAnonymousToPage("/OrganizacionSuspendida");
    options.Conventions.AllowAnonymousToPage("/Salir");
    options.Conventions.AllowAnonymousToPage("/SolicitarRecuperacion");
});
builder.Services.AddAntiforgery(options => options.Cookie.Name = "NexoRuta.Backoffice.Antiforgery");
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "NexoRuta.Backoffice";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.LoginPath = "/ingresar";
    options.AccessDeniedPath = "/acceso-denegado";
    options.SlidingExpiration = false;
    options.Events.OnRedirectToAccessDenied = async context =>
    {
        if (context.HttpContext.User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            context.Response.Redirect("/OrganizacionSuspendida");
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
    options.AddPolicy(SesionUsuario.Operador, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Operador));
    foreach (var permission in new[] { "envios.consultar", "usuarios.gestionar", ZonaCoberturaUi.PermisoGestionar })
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new ApiPermissionRequirement(permission)));
});
builder.Services.AddScoped<IAuthorizationHandler, ApiPermissionAuthorizationHandler>();
builder.Services.AddHttpClient("NexoRuta", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5000/"))
    .AddTypedClient<EnviosApiClient>()
    .AddTypedClient<AuthApiClient>()
    .AddTypedClient<RolesApiClient>()
    .AddTypedClient<UsuariosApiClient>()
    .AddTypedClient<OrganizacionPropiaApiClient>()
      .AddTypedClient<OrganizacionSuspendidaApiClient>()
      .AddTypedClient<ZonasCoberturaApiClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program { }
