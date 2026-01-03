using eduHub.Application.DTOs.Public;
using eduHub.api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace eduHub.api.Controllers;

[ApiController]
[Route("api/public/contact")]
public class PublicContactController : ApiControllerBase
{
    private readonly IContactEmailSender _emailSender;
    private readonly ILogger<PublicContactController> _logger;

    public PublicContactController(
        IContactEmailSender emailSender,
        ILogger<PublicContactController> logger)
    {
        _emailSender = emailSender;
        _logger = logger;
    }

    [HttpPost]
    [AllowAnonymous]
    [EnableRateLimiting("public-contact")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Submit(
        [FromBody] PublicContactRequestDto dto,
        CancellationToken cancellationToken)
    {
        var result = await _emailSender.SendAsync(dto, cancellationToken);
        if (!result.Sent && !result.Skipped)
        {
            return ProblemResult(
                StatusCodes.Status502BadGateway,
                "ContactSendFailed",
                "Unable to send message",
                "Unable to deliver contact request right now.");
        }

        if (result.Skipped)
        {
            _logger.LogInformation("Public contact request accepted with email sending disabled.");
        }

        return Ok(new { success = true });
    }
}
