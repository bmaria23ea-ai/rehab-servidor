// POST /sesiones: el visor envía una sesión terminada (completa o incompleta).
// Valida cada campo, evita duplicados por sesionId y registra la hora de recepción del servidor.
// Si el visor no tenía paciente asignado, "paciente" llega en null y la sesión queda sin asignar.
using System.Text.RegularExpressions;
using MySqlConnector;
using Rehab.Api.Seguridad;

namespace Rehab.Api.Endpoints;

public static partial class SesionesEndpoints
{
    // Formato v1 enviado por el visor. Todo es anulable para detectar campos faltantes.
    public sealed record SesionV1(
        int? VersionEsquema, Guid? SesionId, string? Paciente, string? Ejercicio, string? Escena,
        string? VersionApp, bool? EsPrueba, DateTime? InicioUtc, DateTime? FinUtc,
        int? DuracionConfiguradaS, decimal? DuracionRealS, string? Estado, string? MotivoFin,
        CaminataV1? Caminata);

    public sealed record CaminataV1(
        decimal? DistanciaM, decimal? LargoPistaM, decimal? TiempoDetenidoS, int? NumParadas,
        decimal[]? DistanciaPorMinutoM);

    public sealed record RespuestaSesion(Guid SesionId, string Estado);

    private const int MinutosAsignacion = 30;

    private static readonly HashSet<string> Motivos =
        ["completada", "detenida", "salida_menu", "visor_retirado", "app_pausada", "error"];

    [GeneratedRegex(@"^P-[0-9]{3,6}$")]
    private static partial Regex CodigoPaciente();

    [GeneratedRegex(@"^[\p{L}\p{N} ._()-]{1,64}$")]
    private static partial Regex NombreEscena();

    [GeneratedRegex(@"^[0-9A-Za-z.+-]{1,20}$")]
    private static partial Regex VersionApp();

    public static void MapSesiones(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sesiones", RecibirAsync)
           .RequireAuthorization()
           .RequireRateLimiting(Limites.Sesiones);
    }

    private static async Task<IResult> RecibirAsync(
        SesionV1 s, HttpContext http, MySqlDataSource ds, ILogger<Program> log, CancellationToken ct)
    {
        var errores = Validar(s, DateTime.UtcNow);
        if (errores.Count > 0)
            return Results.ValidationProblem(errores, statusCode: 422, title: "Datos de sesión no válidos.");

        // A partir de aquí la validación garantiza que los campos obligatorios tienen valor
        ushort visorId = ushort.Parse(http.User.FindFirst(EmisorJwt.ClaimVisor)!.Value);
        Guid sesionId = s.SesionId!.Value;
        var c = s.Caminata!;

        await using var cn = await ds.OpenConnectionAsync(ct);

        // Reenvío desde la cola del visor: si ya existe, no se guarda de nuevo
        if (await ExisteAsync(cn, sesionId, ct))
            return Results.Ok(new RespuestaSesion(sesionId, "duplicada"));

        await using var tx = await cn.BeginTransactionAsync(ct);

        // Paciente registrado (de prueba, o real con consentimiento). Sin paciente: queda "sin asignar".
        int? pacienteId = null;
        bool pacienteDePrueba = false;
        if (s.Paciente is not null)
        {
            await using var buscar = new MySqlCommand(
                "SELECT id, es_prueba FROM pacientes " +
                "WHERE codigo = @codigo AND (es_prueba OR consentimiento_utc IS NOT NULL)", cn, tx);
            buscar.Parameters.AddWithValue("@codigo", s.Paciente);
            await using var r = await buscar.ExecuteReaderAsync(ct);
            if (!await r.ReadAsync(ct))
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["paciente"] = ["Paciente no registrado."] },
                    statusCode: 422, title: "Datos de sesión no válidos.");
            pacienteId = Convert.ToInt32(r.GetValue(0));
            pacienteDePrueba = r.GetBoolean(1);
        }

        bool esPrueba = s.EsPrueba!.Value || pacienteDePrueba;

        long id;
        try
        {
            await using var insertar = new MySqlCommand(
                "INSERT INTO sesiones (sesion_uuid, paciente_id, visor_id, ejercicio, escena, version_esquema, " +
                "version_app, es_prueba, inicio_utc, fin_utc, duracion_configurada_s, duracion_real_s, estado, motivo_fin) " +
                "VALUES (@uuid, @paciente, @visor, @ejercicio, @escena, @version, @app, @prueba, @inicio, @fin, " +
                "@dconf, @dreal, @estado, @motivo)", cn, tx);
            insertar.Parameters.AddWithValue("@uuid", sesionId.ToString("D"));
            insertar.Parameters.AddWithValue("@paciente", pacienteId is int pid ? pid : DBNull.Value);
            insertar.Parameters.AddWithValue("@visor", visorId);
            insertar.Parameters.AddWithValue("@ejercicio", s.Ejercicio);
            insertar.Parameters.AddWithValue("@escena", s.Escena);
            insertar.Parameters.AddWithValue("@version", s.VersionEsquema);
            insertar.Parameters.AddWithValue("@app", s.VersionApp);
            insertar.Parameters.AddWithValue("@prueba", esPrueba);
            insertar.Parameters.AddWithValue("@inicio", s.InicioUtc);
            insertar.Parameters.AddWithValue("@fin", s.FinUtc);
            insertar.Parameters.AddWithValue("@dconf", s.DuracionConfiguradaS);
            insertar.Parameters.AddWithValue("@dreal", Math.Round(s.DuracionRealS!.Value, 1));
            insertar.Parameters.AddWithValue("@estado", s.Estado);
            insertar.Parameters.AddWithValue("@motivo", s.MotivoFin);
            await insertar.ExecuteNonQueryAsync(ct);
            id = insertar.LastInsertedId;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            // Dos envíos simultáneos de la misma sesión: el otro ya la guardó
            return Results.Ok(new RespuestaSesion(sesionId, "duplicada"));
        }

        await using (var resultado = new MySqlCommand(
            "INSERT INTO caminata_resultados (sesion_id, distancia_m, largo_pista_m, tiempo_detenido_s, num_paradas) " +
            "VALUES (@id, @dist, @pista, @detenido, @paradas)", cn, tx))
        {
            resultado.Parameters.AddWithValue("@id", id);
            resultado.Parameters.AddWithValue("@dist", Math.Round(c.DistanciaM!.Value, 2));
            resultado.Parameters.AddWithValue("@pista", c.LargoPistaM is { } p ? Math.Round(p, 2) : DBNull.Value);
            resultado.Parameters.AddWithValue("@detenido", Math.Round(c.TiempoDetenidoS!.Value, 1));
            resultado.Parameters.AddWithValue("@paradas", c.NumParadas);
            await resultado.ExecuteNonQueryAsync(ct);
        }

        var minutos = c.DistanciaPorMinutoM ?? [];
        for (int i = 0; i < minutos.Length; i++)
        {
            await using var minuto = new MySqlCommand(
                "INSERT INTO caminata_minutos (sesion_id, minuto, distancia_m) VALUES (@id, @minuto, @dist)", cn, tx);
            minuto.Parameters.AddWithValue("@id", id);
            minuto.Parameters.AddWithValue("@minuto", i + 1);
            minuto.Parameters.AddWithValue("@dist", Math.Round(minutos[i], 2));
            await minuto.ExecuteNonQueryAsync(ct);
        }

        // Paciente en uso: renueva la asignación del visor 30 min desde el fin de la sesión
        if (pacienteId is int asignado)
        {
            await using var renovar = new MySqlCommand(
                "UPDATE asignaciones SET expira_utc = GREATEST(expira_utc, " +
                "LEAST(@fin, UTC_TIMESTAMP(3)) + INTERVAL @minutos MINUTE) " +
                "WHERE visor_id = @visor AND paciente_id = @paciente AND expira_utc > UTC_TIMESTAMP(3)", cn, tx);
            renovar.Parameters.AddWithValue("@fin", s.FinUtc);
            renovar.Parameters.AddWithValue("@minutos", MinutosAsignacion);
            renovar.Parameters.AddWithValue("@visor", visorId);
            renovar.Parameters.AddWithValue("@paciente", asignado);
            await renovar.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
        log.LogInformation("Sesión {SesionId} registrada (visor {VisorId})", sesionId, visorId);

        return Results.Json(new RespuestaSesion(sesionId, "registrada"), statusCode: 201);
    }

    private static async Task<bool> ExisteAsync(MySqlConnection cn, Guid sesionId, CancellationToken ct)
    {
        await using var cmd = new MySqlCommand("SELECT 1 FROM sesiones WHERE sesion_uuid = @uuid", cn);
        cmd.Parameters.AddWithValue("@uuid", sesionId.ToString("D"));
        return await cmd.ExecuteScalarAsync(ct) is not null;
    }

    // Reglas de validación del formato v1. Devuelve campo -> mensajes.
    public static Dictionary<string, string[]> Validar(SesionV1 s, DateTime ahoraUtc)
    {
        var e = new Dictionary<string, string[]>();
        void Error(string campo, string mensaje) => e[campo] = [mensaje];

        if (s.VersionEsquema != 1) Error("versionEsquema", "Versión de esquema no soportada.");
        if (s.SesionId is null || s.SesionId == Guid.Empty) Error("sesionId", "Obligatorio.");
        if (s.Paciente is not null && !CodigoPaciente().IsMatch(s.Paciente))
            Error("paciente", "Formato P-000, o null si la sesión no tiene paciente asignado.");
        if (s.Ejercicio != "caminata_6m") Error("ejercicio", "Solo se admite 'caminata_6m'.");
        if (s.Escena is null || !NombreEscena().IsMatch(s.Escena)) Error("escena", "Obligatoria (1 a 64 caracteres).");
        if (s.VersionApp is null || !VersionApp().IsMatch(s.VersionApp)) Error("versionApp", "Obligatoria.");
        if (s.EsPrueba is null) Error("esPrueba", "Obligatorio.");

        // Fechas en UTC (con 'Z'), en orden y dentro de un rango razonable
        bool fechasOk = true;
        if (s.InicioUtc is not { Kind: DateTimeKind.Utc }) { Error("inicioUtc", "Obligatoria, en UTC (terminada en Z)."); fechasOk = false; }
        if (s.FinUtc is not { Kind: DateTimeKind.Utc }) { Error("finUtc", "Obligatoria, en UTC (terminada en Z)."); fechasOk = false; }
        if (fechasOk)
        {
            var inicio = s.InicioUtc!.Value;
            var fin = s.FinUtc!.Value;
            if (fin < inicio) Error("finUtc", "No puede ser anterior al inicio.");
            else if ((fin - inicio).TotalHours > 2) Error("finUtc", "La sesión no puede durar más de 2 horas.");
            if (fin > ahoraUtc.AddHours(24)) Error("finUtc", "Está en el futuro (revise el reloj del visor).");
            if (inicio < ahoraUtc.AddDays(-90)) Error("inicioUtc", "Tiene más de 90 días.");
        }

        // Duraciones
        if (s.DuracionConfiguradaS is not (>= 30 and <= 1800)) Error("duracionConfiguradaS", "Entre 30 y 1800 s.");
        else if (s.DuracionRealS is null || s.DuracionRealS < 0 || s.DuracionRealS > s.DuracionConfiguradaS + 5)
            Error("duracionRealS", "Entre 0 y la duración configurada.");
        else if (fechasOk && (double)s.DuracionRealS > (s.FinUtc!.Value - s.InicioUtc!.Value).TotalSeconds + 5)
            Error("duracionRealS", "Mayor que el tiempo entre inicio y fin.");

        // Estado y motivo coherentes
        if (s.Estado is not ("completa" or "incompleta")) Error("estado", "'completa' o 'incompleta'.");
        if (s.MotivoFin is null || !Motivos.Contains(s.MotivoFin)) Error("motivoFin", "Motivo no válido.");
        if (s.Estado is not null && s.MotivoFin is not null && (s.Estado == "completa") != (s.MotivoFin == "completada"))
            Error("estado", "'completa' solo con el motivo 'completada'.");
        if (s.Estado == "completa" && s.DuracionConfiguradaS is int dc && s.DuracionRealS is decimal dr && dr < dc - 5)
            Error("duracionRealS", "Una sesión completa debe durar lo configurado.");

        // Datos de la caminata
        var c = s.Caminata;
        if (c is null) { Error("caminata", "Obligatorio."); return e; }

        if (c.DistanciaM is not (>= 0 and <= 2000)) Error("caminata.distanciaM", "Entre 0 y 2000 m.");
        if (c.LargoPistaM is not null and not (>= 0.5m and <= 100)) Error("caminata.largoPistaM", "Entre 0.5 y 100 m.");
        if (c.TiempoDetenidoS is null || c.TiempoDetenidoS < 0 || (s.DuracionRealS is decimal real && c.TiempoDetenidoS > real + 1))
            Error("caminata.tiempoDetenidoS", "Entre 0 y la duración real.");
        if (c.NumParadas is not (>= 0 and <= 1000)) Error("caminata.numParadas", "Entre 0 y 1000.");

        var m = c.DistanciaPorMinutoM;
        int maxMinutos = s.DuracionConfiguradaS is int conf ? (conf + 59) / 60 : 30;
        if (m is null) Error("caminata.distanciaPorMinutoM", "Obligatorio (puede ser una lista vacía).");
        else if (m.Length > maxMinutos) Error("caminata.distanciaPorMinutoM", $"Máximo {maxMinutos} valores.");
        else if (m.Any(v => v < 0 || v > 400)) Error("caminata.distanciaPorMinutoM", "Cada valor entre 0 y 400 m.");
        else if (m.Length > 0 && c.DistanciaM is decimal total && Math.Abs(m.Sum() - total) > 1)
            Error("caminata.distanciaPorMinutoM", "La suma no coincide con la distancia total.");

        return e;
    }
}
