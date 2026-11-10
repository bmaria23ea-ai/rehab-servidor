// El middleware de antifalsificación solo marca la petición; los endpoints sin campos de formulario
// no la rechazan solos. Este filtro exige un token válido en todos los POST que lo usan.
using Microsoft.AspNetCore.Antiforgery;

namespace Rehab.Portal.Seguridad;

public static class Antifalsificacion
{
    public static TBuilder ExigirToken<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder
    {
        builder.WithMetadata(new RequireAntiforgeryTokenAttribute());
        builder.AddEndpointFilter(async (ctx, next) =>
            ctx.HttpContext.Features.Get<IAntiforgeryValidationFeature>() is { IsValid: true }
                ? await next(ctx)
                : Results.BadRequest());
        return builder;
    }
}
