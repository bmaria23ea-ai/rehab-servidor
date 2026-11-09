// Limitación de tasa por IP del cliente (la IP real llega de Nginx en X-Forwarded-For).
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Rehab.Api.Seguridad;

public static class Limites
{
    public const string Enrolamiento = "enrolamiento";

    public static IServiceCollection AddLimites(this IServiceCollection services) =>
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Enrolamiento: 5 intentos por minuto por IP (frena la adivinación de códigos)
            o.AddPolicy(Enrolamiento, ctx => RateLimitPartition.GetFixedWindowLimiter(
                ctx.Connection.RemoteIpAddress?.ToString() ?? "desconocida",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = 5,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                }));
        });
}
