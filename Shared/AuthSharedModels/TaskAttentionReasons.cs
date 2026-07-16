namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  TaskAttentionReasons — canonical vocabulary for "why does this task need
//  attention", computed by Server/Services/TaskUrgencyService.cs (business-logic
//  consolidation epic, Concept 4). Lives in Shared — not Server.Services — so
//  client components can render the same Hebrew labels the server assigned,
//  the same pattern already used by RoadmapStageStatuses/ProjectHealthStatuses.
// ─────────────────────────────────────────────────────────────────────────────

public static class TaskAttentionReasons
{
    public const string None                     = "None";
    public const string ReturnedForRevision       = "ReturnedForRevision";
    public const string Overdue                   = "Overdue";
    public const string PendingMoodleConfirmation = "PendingMoodleConfirmation";
    public const string PendingMentorReview       = "PendingMentorReview";
    public const string DueSoon                   = "DueSoon";

    public static string Label(string reason) => reason switch
    {
        ReturnedForRevision       => "הוחזר לתיקון",
        Overdue                   => "באיחור",
        PendingMoodleConfirmation => "ממתין לאישור מודל",
        PendingMentorReview       => "ממתין למנחה",
        DueSoon                   => "מתקרב למועד",
        _                         => "אין",
    };
}
