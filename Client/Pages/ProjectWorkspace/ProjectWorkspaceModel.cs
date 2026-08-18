namespace AuthWithAdmin.Client.Pages.ProjectWorkspace;

/// <summary>
/// Presentation helpers for the student project workspace (/project).
///
/// Only formatting lives here — nothing in this file talks to the network or
/// holds state, so the page and its section components can share one answer
/// for "how is a project type written in Hebrew" or "what are this person's
/// initials" without either owning it.
/// </summary>
public static class ProjectWorkspaceModel
{
    /// <summary>
    /// ProjectTypes.Name is stored in English ("Technological" /
    /// "Methodological"). Every Hebrew screen that shows it translates it at
    /// the edge — ProjectOverviewPage.razor:434 and
    /// MilestonesOverviewPage.razor:258 already carry this exact mapping, and
    /// StudentProfileTeamCard deliberately withheld the raw value rather than
    /// print English inside a Hebrew card. This is that same mapping, reused
    /// rather than re-decided; an unrecognised value is dropped, never shown
    /// raw.
    /// </summary>
    public static string? ProjectTypeLabel(string? projectType) => projectType switch
    {
        "Technological"  => "טכנולוגי",
        "Methodological" => "מתודולוגי",
        _                => null
    };

    /// <summary>
    /// "פרויקט 15 · טכנולוגי · 2025-2026" — the quiet system-information line
    /// under the project name. Every part is optional and an absent part is
    /// dropped rather than printed as a bare separator (the same rule
    /// StudentProfileTeamCard's subtitle follows).
    /// </summary>
    public static string IdentityMetaLine(int projectNumber, string? projectType, string? academicYear)
    {
        var parts = new List<string>(3);

        if (projectNumber > 0) parts.Add($"פרויקט {projectNumber}");

        if (ProjectTypeLabel(projectType) is { } typeLabel) parts.Add(typeLabel);

        if (!string.IsNullOrWhiteSpace(academicYear)) parts.Add(academicYear.Trim());

        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Two-letter initials for a member chip, from a "First Last" string. Same
    /// derivation AppSideNav uses for the profile avatar; a single-word name
    /// yields one letter rather than a slice of the same word.
    /// </summary>
    public static string Initials(string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName)) return "?";

        var words = fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (words.Length == 0) return "?";
        if (words.Length == 1) return words[0][..1];

        return $"{words[0][0]}{words[^1][0]}";
    }
}
