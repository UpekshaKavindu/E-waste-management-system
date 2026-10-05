using EWasteManagement.API.Features.Workflow.DTOs;
using System.Net.Http.Json;

namespace EWasteManagement.API.Infrastructure.ExternalServices;

public interface IAnalyzerAgentClient
{
    Task<AnalyzerAgentResult> RunAsync(Guid workflowId, Guid submissionId, CancellationToken ct = default);
}

public class AnalyzerAgentClient : IAnalyzerAgentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<AnalyzerAgentClient> _logger;

    public AnalyzerAgentClient(HttpClient http, IConfiguration config, ILogger<AnalyzerAgentClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri(config["Agent:AnalyzerBaseUrl"] ?? "http://localhost:8003");
        _http.Timeout = TimeSpan.FromSeconds(60);   // vision calls can be slower than text-only

        var apiKey = config["Agent:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-Agent-Key", apiKey);
    }

    public async Task<AnalyzerAgentResult> RunAsync(Guid workflowId, Guid submissionId, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/run", new { workflowId, submissionId }, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Analyzer /run returned {Status}: {Body}", response.StatusCode, body);
            throw new InvalidOperationException($"Analyzer agent failed with status {(int)response.StatusCode}.");
        }
        return await response.Content.ReadFromJsonAsync<AnalyzerAgentResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Analyzer agent returned an empty response.");
    }
}

public class AnalyzerAgentResult
{
    public Guid WorkflowId { get; set; }
    public string WasteCategory { get; set; } = string.Empty;
    public string HazardLevel { get; set; } = string.Empty;
    public decimal EstimatedVolumeKg { get; set; }
    public decimal EstimatedValueLkr { get; set; }
    public double ConfidenceScore { get; set; }
    public List<AnalyzedItem> Items { get; set; } = new();
}
