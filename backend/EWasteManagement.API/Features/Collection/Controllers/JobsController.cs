using System.Security.Claims;
using EWasteManagement.API.Features.Collection.DTOs;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Collection.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace EWasteManagement.API.Features.Collection.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
[Authorize]
public class JobsController : ControllerBase
{
    private readonly IJobService _jobService;

    public JobsController(IJobService jobService) => _jobService = jobService;

    // POST /api/v1/jobs/assign
    // Called once a submission is approved — geocodes the address, matches
    // the best available collector, and creates the Job. Restricted to
    // Staff/Admin for now, since it's expected to be triggered from the
    // approval action on the React dashboard. If the agent orchestrator
    // needs to call this without a logged-in staff user, that needs a
    // service-account/token story that doesn't exist yet in this codebase —
    // flagging rather than guessing at one.
    [HttpPost("assign")]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<ActionResult<JobResponseDto>> Assign(CreateJobDto dto)
    {
        try
        {
            var result = await _jobService.CreateAndAssignAsync(dto);
            return CreatedAtAction(nameof(GetById), new { id = result.JobId }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    // GET /api/v1/jobs/my?status=Assigned
    [HttpGet("my")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<List<JobResponseDto>>> GetMy([FromQuery] JobStatus? status)
    {
        try
        {
            var result = await _jobService.GetMyJobsAsync(CurrentUserId, status);
            return Ok(result);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    // GET /api/v1/jobs/{id}
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<JobResponseDto>> GetById(Guid id)
    {
        try
        {
            var isPrivileged = User.IsInRole("Staff") || User.IsInRole("Admin");
            var result = await _jobService.GetByIdAsync(id, CurrentUserId, isPrivileged);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    // GET /api/v1/jobs/{id}/route?fromLat=..&fromLng=..
    // Road route from the collector to the pickup, for the map on the job screen.
    [HttpGet("{id:guid}/route")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<JobRouteDto>> GetRoute(Guid id, [FromQuery] decimal? fromLat, [FromQuery] decimal? fromLng)
    {
        try
        {
            var result = await _jobService.GetRouteAsync(id, CurrentUserId, fromLat, fromLng);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    // GET /api/v1/jobs/{id}/collector-info
    // Customer contact (once accepted) and the payment for this job, for the assigned collector.
    [HttpGet("{id:guid}/collector-info")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<CollectorJobInfoDto>> GetCollectorInfo(
        Guid id, [FromServices] ICollectorJobInfoService infoService, CancellationToken ct)
    {
        try
        {
            var result = await infoService.GetAsync(id, CurrentUserId, ct);
            return result is null ? NotFound() : Ok(result);
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
    }

    // GET /api/v1/jobs?status=Completed
    [HttpGet]
    [Authorize(Roles = "Staff,Admin")]
    public async Task<ActionResult<List<JobResponseDto>>> GetAll([FromQuery] JobStatus? status)
    {
        var result = await _jobService.GetAllAsync(status);
        return Ok(result);
    }

    // PUT /api/v1/jobs/{id}/accept
    [HttpPut("{id:guid}/accept")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<JobResponseDto>> Accept(Guid id)
    {
        try
        {
            var result = await _jobService.AcceptAsync(id, CurrentUserId);
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // PUT /api/v1/jobs/{id}/start
    // Collector sets off for the pickup: Accepted -> InProgress. The Flutter
    // app calls this when the collector taps Navigate.
    [HttpPut("{id:guid}/start")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<JobResponseDto>> Start(Guid id)
    {
        try
        {
            var result = await _jobService.StartAsync(id, CurrentUserId);
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // PUT /api/v1/jobs/{id}/reject
    // Triggers reassignment internally — the response reflects whatever
    // happened next (reassigned to someone new, or NoCollectorAvailable).
    [HttpPut("{id:guid}/reject")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<JobResponseDto>> Reject(Guid id, RejectJobDto dto)
    {
        try
        {
            var result = await _jobService.RejectAsync(id, CurrentUserId, dto);
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
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // POST /api/v1/jobs/{id}/complete
    [HttpPost("{id:guid}/complete")]
    [Authorize(Roles = "Collector")]
    public async Task<ActionResult<JobResponseDto>> Complete(Guid id, CompleteJobDto dto)
    {
        try
        {
            var result = await _jobService.CompleteAsync(id, CurrentUserId, dto);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    // GET /api/v1/jobs/{id}/history
    // Every offer/accept/reject for this job, oldest first.
    [HttpGet("{id:guid}/history")]
    [Authorize(Roles = "Staff,Admin")]
    public Task<ActionResult<List<JobAssignmentHistoryDto>>> GetHistory(Guid id) =>
        RunStaffAction(() => _jobService.GetHistoryAsync(id));

    // PUT /api/v1/jobs/{id}/address
    // Correct an address that couldn't be geocoded; matching re-runs on success.
    [HttpPut("{id:guid}/address")]
    [Authorize(Roles = "Staff,Admin")]
    public Task<ActionResult<JobResponseDto>> UpdateAddress(Guid id, UpdateJobAddressDto dto) =>
        RunStaffAction(() => _jobService.UpdateAddressAsync(id, dto));

    // PUT /api/v1/jobs/{id}/reassign
    // Body { "collectorId": "..." } to hand-pick, or {} to re-run matching.
    [HttpPut("{id:guid}/reassign")]
    [Authorize(Roles = "Staff,Admin")]
    public Task<ActionResult<JobResponseDto>> Reassign(Guid id, ReassignJobDto dto) =>
        RunStaffAction(() => _jobService.ReassignAsync(id, dto));

    // PUT /api/v1/jobs/{id}/cancel
    [HttpPut("{id:guid}/cancel")]
    [Authorize(Roles = "Staff,Admin")]
    public Task<ActionResult<JobResponseDto>> Cancel(Guid id) =>
        RunStaffAction(() => _jobService.CancelAsync(id));

    // Same exception-to-status mapping as the collector actions above:
    // bad input 400, missing 404, wrong state 409.
    private async Task<ActionResult<T>> RunStaffAction<T>(Func<Task<T>> action)
    {
        try
        {
            return Ok(await action());
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    private Guid CurrentUserId =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new UnauthorizedAccessException("Token is missing a user id claim."));
}