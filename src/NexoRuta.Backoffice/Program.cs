using Microsoft.AspNetCore.Authentication.Cookies;
using NexoRuta.ApiClient;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddHttpClient<EnviosApiClient>(client =>
    client.BaseAddress = new Uri(builder.Configuration["Api:BaseAddress"] ?? "http://localhost:5000/"));

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
app.MapRazorPages()
   .WithStaticAssets();

app.Run();
