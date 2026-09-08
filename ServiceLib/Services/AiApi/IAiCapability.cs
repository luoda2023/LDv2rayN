using System.Text.Json;

namespace ServiceLib.Services.AiApi;

/// <summary>
/// Metadata for an externally exposed AI capability.
/// </summary>
public sealed record AiCapabilityDescriptor(
    string Name,
    string Description,
    bool IsReadOnly,
    string Method,
    string Path,
    Dictionary<string, AiParameterDescriptor>? Parameters)
{
    public static AiCapabilityDescriptor Of(
        string name,
        string description,
        string method,
        string path,
        bool readOnly = true,
        Dictionary<string, AiParameterDescriptor>? parameters = null)
        => new(name, description, readOnly, method, path, parameters);
}

public sealed record AiParameterDescriptor(string Name, string Type, string Description, bool Required = false);

/// <summary>
/// Uniform envelope returned by every capability.
/// </summary>
public sealed class AiResult
{
    public bool Ok { get; set; }
    public string? Message { get; set; }
    public JsonElement? Data { get; set; }

    public static AiResult Success(JsonElement data, string? message = null)
        => new() { Ok = true, Message = message, Data = data };

    public static AiResult Success(string message = "ok")
        => new() { Ok = true, Message = message, Data = null };

    public static AiResult Error(string message, int statusCode = 500)
        => new() { Ok = false, Message = message, Data = new AiError(message, statusCode).ToJsonElement() };
}

public sealed record AiError(string Message, int Code)
{
    public JsonElement ToJsonElement()
        => JsonSerializer.SerializeToElement(this, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}

/// <summary>
/// A single capability exposed to the AI layer.
/// </summary>
public interface IAiCapability
{
    AiCapabilityDescriptor Descriptor { get; }
    Task<AiResult> InvokeAsync(JsonElement? body, CancellationToken ct);
}

/// <summary>
/// Static registry of all AI capabilities. Capabilities are registered once at
/// server startup; the registry hands them to the HTTP dispatcher.
/// </summary>
public static class AiCapabilityRegistry
{
    private static readonly List<IAiCapability> _capabilities = [];
    private static readonly object _lock = new();

    public static IReadOnlyList<IAiCapability> All
    {
        get
        {
            lock (_lock) return _capabilities.ToArray();
        }
    }

    public static void Register(IAiCapability capability)
    {
        if (capability is null) return;
        lock (_lock)
        {
            // Idempotent: keep the latest registration for the same name.
            _capabilities.RemoveAll(c => string.Equals(c.Descriptor.Name, capability.Descriptor.Name, StringComparison.Ordinal));
            _capabilities.Add(capability);
        }
    }

    public static void Clear()
    {
        lock (_lock) _capabilities.Clear();
    }

    public static bool TryFind(string name, out IAiCapability capability)
    {
        lock (_lock)
        {
            capability = _capabilities.FirstOrDefault(c =>
                string.Equals(c.Descriptor.Name, name, StringComparison.OrdinalIgnoreCase))!;
            return capability is not null;
        }
    }

    public static bool TryMatchByPath(string method, string path, out IAiCapability capability)
    {
        lock (_lock)
        {
            capability = _capabilities.FirstOrDefault(c =>
                string.Equals(c.Descriptor.Method, method, StringComparison.OrdinalIgnoreCase)
                && string.Equals(c.Descriptor.Path, path, StringComparison.OrdinalIgnoreCase))!;
            return capability is not null;
        }
    }
}
