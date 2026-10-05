using System.Net.Http.Json;

namespace EWasteManagement.API.Infrastructure.ExternalServices;

public interface IValidatorAgentClient
{
    Task<ValidatorAgentResult> RunAsync(Guid workflowId, Guid submissionId, AnalyzerAgentResult analyzerResult, CancellationToken ct = default);
}

public class ValidatorAgentClient : IValidatorAgentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<ValidatorAgentClient> _logger;

    public ValidatorAgentClient(HttpClient http, IConfiguration config, ILogger<ValidatorAgentClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri(config["Agent:ValidatorBaseUrl"] ?? "http://localhost:8004");
        _http.Timeout = TimeSpan.FromSeconds(30);   // deterministic, no LLM — should be fast

        var apiKey = config["Agent:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-Agent-Key", apiKey);
    }

    public async Task<ValidatorAgentResult> RunAsync(Guid workflowId, Guid submissionId, AnalyzerAgentResult analyzerResult, CancellationToken ct = default)
    {
        var body = new
        {
            workflowId,
            submissionId,
            analyzerResult = new
            {
                wasteCategory = analyzerResult.WasteCategory,
                hazardLevel = analyzerResult.HazardLevel,
                estimatedVolumeKg = analyzerResult.EstimatedVolumeKg,
                estimatedValueLkr = analyzerResult.EstimatedValueLkr,
                confidenceScore = analyzerResult.ConfidenceScore
            }
        };

        var response = await _http.PostAsJsonAsync("/run", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var respBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Validator /run returned {Status}: {Body}", response.StatusCode, respBody);
            throw new InvalidOperationException($"Validator agent failed with status {(int)response.StatusCode}.");
        }
        return await response.Content.ReadFromJsonAsync<ValidatorAgentResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Validator agent returned an empty response.");
    }
}

public class ValidatorAgentResult
{
    public Guid WorkflowId { get; set; }
    public bool ApprovedForAutoAssignment { get; set; }
    public bool RequiresHumanApproval { get; set; }
    public List<string> Reasons { get; set; } = new();
}
