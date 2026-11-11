using MySqlConnector;

namespace Rehab.Portal.Datos;

// Derecho de acceso y portabilidad (LOPDP): datos en Excel, un valor por casilla
public sealed class Exportacion(MySqlDataSource ds)
{
    private const int MinutosPrueba = 6;

    private const string Desde = "FROM sesiones s LEFT JOIN pacientes p ON p.id = s.paciente_id ";
    private const string Donde = "WHERE s.ejercicio = 'caminata_6m' AND (@codigo IS NULL OR p.codigo = @codigo) ";

    // codigo null = todos, incluidas las sesiones sin paciente
    public async Task<byte[]> CrearAsync(string? codigo)
    {
        await using var cn = await ds.OpenConnectionAsync();
        var minutos = await MinutosAsync(cn, codigo);
        int columnas = Math.Max(MinutosPrueba, minutos.Values.Select(m => m.Count).DefaultIfEmpty(0).Max());

        await using var cmd = new MySqlCommand(
            "SELECT s.id, p.codigo, s.sesion_uuid, s.inicio_utc, v.nombre, s.motivo_fin, s.duracion_real_s, " +
            "c.distancia_m, c.num_paradas, o.texto, c.tiempo_detenido_s " + Desde +
            "JOIN visores v ON v.id = s.visor_id " +
            "LEFT JOIN caminata_resultados c ON c.sesion_id = s.id " +
            "LEFT JOIN sesion_observaciones o ON o.sesion_id = s.id " + Donde +
            "ORDER BY p.codigo IS NULL, p.codigo, s.inicio_utc", cn);
        cmd.Parameters.AddWithValue("@codigo", (object?)codigo ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();

        var filas = new List<Celda[]>();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            DateTime local = Hora.Ecuador(r.GetDateTime(3));
            var fila = new List<Celda>
            {
                new(r.IsDBNull(1) ? "Sin paciente" : r.GetString(1)),
                new(Sesiones.CodigoCorto(r.GetValue(2))),
                new(local.Date, 2),
                new(local.TimeOfDay, 3),
                new(r.GetString(4)),
                new(Textos.Motivo(r.GetString(5))),
                new(Convert.ToDecimal(r.GetValue(6)) / 60m, 5),
                new(r.IsDBNull(7) ? null : (object)Convert.ToDecimal(r.GetValue(7)), 4),
                new(r.IsDBNull(8) ? null : (object)Convert.ToInt32(r.GetValue(8))),
                new(r.IsDBNull(10) ? null : (object)Convert.ToDecimal(r.GetValue(10)), 5),
            };
            var m = minutos.TryGetValue(id, out var lista) ? lista : [];
            for (int i = 0; i < columnas; i++) fila.Add(new(i < m.Count ? (object)m[i] : null, 4));
            fila.Add(new(r.IsDBNull(9) ? null : r.GetString(9), 6));
            filas.Add(fila.ToArray());
        }

        var titulos = new List<string> { "Código", "Sesión", "Fecha", "Hora", "Visor", "Cómo terminó",
                                         "Tiempo de exposición (min)", "Distancia (m)", "Veces que se detuvo", "Tiempo detenido (s)" };
        var anchos = new List<double> { 13, 11, 12, 8, 14, 18, 14, 12, 12, 12 };
        for (int i = 1; i <= columnas; i++) { titulos.Add($"Minuto {i} (m)"); anchos.Add(11); }
        titulos.Add("Observaciones");
        anchos.Add(50);

        return Excel.Crear([new Hoja("Marcha", titulos.ToArray(), anchos.ToArray(), filas)]);
    }

    private static async Task<Dictionary<ulong, List<decimal>>> MinutosAsync(MySqlConnection cn, string? codigo)
    {
        await using var cmd = new MySqlCommand(
            "SELECT m.sesion_id, m.distancia_m " +
            Desde + "JOIN caminata_minutos m ON m.sesion_id = s.id " + Donde +
            "ORDER BY m.sesion_id, m.minuto", cn);
        cmd.Parameters.AddWithValue("@codigo", (object?)codigo ?? DBNull.Value);
        await using var r = await cmd.ExecuteReaderAsync();
        var d = new Dictionary<ulong, List<decimal>>();
        while (await r.ReadAsync())
        {
            ulong id = Convert.ToUInt64(r.GetValue(0));
            if (!d.TryGetValue(id, out var lista)) d[id] = lista = [];
            lista.Add(Convert.ToDecimal(r.GetValue(1)));
        }
        return d;
    }
}
