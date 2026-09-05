using AuthWithAdmin.Shared.AuthSharedModels;

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

    // ProjectMonogram lived here: a two-letter mark derived from the project's
    // display name, drawn on the identity card when a project had no logo.
    //
    // It is gone because the fallback it served is gone. A project logo is a
    // real uploaded image now (ProjectTeamProfile.LogoPath), and the tile's
    // empty state is a neutral glyph rather than generated initials — a
    // monogram is machine-made content that reads as the team's own choice,
    // and an unset logo should invite one instead of pre-filling it. Removed
    // rather than left unused so it cannot quietly come back.
    //
    // Initials() below is NOT the same helper and stays: it marks PEOPLE, on
    // the team chips, and a person's initials follow different rules (see its
    // own summary).


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

    // ═════════════════════════════════════════════════════════════════════
    //  DELIVERABLE STATUS — DERIVED, NOT DECLARED
    //
    //  A deliverable's displayed state is computed from what the system
    //  actually holds, in this order:
    //
    //    1. ProjectSubmissionStatuses.Status = 'Done'  → הושלם
    //       The team's own completion record. This is the ONLY state a person
    //       declares, and the only one the "X מתוך 8 הושלמו" count reads.
    //
    //    2. At least one ProjectResource whose DeliverableKey is this key
    //                                                    → בעבודה
    //       Real evidence of work: a persisted, team-created artifact that
    //       names this deliverable. Nobody has to move a control to get here.
    //
    //    3. ProjectSubmissionStatuses.Status = 'InProgress' → בעבודה
    //       Honoured for rows written before the association existed, when
    //       'בעבודה' was still something a student set by hand. Nothing in the
    //       UI writes this value any more; it is read-only history.
    //
    //    4. otherwise                                   → לא התחיל
    //
    //  THERE IS NO SECOND STATUS SYSTEM. The vocabulary is
    //  SubmissionStatusValues — the same three strings the table already
    //  stores and the same ones the API validates. What changed is that the
    //  middle value is now INFERRED rather than typed in.
    //
    //  WHY 'הושלם' AND NOT 'הוגש'. Nothing in this product records a formal
    //  submission of a graduation deliverable to the faculty: there is no
    //  submission row, no reviewer and no returned-for-changes state for the
    //  eight categories (TaskSubmissions is the milestone pipeline, keyed to a
    //  Task, and is a different domain). The record that does exist is the
    //  team's own "we finished this", which is what 'הושלם' says and what the
    //  progress count above the list already calls it. Calling it 'הוגש' would
    //  claim a handover the system cannot see.
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The state to DISPLAY for one deliverable, from the persisted status row
    /// (or null when the team has never touched it) and the number of work
    /// artifacts associated with it. Returns a
    /// <see cref="SubmissionStatusValues"/> value.
    /// </summary>
    public static string DeriveDeliverableStatus(string? declaredStatus, int artifactCount)
    {
        if (declaredStatus == SubmissionStatusValues.Done) return SubmissionStatusValues.Done;

        if (artifactCount > 0) return SubmissionStatusValues.InProgress;

        return declaredStatus == SubmissionStatusValues.InProgress
            ? SubmissionStatusValues.InProgress
            : SubmissionStatusValues.NotStarted;
    }

    /// <summary>The Hebrew label for a derived status. One definition, so the
    /// list row, the detail panel and the resource tile can never word the same
    /// state differently.</summary>
    public static string DeliverableStatusLabel(string status) => status switch
    {
        SubmissionStatusValues.Done       => "הושלם",
        SubmissionStatusValues.InProgress => "בעבודה",
        _                                 => "לא התחיל"
    };

    /// <summary>
    /// The tone suffix a component appends to its own prefix
    /// (<c>pwd-status-@(Tone)</c>). A NAME rather than a colour, because each
    /// component owns its own scoped CSS and only the vocabulary is shared.
    ///
    /// <para>Rose has no member on purpose: a deliverable category carries no
    /// date anywhere in the model, so nothing here can be overdue.</para>
    /// </summary>
    public static string DeliverableStatusTone(string status) => status switch
    {
        SubmissionStatusValues.Done       => "done",
        SubmissionStatusValues.InProgress => "progress",
        _                                 => "idle"
    };
}
