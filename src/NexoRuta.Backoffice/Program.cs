using NexoRuta.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Roles.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;
using NexoRuta.ApiClient.Features.Organizaciones.Clients;
using NexoRuta.ApiClient.Features.IdentidadVisual.Clients;
using NexoRuta.ApiClient.Features.ZonasCobertura.Clients;
using NexoRuta.ApiClient.Features.Reparto.Clients;
using NexoRuta.ApiClient.Features.Tarifas.Clients;
using NexoRuta.ApiClient.Features.ReglasOperativas.Clients;
using NexoRuta.Backoffice.Core.Security;
using NexoRuta.Backoffice.Features.Tarifas;
using NexoRuta.Backoffice.Features.ZonasCobertura;
using NexoRuta.Backoffice.Features.ReglasOperativas;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR(options => options.MaximumParallelInvocationsPerClient = 1);
builder.Host.UseNexoRutaLogging("NexoRuta.Backoffice");
var sharedDataProtection = builder.Services.AddSharedDataProtection(builder.Configuration,
    builder.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, "backoffice");
if (!sharedDataProtection && builder.Environment.IsProduction())
    throw new InvalidOperationException("Production Backoffice requires shared Data Protection storage.");
builder.Services.AddTrustedProxyHeaders(builder.Configuration);
builder.Services.AddHostingTelemetry(builder.Configuration, "NexoRuta.Backoffice");
builder.Services.AddHealthChecks().AddNexoRutaReadiness(sharedDataProtection);
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", SesionUsuario.Operador);
    options.Conventions.AuthorizePage("/Index", "envios.consultar");
    options.Conventions.AuthorizePage("/ReporteOperaciones", "envios.consultar");
    options.Conventions.AuthorizeFolder("/Liquidaciones", "envios.consultar");
    options.Conventions.AuthorizePage("/DetalleGestion", "envios.consultar");
    options.Conventions.AuthorizePage("/Usuarios", "usuarios.gestionar");
    options.Conventions.AuthorizePage("/Rutas", "envios.gestionar");
    options.Conventions.AuthorizePage("/Rutas/Planificar", "envios.gestionar");
    options.Conventions.AuthorizePage("/Rutas/Detalle", "envios.gestionar");
    options.Conventions.AuthorizePage("/Deposito", "envios.gestionar");
    options.Conventions.AuthorizePage("/Flota", "envios.gestionar");
    options.Conventions.AuthorizePage("/ConfiguracionReparto", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/Vehiculos", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/Roles", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/ZonasCobertura/Index", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/ZonasCobertura/Crear", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/ZonasCobertura/Detalle", ZonaCoberturaUi.PermisoGestionar);
    options.Conventions.AuthorizePage("/Tarifas/Index", TarifasUi.PermissionManage);
    options.Conventions.AuthorizePage("/Tarifas/Borrador", TarifasUi.PermissionManage);
    options.Conventions.AuthorizePage("/Tarifas/Publicaciones/Index", TarifasUi.PermissionManage);
    options.Conventions.AuthorizePage("/Tarifas/Publicaciones/Detalle", TarifasUi.PermissionManage);
    options.Conventions.AuthorizePage("/Tarifas/Publicar", TarifasUi.PermissionManage);
    options.Conventions.AuthorizePage("/Tarifas/Simular", TarifasUi.PermissionManage);
    options.Conventions.AuthorizeFolder("/ReglasOperativas", ReglasOperativasUi.PermissionManage);
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
    options.Events.OnRedirectToLogin = context =>
    {
        var cookiePresent = context.Request.Cookies.ContainsKey(options.Cookie.Name!);
        context.Response.Redirect(new Uri(new Uri(context.RedirectUri), RutaSesion.Ingreso(context.Request.Path, cookiePresent)).AbsoluteUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.HttpContext.User.HasClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.OrganizacionSuspendida))
            context.Response.Redirect("/OrganizacionSuspendida");
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ExpiredSessionItemKey))
        {
            context.Response.Redirect(RutaSesion.Ingreso(context.Request.Path, true, SesionUsuario.IngresoWeb(context.HttpContext.User)));
        }
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ApiUnavailableItemKey))
            context.Response.Redirect(RutaSesion.Indisponible(context.Request.Path));
        else context.Response.Redirect(context.RedirectUri);
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(SesionUsuario.Operador, policy =>
        policy.RequireAuthenticatedUser().RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Operador));
    foreach (var permission in new[] { "envios.consultar", "envios.gestionar", "usuarios.gestionar", ZonaCoberturaUi.PermisoGestionar })
        options.AddPolicy(permission, policy => policy.RequireAuthenticatedUser().AddRequirements(new ApiPermissionRequirement(permission)));
    options.AddPolicy(ReglasOperativasUi.PermissionManage, policy =>
        policy.RequireAuthenticatedUser().AddRequirements(new ApiPermissionRequirement(ReglasOperativasUi.PermissionManage)));
});
builder.Services.AddScoped<IAuthorizationHandler, ApiPermissionAuthorizationHandler>();
builder.Services.AddHttpClient("NexoRuta", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5000/"))
    .AddTypedClient<NexoRuta.ApiClient.Features.Gestion.GestionApiClient>()
    .AddTypedClient<EnviosApiClient>()
    .AddTypedClient<AuthApiClient>()
    .AddTypedClient<RolesApiClient>()
    .AddTypedClient<UsuariosApiClient>()
    .AddTypedClient<RepartidoresApiClient>()
    .AddTypedClient<RutasApiClient>()
    .AddTypedClient<ConfiguracionRepartoApiClient>()
    .AddTypedClient<OrganizacionPropiaApiClient>()
      .AddTypedClient<OrganizacionSuspendidaApiClient>()
      .AddTypedClient<ZonasCoberturaApiClient>()
      .AddTypedClient<TarifasApiClient>()
      .AddTypedClient<ReglasOperativasApiClient>()
      .AddTypedClient<IdentidadVisualApiClient>();

var app = builder.Build();
app.UseForwardedHeaders();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseRequestLocalization("es-UY");
app.UseNexoRutaRequestLogging();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs/tablero") && context.Request.Headers.Origin.FirstOrDefault() is { Length: > 0 } origin
        && (!Uri.TryCreate(origin, UriKind.Absolute, out var source) || source.GetLeftPart(UriPartial.Authority) != $"{context.Request.Scheme}://{context.Request.Host}"))
    { context.Response.StatusCode = StatusCodes.Status403Forbidden; return; }
    await next(context);
});
app.MapHub<NexoRuta.Backoffice.Features.Operacion.BoardHub>("/hubs/tablero", options => options.CloseOnAuthenticationExpiration = true);
app.MapStaticAssets();
app.MapNexoRutaHealthEndpoints();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();

public partial class Program { }
