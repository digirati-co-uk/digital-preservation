using System.Net;
using System.Xml.Linq;
using DigitalPreservation.Common.Model;
using DigitalPreservation.Core.Web;
using DigitalPreservation.Core.Web.Headers;
using DigitalPreservation.Utils;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Preservation.API.Features.Repository.Requests;
using DigitalPreservation.Mets;

namespace Preservation.API.Features.Repository;


[Route(PreservedResource.BasePathElement + "/{*path}")]
[ApiController]
public class RepositoryController(IMediator mediator, IMetsParser metsParser) : ControllerBase
{
    /// <summary>
    /// The name every METS this platform writes is given, and the one <see cref="TryStreamConventionalMets"/>
    /// looks for. Anything else still resolves, just by the longer route.
    /// </summary>
    private const string ConventionalMetsName = "mets.xml";

    [HttpGet(Name = "Browse")]
    [ProducesResponseType<Container>(200, "application/json")]
    [ProducesResponseType<Binary>(200, "application/json")]
    [ProducesResponseType<ArchivalGroup>(200, "application/json")]
    [ProducesResponseType<MetsFileWrapper>(200, "application/json")]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(410)]
    public async Task<IActionResult> Browse(
        [FromRoute] string? path = null,
        [FromQuery] string? view = null,
        [FromQuery] string? version = null)
    {
        if (IsRepositoryRootRequest())
        {
            return NoResourcePathProblem();
        }

        if (version.HasText() && view != ViewValues.Lightweight)
        {
            var problem = new ProblemDetails
            {
                Status = (int)HttpStatusCode.BadRequest,
                Title = "Version only supported on lightweight view",
                Detail = "View " + (view ?? "[empty]") + " is not a valid value when a version is requested."
            };
            return BadRequest(problem);
        }
        
        if (view == ViewValues.Lightweight)
        {
            var lwResult = await mediator.Send(new GetLightweightResource(path!, version));
            return this.StatusResponseFromResult(lwResult);
        }
        
        if (view == ViewValues.Mets)
        {
            var streamed = await TryStreamConventionalMets(Request.Path);
            if (streamed is not null)
            {
                return streamed;
            }
        }

        var result = await mediator.Send(new GetResource(Request.Path));
        if ((view is ViewValues.Mets or ViewValues.ParsedMets) && result is { Success: true, Value: ArchivalGroup archivalGroup })
        {
            return await GetMetsResult(archivalGroup, view);
        }

        // default 99.999% scenario:
        return this.StatusResponseFromResult(result);
    }

    /// <summary>
    /// Stream the METS without materialising the Archival Group first, for the common case where it
    /// is called <see cref="ConventionalMetsName"/> and sits at the root of the group.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The route below asks Storage API for the whole Archival Group, which means one Fedora request
    /// per container in the tree plus the OCFL inventory from S3 - and then caches the materialised
    /// group for an hour. All of that work exists here only to learn the name of a single binary. A
    /// reader that walks the whole repository, as the METS ID migration survey does, pays it once per
    /// Archival Group and leaves a copy of every one of them in the Storage API's memory.
    /// </para>
    /// <para>
    /// So ask whether the resource really is an Archival Group - one HEAD, and the guard that stops
    /// this changing what any other kind of resource returns - and then go straight for the binary.
    /// A miss costs one further HEAD and falls back to the full route, which finds a METS under any
    /// name and anywhere in the group; that is what a BagIt layout, with its METS at data/mets.xml,
    /// will do.
    /// </para>
    /// <para>
    /// Only <see cref="ViewValues.Mets"/> comes this way. <see cref="ViewValues.ParsedMets"/> needs
    /// the binary's canonical Id to build its wrapper, and that is not known until the group is read.
    /// </para>
    /// </remarks>
    /// <returns>The response, or null meaning "fall back to the full route".</returns>
    private async Task<IActionResult?> TryStreamConventionalMets(string requestPath)
    {
        if (!CanSafelyAppendTo(requestPath))
        {
            return null;
        }

        var typeResult = await mediator.Send(new GetResourceType(requestPath));
        if (typeResult is not { Success: true, Value: nameof(ArchivalGroup) })
        {
            return null;
        }

        var metsPath = $"{requestPath.TrimEnd('/')}/{ConventionalMetsName}";
        var streamResult = await mediator.Send(new GetBinaryStream(metsPath));
        if (streamResult is { Success: true, Value: not null })
        {
            return new FileStreamResult(streamResult.Value, "application/xml");
        }

        return null;
    }

    /// <summary>
    /// Whether a request path can have a file name appended to it and still mean what it says.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This method builds a second path by appending to the request path, which is worth being
    /// careful about, because Preservation API deliberately offers no general route to a preserved
    /// binary's bytes. What keeps this from becoming one is that the caller supplies only the
    /// container: the file name is a constant, the resource has to be an Archival Group, and the
    /// result is therefore a strict subset of what the route below would have streamed anyway.
    /// </para>
    /// <para>
    /// The other half is that the caller cannot smuggle a separator, a query or a fragment into that
    /// container path. It cannot: converting <c>Request.Path</c> to a string calls
    /// <c>PathString.ToUriComponent</c>, which percent-encodes - a <c>#</c> in a name reaches us as
    /// <c>%23</c>, so it stays part of the path rather than truncating it. That is the same encoded
    /// form the route below gets from the binary's own Id, which is why the two agree.
    /// <c>Encoding_Is_What_Keeps_The_Appended_Name_Part_Of_The_Path</c> pins that behaviour, because
    /// switching this to <c>Request.Path.Value</c> would quietly undo it.
    /// </para>
    /// <para>
    /// Dot segments are the exception, being unreserved and so never escaped. Kestrel resolves them
    /// before routing and one should never arrive; declining the shortcut when one does costs a
    /// single comparison and means none of the above rests on that being true.
    /// </para>
    /// </remarks>
    private static bool CanSafelyAppendTo(string requestPath) =>
        !requestPath.Split('/').Contains("..");

    /// <summary>
    /// The deliberate answer to the repository root having no path to resolve a resource for - HEAD
    /// or GET on /repository or /repository/ - instead of either an accidental validation artifact
    /// (HeadResource, pre-#386) or an unhandled exception downstream of a null path (Browse,
    /// pre-#386). The root isn't itself a Container, Binary or ArchivalGroup, so there is no type to
    /// report and no resource to redirect a browser to; 400 says that plainly, in the same
    /// ProblemDetails shape every other 400 in this controller uses.
    /// </summary>
    private ObjectResult NoResourcePathProblem() =>
        BadRequest(new ProblemDetails
        {
            Status = (int)HttpStatusCode.BadRequest,
            Title = "No resource path given",
            Detail = "A path under /repository is required; the repository root itself is not a resource."
        });

    /// <summary>
    /// Whether this request's full path is the repository root itself - "/repository" or
    /// "/repository/", nothing after - checked against Request.Path, the same full-path source the
    /// rest of this controller already treats as authoritative (see HeadResource's own comment on
    /// why), rather than the route-bound path parameter. A direct call bypassing routing, as most
    /// of this controller's own unit tests do, never populates that parameter at all - checking it
    /// would misread every one of those as a root request too.
    /// </summary>
    private bool IsRepositoryRootRequest() =>
        (Request.Path.Value?.TrimEnd('/') ?? string.Empty)
            .Equals($"/{PreservedResource.BasePathElement}", StringComparison.Ordinal);

    private async Task<IActionResult> GetMetsResult(ArchivalGroup archivalGroup, string view)
    {            
        var mets = archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, true));
        if (mets is null)
        {
            mets = archivalGroup.Binaries.SingleOrDefault(b => MetsUtils.IsMetsFile(b.Id!.GetSlug()!, false));
        }

        if (mets is null)
        {
            return NotFound();
        }
        
        // We need to stream the METS view from storage
        var streamResult = await mediator.Send(new GetBinaryStream(mets.Id!.AbsolutePath));
        if (streamResult is { Success: true, Value: not null })
        {
            // there is a METS file there to be read
            if (view == ViewValues.Mets)
            {
                return new FileStreamResult(streamResult.Value, "application/xml");
            }

            var xMets = XDocument.Load(streamResult.Value);
            var parsedMetsResult = metsParser.GetMetsFileWrapperFromXDocument(mets.Id, xMets);
            if (parsedMetsResult.Success)
            {
                parsedMetsResult.Value!.XDocument = null;
                return Ok(parsedMetsResult.Value);
            }

            return this.StatusResponseFromResult(parsedMetsResult);
        }

        return this.StatusResponseFromResult(streamResult);
    }


    [HttpHead(Name = "HeadResource")]
    [ProducesResponseType(200)]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    [ProducesResponseType(404)]
    [ProducesResponseType(401)]
    [ProducesResponseType(410)]
    public async Task<IActionResult?> HeadResource([FromRoute] string? path = null)
    {
        // path was declared non-nullable with no default, so HEAD /repository or /repository/ (the
        // {*path} catch-all capturing nothing) left [ApiController]'s automatic model validation to
        // reject it before this method ever ran, with FluentValidation's generic "field is required"
        // shape rather than a deliberate answer (issue #386). Giving path a default, like Browse
        // already has, moves that decision in here instead - checked against Request.Path like
        // Browse's own guard, not this now-unused path parameter (see IsRepositoryRootRequest).
        if (IsRepositoryRootRequest())
        {
            return NoResourcePathProblem();
        }

        // GetResourceType needs the full /repository/... path (CommonApiBase.GetResourceType HEADs
        // it as-is against the Storage API), not the route-bound path parameter, which the
        // {*path} catch-all strips down to everything after "repository/" - that stripped form
        // 404s every request against the Storage API, including ones that exist (issue #348).
        // Request.Path is the same full-path source Browse already uses for this (via
        // TryStreamConventionalMets and GetResource).
        var result = await mediator.Send(new GetResourceType(Request.Path));
        if (result.Success)
        {
            // A tombstone never reaches here as a success: CommonApiBase.GetResourceType reports
            // it as a failure with ErrorCode Tombstone, which the else branch below turns into
            // 410 via ResultX.ToProblemDetails (issue #348).
            Response.Headers[HttpHeaders.XPreservationResourceType] = result.Value;
        }
        else
        {
            Response.StatusCode = result.ToProblemDetails().Status ?? 500;
        }
        return new EmptyResult();
    }
    
    
    [HttpPut(Name = "CreateContainer")]
    [ProducesResponseType<Container>(201, "application/json")]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    [ProducesResponseType<ProblemDetails>(409, "application/json")]
    [ProducesResponseType<ProblemDetails>(400, "application/json")]
    public async Task<ActionResult> CreateContainer([FromRoute] string? path = null, [FromBody] Container? container = null)
    {
        string? name = null;
        if (container != null)
        {
            name = container.Name;
        }
        var result = await mediator.Send(new CreateContainer(Request.Path, name));
        var createdLocation = result.Success ? result.Value!.Id : null;
        return this.StatusResponseFromResult(result, 201, createdLocation);
    }

    [HttpDelete(Name = "DeleteContainer")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(401, "application/json")]
    [ProducesResponseType<ProblemDetails>(404, "application/json")]
    [ProducesResponseType<ProblemDetails>(410, "application/json")]
    public async Task<ActionResult> DeleteContainer([FromRoute] string path, [FromQuery] bool purge)
    {
        var result = await mediator.Send(new DeleteContainer(Request.Path, purge));
        return this.StatusResponseFromResult(result, 204);
    }
}