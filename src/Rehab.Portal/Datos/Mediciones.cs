using System.Globalization;
using Microsoft.AspNetCore.Http;
using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed record Medicion(int? Fc, int? SpO2, decimal? Borg, int? PaSis, int? PaDia);

public sealed record Momento(string Clave, string Nombre, bool ConPa);

// Mediciones del terapeuta en la marcha: FC, SpO2, Borg disnea (0-10) y PA (reposo y final)
public sealed class Mediciones(MySqlDataSource ds)
{
    // Orden de la ficha (portal)
    public static readonly Momento[] Ficha =
    [
        new("reposo", "Reposo", true), new("final", "Final", true),
        new("min1", "Minuto 1", false), new("min3", "Minuto 3", false), new("min5", "Minuto 5", false),
    ];

    // Orden de tiempo (Excel)
    public static readonly Momento[] Cronologico = [Ficha[0], Ficha[2], Ficha[3], Ficha[4], Ficha[1]];

    public async Task<Dictionary<ulong, Dictionary<string, Medicion>>> DeSesionesAsync(IReadOnlyCollection<ulong> ids)
    {
        var d = new Dictionary<ulong, Dictionary<string, Medicion>>();
        if (ids.Count == 0) return d;

        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT sesion_id, momento, fc, spo2, borg, pa_sistolica, pa_diastolica FROM marcha_mediciones " +
            $"WHERE sesion_id IN ({string.Join(",", ids.Select((_, i) => "@s" + i))})", cn);
        int n = 0;
        foreach (var id in ids) cmd.Parameters.AddWithValue("@s" + n++, id);
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var porMomento)) d[id] = porMomento = [];
            porMomento[r.GetString(1)] = Leer(r, 2);
        }
        return d;
    }

    public static Medicion Leer(MySqlDataReader r, int i) => new(
        Entero(r, i), Entero(r, i + 1), r.IsDBNull(i + 2) ? null : r.GetDecimal(i + 2), Entero(r, i + 3), Entero(r, i + 4));

    private static int? Entero(MySqlDataReader r, int i) => r.IsDBNull(i) ? null : Convert.ToInt32(r.GetValue(i));

    // null si se guardó; si no, el momento con el valor fuera de rango
    public async Task<string?> GuardarAsync(ulong sesionId, IFormCollection form, string terapeuta)
    {
        var filas = new List<(Momento M, Medicion? V)>();
        foreach (var m in Ficha)
        {
            if (!Validar(m, form, out var v)) return m.Nombre;
            filas.Add((m, v));
        }

        await using var cn = await ds.OpenConnectionAsync();
        await using var tx = await cn.BeginTransactionAsync();
        await using (var existe = new MySqlCommand(
            "SELECT 1 FROM sesiones WHERE id = @id AND ejercicio = 'caminata_6m' FOR UPDATE", cn, tx))
        {
            existe.Parameters.AddWithValue("@id", sesionId);
            if (await existe.ExecuteScalarAsync() is null) return "sesión";
        }

        foreach (var (m, v) in filas)
        {
            await using var cmd = new MySqlCommand(v is null
                ? "DELETE FROM marcha_mediciones WHERE sesion_id = @id AND momento = @m"
                : "INSERT INTO marcha_mediciones (sesion_id, momento, fc, spo2, borg, pa_sistolica, pa_diastolica, terapeuta) " +
                  "VALUES (@id, @m, @fc, @spo2, @borg, @pas, @pad, @t) ON DUPLICATE KEY UPDATE fc = @fc, spo2 = @spo2, " +
                  "borg = @borg, pa_sistolica = @pas, pa_diastolica = @pad, terapeuta = @t, actualizado_utc = UTC_TIMESTAMP(3)", cn, tx);
            cmd.Parameters.AddWithValue("@id", sesionId);
            cmd.Parameters.AddWithValue("@m", m.Clave);
            if (v is not null)
            {
                cmd.Parameters.AddWithValue("@fc", v.Fc);
                cmd.Parameters.AddWithValue("@spo2", v.SpO2);
                cmd.Parameters.AddWithValue("@borg", v.Borg);
                cmd.Parameters.AddWithValue("@pas", v.PaSis);
                cmd.Parameters.AddWithValue("@pad", v.PaDia);
                cmd.Parameters.AddWithValue("@t", terapeuta);
            }
            await cmd.ExecuteNonQueryAsync();
        }
        await tx.CommitAsync();
        return null;
    }

    // Casillas vacías = null; todas vacías = sin fila
    private static bool Validar(Momento m, IFormCollection form, out Medicion? v)
    {
        v = null;
        if (!Num(form, m.Clave + "_fc", out var fc) || !Num(form, m.Clave + "_spo2", out var spo2) ||
            !Num(form, m.Clave + "_borg", out var borg) || !Num(form, m.Clave + "_pas", out var pas) ||
            !Num(form, m.Clave + "_pad", out var pad))
            return false;

        if (fc is not null && (fc < 20 || fc > 250 || fc % 1 != 0)) return false;
        if (spo2 is not null && (spo2 < 0 || spo2 > 100 || spo2 % 1 != 0)) return false;
        if (borg is not null && (borg < 0 || borg > 10 || borg * 2 % 1 != 0)) return false;
        if (!m.ConPa && (pas is not null || pad is not null)) return false;
        if ((pas is null) != (pad is null)) return false;
        if (pas is not null && (pas % 1 != 0 || pad % 1 != 0 || pas > 300 || pad < 0 || pas <= pad)) return false;

        if (fc is null && spo2 is null && borg is null && pas is null) return true;
        v = new Medicion((int?)fc, (int?)spo2, borg, (int?)pas, (int?)pad);
        return true;
    }

    // Acepta coma o punto decimal
    private static bool Num(IFormCollection form, string campo, out decimal? valor)
    {
        valor = null;
        string t = form[campo].ToString().Trim().Replace(',', '.');
        if (t.Length == 0) return true;
        if (t.Length > 6 || !decimal.TryParse(t, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var d))
            return false;
        valor = d;
        return true;
    }

    public static string Valor(decimal? v) => v?.ToString("0.#", CultureInfo.InvariantCulture) ?? "";
    public static string Valor(int? v) => v?.ToString(CultureInfo.InvariantCulture) ?? "";

    public async Task<bool> BorrarAsync(ulong sesionId)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand("DELETE FROM marcha_mediciones WHERE sesion_id = @id", cn);
        cmd.Parameters.AddWithValue("@id", sesionId);
        return await cmd.ExecuteNonQueryAsync() > 0;
    }
}
