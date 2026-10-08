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
    string TaskQueue)
{
    public static SandboxConfig FromEnvironment() => new(
        Address: Get("TEMPORAL_ADDRESS", "localhost:7233"),
        Namespace: Get("TEMPORAL_NAMESPACE", "default"),
        TaskQueue: Get("TEMPORAL_TASK_QUEUE", "sandbox"));

    private static string Get(string key, string fallback)
    {
        var value = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
