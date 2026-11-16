// POST /sesiones/respiracion: el visor envía un ejercicio de respiración terminado (completo o incompleto).
// Una sesión = un ejercicio (apnea en sedestación). La duración la mide el visor con un reloj monótono.
using MySqlConnector;
using Rehab.Api.Seguridad;
using static Rehab.Api.Endpoints.SesionesEndpoints;

namespace Rehab.Api.Endpoints;

public static class RespiracionEndpoints
{
    // Formato v1 de la respiración. Todo es anulable para detectar campos faltantes.
    public sealed record SesionRespiracionV1(
        int? VersionEsquema, Guid? SesionId, string? Paciente, bool? EsPrueba, string? Ejercicio, string? Escena,
        string? VersionApp, DateTime? InicioUtc, DateTime? FinUtc, decimal? DuracionS, string? Estado, string? MotivoFin,
        RespiracionV1? Respiracion);

    // ResultadoSeries: repeticiones hechas en cada serie (la última puede estar a medias)
    public sealed record RespiracionV1(int? Series, int? RepsPorSerie, int? RetirosVisor, int[]? ResultadoSeries);

    private static readonly HashSet<string> Ejercicios = ["apnea_sedestacion"];

    // El ejercicio no se detiene al retirar el visor: ese caso solo suma a retirosVisor
    private static readonly HashSet<string> Motivos = ["completada", "salida_menu", "app_pausada", "error"];

    private const double ToleranciaDuracionS = 5;

    public static void MapRespiracion(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sesiones/respiracion", RecibirAsync)
           .RequireAuthorization()
           .RequireRateLimiting(Limites.Sesiones);
    }

    private static async Task<IResult> RecibirAsync(
        SesionRespiracionV1 s, HttpContext http, MySqlDataSource ds, ILogger<Program> log, CancellationToken ct)
    {
        var errores = Validar(s, DateTime.UtcNow);
        if (errores.Count > 0)
            return Results.ValidationProblem(errores, statusCode: 422, title: "Datos de sesión no válidos.");

        // visor siempre desde el JWT (OWASP API1)
        ushort visorId = ushort.Parse(http.User.FindFirst(EmisorJwt.ClaimVisor)!.Value);
        Guid sesionId = s.SesionId!.Value;
        var r = s.Respiracion!;
        var series = r.ResultadoSeries!;

        await using var cn = await ds.OpenConnectionAsync(ct);

        // Reenvío desde la cola del visor
        if (await ExisteAsync(cn, sesionId, ct))
            return Results.Ok(new RespuestaSesion(sesionId, "duplicada"));

        await using var tx = await cn.BeginTransactionAsync(ct);

        int? pacienteId = null;
        bool pacienteDePrueba = false;
        if (s.Paciente is not null)
        {
            await using var buscar = new MySqlCommand(
                "SELECT id, es_prueba FROM pacientes " +
                "WHERE codigo = @codigo AND (es_prueba OR consentimiento_utc IS NOT NULL)", cn, tx);
            buscar.Parameters.AddWithValue("@codigo", s.Paciente);
            await using var lector = await buscar.ExecuteReaderAsync(ct);
            if (!await lector.ReadAsync(ct))
                return Results.ValidationProblem(
                    new Dictionary<string, string[]> { ["paciente"] = ["Paciente no registrado."] },
                    statusCode: 422, title: "Datos de sesión no válidos.");
            pacienteId = Convert.ToInt32(lector.GetValue(0));
            pacienteDePrueba = lector.GetBoolean(1);
        }

        long id;
        try
        {
            await using var insertar = new MySqlCommand(
                "INSERT INTO sesiones (sesion_uuid, paciente_id, visor_id, ejercicio, escena, version_esquema, " +
                "version_app, es_prueba, inicio_utc, fin_utc, duracion_configurada_s, duracion_real_s, estado, motivo_fin) " +
                "VALUES (@uuid, @paciente, @visor, @ejercicio, @escena, @version, @app, @prueba, @inicio, @fin, " +
                "NULL, @dreal, @estado, @motivo)", cn, tx);
            insertar.Parameters.AddWithValue("@uuid", sesionId.ToString("D"));
            insertar.Parameters.AddWithValue("@paciente", pacienteId);
            insertar.Parameters.AddWithValue("@visor", visorId);
            insertar.Parameters.AddWithValue("@ejercicio", s.Ejercicio);
            insertar.Parameters.AddWithValue("@escena", s.Escena);
            insertar.Parameters.AddWithValue("@version", s.VersionEsquema);
            insertar.Parameters.AddWithValue("@app", s.VersionApp);
            insertar.Parameters.AddWithValue("@prueba", s.EsPrueba!.Value || pacienteDePrueba);
            insertar.Parameters.AddWithValue("@inicio", s.InicioUtc);
            insertar.Parameters.AddWithValue("@fin", s.FinUtc);
            insertar.Parameters.AddWithValue("@dreal", Math.Round(s.DuracionS!.Value, 1));
            insertar.Parameters.AddWithValue("@estado", s.Estado);
            insertar.Parameters.AddWithValue("@motivo", s.MotivoFin);
            await insertar.ExecuteNonQueryAsync(ct);
            id = insertar.LastInsertedId;
        }
        catch (MySqlException ex) when (ex.Number == 1062)
        {
            return Results.Ok(new RespuestaSesion(sesionId, "duplicada"));
        }

        await using (var cabecera = new MySqlCommand(
            "INSERT INTO respiracion_sesiones (sesion_id, series_config, reps_por_serie, retiros_visor) " +
            "VALUES (@id, @series, @reps, @retiros)", cn, tx))
        {
            cabecera.Parameters.AddWithValue("@id", id);
            cabecera.Parameters.AddWithValue("@series", r.Series);
            cabecera.Parameters.AddWithValue("@reps", r.RepsPorSerie);
            cabecera.Parameters.AddWithValue("@retiros", r.RetirosVisor);
            await cabecera.ExecuteNonQueryAsync(ct);
        }

        // Todas las series en una sola sentencia
        await using (var filas = new MySqlCommand { Connection = cn, Transaction = tx })
        {
            var valores = new List<string>(series.Length);
            filas.Parameters.AddWithValue("@id", id);
            for (int i = 0; i < series.Length; i++)
            {
                valores.Add($"(@id, {i + 1}, @r{i})");
                filas.Parameters.AddWithValue($"@r{i}", series[i]);
            }
            filas.CommandText = "INSERT INTO respiracion_series (sesion_id, serie, reps) VALUES " + string.Join(", ", valores);
            await filas.ExecuteNonQueryAsync(ct);
        }

        // Paciente en uso: renueva la asignación del visor, igual que la marcha y el gimnasio
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
        log.LogInformation("Sesión de respiración {SesionId} registrada (visor {VisorId})", sesionId, visorId);

        return Results.Json(new RespuestaSesion(sesionId, "registrada"), statusCode: 201);
    }

    // Reglas de validación del formato v1 de la respiración. Devuelve campo -> mensajes.
    public static Dictionary<string, string[]> Validar(SesionRespiracionV1 s, DateTime ahoraUtc)
    {
        var e = new Dictionary<string, string[]>();
        void Error(string campo, string mensaje) => e[campo] = [mensaje];

        if (s.VersionEsquema != 1) Error("versionEsquema", "Versión de esquema no soportada.");
        if (s.SesionId is null || s.SesionId == Guid.Empty) Error("sesionId", "Obligatorio.");
        if (s.Paciente is not null && !CodigoPaciente().IsMatch(s.Paciente))
            Error("paciente", "Formato P-000, o null si la sesión no tiene paciente asignado.");
        if (s.EsPrueba is null) Error("esPrueba", "Obligatorio.");
        if (s.Ejercicio is null || !Ejercicios.Contains(s.Ejercicio)) Error("ejercicio", "Ejercicio no válido.");
        if (s.Escena is null || !NombreEscena().IsMatch(s.Escena)) Error("escena", "Obligatoria (1 a 64 caracteres).");
        if (s.VersionApp is null || !VersionApp().IsMatch(s.VersionApp)) Error("versionApp", "Obligatoria.");

        // Mismas reglas de fechas que la marcha y el gimnasio
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

        // Duración medida en el visor: no puede superar el intervalo entre inicio y fin (más una tolerancia)
        if (s.DuracionS is not (>= 0 and <= 7200)) Error("duracionS", "Entre 0 y 7200 s.");
        else if (fechasOk && (double)s.DuracionS > (s.FinUtc!.Value - s.InicioUtc!.Value).TotalSeconds + ToleranciaDuracionS)
            Error("duracionS", "Mayor que el tiempo entre inicio y fin.");

        if (s.Estado is not ("completa" or "incompleta")) Error("estado", "'completa' o 'incompleta'.");
        if (s.MotivoFin is null || !Motivos.Contains(s.MotivoFin)) Error("motivoFin", "Motivo no válido.");
        if (s.Estado is not null && s.MotivoFin is not null && (s.Estado == "completa") != (s.MotivoFin == "completada"))
            Error("estado", "'completa' solo con el motivo 'completada'.");

        var r = s.Respiracion;
        if (r is null) { Error("respiracion", "Obligatorio."); return e; }

        if (r.Series is not (>= 1 and <= 10)) Error("respiracion.series", "Entre 1 y 10.");
        if (r.RepsPorSerie is not (>= 1 and <= 20)) Error("respiracion.repsPorSerie", "Entre 1 y 20.");
        if (r.RetirosVisor is not (>= 0 and <= 1000)) Error("respiracion.retirosVisor", "Entre 0 y 1000.");

        var lista = r.ResultadoSeries;
        if (lista is null) { Error("respiracion.resultadoSeries", "Obligatorio."); return e; }
        if (r.Series is int total)
        {
            // Completa: todas las series. Incompleta: de 1 a total (la última puede estar a medias)
            if (s.Estado == "completa" && lista.Length != total)
                Error("respiracion.resultadoSeries", $"Una sesión completa debe traer {total} series.");
            else if (lista.Length < 1 || lista.Length > total)
                Error("respiracion.resultadoSeries", $"Entre 1 y {total} series.");
        }
        if (r.RepsPorSerie is int maximo && lista.Any(x => x < 0 || x > maximo))
            Error("respiracion.resultadoSeries", $"Cada serie entre 0 y {maximo} repeticiones.");

        return e;
    }
}
