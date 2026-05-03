using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  Lecturer Submissions DTOs  —  /api/task-submissions/all
//
//  Used by the lecturer/admin "הגשות" monitoring page.
//  Each row represents one TaskSubmissions record, enriched with project,
//  team, and milestone context so the page needs only one API call.
//
//  Detail (files) reuses the existing TaskSubmissionDto from TaskSubmissionDto.cs
//  which is already returned by GET /api/task-submissions/{id}.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// One submission row for the lecturer list view.
/// Returned by GET /api/task-submissions/all (Admin / Staff only).
/// </summary>
public class LecturerSubmissionRowDto
{
    public int      SubmissionId      { get; set; }
    public int      TaskId            { get; set; }
    public string   TaskTitle         { get; set; } = "";

    public int      ProjectId         { get; set; }
    public int      ProjectNumber     { get; set; }
    public string   ProjectTitle      { get; set; } = "";

    public string   MilestoneTitle    { get; set; } = "";

    public int      SubmittedByUserId { get; set; }
    public string   SubmittedByName   { get; set; } = "";

    public DateTime SubmittedAt       { get; set; }
    public string?  Notes             { get; set; }

    /// <summary>Legacy reviewer status: "Submitted" | "Reviewed" | "NeedsRevision".
    /// Kept for compatibility with the existing PATCH /status endpoint.</summary>
    public string   Status            { get; set; } = "Submitted";
    /// <summary>"Pending" | "Approved" | "Returned"</summary>
    public string?  MentorStatus      { get; set; }
    public DateTime? MentorReviewedAt { get; set; }
    /// <summary>When the student formally forwarded to course staff.</summary>
    public DateTime? CourseSubmittedAt { get; set; }

    /// <summary>One of LecturerReviewStatuses.* — the lecturer review queue state.</summary>
    public string   ReviewStatus        { get; set; } = LecturerReviewStatuses.PendingReview;
    public bool     IsFeedbackPublished { get; set; }
    public DateTime? FeedbackPublishedAt { get; set; }

    /// <summary>Comma-separated mentor names assigned to the project.</summary>
    public string?  AssignedMentorName { get; set; }

    public int      FileCount         { get; set; }

    public int      AcademicYearId    { get; set; }
    public string   AcademicYearName  { get; set; } = "";
}

/// <summary>
/// Full lecturer-side submission record. Returned by GET /api/task-submissions/{id}/lecturer-detail.
/// Includes everything the review drawer needs in one round-trip.
/// </summary>
public class LecturerSubmissionDetailDto
{
    public int      Id                  { get; set; }
    public int      TaskId              { get; set; }
    public string   TaskTitle           { get; set; } = "";

    public int      ProjectId           { get; set; }
    public int      ProjectNumber       { get; set; }
    public string   ProjectTitle        { get; set; } = "";
    public string?  TeamName            { get; set; }
    public string   MilestoneTitle      { get; set; } = "";

    public int      SubmittedByUserId   { get; set; }
    public string   SubmittedByName     { get; set; } = "";
    public DateTime SubmittedAt         { get; set; }
    public string?  Notes               { get; set; }

    /// <summary>Mentor side</summary>
    public string?  MentorStatus        { get; set; }
    public string?  MentorFeedback      { get; set; }
    public DateTime? MentorReviewedAt   { get; set; }
    /// <summary>Comma-separated mentor names assigned to the project.</summary>
    public string?  MentorName          { get; set; }
    public DateTime? CourseSubmittedAt  { get; set; }

    /// <summary>Lecturer review</summary>
    public string   Status              { get; set; } = "Submitted";
    public string   ReviewStatus        { get; set; } = LecturerReviewStatuses.PendingReview;
    public string?  ReviewerFeedback    { get; set; }
    public bool     IsFeedbackPublished { get; set; }
    public DateTime? FeedbackPublishedAt { get; set; }
    public string?  ReviewedByName      { get; set; }

    /// <summary>All files attached to this submission — student and lecturer mixed.
    /// Use IsLecturerFeedback to split for rendering.</summary>
    public List<TaskSubmissionFileDto> Files { get; set; } = new();

    /// <summary>Full history of every submission for the same task, newest first.
    /// Includes the currently-anchored submission. Lecturer feedback inside each item
    /// must only be rendered when its IsFeedbackPublished = true.</summary>
    public List<SubmissionHistoryItemDto> SubmissionHistory { get; set; } = new();
}

/// <summary>Payload for PATCH /api/task-submissions/{id}/lecturer-review (save draft).</summary>
public class SaveLecturerReviewRequest
{
    /// <summary>One of LecturerReviewStatuses.*</summary>
    public string  ReviewStatus     { get; set; } = LecturerReviewStatuses.InReview;
    /// <summary>Free-text feedback. Stored on the submission row as ReviewerFeedback.</summary>
    public string? ReviewerFeedback { get; set; }
}

/// <summary>Payload for POST /api/task-submissions/{id}/lecturer-files.</summary>
public class UploadLecturerFilesRequest
{
    /// <summary>Files to attach to the lecturer feedback. Reuses the same
    /// base64 transport as student submission files (SubmissionFileRequest).</summary>
    public List<SubmissionFileRequest> Files { get; set; } = new();
}

/// <summary>Controlled vocabulary for the lecturer/admin final-review flow.</summary>
public static class LecturerReviewStatuses
{
    public const string PendingReview    = "PendingReview";
    public const string InReview         = "InReview";
    public const string FeedbackReturned = "FeedbackReturned";
    public const string FinalApproved    = "FinalApproved";

    public static readonly IReadOnlyList<string> All = new[]
    {
        PendingReview, InReview, FeedbackReturned, FinalApproved,
    };

    public static string Label(string s) => s switch
    {
        PendingReview    => "ממתין לבדיקה",
        InReview         => "בבדיקה",
        FeedbackReturned => "הוחזר עם משוב",
        FinalApproved    => "אושר סופית",
        _                => s,
    };
}