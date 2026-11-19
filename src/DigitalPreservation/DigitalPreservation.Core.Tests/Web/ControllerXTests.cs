using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using Microsoft.AspNetCore.Mvc;

namespace DigitalPreservation.Core.Tests.Web;

/// <summary>
/// Pins ProblemFor/ProblemResult - the one place a controller (or a resource filter with no
/// controller instance to call an extension method on) builds a failure response with no
/// <see cref="DigitalPreservation.Common.Model.Results.Result"/> behind it, instead of hand-building
/// a ProblemDetails, calling the built-in Problem() helper, or returning a plain string
/// (issue #388 item 2.3). Goes through the same error-code-to-status mapping as
/// <see cref="ResultXTests"/> pins for ToProblemDetails, since ProblemFor is built on it.
/// </summary>
public class ControllerXTests
{
    [Fact]
    public void ProblemResult_Sets_StatusCode_To_Match_The_Body()
    {
        var result = ControllerX.ProblemResult(ErrorCodes.Conflict, "the deposit is locked");

        result.StatusCode.Should().Be(409);
        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Status.Should().Be(409);
        problem.Detail.Should().Be("the deposit is locked");
    }

    [Fact]
    public void ProblemResult_Uses_The_Given_Title_When_Supplied()
    {
        var result = ControllerX.ProblemResult(ErrorCodes.BadRequest, "x", title: "Unsupported source");

        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Unsupported source");
    }

    [Fact]
    public void ProblemResult_Falls_Back_To_The_Status_Text_Title_When_None_Is_Given()
    {
        var result = ControllerX.ProblemResult(ErrorCodes.NotFound, null);

        var problem = result.Value.Should().BeOfType<ProblemDetails>().Subject;
        problem.Title.Should().Be("Status 404");
    }
}
