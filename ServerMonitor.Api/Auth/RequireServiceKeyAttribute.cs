using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServerMonitor.Infrastructure.Agents;

namespace ServerMonitor.Api.Auth;

/// <summary>
/// Requires an <c>X-Service-Key</c> header matching the <c>Api:ServiceKey</c> setting.
/// </summary>
/// <remarks>
/// This protects the API from everyone except its <b>server-side client</b> — the web app,
/// which calls the API on its own behalf. The agent key (<c>X-Api-Key</c>) is deliberately a
/// different header: it answers "which machine is this", while the service key answers "is
/// this our web app". One header for two meanings would make the handler guess, and guessing
/// in an access check is a bad idea.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class RequireServiceKeyAttribute : Attribute, IAuthorizationFilter
{
    public const string HeaderName = "X-Service-Key";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var services = context.HttpContext.RequestServices;
        var configuration = services.GetRequiredService<IConfiguration>();
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("ServiceKey");

        var expected = configuration["Api:ServiceKey"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            // An unset secret means "let nobody in", not "let everybody in". The opposite
            // behaviour is the classic way to deploy a system and quietly leave it open.
            logger.LogError("Api:ServiceKey is not configured; refusing every request that needs it.");

            context.Result = new ObjectResult("Service key is not configured on the server.")
            {
                StatusCode = StatusCodes.Status503ServiceUnavailable
            };

            return;
        }

        var provided = context.HttpContext.Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(provided) || !ApiKeyGenerator.FixedTimeEquals(provided, expected))
        {
            logger.LogWarning(
                "Rejected a request to {Path} with a missing or invalid service key.",
                context.HttpContext.Request.Path);

            context.Result = new UnauthorizedResult();
        }
    }
}
