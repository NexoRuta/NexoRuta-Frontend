using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using NexoRuta.Admin.Core.Security;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Auth.Clients;
using NexoRuta.ApiClient.Features.Plataforma.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();
builder.Services.AddDataProtection().SetApplicationName("NexoRuta.Admin");
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", AdminAccess.Permission);
    foreach (var page in new[] { "/Ingresar", "/SolicitarRecuperacion", "/AccesoDenegado", "/ApiIndisponible", "/Error", "/Salir" })
        options.Conventions.AllowAnonymousToPage(page);
});
builder.Services.AddAntiforgery(options => options.Cookie.Name = "NexoRuta.Admin.Antiforgery");
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "NexoRuta.Admin";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.LoginPath = "/ingresar";
    options.AccessDeniedPath = "/acceso-denegado";
    options.SlidingExpiration = false;
    options.Events.OnRedirectToAccessDenied = async context =>
    {
        if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ExpiredSessionItemKey))
        {
            await context.HttpContext.SignOutAsync();
            context.Response.Redirect("/ingresar?expirada=true");
        }
        else if (context.HttpContext.Items.ContainsKey(ApiPermissionAuthorizationHandler.ApiUnavailableItemKey))
            context.Response.Redirect("/api-indisponible");
        else context.Response.Redirect(context.RedirectUri);
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy(AdminAccess.Permission, policy => policy.RequireAuthenticatedUser()
    .RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Plataforma)
    .AddRequirements(new ApiPermissionRequirement(AdminAccess.Permission))));
builder.Services.AddScoped<IAuthorizationHandler, ApiPermissionAuthorizationHandler>();
builder.Services.AddHttpClient("NexoRuta", client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5041/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).AddTypedClient<AuthApiClient>().AddTypedClient<UsuariosApiClient>().AddTypedClient<OrganizacionesAdminApiClient>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Error");
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapRazorPages().WithStaticAssets();
app.Run();

public partial class Program { }
