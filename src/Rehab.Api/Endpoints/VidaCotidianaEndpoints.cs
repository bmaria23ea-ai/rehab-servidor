// POST /sesiones/vida-cotidiana: el visor envía una sesión del Lobby terminada (completa o incompleta).
using Microsoft.AspNetCore.Mvc;
using MySqlConnector;
using Rehab.Api.Seguridad;
using static Rehab.Api.Endpoints.SesionesEndpoints;

namespace Rehab.Api.Endpoints;

public static class VidaCotidianaEndpoints
{
    // Formato v1. Todo es anulable para detectar campos faltantes.
    public sealed record SesionVidaCotidianaV1(
        int? VersionEsquema, Guid? SesionId, string? Paciente, bool? EsPrueba, string? Ejercicio, string? Escena,
        string? VersionApp, DateTime? InicioUtc, DateTime? FinUtc, string? Estado, string? MotivoFin,
        VidaCotidianaV1? VidaCotidiana);

    public sealed record VidaCotidianaV1(decimal? SegundosActivos, LogroV1[]? Logros);

    public sealed record LogroV1(string? Codigo, int? Cantidad, decimal? PrimerSegundo);

    private static readonly HashSet<string> Ejercicios = ["vida_cotidiana"];

    public static readonly HashSet<string> Catalogo =
    [
        "libro_estante_medio", "libro_estante_alto", "libro_recogido_piso", "gato_acariciado", "pesa_levantada",
        "puerta_deslizada", "ventana_deslizada", "pelota_lanzada", "bocado_comido",
    ];

    private const int MaxBytes = 4 * 1024;
    private const double ToleranciaS = 5;

    public static void MapVidaCotidiana(this IEndpointRouteBuilder app)
    {
        app.MapPost("/sesiones/vida-cotidiana", RecibirAsync)
           .WithMetadata(new RequestSizeLimitAttribute(MaxBytes))   // 413 si se supera
           .RequireAuthorization()
           .RequireRateLimiting(Limites.Sesiones);
    }

    private static async Task<IResult> RecibirAsync(
        SesionVidaCotidianaV1 s, HttpContext http, MySqlDataSource ds, ILogger<Program> log, CancellationToken ct)
    {
        var errores = Validar(s, DateTime.UtcNow);
        if (errores.Count > 0)
            return Rechazada(log, http, errores);

        // visor siempre desde el JWT (OWASP API1)
        ushort visorId = ushort.Parse(http.User.FindFirst(EmisorJwt.ClaimVisor)!.Value);
        Guid sesionId = s.SesionId!.Value;
        var v = s.VidaCotidiana!;
        var logros = v.Logros!;

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
                return Rechazada(log, http, new Dictionary<string, string[]> { ["paciente"] = ["Paciente no registrado."] });
            pacienteId = Convert.ToInt32(lector.GetValue(0));
            pacienteDePrueba = lector.GetBoolean(1);
        }

        decimal duracion = Math.Round((decimal)(s.FinUtc!.Value - s.InicioUtc!.Value).TotalSeconds, 1);

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
            insertar.Parameters.AddWithValue("@dreal", duracion);
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
            "INSERT INTO vida_cotidiana_sesiones (sesion_id, segundos_activos) VALUES (@id, @activos)", cn, tx))
        {
            cabecera.Parameters.AddWithValue("@id", id);
            cabecera.Parameters.AddWithValue("@activos", Math.Round(v.SegundosActivos!.Value, 1));
            await cabecera.ExecuteNonQueryAsync(ct);
        }

        // Todos los logros en una sola sentencia (puede no haber ninguno)
        if (logros.Length > 0)
        {
            await using var filas = new MySqlCommand { Connection = cn, Transaction = tx };
            var valores = new List<string>(logros.Length);
            filas.Parameters.AddWithValue("@id", id);
            for (int i = 0; i < logros.Length; i++)
            {
                valores.Add($"(@id, @c{i}, @n{i}, @p{i})");
                filas.Parameters.AddWithValue($"@c{i}", logros[i].Codigo);
                filas.Parameters.AddWithValue($"@n{i}", logros[i].Cantidad);
                filas.Parameters.AddWithValue($"@p{i}", Math.Round(logros[i].PrimerSegundo!.Value, 1));
            }
            filas.CommandText = "INSERT INTO vida_cotidiana_logros (sesion_id, codigo, cantidad, primer_segundo) VALUES " +
                                string.Join(", ", valores);
            await filas.ExecuteNonQueryAsync(ct);
        }

        // Paciente en uso: renueva la asignación del visor, igual que los otros ejercicios
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
        log.LogInformation("Sesión de vida cotidiana {SesionId} registrada (visor {VisorId})", sesionId, visorId);

        return Results.Json(new RespuestaSesion(sesionId, "registrada"), statusCode: 201);
    }

    // Reglas de validación del formato v1. Devuelve campo -> mensajes.
    public static Dictionary<string, string[]> Validar(SesionVidaCotidianaV1 s, DateTime ahoraUtc)
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

        // Mismas reglas de fechas que los otros ejercicios
        double? duracion = null;
        bool fechasOk = true;
        if (s.InicioUtc is not { Kind: DateTimeKind.Utc }) { Error("inicioUtc", "Obligatoria, en UTC (terminada en Z)."); fechasOk = false; }
        if (s.FinUtc is not { Kind: DateTimeKind.Utc }) { Error("finUtc", "Obligatoria, en UTC (terminada en Z)."); fechasOk = false; }
        if (fechasOk)
        {
            var inicio = s.InicioUtc!.Value;
            var fin = s.FinUtc!.Value;
            if (fin < inicio) Error("finUtc", "No puede ser anterior al inicio.");
            else if ((fin - inicio).TotalHours > 2) Error("finUtc", "La sesión no puede durar más de 2 horas.");
            else duracion = (fin - inicio).TotalSeconds + ToleranciaS;
            if (fin > ahoraUtc.AddHours(24)) Error("finUtc", "Está en el futuro (revise el reloj del visor).");
            if (inicio < ahoraUtc.AddDays(-90)) Error("inicioUtc", "Tiene más de 90 días.");
        }

        // Salir al menú es el final normal; solo la pausa de la app deja la sesión incompleta
        if (s.Estado is not ("completa" or "incompleta")) Error("estado", "'completa' o 'incompleta'.");
        else if (s.Estado == "completa" && s.MotivoFin != "completada")
            Error("motivoFin", "'completa' solo con el motivo 'completada'.");
        else if (s.Estado == "incompleta" && s.MotivoFin != "app_pausada")
            Error("motivoFin", "'incompleta' solo con el motivo 'app_pausada'.");

        var v = s.VidaCotidiana;
        if (v is null) { Error("vidaCotidiana", "Obligatorio."); return e; }

        if (v.SegundosActivos is not (>= 0 and <= 7200)) Error("vidaCotidiana.segundosActivos", "Entre 0 y 7200 s.");
        else if (duracion is double d1 && (double)v.SegundosActivos > d1)
            Error("vidaCotidiana.segundosActivos", "Mayor que la duración de la sesión.");

        var lista = v.Logros;
        if (lista is null) { Error("vidaCotidiana.logros", "Obligatorio (puede ser una lista vacía)."); return e; }
        if (lista.Length > Catalogo.Count) { Error("vidaCotidiana.logros", $"Máximo {Catalogo.Count} logros."); return e; }

        var vistos = new HashSet<string>();
        for (int i = 0; i < lista.Length; i++)
        {
            var l = lista[i];
            string c = $"vidaCotidiana.logros[{i}]";
            if (l is null) { Error(c, "Obligatorio."); continue; }
            if (l.Codigo is null || !Catalogo.Contains(l.Codigo)) Error($"{c}.codigo", "Código no válido.");
            else if (!vistos.Add(l.Codigo)) Error($"{c}.codigo", "Código repetido.");
            if (l.Cantidad is not (>= 1 and <= 999)) Error($"{c}.cantidad", "Entre 1 y 999.");
            if (l.PrimerSegundo is not (>= 0 and <= 7200)) Error($"{c}.primerSegundo", "Entre 0 y 7200 s.");
            else if (duracion is double d2 && (double)l.PrimerSegundo > d2)
                Error($"{c}.primerSegundo", "Mayor que la duración de la sesión.");
        }

        return e;
    }
}
