// Limitación de tasa por IP del cliente (la IP real llega de Nginx en X-Forwarded-For).
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Rehab.Api.Seguridad;

public static class Limites
{
    public const string Enrolamiento = "enrolamiento";
    public const string Consulta = "consulta";
    public const string Token = "token";
    public const string Sesiones = "sesiones";

    public static IServiceCollection AddLimites(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Solicitud de vinculación: 5 por minuto por IP
            o.AddPolicy(Enrolamiento, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Consulta de vinculación: las gafas preguntan cada 5 s mientras esperan la aprobación
            o.AddPolicy(Consulta, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Token: 10 solicitudes por minuto por IP (un visor pide uno cada 15 minutos)
            o.AddPolicy(Token, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 10,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));

            // Sesiones: 30 envíos por minuto por IP (alcanza para vaciar la cola sin conexión)
            o.AddPolicy(Sesiones, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 30,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
}
