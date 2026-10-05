using System.Text;
using EWasteManagement.Api.Services;
using EWasteManagement.API.Features.Admin.Services;
using EWasteManagement.API.Features.Auth.Services;
using EWasteManagement.API.Infrastructure.Persistence;
using EWasteManagement.API.Features.Processing.Events;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

using EWasteManagement.API.Features.Sales.Services;
using EWasteManagement.API.Infrastructure.Middleware;
using FluentValidation;
using FluentValidation.AspNetCore;
using EWasteManagement.API.Infrastructure.ExternalServices;

using EWasteManagement.API.Shared.Common;
using EWasteManagement.API.Features.Collection.Services;
using EWasteManagement.API.Features.Processing.Services;
using EWasteManagement.API.Features.Workflow.Services;
using EWasteManagement.API.Features.Notifications.Services;
using EWasteManagement.API.Infrastructure.BackgroundTasks;
using EWasteManagement.API.Shared.Storage;


var builder = WebApplication.CreateBuilder(args);


// wwwroot/uploads must exist before Build() — ASP.NET Core snapshots
// IWebHostEnvironment.WebRootFileProvider (what UseStaticFiles() serves from)
// at build time, and falls back to a provider that never sees files created
// afterwards if wwwroot didn't exist yet. LocalFileStorage also creates this
// directory itself, defensively, for callers that construct it directly.
Directory.CreateDirectory(Path.Combine(builder.Environment.ContentRootPath, "wwwroot", "uploads"));

//render database connection string from DATABASE_URL environment variable if it exists
var databaseUrl = Environment.GetEnvironmentVariable("DATABASE_URL");
if (!string.IsNullOrWhiteSpace(databaseUrl))
{
    var uri = new Uri(databaseUrl);
    var port = uri.Port == -1 ? 5432 : uri.Port;
    var userInfo = uri.UserInfo.Split(':', 2);
    var npgsqlConnectionString =
        $"Host={uri.Host};Port={port};Database={uri.AbsolutePath.TrimStart('/')};" +
        $"Username={userInfo[0]};Password={userInfo[1]};SSL Mode=Require;Trust Server Certificate=true";
    builder.Configuration["ConnectionStrings:DefaultConnection"] = npgsqlConnectionString;
}


// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste ONLY the token - no 'Bearer ' prefix."
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Database Context (PostgreSQL Persistence)
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Services Registration
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IJwtService, JwtService>();
builder.Services.AddScoped<IStaffService, StaffService>();
builder.Services.AddScoped<IAdminAccountService, AdminAccountService>();
builder.Services.AddScoped<ICollectorService, CollectorService>();
builder.Services.AddScoped<IMatchingService, MatchingService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<ICollectorJobInfoService, CollectorJobInfoService>();
builder.Services.AddScoped<ISubmissionService, SubmissionService>();
builder.Services.AddScoped<IJobVerificationService, JobVerificationService>();
builder.Services.AddScoped<IJobReceiptService, JobReceiptService>();
builder.Services.AddScoped<IRatePolicyLookupService, RatePolicyLookupService>();
builder.Services.AddScoped<IPaymentCalculator, JobPaymentCalculator>();
builder.Services.AddScoped<IPaymentCalculator, ExtraWastePaymentCalculator>();
builder.Services.AddScoped<ICollectorPaymentService, CollectorPaymentService>();
builder.Services.AddScoped<IInventoryProcessingService, InventoryProcessingService>();
builder.Services.AddScoped<IClassificationValidationService, ClassificationValidationService>();
builder.Services.AddScoped<IProcessingLookupService, ProcessingLookupService>();
builder.Services.AddScoped<IItemTypeCatalogService, ItemTypeCatalogService>();
builder.Services.AddScoped<IRecoveredMaterialSummaryService, RecoveredMaterialSummaryService>();
builder.Services.AddScoped<IRatePolicyService, RatePolicyService>();
builder.Services.AddScoped<IWorkflowApprovalHistoryService, WorkflowApprovalHistoryService>();
// In-app notifications (header bell) — producers across the app depend on this.
builder.Services.AddScoped<INotificationService, NotificationService>();
builder.Services.AddHttpClient<IGeoService, OpenStreetMapService>();

// Shared image upload (Submission items today, Collection completion photos
// later). Singleton: holds no per-request state, and constructing it once
// creates wwwroot/uploads up front.
var storageProvider = builder.Configuration["Storage:Provider"] ?? "Local";
if (storageProvider.Equals("Cloudinary", StringComparison.OrdinalIgnoreCase))
    builder.Services.AddSingleton<IFileStorage, CloudinaryFileStorage>();
else
    builder.Services.AddSingleton<IFileStorage, LocalFileStorage>();

// Component D — Sales services
builder.Services.AddScoped<IBuyerService, BuyerService>();
builder.Services.AddScoped<IMaterialPricingService, MaterialPricingService>();
builder.Services.AddScoped<IRevenueService, RevenueService>();
builder.Services.AddScoped<ISalesOrderService, SalesOrderService>();
builder.Services.AddScoped<IExportOrderService, ExportOrderService>();
builder.Services.AddScoped<ICommercialPlanService, CommercialPlanService>();
builder.Services.AddScoped<IMaterialRequestService, MaterialRequestService>();
builder.Services.AddScoped<IMaterialRestockMatcher, MaterialRestockMatcher>();

// Component D — external data providers
builder.Services.AddScoped<IRecoveredMaterialsProvider, EfRecoveredMaterialsProvider>();
builder.Services.AddHttpClient<IAgentClient, AgentClient>();

// --- Intake-and-collection-planning agentic workflow (slice 3) ---
builder.Services.AddScoped<IWorkflowService, WorkflowService>();
builder.Services.AddScoped<IWorkflowOrchestrationService, WorkflowOrchestrationService>();
builder.Services.AddHttpClient<IPlannerAgentClient, PlannerAgentClient>();
builder.Services.AddHttpClient<IAnalyzerAgentClient, AnalyzerAgentClient>();
builder.Services.AddHttpClient<IValidatorAgentClient, ValidatorAgentClient>();
builder.Services.AddHttpClient<IMatcherAgentClient, MatcherAgentClient>();

// Singleton: one queue shared by every request and by the background
// processor. WorkflowQueueProcessor is a BackgroundService — it starts
// with the app and runs for the app's whole lifetime.
builder.Services.AddSingleton<IWorkflowBackgroundQueue, WorkflowBackgroundQueue>();
builder.Services.AddHostedService<WorkflowQueueProcessor>();

// Runs once at startup: re-queues workflows a restart interrupted mid-chain
// (the queue above is in memory). Leaves PendingApproval for the admin.
builder.Services.AddHostedService<WorkflowStartupRecovery>();
builder.Services.AddSingleton<IMaterialRestockQueue, MaterialRestockQueue>();
builder.Services.AddHostedService<MaterialRestockQueueProcessor>();

// Component D — flips Approved material prices whose expiry date has passed to Expired,
// once at startup and every few hours. Purely cosmetic to the data: order pricing already
// refuses to use expired-by-date rows (see MaterialPricingPolicy).
builder.Services.AddHostedService<MaterialPricingExpirySweeper>();

// FluentValidation — scans the assembly for AbstractValidator<T> classes
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"]?? "SuperSecretKeyForEWasteManagementProject2026SecureKey!";
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

// CORS Policy
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowClients", policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddValidatorsFromAssemblyContaining<Program>();
builder.Services.AddFluentValidationAutoValidation();
builder.Services.AddScoped<IDomainEventDispatcher, SimpleDomainEventDispatcher>();
builder.Services.AddScoped<IDomainEventHandler<InventoryStatusChangedEvent>, InventoryStatusChangedEventHandler>();
builder.Services.AddScoped<IExtraWasteReceiptService, ExtraWasteReceiptService>();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { status = "ok" }));

using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider
        .GetRequiredService<EWasteManagement.API.Infrastructure.Persistence.ApplicationDbContext>()
        .Database.Migrate();
}

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseExceptionHandler();

// Pipeline Configuration
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Serves wwwroot/uploads with no authentication check — the Analyzer agent
// downloads submitted images by URL and has no login token of its own.
app.UseStaticFiles();

app.UseCors("AllowClients");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();