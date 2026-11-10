// Portal de terapeutas. Con un comando "terapeuta-..." ejecuta la tarea y termina.
using MySqlConnector;
using Rehab.Portal.Admin;

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

builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);
builder.Services.AddSingleton(ds);

var app = builder.Build();

app.MapGet("/salud", async (MySqlDataSource d) =>
{
    await using var cn = await d.OpenConnectionAsync();
    return Results.Ok("ok");
});

await app.RunAsync();
return 0;
