// API de sesiones de rehabilitación.
// Sin argumentos inicia la API. Con un comando "visor-..." ejecuta una tarea de administración y termina.
using Microsoft.AspNetCore.HttpOverrides;
using MySqlConnector;
using Rehab.Api.Admin;
using Rehab.Api.Endpoints;
using Rehab.Api.Seguridad;

bool esComandoAdmin = args.Length > 0 && args[0].StartsWith("visor-", StringComparison.Ordinal);

var builder = WebApplication.CreateBuilder(esComandoAdmin ? [] : args);

// Kestrel: peticiones pequeñas (una sesión pesa pocos KB) y sin cabecera "Server"
builder.WebHost.ConfigureKestrel(o =>
{
    o.Limits.MaxRequestBodySize = 16 * 1024;   // 16 KB
    o.AddServerHeader = false;
});

// Conexión a MariaDB: la contraseña se lee de un archivo (Docker secret), nunca del código
string archivoClave = builder.Configuration["DB_PASSWORD_FILE"] ?? "/run/secrets/db_app_password";
if (!File.Exists(archivoClave))
    throw new InvalidOperationException("No se encontró el archivo con la contraseña de la base de datos.");

var cadena = new MySqlConnectionStringBuilder
{
    Server = builder.Configuration["DB_HOST"] ?? "db",
    Database = builder.Configuration["DB_NAME"] ?? "rehab",
    UserID = builder.Configuration["DB_USER"] ?? "rehab_api",
    Password = File.ReadAllText(archivoClave).Trim(),
    ConnectionTimeout = 5,
    MaximumPoolSize = 20,                       // pocas conexiones: VPS pequeño
};
var dataSource = new MySqlDataSource(cadena.ConnectionString);

// Tareas de administración desde la terminal del servidor: no inician la API
if (esComandoAdmin)
    return await ComandosVisor.EjecutarAsync(args, dataSource);

builder.Services.AddSingleton(dataSource);

// IP real del cliente: la envía Nginx en X-Forwarded-For. Se confía en cualquier origen porque
// la API solo es alcanzable desde la red interna de Docker, donde el único cliente HTTP es Nginx.
builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    o.ForwardLimit = 1;
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

builder.Services.AddLimites();

// Errores: respuesta genérica, sin detalles internos
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.MapEnrolamiento();

// Salud: confirma que la API llega a la base. No revela detalles del error.
app.MapGet("/salud", async (MySqlDataSource ds, ILogger<Program> log, CancellationToken ct) =>
{
    try
    {
        await using var cn = await ds.OpenConnectionAsync(ct);
        await using var cmd = cn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        await cmd.ExecuteScalarAsync(ct);
        return Results.Ok(new { estado = "ok" });
    }
    catch (Exception ex)
    {
        log.LogError("Sin conexión con la base de datos ({Tipo})", ex.GetType().Name);
        return Results.Json(new { estado = "sin_base" }, statusCode: 503);
    }
});

await app.RunAsync();
return 0;
