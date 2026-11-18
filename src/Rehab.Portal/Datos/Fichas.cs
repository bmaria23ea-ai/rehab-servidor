using Microsoft.AspNetCore.Http;
using MySqlConnector;

namespace Rehab.Portal.Datos;

// Exposición del primer día del paciente (fecha de Ecuador)
public sealed record Exposicion(decimal Segundos, DateOnly Dia, int Sesiones);

public sealed record Ficha(Exposicion Exposicion, string Respuesta, string? Otro, string Tolerancia, int[] Notas,
                           string Terapeuta, DateTime Actualizado)
{
    public int Subtotal(int dimension) => Notas.Skip(dimension * Fichas.PorDimension).Take(Fichas.PorDimension).Sum();
    public int Total => Notas.Sum();
    public string Nivel => Fichas.Nivel(Total);
}

// Ficha de retroalimentación del entorno de RV: una por paciente
public sealed class Fichas(MySqlDataSource ds)
{
    public static readonly string[] Dimensiones =
        ["Retroalimentación en tiempo real", "Recompensas visuales", "Motivación y compromiso", "Adherencia terapéutica"];

    public const int PorDimension = 4;

    // Texto de la ficha en papel, 4 por dimensión y en el mismo orden
    public static readonly string[] Indicadores =
    [
        "El sistema proporciona información inmediata sobre la ejecución del movimiento.",
        "El paciente identifica fácilmente los mensajes de corrección o guía durante la actividad.",
        "La retroalimentación favorece la corrección de errores durante la ejecución del ejercicio.",
        "Los mensajes emitidos son claros, comprensibles y adecuados al nivel del paciente.",
        "El entorno virtual otorga recompensas visuales al cumplir objetivos específicos.",
        "Las recompensas visuales son percibidas por el paciente como un reconocimiento a su desempeño.",
        "El sistema muestra indicadores de progreso (puntos, niveles, insignias o logros).",
        "Las recompensas visuales generan satisfacción y deseo de continuar con la actividad.",
        "El paciente mantiene la atención durante toda la actividad.",
        "El paciente demuestra interés por alcanzar los objetivos propuestos.",
        "El paciente participa activamente sin requerir constantes recordatorios del terapeuta.",
        "El paciente manifiesta satisfacción o entusiasmo durante la sesión.",
        "El paciente completa las tareas propuestas dentro del entorno virtual.",
        "El paciente intenta mejorar su rendimiento tras recibir retroalimentación.",
        "El paciente responde positivamente ante los estímulos visuales de recompensa.",
        "El paciente expresa disposición para participar en futuras sesiones.",
    ];

    public static readonly (string Clave, string Texto)[] Respuestas =
        [("normal", "Normal"), ("mareo", "Mareo"), ("desorientacion", "Desorientación"), ("otro", "Otro")];

    public static readonly (string Clave, string Texto)[] Tolerancias =
        [("buena", "Buena"), ("regular", "Regular"), ("mala", "Mala")];

    public static readonly (int Desde, int Hasta, string Texto)[] Niveles =
        [(64, 80, "Excelente"), (48, 63, "Adecuada"), (32, 47, "Moderada"), (16, 31, "Baja")];

    public const int MaxOtro = 60;

    public static string Nivel(int total) => Niveles.First(n => total >= n.Desde && total <= n.Hasta).Texto;

    public static string Texto((string Clave, string Texto)[] opciones, string clave) =>
        Array.Find(opciones, o => o.Clave == clave).Texto ?? clave;

    public async Task<Ficha?> BuscarAsync(string codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT f.exposicion_s, f.exposicion_dia, f.exposicion_sesiones, f.respuesta, f.respuesta_otro, f.tolerancia, " +
            "f.calificaciones, f.terapeuta, f.actualizado_utc FROM fichas_retroalimentacion f " +
            "JOIN pacientes p ON p.id = f.paciente_id WHERE p.codigo = @codigo", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await using var r = await cmd.ExecuteReaderAsync();
        if (!await r.ReadAsync()) return null;
        return new Ficha(
            new Exposicion(r.GetDecimal(0), DateOnly.FromDateTime(r.GetDateTime(1)), Convert.ToInt32(r.GetValue(2))),
            r.GetString(3), r.IsDBNull(4) ? null : r.GetString(4), r.GetString(5),
            r.GetString(6).Select(c => c - '0').ToArray(), r.GetString(7), r.GetDateTime(8));
    }

    // null si el paciente aún no tiene sesiones
    public async Task<Exposicion?> ExposicionAsync(string codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        return await ExposicionAsync(cn, null, codigo);
    }

    private static async Task<Exposicion?> ExposicionAsync(MySqlConnection cn, MySqlTransaction? tx, string codigo)
    {
        await using var cmd = new MySqlCommand(
            "SELECT s.inicio_utc, s.duracion_real_s FROM sesiones s JOIN pacientes p ON p.id = s.paciente_id " +
            "WHERE p.codigo = @codigo ORDER BY s.inicio_utc", cn, tx);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        await using var r = await cmd.ExecuteReaderAsync();

        DateOnly? primero = null;
        decimal suma = 0;
        int n = 0;
        while (await r.ReadAsync())
        {
            var dia = DateOnly.FromDateTime(Hora.Ecuador(r.GetDateTime(0)));
            primero ??= dia;
            if (dia != primero) break;
            suma += r.GetDecimal(1);
            n++;
        }
        return primero is { } d ? new Exposicion(suma, d, n) : null;
    }

    // false si falta un dato o el paciente no tiene sesiones
    public async Task<bool> GuardarAsync(string codigo, IFormCollection form, string terapeuta)
    {
        if (!Validar(form, out var respuesta, out var otro, out var tolerancia, out var notas)) return false;

        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();

        uint pacienteId;
        await using (var buscar = new MySqlCommand("SELECT id FROM pacientes WHERE codigo = @codigo FOR UPDATE", cn, tx))
        {
            buscar.Parameters.AddWithValue("@codigo", codigo);
            if (await buscar.ExecuteScalarAsync() is not { } id) return false;
            pacienteId = Convert.ToUInt32(id);
        }

        // Corregir no cambia la exposición; la primera vez se calcula con las sesiones del primer día
        await using var cmd = new MySqlCommand(
            "UPDATE fichas_retroalimentacion SET respuesta = @resp, respuesta_otro = @otro, tolerancia = @tol, " +
            "calificaciones = @notas, terapeuta = @t, actualizado_utc = UTC_TIMESTAMP(3) WHERE paciente_id = @p", cn, tx);
        cmd.Parameters.AddWithValue("@p", pacienteId);
        cmd.Parameters.AddWithValue("@resp", respuesta);
        cmd.Parameters.AddWithValue("@otro", otro);
        cmd.Parameters.AddWithValue("@tol", tolerancia);
        cmd.Parameters.AddWithValue("@notas", notas);
        cmd.Parameters.AddWithValue("@t", terapeuta);
        if (await cmd.ExecuteNonQueryAsync() == 0)
        {
            if (await ExposicionAsync(cn, tx, codigo) is not { } e) return false;
            cmd.CommandText =
                "INSERT INTO fichas_retroalimentacion (paciente_id, exposicion_s, exposicion_dia, exposicion_sesiones, " +
                "respuesta, respuesta_otro, tolerancia, calificaciones, terapeuta) " +
                "VALUES (@p, @s, @dia, @n, @resp, @otro, @tol, @notas, @t)";
            cmd.Parameters.AddWithValue("@s", e.Segundos);
            cmd.Parameters.AddWithValue("@dia", e.Dia.ToDateTime(TimeOnly.MinValue));
            cmd.Parameters.AddWithValue("@n", e.Sesiones);
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return true;
    }

    // Campos: respuesta, otro, tolerancia, n0..n15 (dimensión x 4 + indicador)
    private static bool Validar(IFormCollection form, out string respuesta, out string? otro,
                                out string tolerancia, out string notas)
    {
        respuesta = form["respuesta"].ToString();
        tolerancia = form["tolerancia"].ToString();
        otro = null;
        notas = "";

        string r = respuesta, t = tolerancia;
        if (!Respuestas.Any(o => o.Clave == r) || !Tolerancias.Any(o => o.Clave == t)) return false;

        if (respuesta == "otro")
        {
            otro = form["otro"].ToString().Trim();
            if (otro.Length is 0 or > MaxOtro || otro.Any(char.IsControl)) return false;
        }

        var c = new char[Indicadores.Length];
        for (int i = 0; i < c.Length; i++)
        {
            string v = form[$"n{i}"].ToString();
            if (v.Length != 1 || v[0] is < '1' or > '5') return false;
            c[i] = v[0];
        }
        notas = new string(c);
        return true;
    }

    public async Task<bool> BorrarAsync(string codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "DELETE f FROM fichas_retroalimentacion f JOIN pacientes p ON p.id = f.paciente_id WHERE p.codigo = @codigo", cn);
        cmd.Parameters.AddWithValue("@codigo", codigo);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
