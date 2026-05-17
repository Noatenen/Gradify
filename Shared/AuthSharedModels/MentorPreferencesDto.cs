using System;
using System.Collections.Generic;

namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Mentor Preferences — drives the mentor-only card on the shared /settings
//  page. Each mentor has at most one row in MentorPreferences, keyed by
//  UserId. Multi-select fields are stored as comma-separated TEXT values
//  (matching the convention used in Tasks.AllowedFileTypes etc.).
//
//  All controlled-vocabulary values below are stored verbatim in the DB; the
//  client maps them to Hebrew labels for display. Keep the constants in sync
//  with the schema columns and the page's toggle/select bindings.
// ─────────────────────────────────────────────────────────────────────────────

public static class MentorInvolvementLevels
{
    public const string Low    = "low";
    public const string Medium = "medium";
    public const string High   = "high";

    public static string Label(string v) => v switch
    {
        Low    => "נמוכה",
        Medium => "בינונית",
        High   => "גבוהה",
        _      => v ?? "",
    };
}

public static class MentorChannels
{
    public const string Email    = "email";
    public const string WhatsApp = "whatsapp";
    public const string Slack    = "slack";
    public const string Other    = "other";

    public static string Label(string v) => v switch
    {
        Email    => "אימייל",
        WhatsApp => "וואטסאפ",
        Slack    => "סלאק",
        Other    => "אחר",
        _        => v ?? "",
    };
}

public static class MentorResponseTimes
{
    public const string TwentyFourHours = "24h";
    public const string OneToThreeDays  = "1to3d";
    public const string Week            = "week";

    public static string Label(string v) => v switch
    {
        TwentyFourHours => "עד 24 שעות",
        OneToThreeDays  => "1–3 ימים",
        Week            => "עד שבוע",
        _               => v ?? "",
    };
}

public static class MentorCommunicationFrequencies
{
    public const string Weekly   = "weekly";
    public const string Biweekly = "biweekly";
    public const string AsNeeded = "asneeded";

    public static string Label(string v) => v switch
    {
        Weekly   => "שבועית",
        Biweekly => "דו־שבועית",
        AsNeeded => "לפי הצורך",
        _        => v ?? "",
    };
}

public static class MentorClientInvolvementOptions
{
    public const string Yes           = "yes";
    public const string No            = "no";
    public const string KeyMilestones = "key_milestones";

    public static string Label(string v) => v switch
    {
        Yes           => "כן",
        No            => "לא",
        KeyMilestones => "באבני דרך מרכזיות בלבד",
        _             => v ?? "",
    };
}

public static class MentorFeedbackTypes
{
    public const string Written = "written";
    public const string Verbal  = "verbal";
    public const string Both    = "both";

    public static string Label(string v) => v switch
    {
        Written => "כתוב",
        Verbal  => "בעל פה",
        Both    => "שניהם",
        _       => v ?? "",
    };
}

public static class MentorMeetingFormats
{
    public const string Online   = "online";
    public const string InPerson = "in_person";
    public const string Both     = "both";

    public static string Label(string v) => v switch
    {
        Online   => "מקוון",
        InPerson => "פרונטלי",
        Both     => "שילוב",
        _        => v ?? "",
    };
}

public static class MentorSchedulingMethods
{
    public const string Student = "student";
    public const string Fixed   = "fixed";

    public static string Label(string v) => v switch
    {
        Student => "ביוזמת הסטודנט",
        Fixed   => "מועדים קבועים",
        _       => v ?? "",
    };
}

public static class MentorDigestFrequencies
{
    public const string Immediate = "immediate";
    public const string Daily     = "daily";
    public const string Weekly    = "weekly";

    public static string Label(string v) => v switch
    {
        Immediate => "מיידי",
        Daily     => "יומי",
        Weekly    => "שבועי",
        _         => v ?? "",
    };
}

/// <summary>Multi-select tokens for the "update content" check group.</summary>
public static class MentorUpdateContentTokens
{
    public const string Progress = "progress";
    public const string Plans    = "plans";
    public const string Blockers = "blockers";

    public static string Label(string v) => v switch
    {
        Progress => "התקדמות",
        Plans    => "תכניות",
        Blockers => "חסמים",
        _        => v ?? "",
    };
}

/// <summary>Multi-select tokens for "decision involvement".</summary>
public static class MentorDecisionInvolvementTokens
{
    public const string Major      = "major";
    public const string Milestones = "milestones";
    public const string Ongoing    = "ongoing";

    public static string Label(string v) => v switch
    {
        Major      => "החלטות מהותיות",
        Milestones => "אבני דרך",
        Ongoing    => "באופן שוטף",
        _          => v ?? "",
    };
}

/// <summary>Multi-select tokens for "quality focus areas".</summary>
public static class MentorQualityFocusTokens
{
    public const string UX           = "ux";
    public const string Technical    = "technical";
    public const string Pedagogical  = "pedagogical";
    public const string Research     = "research";

    public static string Label(string v) => v switch
    {
        UX          => "חוויית משתמש",
        Technical   => "טכני",
        Pedagogical => "פדגוגי",
        Research    => "מחקרי",
        _           => v ?? "",
    };
}

/// <summary>Multi-select tokens for the mentor's notification event subscriptions.</summary>
public static class MentorEventTokens
{
    public const string NewSubmission = "new_submission";
    public const string Delays        = "delays";
    public const string NoResponse    = "no_response";

    public static string Label(string v) => v switch
    {
        NewSubmission => "הגשה חדשה",
        Delays        => "עיכובים",
        NoResponse    => "ללא מענה",
        _             => v ?? "",
    };
}

// ── Helpers for comma-separated multi-select fields ─────────────────────────

/// <summary>Stable serializer for multi-select tokens. Empty list ⇒ null so
/// the DB stores NULL instead of an empty string.</summary>
public static class MentorPreferenceTokens
{
    public static string? Join(IEnumerable<string>? tokens)
    {
        if (tokens is null) return null;
        var clean = new List<string>();
        foreach (var t in tokens)
        {
            if (string.IsNullOrWhiteSpace(t)) continue;
            clean.Add(t.Trim());
        }
        return clean.Count == 0 ? null : string.Join(",", clean);
    }

    public static List<string> Split(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        var parts = csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new List<string>(parts);
    }
}

/// <summary>
/// Wire shape for GET /api/mentor-preferences/me and PUT /api/mentor-preferences/me.
/// Multi-select fields use string lists on the wire and comma-separated TEXT
/// in the database; the controller bridges via MentorPreferenceTokens.
/// </summary>
public class MentorPreferencesDto
{
    // 1. Mentorship style
    public string?       RoleDescription                  { get; set; }
    public string?       InvolvementLevel                 { get; set; }

    // 2. Communication preferences
    public string?       PreferredChannel                 { get; set; }
    public string?       PreferredChannelOther            { get; set; }
    public string?       ExpectedResponseTime             { get; set; }
    public string?       CommunicationFrequency           { get; set; }

    // 3. Student updates
    public bool          RequirePeriodicUpdates           { get; set; }
    public string?       UpdateFrequency                  { get; set; }
    public List<string>  UpdateContent                    { get; set; } = new();

    // 4. Client interaction
    public string?       ClientInteractionFrequency       { get; set; }
    public string?       ClientInteractionFrequencyCustom { get; set; }
    public string?       MentorInClientInteraction        { get; set; }

    // 5. Submission & feedback
    public string?       SubmissionLeadTime               { get; set; }
    public string?       SubmissionLeadTimeCustom         { get; set; }
    public int?          ReviewIterations                 { get; set; }
    public string?       FeedbackType                     { get; set; }

    // 6. Decision involvement
    public List<string>  DecisionInvolvement              { get; set; } = new();

    // 7. Quality expectations
    public string?       QualityDescription               { get; set; }
    public List<string>  QualityFocusAreas                { get; set; } = new();

    // 8. Red lines
    public string?       RedLines                         { get; set; }

    // 9. Availability
    public string?       AvailableTimes                   { get; set; }
    public string?       MeetingFormat                    { get; set; }
    public string?       SchedulingMethod                 { get; set; }

    // 10. Notification preferences (digest controls)
    public List<string>  PreferenceEvents                 { get; set; } = new();
    public string?       PreferenceFrequency              { get; set; }
}
