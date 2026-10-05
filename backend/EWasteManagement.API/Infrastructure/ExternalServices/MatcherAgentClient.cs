using System.Net.Http.Json;

namespace EWasteManagement.API.Infrastructure.ExternalServices;

public interface IMatcherAgentClient
{
    Task<MatcherAgentResult> RunAsync(
        Guid workflowId, decimal pickupLatitude, decimal pickupLongitude,
        decimal estimatedWeightKg, decimal estimatedValueLkr, bool alreadyEscalated,
        List<Guid>? excludeCollectorIds = null, CancellationToken ct = default);
}

public class MatcherAgentClient : IMatcherAgentClient
{
    private readonly HttpClient _http;
    private readonly ILogger<MatcherAgentClient> _logger;

    public MatcherAgentClient(HttpClient http, IConfiguration config, ILogger<MatcherAgentClient> logger)
    {
        _http = http;
        _logger = logger;
        _http.BaseAddress = new Uri(config["Agent:MatcherBaseUrl"] ?? "http://localhost:8005");
        _http.Timeout = TimeSpan.FromSeconds(30);   // deterministic decision layer — should be fast

        var apiKey = config["Agent:ApiKey"];
        if (!string.IsNullOrWhiteSpace(apiKey))
            _http.DefaultRequestHeaders.Add("X-Agent-Key", apiKey);
    }

    public async Task<MatcherAgentResult> RunAsync(
        Guid workflowId, decimal pickupLatitude, decimal pickupLongitude,
        decimal estimatedWeightKg, decimal estimatedValueLkr, bool alreadyEscalated,
        List<Guid>? excludeCollectorIds = null, CancellationToken ct = default)
    {
        var body = new
        {
            workflowId,
            pickupLatitude,
            pickupLongitude,
            estimatedWeightKg,
            estimatedValueLkr,
            alreadyEscalated,
            excludeCollectorIds = excludeCollectorIds ?? new List<Guid>()
        };

        var response = await _http.PostAsJsonAsync("/run", body, ct);
        if (!response.IsSuccessStatusCode)
        {
            var respBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("Matcher /run returned {Status}: {Body}", response.StatusCode, respBody);
            throw new InvalidOperationException($"Matcher agent failed with status {(int)response.StatusCode}.");
        }
        return await response.Content.ReadFromJsonAsync<MatcherAgentResult>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Matcher agent returned an empty response.");
    }
}

public class MatcherAgentResult
{
    public Guid WorkflowId { get; set; }
    public Guid? RecommendedCollectorId { get; set; }
    public bool AutoAssign { get; set; }
    public bool Ambiguous { get; set; }
    public string Reasoning { get; set; } = string.Empty;
}
