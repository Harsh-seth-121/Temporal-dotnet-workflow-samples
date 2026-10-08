namespace Sandbox.Abstractions;

/// <summary>
/// Everything the worker needs to reach the server, read from the environment.
///
/// The defaults target a host-run worker against the containerized server, which
/// is the fast iteration loop. Compose overrides them to use service names.
/// </summary>
public sealed record SandboxConfig(
    string Address,
    string Namespace,
    string TaskQueue,
    int ConnectAttempts,
    string MetricsAddress)
{
    public static SandboxConfig FromEnvironment() => new(
        Address: Get("TEMPORAL_ADDRESS", "localhost:7233"),
        Namespace: Get("TEMPORAL_NAMESPACE", "default"),
        TaskQueue: Get("TEMPORAL_TASK_QUEUE", "sandbox"),
        ConnectAttempts: GetInt("TEMPORAL_CONNECT_ATTEMPTS", 10),
        MetricsAddress: Get("WORKER_METRICS_ADDRESS", "0.0.0.0:9464"));

    private static int GetInt(string key, int fallback) =>
        int.TryParse(Environment.GetEnvironmentVariable(key), out var value) && value > 0
            ? value
            : fallback;

    private static string Get(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
