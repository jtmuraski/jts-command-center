namespace JtsCommandCenter.Core.Projects;

/// <summary>An edit was made against an older copy, e.g. the item changed on another device since it was opened.</summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);
