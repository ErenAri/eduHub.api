using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace eduHub.api.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ObjectResult ProblemResult(int statusCode, string code, string title, string detail)
    {
        var factory = HttpContext.RequestServices.GetRequiredService<ProblemDetailsFactory>();
        var problem = factory.CreateProblemDetails(
            HttpContext,
            statusCode: statusCode,
            title: title,
            detail: detail,
            type: $"https://httpstatuses.com/{statusCode}");

        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = HttpContext.TraceIdentifier;

        var result = new ObjectResult(problem)
        {
            StatusCode = statusCode
        };
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    protected ObjectResult BadRequestProblem(string detail, string code = "ValidationError")
        => ProblemResult(StatusCodes.Status400BadRequest, code, "Invalid request", detail);

    protected ObjectResult UnauthorizedProblem(string detail = "Unauthorized.", string code = "Unauthorized")
        => ProblemResult(StatusCodes.Status401Unauthorized, code, "Unauthorized", detail);

    protected ObjectResult ForbiddenProblem(string detail = "Forbidden.", string code = "Forbidden")
        => ProblemResult(StatusCodes.Status403Forbidden, code, "Forbidden", detail);

    protected ObjectResult NotFoundProblem(string detail = "Not found.", string code = "NotFound")
        => ProblemResult(StatusCodes.Status404NotFound, code, "Not found", detail);

    protected ObjectResult ConflictProblem(string detail = "Conflict.", string code = "Conflict")
        => ProblemResult(StatusCodes.Status409Conflict, code, "Conflict", detail);
}
