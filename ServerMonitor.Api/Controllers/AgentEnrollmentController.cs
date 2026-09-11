using Microsoft.AspNetCore.Mvc;
using ServerMonitor.Api.Auth;
using ServerMonitor.Api.Dtos;

namespace ServerMonitor.Api.Controllers;

/// <summary>
/// Hands the enrollment token to the web app, so it can show an install command with the token
/// already in it.
/// </summary>
/// <remarks>
/// Behind the service key, like every read, so only the web app can ask; and the web app shows it
/// only to someone signed in. That is no wider than before: accounts have no roles, and anyone who
/// can sign in could already see and change everything else on the server.
/// <para>
/// The token lets a machine join the fleet and nothing more — it reads no data and changes no
/// settings. Rotating it is a matter of changing ENROLLMENT_TOKEN and restarting; machines that
/// have already registered work from their own keys and are unaffected.
/// </para>
/// </remarks>
[ApiController]
[Route("api/agents/enrollment")]
[RequireServiceKey]
public class AgentEnrollmentController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AgentEnrollmentController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpGet]
    public ActionResult<EnrollmentInfoDto> Get()
    {
        var token = _configuration["Agents:EnrollmentToken"];

        return Ok(new EnrollmentInfoDto
        {
            Token = string.IsNullOrWhiteSpace(token) ? null : token
        });
    }
}
