using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TimePlanner.Core.Ai;
using TimePlanner.Dashboard.Models;
using TimePlanner.Dashboard.Security;

namespace TimePlanner.Dashboard.Controllers.Api
{
    //-----------------------------
    //api controller for organisation ai day compilation and status
    public sealed class AiController : ApiControllerBase
    {
        private readonly IOptions<AiServerOptions> _options;
        private readonly IHttpClientFactory _clientFactory;

        //-----------------------------
        //initializes ai controller with options and client factory
        public AiController(IOptions<AiServerOptions> options, IHttpClientFactory clientFactory)
        {
            _options = options;
            _clientFactory = clientFactory;
        }

        //-----------------------------
        //reports availability and model of organisation ai
        [AllowAnonymous]
        [HttpGet("status")]
        [ProducesResponseType(typeof(DashboardAiStatus), StatusCodes.Status200OK)]
        public ActionResult<DashboardAiStatus> GetStatus()
        {
            var options = _options.Value;
            var available = options.IsConfigured;
            var model = available ? options.Model : null;
            return Ok(new DashboardAiStatus(available, model));
        }

        //-----------------------------
        //compiles a day using the configured organisation ai model
        [HttpPost("build-day")]
        [EnableRateLimiting(SecurityExtensions.AiLimiter)]
        [ProducesResponseType(typeof(DayBuildResult), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public async Task<IActionResult> BuildDay([FromBody] DayBuildRequest request, CancellationToken ct)
        {
            var options = _options.Value;

            //refuse when organisation ai is not configured
            if (!options.IsConfigured)
            {
                return Problem(
                    title: "Your organisation hasn't set up AI yet.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }

            //caller must have a time tracking profile
            if (CurrentAppUserId == null)
            {
                return NoProfile();
            }

            //enforce request limits
            if (request == null ||
                request.Windows.Count > 40 ||
                request.Windows.Any(w => w.Transcript?.Length > 500) ||
                request.Projects.Count > 500 ||
                request.Activities.Count > 500)
            {
                return Problem(
                    title: "Request exceeds allowed size limits.",
                    statusCode: StatusCodes.Status400BadRequest);
            }

            var settings = new AiSettings
            {
                CompilerUrl = options.Url,
                CompilerModel = options.Model
            };

            var client = _clientFactory.CreateClient("ai");
            var compiler = new OpenAiCompatibleDayCompiler(client, settings, options.ApiKey, DayPromptBuilder.Default);

            //execute day compilation with upstream provider
            try
            {
                var result = await compiler.BuildAsync(request, ct);
                return Ok(result);
            }
            //map compilation failure to service unavailable
            catch (AiUnavailableException ex)
            {
                return Problem(title: ex.Message, statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        }
    }
}
//------------------------------EOF-----------------------------\\
