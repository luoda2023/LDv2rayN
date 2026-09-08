namespace ServiceLib.Models;

/// <summary>
/// Represents a single chat message in the AI assistant dialog
/// </summary>
[Serializable]
public class AIChatMessage
{
    /// <summary>Who sent this message</summary>
    public AIChatRole Role { get; set; }

    /// <summary>Rich text content (supports basic formatting)</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Timestamp</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;

    /// <summary>Optional: structured data for node results</summary>
    public List<AIChatNodeResult>? NodeResults { get; set; }

    /// <summary>Optional: progress percentage for long operations (0-100, -1 = indeterminate)</summary>
    public int Progress { get; set; } = -1;

    // Helper properties for XAML DataTriggers
    public bool IsUser => Role == AIChatRole.User;
    public bool IsAI => Role == AIChatRole.AI;
    public bool IsSystem => Role == AIChatRole.System;
}

public enum AIChatRole
{
    User,
    AI,
    System
}

/// <summary>
/// Represents a single node extraction/testing result
/// </summary>
[Serializable]
public class AIChatNodeResult
{
    /// <summary>Node link URI</summary>
    public string NodeLink { get; set; } = string.Empty;

    /// <summary>Short display name</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Test status</summary>
    public AIChatNodeStatus Status { get; set; }

    /// <summary>Status description text</summary>
    public string StatusText { get; set; } = string.Empty;

    /// <summary>Protocol type (vmess, vless, trojan, ss, etc.)</summary>
    public string Protocol { get; set; } = string.Empty;

    /// <summary>Server address</summary>
    public string Address { get; set; } = string.Empty;
}

public enum AIChatNodeStatus
{
    Pending,
    Testing,
    Passed,
    Failed,
    Added
}
