using System.Security.Claims;
using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using EWasteManagement.API.Shared.Security;

namespace EWasteManagement.API.Features.Collection.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class CollectorsController : ControllerBase
{
    private readonly ICollectorService _collectorService;
    private readonly IMatchingService _matchingService;

    public CollectorsController(ICollectorService collectorService, IMatchingService matchingService)
    {
        _collectorService = collectorService;
        _matchingService = matchingService;
    }

    // POST /api/v1/collectors
    // Creates the collector profile for whoever is logged in. UserId comes
    // from the JWT, never from the request body — otherwise anyone could
    // create a profile "for" another user.
    [HttpPost]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<CollectorResponseDto>> CreateProfile(CreateCollectorProfileDto dto)
    {
        try
        {
            var result = await _collectorService.CreateProfileAsync(CurrentUserId, dto);
            return CreatedAtAction(nameof(GetMe), result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // GET /api/v1/collectors/me
    // Convenience endpoint the Flutter app calls on startup to check
    // whether the logged-in collector already has a profile.
    [HttpGet("me")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<CollectorResponseDto>> GetMe()
    {
        var result = await _collectorService.GetByUserIdAsync(CurrentUserId);
        return result is null ? NotFound(new { message = "No collector profile exists for this user yet." }) : Ok(result);
    }

    // PUT /api/v1/collectors/me
    // The logged-in collector edits their own name, phone, vehicle and capacity.
    [HttpPut("me")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<CollectorResponseDto>> UpdateMe(UpdateCollectorProfileDto dto)
    {
        try
        {
            return Ok(await _collectorService.UpdateMyProfileAsync(CurrentUserId, dto));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // GET /api/v1/collectors?isAvailable=true
    // Staff/admin list for the collectors page, with names and current load.
    [HttpGet]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<ActionResult<List<CollectorResponseDto>>> GetAll([FromQuery] bool? isAvailable)
    {
        var result = await _collectorService.GetAllAsync(isAvailable);
        return Ok(result);
    }

    // GET /api/v1/collectors/{id}
    // Staff/admin lookup — e.g. for the dashboard.
    [HttpGet("{id:guid}")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<ActionResult<CollectorResponseDto>> GetById(Guid id)
    {
        var result = await _collectorService.GetByIdAsync(id);
        return result is null ? NotFound() : Ok(result);
    }

    // PUT /api/v1/collectors/{id}/availability
    [HttpPut("{id:guid}/availability")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<CollectorResponseDto>> UpdateAvailability(Guid id, UpdateAvailabilityDto dto)
    {
        try
        {
            var result = await _collectorService.UpdateAvailabilityAsync(id, CurrentUserId, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    // PUT /api/v1/collectors/{id}/location
    // Called periodically by the Flutter app while the collector is on shift.
    [HttpPut("{id:guid}/location")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<CollectorResponseDto>> UpdateLocation(Guid id, UpdateLocationDto dto)
    {
        try
        {
            var result = await _collectorService.UpdateLocationAsync(id, CurrentUserId, dto);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    // The "sub" claim from JwtService carries the UserId, mapped by the
    // default inbound claim handler to ClaimTypes.NameIdentifier.
    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Token is missing a user id claim."));

    // GET /api/v1/collectors/available?pickupLatitude=&pickupLongitude=&...
    // Staff-facing: manual override / dashboard visibility into who would be matched.
    [HttpGet("available")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<ActionResult<List<CollectorMatchDto>>> GetAvailable([FromQuery] MatchRequestDto request)
    {
        var results = await _matchingService.FindCandidatesAsync(request);
        return Ok(results);
    }

    // POST /api/v1/collectors/match
    // The Matcher/Logistics agent's tool call. Machine-to-machine, so there's
    // no user JWT ([AllowAnonymous]); instead the agent must present the
    // shared X-Agent-Key. The response contains collectors' live locations,
    // so it must not be open to anyone who knows the URL.
    [HttpPost("match")]
    [AllowAnonymous]
    [RequireAgentKey]
    public async Task<ActionResult<List<CollectorMatchDto>>> Match(MatchRequestDto request)
    {
        var results = await _matchingService.FindCandidatesAsync(request);
        return Ok(results);
    }
}