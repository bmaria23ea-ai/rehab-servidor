// Registro de acciones del portal. La tabla solo admite INSERT (triggers en la migración 10).
using System.Security.Claims;
using MySqlConnector;

namespace Rehab.Portal.Datos;

public sealed record AuditoriaFila(DateTime Fecha, string Terapeuta, string Accion, string? Objeto);

public sealed class Auditoria(MySqlDataSource ds)
{
    public Task RegistrarAsync(ClaimsPrincipal u, string accion, string? objeto = null) =>
        RegistrarAsync(u.Identity!.Name!, accion, objeto);

    public async Task RegistrarAsync(string terapeuta, string accion, string? objeto = null)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "INSERT INTO auditoria (terapeuta, accion, objeto) VALUES (@terapeuta, @accion, @objeto)", cn);
        cmd.Parameters.AddWithValue("@terapeuta", terapeuta);
        cmd.Parameters.AddWithValue("@accion", accion);
        cmd.Parameters.AddWithValue("@objeto", objeto);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<List<AuditoriaFila>> ListarAsync(int maximo = 200)
    {
        await using var cn = await ds.OpenConnectionAsync();
        await using var cmd = new MySqlCommand(
            "SELECT creado_utc, terapeuta, accion, objeto FROM auditoria ORDER BY id DESC LIMIT @max", cn);
        cmd.Parameters.AddWithValue("@max", maximo);
        await using var r = await cmd.ExecuteReaderAsync();
        var lista = new List<AuditoriaFila>();
        while (await r.ReadAsync())
            lista.Add(new(r.GetDateTime(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3)));
        return lista;
    }

    public static string Texto(string accion) => accion switch
    {
        "entrar" => "Entró al portal",
        "entrar_fallido" => "Contraseña incorrecta",
        "cambiar_clave" => "Cambió su contraseña",
        "registrar_paciente" => "Registró paciente",
        "exportar_paciente" => "Descargó datos de paciente",
        "exportar_todo" => "Descargó datos de todos los pacientes",
        "eliminar_paciente" => "Eliminó paciente y sus datos",
        "aprobar_gafas" => "Vinculó visor",
        "asignar_gafas" => "Asignó paciente a visor",
        "quitar_gafas" => "Liberó visor",
        "desactivar_gafas" => "Desvinculó visor",
        "asignar_sesion" => "Asignó sesión a paciente",
        "cambiar_paciente_sesion" => "Cambió paciente de sesión",
        "observacion_sesion" => "Escribió observación de sesión",
        "borrar_observacion" => "Borró observación de sesión",
        "mediciones_sesion" => "Registró mediciones de sesión",
        "borrar_mediciones" => "Borró mediciones de sesión",
        _ => accion,
    };
}
