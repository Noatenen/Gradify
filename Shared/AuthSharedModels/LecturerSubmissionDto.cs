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

    /// <summary>"Submitted" | "Reviewed" | "NeedsRevision"</summary>
    public string   Status            { get; set; } = "Submitted";
    /// <summary>"Pending" | "Approved" | "Returned"</summary>
    public string?  MentorStatus      { get; set; }
    /// <summary>When the student formally forwarded to course staff.</summary>
    public DateTime? CourseSubmittedAt { get; set; }

    public int      FileCount         { get; set; }

    public int      AcademicYearId    { get; set; }
    public string   AcademicYearName  { get; set; } = "";
}