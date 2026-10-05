using System.Data.Common;
using System.Text.Json;
using EWasteManagement.Api.Controllers;
using EWasteManagement.Api.Dtos;
using EWasteManagement.Api.Entities;
using EWasteManagement.Api.Services;
using EWasteManagement.API.Features.Auth.Entities;
using EWasteManagement.API.Features.Collection.Entities;
using EWasteManagement.API.Features.Workflow.DTOs;
using EWasteManagement.API.Features.Workflow.Entities;
using EWasteManagement.API.Features.Workflow.Services;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.Tests.TestHelpers;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EWasteManagement.Tests.Submissions;

/// <summary>
/// Locks in the Phase 1/2 submission rules:
///  - a submission and its workflow are committed together, and the workflow
///    is only queued once it is in the database;
///  - status is derived from the job (once one exists) or the workflow;
///  - the list loads workflows and jobs in ONE query, however many rows;
///  - users only ever see their own submissions.
/// </summary>
public class SubmissionServiceTests : IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ApplicationDbContext _db = null!;
    private CommandCounter _commands = null!;
    private RecordingOrchestrator _orchestrator = null!;
    private SubmissionService _service = null!;

    private readonly Guid _alice = Guid.NewGuid();
    private readonly Guid _bob = Guid.NewGuid();

    public async Task InitializeAsync()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        await _connection.OpenAsync();

        _commands = new CommandCounter();
        _db = NewContext(_commands);
        await _db.Database.EnsureCreatedAsync();

        var workflows = new WorkflowService(_db, new ConfigurationBuilder().Build(), new NotificationService(_db));
        _orchestrator = new RecordingOrchestrator(() => NewContext());
        _service = new SubmissionService(_db, workflows, _orchestrator, new NotificationService(_db));
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private ApplicationDbContext NewContext(IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection);
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new ApplicationDbContext(builder.Options, new NoOpDomainEventDispatcher());
    }

    // ---------- Create ----------

    [Fact]
    public async Task Create_commits_submission_and_workflow_together_then_queues_the_workflow()
    {
        var result = await _service.CreateSubmissionAsync(NewDto(), _alice, "Household");

        var workflow = await _db.CollectionWorkflows.SingleAsync();
        Assert.Equal(result.Id, workflow.SubmissionId);
        Assert.Equal(WorkflowStatus.Planning, workflow.Status);

        // Queued exactly once, and the row was already visible from another
        // connection-level context at that moment (i.e. committed first).
        Assert.Equal(new[] { workflow.WorkflowId }, _orchestrator.Started);
        Assert.True(_orchestrator.WorkflowExistedWhenStarted);

        Assert.Equal(_alice, result.UserId);
        Assert.Equal("Household", result.UserType);
        Assert.Equal("Analyzing", result.Status);
        Assert.Equal(workflow.WorkflowId, result.Workflow!.WorkflowId);
        Assert.Null(result.JobId);
    }

    [Fact]
    public async Task Create_does_not_queue_anything_if_the_save_fails()
    {
        // An item with a null ItemName violates the NOT NULL column.
        var dto = NewDto();
        dto.Items[0].ItemName = null!;

        await Assert.ThrowsAsync<DbUpdateException>(() => _service.CreateSubmissionAsync(dto, _alice, "Household"));

        Assert.Empty(_orchestrator.Started);
        await using var fresh = NewContext();
        Assert.Equal(0, await fresh.Submissions.CountAsync());
        Assert.Equal(0, await fresh.CollectionWorkflows.CountAsync());
    }

    // ---------- Derived status + single query ----------

    [Fact]
    public async Task List_derives_each_status_and_uses_a_single_query()
    {
        var withJob = await SeedAsync(_alice, WorkflowStatus.Completed, JobStatus.Accepted);
        var pending = await SeedAsync(_alice, WorkflowStatus.PendingApproval, analysis: new AnalyzerResultRequest
        {
            WasteCategory = "Batteries", HazardLevel = "High",
            EstimatedVolumeKg = 2.5m, EstimatedValueLkr = 40m, ConfidenceScore = 0.9,
        });
        var rejected = await SeedAsync(_bob, WorkflowStatus.Rejected);
        var failed = await SeedAsync(_bob, WorkflowStatus.Failed);
        var legacy = await SeedAsync(_bob, workflowStatus: null);

        var admin = new User { Email = "admin@test.lk", FullName = "Admin", PasswordHash = "x", Role = UserRole.Admin };
        _db.Users.Add(admin);
        _db.WorkflowApprovalActions.Add(new WorkflowApprovalAction
        {
            WorkflowId = rejected.WorkflowId!.Value, ActionType = WorkflowApprovalActionType.Rejected,
            PerformedByUserId = admin.UserId, Comments = "Not e-waste.",
        });
        _db.AgentExecutionLogs.Add(new AgentExecutionLog
        {
            WorkflowId = failed.WorkflowId!.Value, AgentName = "Orchestrator",
            Succeeded = false, ErrorMessage = "Analyzer agent failed with status 500.",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        _commands.Reset();
        var list = await _service.GetAllSubmissionsAsync();
        Assert.Equal(1, _commands.Count);

        Assert.Equal(5, list.Count);
        var byId = list.ToDictionary(s => s.Id);

        var job = byId[withJob.SubmissionId];
        Assert.Equal("CollectorAssigned", job.Status);
        Assert.Equal("Collector assigned", job.StatusLabel);
        Assert.Equal(withJob.JobId, job.JobId);
        Assert.Equal("Accepted", job.JobStatus);

        var review = byId[pending.SubmissionId];
        Assert.Equal("AwaitingReview", review.Status);
        Assert.Equal("Batteries", review.Workflow!.Analysis!.WasteCategory);
        Assert.Equal(2.5m, review.Workflow.Analysis.EstimatedVolumeKg);
        Assert.Single(review.Items);

        Assert.Equal("Rejected", byId[rejected.SubmissionId].Status);
        Assert.Equal("Not e-waste.", byId[rejected.SubmissionId].StatusReason);

        Assert.Equal("Failed", byId[failed.SubmissionId].Status);
        Assert.Equal("Analyzer agent failed with status 500.", byId[failed.SubmissionId].StatusReason);

        Assert.Equal("NotProcessed", byId[legacy.SubmissionId].Status);
        Assert.Null(byId[legacy.SubmissionId].Workflow);
    }

    [Fact]
    public async Task Completed_workflow_without_a_job_is_closed_with_the_finalize_reasoning()
    {
        var seeded = await SeedAsync(_alice, WorkflowStatus.Completed, finalSummary: "Duplicate of an earlier pickup.");

        var result = await _service.GetSubmissionByIdAsync(seeded.SubmissionId);

        Assert.Equal("Closed", result!.Status);
        Assert.Equal("Duplicate of an earlier pickup.", result.StatusReason);
        Assert.Null(result.JobId);
    }

    [Fact]
    public async Task Status_uses_the_latest_job_when_there_are_several()
    {
        var seeded = await SeedAsync(_alice, WorkflowStatus.Completed, JobStatus.Cancelled);
        var newer = new Job
        {
            SubmissionId = seeded.SubmissionId, PickupAddress = "12 Main St",
            Status = JobStatus.Completed, CreatedAt = DateTime.UtcNow.AddMinutes(5),
        };
        _db.Jobs.Add(newer);
        await _db.SaveChangesAsync();

        var result = await _service.GetSubmissionByIdAsync(seeded.SubmissionId);

        Assert.Equal("Collected", result!.Status);
        Assert.Equal(newer.JobId, result.JobId);
    }

    // ---------- Ownership ----------

    [Fact]
    public async Task Mine_returns_only_the_callers_submissions()
    {
        var mine = await SeedAsync(_alice, WorkflowStatus.Analyzing);
        await SeedAsync(_bob, WorkflowStatus.Analyzing);

        var result = await _service.GetSubmissionsForUserAsync(_alice);

        Assert.Equal(mine.SubmissionId, Assert.Single(result).Id);
    }

    [Fact]
    public async Task Get_by_id_hides_other_users_submissions_but_not_from_staff()
    {
        var bobs = await SeedAsync(_bob, WorkflowStatus.Analyzing);

        var asAlice = await new SubmissionsController(_service).WithUser(_alice, "Household")
            .GetSubmissionById(bobs.SubmissionId, CancellationToken.None);
        var asBob = await new SubmissionsController(_service).WithUser(_bob, "Household")
            .GetSubmissionById(bobs.SubmissionId, CancellationToken.None);
        var asStaff = await new SubmissionsController(_service).WithUser(Guid.NewGuid(), "Staff")
            .GetSubmissionById(bobs.SubmissionId, CancellationToken.None);

        Assert.Equal(404, ControllerTestExtensions.StatusCodeOf(asAlice));
        Assert.Equal(bobs.SubmissionId, ControllerTestExtensions.ValueOf(asBob).Id);
        Assert.Equal(bobs.SubmissionId, ControllerTestExtensions.ValueOf(asStaff).Id);
    }

    [Fact]
    public async Task Staff_and_admin_see_submissions_from_every_user()
    {
        var alices = await SeedAsync(_alice, WorkflowStatus.Analyzing);
        var bobs = await SeedAsync(_bob, WorkflowStatus.Completed, JobStatus.Assigned);

        var asStaff = await new SubmissionsController(_service).WithUser(Guid.NewGuid(), "Staff")
            .GetAllSubmissions(CancellationToken.None);
        var asAdmin = await new SubmissionsController(_service).WithUser(Guid.NewGuid(), "Admin")
            .GetAllSubmissions(CancellationToken.None);

        var expected = new[] { alices.SubmissionId, bobs.SubmissionId }.OrderBy(x => x);
        var staffIds = ControllerTestExtensions.ValueOf(asStaff).Select(s => s.Id).OrderBy(x => x);
        var adminIds = ControllerTestExtensions.ValueOf(asAdmin).Select(s => s.Id).OrderBy(x => x);
        Assert.Equal(expected, staffIds);
        Assert.Equal(expected, adminIds);
    }

    [Fact]
    public async Task Create_takes_owner_and_user_type_from_the_token()
    {
        var controller = new SubmissionsController(_service).WithUser(_bob, "Corporate");

        var result = await controller.CreateSubmission(NewDto(), CancellationToken.None);

        var created = Assert.IsType<Microsoft.AspNetCore.Mvc.CreatedAtActionResult>(result.Result);
        var dto = Assert.IsType<SubmissionResponseDto>(created.Value);
        Assert.Equal(_bob, dto.UserId);
        Assert.Equal("Corporate", dto.UserType);
    }

    // ---------- Helpers ----------

    // ---------- CSV submissions ----------

    private static CreateSubmissionDto CsvDto() => new()
    {
        Source = SubmissionSources.Csv,
        PickupAddress = "12 Main St, Colombo",
        PhoneNumber = "0771234567",
        Items = new()
        {
            new CreateSubmissionItemDto { ItemName = "Dell monitor", Quantity = 40, EstimatedWeightKg = 4.5m, Category = "it equipment" },
            new CreateSubmissionItemDto { ItemName = "UPS battery", Quantity = 10, EstimatedWeightKg = 12m, Category = "Batteries" },
            new CreateSubmissionItemDto { ItemName = "Keyboard", Quantity = 25, Category = "IT Equipment" },
            new CreateSubmissionItemDto { ItemName = "Cables", Quantity = 1, Category = "Not a real category" },
        },
    };

    [Theory]
    [InlineData("Household")]
    [InlineData("Collector")]
    public async Task Csv_from_a_non_corporate_account_is_refused(string role)
    {
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => _service.CreateSubmissionAsync(CsvDto(), _alice, role));

        Assert.Empty(_orchestrator.Started);
        Assert.Equal(0, await _db.Submissions.CountAsync());
    }

    [Fact]
    public async Task Csv_from_a_corporate_account_keeps_rows_in_order_with_their_quantities()
    {
        var result = await _service.CreateSubmissionAsync(CsvDto(), _alice, "Corporate");

        Assert.Equal(SubmissionSources.Csv, result.Source);
        Assert.Equal(new[] { "Dell monitor", "UPS battery", "Keyboard", "Cables" }, result.Items.Select(i => i.ItemName));
        Assert.Equal(new[] { 40, 10, 25, 1 }, result.Items.Select(i => i.Quantity));
        Assert.Equal(4.5m, result.Items[0].EstimatedWeightKg);
        Assert.Single(_orchestrator.Started);
    }

    [Fact]
    public async Task Csv_category_is_the_most_common_known_row_category_and_weight_is_the_row_total()
    {
        var result = await _service.CreateSubmissionAsync(CsvDto(), _alice, "Corporate");

        Assert.Equal("IT Equipment", result.Category);           // 2 rows (case-insensitive) beat 1
        Assert.Equal(40 * 4.5m + 10 * 12m, result.EstimatedWeight); // rows without a weight add nothing
    }

    [Fact]
    public async Task Csv_with_no_known_category_falls_back_to_other()
    {
        var dto = CsvDto();
        dto.Items.ForEach(i => i.Category = null);

        var result = await _service.CreateSubmissionAsync(dto, _alice, "Corporate");

        Assert.Equal(SubmissionCategories.Other, result.Category);
    }

    [Fact]
    public async Task Corporate_accounts_can_still_submit_manually()
    {
        var result = await _service.CreateSubmissionAsync(NewDto(), _alice, "Corporate");

        Assert.Equal(SubmissionSources.Manual, result.Source);
        Assert.Equal(1, result.Items.Single().Quantity);
    }

    private static CreateSubmissionDto NewDto() => new()
    {
        Category = "IT Equipment",
        EstimatedWeight = 3m,
        PickupAddress = "12 Main St, Colombo",
        PhoneNumber = "0771234567",
        Items = new() { new CreateSubmissionItemDto { ItemName = "Laptop", Description = "Old laptop", ImageUrl = "https://example.com/a.jpg" } },
    };

    private sealed record Seeded(Guid SubmissionId, Guid? WorkflowId, Guid? JobId);

    private async Task<Seeded> SeedAsync(
        Guid userId,
        WorkflowStatus? workflowStatus,
        JobStatus? jobStatus = null,
        AnalyzerResultRequest? analysis = null,
        string? finalSummary = null)
    {
        var submission = new Submission
        {
            UserId = userId, Category = "IT Equipment", PickupAddress = "12 Main St", PhoneNumber = "0771234567",
            Items = { new SubmissionItem { ItemName = "Laptop", Description = "Old laptop", ImageUrl = "https://example.com/a.jpg" } },
        };
        _db.Submissions.Add(submission);

        CollectionWorkflow? workflow = null;
        if (workflowStatus is WorkflowStatus status)
        {
            workflow = new CollectionWorkflow
            {
                SubmissionId = submission.Id, Status = status,
                AnalyzerResultJson = analysis is null ? null : JsonSerializer.Serialize(analysis),
                FinalReasoningSummary = finalSummary,
            };
            _db.CollectionWorkflows.Add(workflow);
        }

        Job? job = null;
        if (jobStatus is JobStatus js)
        {
            job = new Job { SubmissionId = submission.Id, PickupAddress = "12 Main St", Status = js };
            _db.Jobs.Add(job);
        }

        await _db.SaveChangesAsync();
        return new Seeded(submission.Id, workflow?.WorkflowId, job?.JobId);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; private set; }
        public void Reset() => Count = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Count++;
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class RecordingOrchestrator : IWorkflowOrchestrationService
    {
        private readonly Func<ApplicationDbContext> _freshContext;
        public RecordingOrchestrator(Func<ApplicationDbContext> freshContext) => _freshContext = freshContext;

        public List<Guid> Started { get; } = new();
        public bool WorkflowExistedWhenStarted { get; private set; }

        public void Start(Guid workflowId)
        {
            Started.Add(workflowId);
            using var db = _freshContext();
            WorkflowExistedWhenStarted = db.CollectionWorkflows.Any(w => w.WorkflowId == workflowId);
        }

        public Task RunChainAsync(Guid workflowId, CancellationToken ct = default) => Task.CompletedTask;
        public Task ResumeAfterApprovalAsync(Guid workflowId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
