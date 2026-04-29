using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>Result returned from POST /api/airtable/sync-projects.</summary>
public class AirtableSyncResultDto
{
    public int          TotalFetched { get; set; }
    public int          Inserted     { get; set; }
    public int          Updated      { get; set; }
    public int          Failed       { get; set; }
    public List<string> Errors       { get; set; } = new();

    /// <summary>
    /// Set when the entire sync could not run (not configured, network error, etc.).
    /// Individual record failures are captured in <see cref="Errors"/> instead.
    /// </summary>
    public string? SyncError { get; set; }
}
