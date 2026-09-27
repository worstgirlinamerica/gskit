namespace GSKit.CLI;

/// <summary>
/// Shared runtime flags passed into every command handler.
/// </summary>
public record RunContext(bool Debug, bool Verbose);
