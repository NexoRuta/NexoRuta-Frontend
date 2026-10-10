using Microsoft.AspNetCore.Authentication.Cookies;
using NexoRuta.ApiClient.Core.Session;
using NexoRuta.ApiClient.Features.Envios.Clients;
using NexoRuta.ApiClient.Features.Accesos.Clients;
using NexoRuta.ApiClient.Features.Usuarios.Clients;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHealthChecks();

// Add services to the container.
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/", SesionUsuario.Operador);
    options.Conventions.AllowAnonymousToPage("/Ingresar");
    options.Conventions.AllowAnonymousToPage("/Error");
});
builder.Services.AddAntiforgery(options => options.Cookie.Name = "NexoRuta.Backoffice.Antiforgery");
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = "NexoRuta.Backoffice";
    options.LoginPath = "/ingresar";
    options.AccessDeniedPath = "/ingresar";
});
builder.Services.AddAuthorization(options => options.AddPolicy(SesionUsuario.Operador,
    policy => policy.RequireAuthenticatedUser().RequireClaim(SesionUsuario.ClaimTipoAcceso, SesionUsuario.Operador)));
builder.Services.AddHttpClient("NexoRuta", client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5000/"))
    .AddTypedClient<EnviosApiClient>()
    .AddTypedClient<AccesosApiClient>()
    .AddTypedClient<UsuariosApiClient>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapHealthChecks("/health/ready").AllowAnonymous();
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
