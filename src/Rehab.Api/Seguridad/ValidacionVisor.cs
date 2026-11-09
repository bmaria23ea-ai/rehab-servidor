// En cada petición con JWT comprueba que el visor siga activo: la revocación es inmediata.
using Microsoft.AspNetCore.Authentication.JwtBearer;
using MySqlConnector;

namespace Rehab.Api.Seguridad;

public static class ValidacionVisor
{
    public static async Task ComprobarActivoAsync(TokenValidatedContext ctx)
    {
        string? idTexto = ctx.Principal?.FindFirst(EmisorJwt.ClaimVisor)?.Value;
        if (!ushort.TryParse(idTexto, out ushort id))
        {
            ctx.Fail("Token sin visor.");
            return;
        }

        var ds = ctx.HttpContext.RequestServices.GetRequiredService<MySqlDataSource>();
        await using var cn = await ds.OpenConnectionAsync(ctx.HttpContext.RequestAborted);
        await using var cmd = new MySqlCommand("SELECT 1 FROM visores WHERE id = @id AND estado = 'activo'", cn);
        cmd.Parameters.AddWithValue("@id", id);

        if (await cmd.ExecuteScalarAsync(ctx.HttpContext.RequestAborted) is null)
            ctx.Fail("Visor no activo.");
    }
}
