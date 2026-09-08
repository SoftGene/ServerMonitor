using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServerMonitor.Infrastructure.Agents;

namespace ServerMonitor.Api.Auth;

/// <summary>
/// Требует заголовок <c>X-Service-Key</c>, совпадающий с настройкой <c>Api:ServiceKey</c>.
/// </summary>
/// <remarks>
/// Это защита для <b>серверного клиента</b> — веб-приложения, которое ходит в API от своего
/// имени. Ключ агента (<c>X-Api-Key</c>) намеренно другой заголовок: он отвечает на вопрос
/// «какая это машина», а служебный — «это наш веб». Один заголовок для двух смыслов заставил
/// бы обработчик гадать, а гадание в проверке доступа — плохая идея.
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
            // Незаданный секрет означает «никого не пускать», а не «пускать всех». Обратное
            // поведение — классический способ развернуть систему и молча оставить её открытой.
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
