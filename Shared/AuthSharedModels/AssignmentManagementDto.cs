using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class AssignmentSubmissionListItemDto
{
    public int     TeamId                { get; set; }
    public string  TeamName              { get; set; } = "";
    public int     AcademicYearId        { get; set; }
    public string  AcademicYear          { get; set; } = "";
    public string  SubmittedAt           { get; set; } = "";
    public bool    HasOwnProject         { get; set; }
    public string? OwnProjectDescription { get; set; }
    public string? Notes                 { get; set; }
    public List<AssignmentSubmissionMemberDto>     Members     { get; set; } = new();
    public List<AssignmentSubmissionPreferenceDto> Preferences { get; set; } = new();
}

public class AssignmentSubmissionMemberDto
{
    public int          UserId    { get; set; }
    public string       FullName  { get; set; } = "";
    public string       Email     { get; set; } = "";
    public List<string> Strengths { get; set; } = new();
}

public class AssignmentSubmissionPreferenceDto
{
    public int    Priority      { get; set; }
    public int    ProjectId     { get; set; }
    public int    ProjectNumber { get; set; }
    public string ProjectTitle  { get; set; } = "";
    public string ProjectType   { get; set; } = "";
}

// ── Matching ─────────────────────────────────────────────────────────────────

public class TeamProjectMatchDto
{
    public int    TeamId              { get; set; }
    public string TeamName            { get; set; } = "";
    public int    ProjectId           { get; set; }
    public string ProjectName         { get; set; } = "";
    public string ProjectType         { get; set; } = "";
    public int?   PreferenceRank      { get; set; }
    public int    PreferenceScore     { get; set; }
    public int    SkillScore          { get; set; }
    public int    TotalMatchScore     { get; set; }
    public string RecommendationLabel { get; set; } = "";
}

// ── Assignment board ─────────────────────────────────────────────────────────

public class AssignmentBoardDto
{
    public int     AcademicYearId       { get; set; }
    public string  AcademicYearName     { get; set; } = "";
    public bool    AssignmentsPublished { get; set; }
    public string? PublishedAt          { get; set; }
    public bool    HasUnpublishedDrafts { get; set; }
    public int     MaxMentorsPerProject { get; set; } = 2;
    public List<AssignmentBoardProjectDto> Projects        { get; set; } = new();
    public List<AssignmentBoardTeamDto>    UnassignedTeams { get; set; } = new();
    public List<AssignmentBoardMentorDto>  Mentors         { get; set; } = new();
}

public class AssignmentBoardProjectDto
{
    public int     ProjectId          { get; set; }
    public int     ProjectNumber      { get; set; }
    public string  ProjectName        { get; set; } = "";
    public string  ProjectType        { get; set; } = "";
    public int?    AssignedTeamId     { get; set; }
    public string? AssignedTeamName   { get; set; }
    public List<AssignmentBoardTeamMemberDto> AssignedMembers { get; set; } = new();
    public List<AssignmentBoardMentorDto>     Mentors         { get; set; } = new();
    public int     DemandScore        { get; set; }
    public bool    IsDraft            { get; set; }
    public List<TeamProjectMatchDto> Recommendations { get; set; } = new();
}

public class AssignmentBoardTeamDto
{
    public int    TeamId              { get; set; }
    public string TeamName            { get; set; } = "";
    public int    AcademicYearId      { get; set; }
    public List<AssignmentBoardTeamMemberDto>      Members            { get; set; } = new();
    public List<AssignmentSubmissionPreferenceDto> Preferences        { get; set; } = new();
    public List<TeamProjectMatchDto>               TopRecommendations { get; set; } = new();
}

public class AssignmentBoardTeamMemberDto
{
    public int          UserId    { get; set; }
    public string       FullName  { get; set; } = "";
    public List<string> Strengths { get; set; } = new();
}

public class AssignmentBoardMentorDto
{
    public int    UserId   { get; set; }
    public string FullName { get; set; } = "";
}

// ── Action requests / responses ──────────────────────────────────────────────

public class AssignTeamRequest
{
    public int  TeamId    { get; set; }
    public int  ProjectId { get; set; }
    public bool Force     { get; set; } // true = move (allow when team already assigned)
}

public class UnassignTeamRequest
{
    public int ProjectId { get; set; }
}

public class AssignMentorRequest
{
    public int ProjectId { get; set; }
    public int MentorId  { get; set; }
}

public class RemoveMentorRequest
{
    public int ProjectId { get; set; }
    public int MentorId  { get; set; }
}

public class PublishAssignmentsRequest
{
    public int AcademicYearId { get; set; } // 0 = current
}

// 409 body returned by assign-team when the team is already assigned and Force is false.
public class AssignTeamConflictDto
{
    public string Message     { get; set; } = "";
    public int    ProjectId   { get; set; }
    public string ProjectName { get; set; } = "";
}

public enum AssignTeamOutcome { Success, Conflict, Error }

public class AssignTeamResult
{
    public AssignTeamOutcome      Outcome  { get; set; }
    public AssignTeamConflictDto? Conflict { get; set; }
    public string?                Error    { get; set; }
}

public enum AssignMentorOutcome { Success, AlreadyAssigned, LimitReached, NotMentor, Error }

public class AssignMentorResult
{
    public AssignMentorOutcome Outcome { get; set; }
    public string?             Error   { get; set; }
}
