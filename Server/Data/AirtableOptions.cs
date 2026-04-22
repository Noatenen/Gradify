namespace AuthWithAdmin.Server.Data;

/// <summary>
/// Configuration for Airtable integration.
/// Bind from the "Airtable" section in appsettings.json (server-side only).
/// BaseId is intentionally kept in config so it can be changed each academic year
/// without touching any code.
/// </summary>
public class AirtableOptions
{
    public const string SectionName = "Airtable";

    /// <summary>Personal Access Token (or legacy API key).</summary>
    public string Token { get; set; } = "";

    /// <summary>Airtable Base ID — starts with "app". Changes each academic year.</summary>
    public string BaseId { get; set; } = "";

    /// <summary>Exact table name inside the base (e.g. "הצעות לפרוייקטים").</summary>
    public string TableName { get; set; } = "";

    /// <summary>
    /// Optional view name to request from Airtable (e.g. "הצגה לסטודנטים").
    /// When set, Airtable returns only the rows visible in that view, already filtered
    /// and sorted according to the view's configuration.
    /// Leave empty to fetch all records from the table.
    /// </summary>
    public string ViewName { get; set; } = "";

    /// <summary>
    /// When true, the service will additionally filter out any record whose
    /// FieldMap.IncludeInPool field is not truthy — acting as a second safety net
    /// in case the Airtable view does not already handle that filtering.
    /// </summary>
    public bool StudentVisibleOnly { get; set; } = true;

    /// <summary>
    /// Maps our internal field names to the actual column headers in your Airtable base.
    /// All values should match the exact Airtable column header (including Hebrew).
    /// </summary>
    public AirtableFieldMap FieldMap { get; set; } = new();

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Token)     &&
        !string.IsNullOrWhiteSpace(BaseId)    &&
        !string.IsNullOrWhiteSpace(TableName);
}

/// <summary>
/// Maps internal project field names → Airtable column headers.
/// Override every value in appsettings when your Airtable base uses different names.
/// </summary>
public class AirtableFieldMap
{
    // ── Core identification ──────────────────────────────────────────────────
    public string ProjectNumber    { get; set; } = "ProjectNumber";
    public string Title            { get; set; } = "Title";

    // ── Organisation info ────────────────────────────────────────────────────
    public string OrganizationName { get; set; } = "OrganizationName";
    /// <summary>Type/sector of the proposing organisation (e.g. "עמותה", "חברה פרטית").</summary>
    public string OrganizationType { get; set; } = "OrganizationType";

    // ── Project details ──────────────────────────────────────────────────────
    /// <summary>High-level topic/domain (e.g. "בריאות דיגיטלית").</summary>
    public string ProjectTopic     { get; set; } = "ProjectTopic";
    public string Description      { get; set; } = "Description";
    public string TargetAudience   { get; set; } = "TargetAudience";
    public string Goals            { get; set; } = "Goals";
    /// <summary>Detailed work breakdown / subject matter.</summary>
    public string Contents         { get; set; } = "Contents";

    // ── Contact info ─────────────────────────────────────────────────────────
    public string ContactPerson    { get; set; } = "ContactPerson";
    public string ContactRole      { get; set; } = "ContactRole";
    public string ContactEmail     { get; set; } = "ContactEmail";
    public string ContactPhone     { get; set; } = "ContactPhone";

    // ── Pool / visibility control ────────────────────────────────────────────
    /// <summary>
    /// Checkbox field. When true the proposal should be included in the student pool.
    /// Used as a secondary filter when StudentVisibleOnly = true and the Airtable view
    /// does not already guarantee all returned records are pool-ready.
    /// Leave empty to skip this filter.
    /// </summary>
    public string IncludeInPool    { get; set; } = "IncludeInPool";

    // ── Metadata ─────────────────────────────────────────────────────────────
    public string SubmittedAt      { get; set; } = "SubmittedAt";

    // ── Legacy / optional — kept for backward compatibility ──────────────────
    public string ProjectType      { get; set; } = "ProjectType";
    public string Status           { get; set; } = "Status";
    public string Priority         { get; set; } = "Priority";
}
