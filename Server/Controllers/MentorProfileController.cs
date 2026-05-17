using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorProfileController — /api/mentor-profile/{userId}
//
//  Read-only public-facing view of a mentor's preferences. Returned fields
//  are limited to expectation-setting items (communication, availability,
//  feedback style, red lines, …). The internal "notification digest"
//  preferences (PreferenceEvents, PreferenceFrequency) are deliberately
//  excluded so the existence of the Project Health monitoring loop never
//  leaks to students.
//
//  Any authenticated user can call this endpoint — typical entry point is a
//  student clicking their team's mentor in the sidebar popover.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/mentor-profile")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class MentorProfileController : ControllerBase
{
    private readonly DbRepository _db;
    public MentorProfileController(DbRepository db) => _db = db;

    [HttpGet("{userId:int}")]
    public async Task<IActionResult> GetProfile(int userId, int authUserId)
    {
        // Confirm the target is actually a mentor — we don't expose this view
        // for students or staff. authUserId is intentionally unused: any
        // authenticated user can read any mentor profile (no per-team gate).
        _ = authUserId;

        const string mentorCheckSql = @"
            SELECT 1 FROM UserRoles
            WHERE  UserId = @UserId AND Role = 'Mentor'
            LIMIT  1";
        var isMentor = (await _db.GetRecordsAsync<int>(
            mentorCheckSql, new { UserId = userId }))?.FirstOrDefault();
        if (isMentor != 1) return NotFound("המנחה לא נמצא");

        // Single round-trip: user identity + (optional) preferences.
        const string sql = @"
            SELECT  u.Id          AS UserId,
                    u.FirstName,
                    u.LastName,
                    u.Email,
                    mp.RoleDescription,
                    mp.InvolvementLevel,
                    mp.PreferredChannel,
                    mp.PreferredChannelOther,
                    mp.ExpectedResponseTime,
                    mp.CommunicationFrequency,
                    mp.RequirePeriodicUpdates,
                    mp.UpdateFrequency,
                    mp.UpdateContent,
                    mp.ClientInteractionFrequency,
                    mp.ClientInteractionFrequencyCustom,
                    mp.MentorInClientInteraction,
                    mp.SubmissionLeadTime,
                    mp.SubmissionLeadTimeCustom,
                    mp.ReviewIterations,
                    mp.FeedbackType,
                    mp.DecisionInvolvement,
                    mp.QualityDescription,
                    mp.QualityFocusAreas,
                    mp.RedLines,
                    mp.AvailableTimes,
                    mp.MeetingFormat,
                    mp.SchedulingMethod
            FROM    users u
            LEFT JOIN MentorPreferences mp ON mp.UserId = u.Id
            WHERE   u.Id = @UserId
            LIMIT   1";

        var row = (await _db.GetRecordsAsync<MentorProfileRow>(
            sql, new { UserId = userId }))?.FirstOrDefault();
        if (row is null) return NotFound("המנחה לא נמצא");

        return Ok(new MentorProfileViewDto
        {
            UserId                            = row.UserId,
            FirstName                         = row.FirstName ?? "",
            LastName                          = row.LastName  ?? "",
            Email                             = row.Email,
            RoleDescription                   = row.RoleDescription,
            InvolvementLevel                  = row.InvolvementLevel,
            PreferredChannel                  = row.PreferredChannel,
            PreferredChannelOther             = row.PreferredChannelOther,
            ExpectedResponseTime              = row.ExpectedResponseTime,
            CommunicationFrequency            = row.CommunicationFrequency,
            RequirePeriodicUpdates            = row.RequirePeriodicUpdates == 1,
            UpdateFrequency                   = row.UpdateFrequency,
            UpdateContent                     = MentorPreferenceTokens.Split(row.UpdateContent),
            ClientInteractionFrequency        = row.ClientInteractionFrequency,
            ClientInteractionFrequencyCustom  = row.ClientInteractionFrequencyCustom,
            MentorInClientInteraction         = row.MentorInClientInteraction,
            SubmissionLeadTime                = row.SubmissionLeadTime,
            SubmissionLeadTimeCustom          = row.SubmissionLeadTimeCustom,
            ReviewIterations                  = row.ReviewIterations,
            FeedbackType                      = row.FeedbackType,
            DecisionInvolvement               = MentorPreferenceTokens.Split(row.DecisionInvolvement),
            QualityDescription                = row.QualityDescription,
            QualityFocusAreas                 = MentorPreferenceTokens.Split(row.QualityFocusAreas),
            RedLines                          = row.RedLines,
            AvailableTimes                    = row.AvailableTimes,
            MeetingFormat                     = row.MeetingFormat,
            SchedulingMethod                  = row.SchedulingMethod,
        });
    }

    private sealed class MentorProfileRow
    {
        public int      UserId                           { get; set; }
        public string?  FirstName                        { get; set; }
        public string?  LastName                         { get; set; }
        public string?  Email                            { get; set; }
        public string?  RoleDescription                  { get; set; }
        public string?  InvolvementLevel                 { get; set; }
        public string?  PreferredChannel                 { get; set; }
        public string?  PreferredChannelOther            { get; set; }
        public string?  ExpectedResponseTime             { get; set; }
        public string?  CommunicationFrequency           { get; set; }
        public int      RequirePeriodicUpdates           { get; set; }
        public string?  UpdateFrequency                  { get; set; }
        public string?  UpdateContent                    { get; set; }
        public string?  ClientInteractionFrequency       { get; set; }
        public string?  ClientInteractionFrequencyCustom { get; set; }
        public string?  MentorInClientInteraction        { get; set; }
        public string?  SubmissionLeadTime               { get; set; }
        public string?  SubmissionLeadTimeCustom         { get; set; }
        public int?     ReviewIterations                 { get; set; }
        public string?  FeedbackType                     { get; set; }
        public string?  DecisionInvolvement              { get; set; }
        public string?  QualityDescription               { get; set; }
        public string?  QualityFocusAreas                { get; set; }
        public string?  RedLines                         { get; set; }
        public string?  AvailableTimes                   { get; set; }
        public string?  MeetingFormat                    { get; set; }
        public string?  SchedulingMethod                 { get; set; }
    }
}
