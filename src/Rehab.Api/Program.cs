// API de sesiones de rehabilitación. Por ahora solo comprueba la conexión con MariaDB.
using MySqlConnector;

var builder = WebApplication.CreateBuilder(args);

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
builder.Services.AddSingleton(new MySqlDataSource(cadena.ConnectionString));

// Errores: respuesta genérica, sin detalles internos
builder.Services.AddProblemDetails();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();

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

app.Run();
