// Portal de terapeutas. Con un comando "terapeuta-..." ejecuta la tarea y termina.
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using MySqlConnector;
using Rehab.Portal.Acciones;
using Rehab.Portal.Admin;
using Rehab.Portal.Componentes;
using Rehab.Portal.Datos;
using Rehab.Portal.Seguridad;

bool esComando = args.Length > 0 && args[0].StartsWith("terapeuta-", StringComparison.Ordinal);
var builder = WebApplication.CreateBuilder(esComando ? [] : args);

string archivoClave = builder.Configuration["DB_PASSWORD_FILE"] ?? "/run/secrets/db_portal_password";
var ds = new MySqlDataSource(new MySqlConnectionStringBuilder
{
    Server = builder.Configuration["DB_HOST"] ?? "db",
    Database = builder.Configuration["DB_NAME"] ?? "rehab",
    UserID = builder.Configuration["DB_USER"] ?? "rehab_portal",
    Password = File.ReadAllText(archivoClave).Trim(),
    ConnectionTimeout = 5,
    MaximumPoolSize = 10,
}.ConnectionString);

if (esComando)
    return await ComandosTerapeuta.EjecutarAsync(args, ds);

builder.WebHost.ConfigureKestrel(o =>
{
    o.AddServerHeader = false;
    o.Limits.MaxRequestBodySize = 64 * 1024;
});

builder.Services.AddSingleton(ds);
builder.Services.AddSingleton<Terapeutas>();
builder.Services.AddSingleton<Pacientes>();
builder.Services.AddSingleton<Visores>();
builder.Services.AddSingleton<Sesiones>();
builder.Services.AddSingleton<Auditoria>();
builder.Services.AddSingleton<Observaciones>();
builder.Services.AddSingleton<Exportacion>();

builder.Services.AddDataProtection().SetApplicationName("rehab-portal");
builder.Services.Configure<KeyManagementOptions>(o => o.XmlRepository = new ClavesMariaDb(ds));

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.Cookie.Name = "__Host-rehab";
        o.Cookie.HttpOnly = true;
        o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.LoginPath = "/entrar";
        o.AccessDeniedPath = "/entrar";
        o.Events.OnValidatePrincipal = Sesion.ValidarAsync;
    });

builder.Services.AddAuthorization(o =>
    o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.AddAntiforgery(o =>
{
    o.Cookie.Name = "__Host-af";
    o.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});

builder.Services.AddRazorComponents();

// Solo el envío del formulario de entrada: 10 por minuto por IP
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        HttpMethods.IsPost(ctx.Request.Method) && ctx.Request.Path.StartsWithSegments("/entrar")
            ? RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "?",
                _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1) })
            : RateLimitPartition.GetNoLimiter("libre"));
});

builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
    o.ForwardLimit = 1;
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "no-cache",   // siempre la versión actual del CSS
});
// Datos de pacientes: el navegador no los guarda en caché
app.Use((ctx, next) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    return next();
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Con contraseña temporal solo puede cambiarla o salir
app.Use(async (ctx, next) =>
{
    var ruta = ctx.Request.Path;
    if (ctx.User.HasClaim(Sesion.Temporal, "1")
        && !ruta.StartsWithSegments("/cambiar-clave") && !ruta.StartsWithSegments("/salir"))
    {
        ctx.Response.Redirect("/cambiar-clave");
        return;
    }
    await next();
});

app.UseAntiforgery();

app.MapGet("/salud", async (MySqlDataSource d) =>
{
    await using var cn = await d.OpenConnectionAsync();
    return Results.Ok("ok");
}).AllowAnonymous();

app.MapPost("/salir", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/entrar");
}).ExigirToken();

app.MapAccionesVisores();
app.MapAccionesSesiones();
app.MapAccionesPacientes();

// Si el navegador abre por GET la dirección de un formulario (Atrás o recargar), vuelve a la página.
app.MapGet("/visores/{**resto}", () => Results.Redirect("/visores"));
app.MapGet("/sesiones/{id:long}/asignar", (long id) => Results.Redirect($"/sesiones/{id}"));
app.MapGet("/sesiones/{id:long}/observacion/{**resto}", (long id) => Results.Redirect($"/sesiones/{id}"));
app.MapGet("/pacientes/{codigo}/eliminar", (string codigo) => Results.Redirect($"/pacientes/{codigo}"));
app.MapGet("/salir", () => Results.Redirect("/"));

app.MapRazorComponents<App>();

await app.RunAsync();
return 0;
