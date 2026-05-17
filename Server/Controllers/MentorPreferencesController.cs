using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorPreferencesController — /api/mentor-preferences/me
//
//  GET  → returns the calling mentor's preferences row (defaults if none).
//  PUT  → upserts the calling mentor's row.
//
//  Mentor-only by design. The data has no meaning for other roles, so we keep
//  the surface minimal and gate everything to Roles.Mentor.
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/mentor-preferences/me")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Mentor)]
public class MentorPreferencesController : ControllerBase
{
    private readonly DbRepository _db;
    public MentorPreferencesController(DbRepository db) => _db = db;

    [HttpGet]
    public async Task<IActionResult> GetMine(int authUserId)
    {
        const string sql = @"
            SELECT  RoleDescription, InvolvementLevel,
                    PreferredChannel, PreferredChannelOther,
                    ExpectedResponseTime, CommunicationFrequency,
                    RequirePeriodicUpdates, UpdateFrequency, UpdateContent,
                    ClientInteractionFrequency, ClientInteractionFrequencyCustom,
                    MentorInClientInteraction,
                    SubmissionLeadTime, SubmissionLeadTimeCustom,
                    ReviewIterations, FeedbackType,
                    DecisionInvolvement,
                    QualityDescription, QualityFocusAreas,
                    RedLines,
                    AvailableTimes, MeetingFormat, SchedulingMethod,
                    PreferenceEvents, PreferenceFrequency
            FROM    MentorPreferences
            WHERE   UserId = @UserId
            LIMIT   1";

        var row = (await _db.GetRecordsAsync<MentorPreferencesRow>(
            sql, new { UserId = authUserId }))?.FirstOrDefault();

        // No row yet → empty defaults so the form renders cleanly. The first
        // PUT will materialize the row.
        if (row is null) return Ok(new MentorPreferencesDto());

        return Ok(ToDto(row));
    }

    [HttpPut]
    public async Task<IActionResult> SaveMine(int authUserId, [FromBody] MentorPreferencesDto req)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");

        // Light validation — reject obviously bad enum values rather than
        // silently storing garbage. Free-text fields are trimmed; very long
        // values are clipped at 4000 chars to keep rows reasonable.
        if (!IsValidEnum(req.InvolvementLevel,        InvolvementLevels))      return BadRequest("רמת מעורבות לא תקינה");
        if (!IsValidEnum(req.PreferredChannel,        Channels))                return BadRequest("ערוץ תקשורת לא תקין");
        if (!IsValidEnum(req.ExpectedResponseTime,    ResponseTimes))           return BadRequest("זמן מענה לא תקין");
        if (!IsValidEnum(req.CommunicationFrequency,  CommunicationFreqs))      return BadRequest("תדירות תקשורת לא תקינה");
        if (!IsValidEnum(req.UpdateFrequency,         UpdateFreqs))             return BadRequest("תדירות עדכונים לא תקינה");
        if (!IsValidEnum(req.ClientInteractionFrequency, ClientInteractionFreqs)) return BadRequest("תדירות מול הלקוח לא תקינה");
        if (!IsValidEnum(req.MentorInClientInteraction, ClientInvolvement))     return BadRequest("מעורבות מנחה מול הלקוח לא תקינה");
        if (!IsValidEnum(req.SubmissionLeadTime,      LeadTimes))               return BadRequest("זמן הקדמה לא תקין");
        if (!IsValidEnum(req.FeedbackType,            FeedbackTypes))           return BadRequest("סוג משוב לא תקין");
        if (!IsValidEnum(req.MeetingFormat,           MeetingFormats))          return BadRequest("פורמט פגישה לא תקין");
        if (!IsValidEnum(req.SchedulingMethod,        SchedulingMethods))       return BadRequest("שיטת תיאום לא תקינה");
        if (!IsValidEnum(req.PreferenceFrequency,     DigestFrequencies))       return BadRequest("תדירות סיכום לא תקינה");

        if (req.ReviewIterations is int ri && (ri < 1 || ri > 3))
            return BadRequest("מספר איטרציות לא תקין");

        // Upsert — INSERT on first save, UPDATE on every save after that.
        // Keeping the column list explicit (rather than SELECT *) prevents
        // schema drift from silently dropping new columns.
        const string upsertSql = @"
            INSERT INTO MentorPreferences (
                UserId,
                RoleDescription, InvolvementLevel,
                PreferredChannel, PreferredChannelOther,
                ExpectedResponseTime, CommunicationFrequency,
                RequirePeriodicUpdates, UpdateFrequency, UpdateContent,
                ClientInteractionFrequency, ClientInteractionFrequencyCustom,
                MentorInClientInteraction,
                SubmissionLeadTime, SubmissionLeadTimeCustom,
                ReviewIterations, FeedbackType,
                DecisionInvolvement,
                QualityDescription, QualityFocusAreas,
                RedLines,
                AvailableTimes, MeetingFormat, SchedulingMethod,
                PreferenceEvents, PreferenceFrequency,
                CreatedAt, UpdatedAt
            ) VALUES (
                @UserId,
                @RoleDescription, @InvolvementLevel,
                @PreferredChannel, @PreferredChannelOther,
                @ExpectedResponseTime, @CommunicationFrequency,
                @RequirePeriodicUpdates, @UpdateFrequency, @UpdateContent,
                @ClientInteractionFrequency, @ClientInteractionFrequencyCustom,
                @MentorInClientInteraction,
                @SubmissionLeadTime, @SubmissionLeadTimeCustom,
                @ReviewIterations, @FeedbackType,
                @DecisionInvolvement,
                @QualityDescription, @QualityFocusAreas,
                @RedLines,
                @AvailableTimes, @MeetingFormat, @SchedulingMethod,
                @PreferenceEvents, @PreferenceFrequency,
                datetime('now'), datetime('now')
            )
            ON CONFLICT(UserId) DO UPDATE SET
                RoleDescription                  = excluded.RoleDescription,
                InvolvementLevel                 = excluded.InvolvementLevel,
                PreferredChannel                 = excluded.PreferredChannel,
                PreferredChannelOther            = excluded.PreferredChannelOther,
                ExpectedResponseTime             = excluded.ExpectedResponseTime,
                CommunicationFrequency           = excluded.CommunicationFrequency,
                RequirePeriodicUpdates           = excluded.RequirePeriodicUpdates,
                UpdateFrequency                  = excluded.UpdateFrequency,
                UpdateContent                    = excluded.UpdateContent,
                ClientInteractionFrequency       = excluded.ClientInteractionFrequency,
                ClientInteractionFrequencyCustom = excluded.ClientInteractionFrequencyCustom,
                MentorInClientInteraction        = excluded.MentorInClientInteraction,
                SubmissionLeadTime               = excluded.SubmissionLeadTime,
                SubmissionLeadTimeCustom         = excluded.SubmissionLeadTimeCustom,
                ReviewIterations                 = excluded.ReviewIterations,
                FeedbackType                     = excluded.FeedbackType,
                DecisionInvolvement              = excluded.DecisionInvolvement,
                QualityDescription               = excluded.QualityDescription,
                QualityFocusAreas                = excluded.QualityFocusAreas,
                RedLines                         = excluded.RedLines,
                AvailableTimes                   = excluded.AvailableTimes,
                MeetingFormat                    = excluded.MeetingFormat,
                SchedulingMethod                 = excluded.SchedulingMethod,
                PreferenceEvents                 = excluded.PreferenceEvents,
                PreferenceFrequency              = excluded.PreferenceFrequency,
                UpdatedAt                        = datetime('now')";

        await _db.SaveDataAsync(upsertSql, new
        {
            UserId                            = authUserId,
            RoleDescription                   = TrimOrNull(req.RoleDescription),
            InvolvementLevel                  = NullIfEmpty(req.InvolvementLevel),
            PreferredChannel                  = NullIfEmpty(req.PreferredChannel),
            PreferredChannelOther             = TrimOrNull(req.PreferredChannelOther),
            ExpectedResponseTime              = NullIfEmpty(req.ExpectedResponseTime),
            CommunicationFrequency            = NullIfEmpty(req.CommunicationFrequency),
            RequirePeriodicUpdates            = req.RequirePeriodicUpdates ? 1 : 0,
            UpdateFrequency                   = NullIfEmpty(req.UpdateFrequency),
            UpdateContent                     = MentorPreferenceTokens.Join(req.UpdateContent),
            ClientInteractionFrequency        = NullIfEmpty(req.ClientInteractionFrequency),
            ClientInteractionFrequencyCustom  = TrimOrNull(req.ClientInteractionFrequencyCustom),
            MentorInClientInteraction         = NullIfEmpty(req.MentorInClientInteraction),
            SubmissionLeadTime                = NullIfEmpty(req.SubmissionLeadTime),
            SubmissionLeadTimeCustom          = TrimOrNull(req.SubmissionLeadTimeCustom),
            ReviewIterations                  = req.ReviewIterations,
            FeedbackType                      = NullIfEmpty(req.FeedbackType),
            DecisionInvolvement               = MentorPreferenceTokens.Join(req.DecisionInvolvement),
            QualityDescription                = TrimOrNull(req.QualityDescription),
            QualityFocusAreas                 = MentorPreferenceTokens.Join(req.QualityFocusAreas),
            RedLines                          = TrimOrNull(req.RedLines),
            AvailableTimes                    = TrimOrNull(req.AvailableTimes),
            MeetingFormat                     = NullIfEmpty(req.MeetingFormat),
            SchedulingMethod                  = NullIfEmpty(req.SchedulingMethod),
            PreferenceEvents                  = MentorPreferenceTokens.Join(req.PreferenceEvents),
            PreferenceFrequency               = NullIfEmpty(req.PreferenceFrequency),
        });

        return NoContent();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static bool IsValidEnum(string? value, HashSet<string> allowed)
        => string.IsNullOrEmpty(value) || allowed.Contains(value);

    private static string? TrimOrNull(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        var trimmed = s.Trim();
        return trimmed.Length > 4000 ? trimmed.Substring(0, 4000) : trimmed;
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static MentorPreferencesDto ToDto(MentorPreferencesRow r) => new()
    {
        RoleDescription                  = r.RoleDescription,
        InvolvementLevel                 = r.InvolvementLevel,
        PreferredChannel                 = r.PreferredChannel,
        PreferredChannelOther            = r.PreferredChannelOther,
        ExpectedResponseTime             = r.ExpectedResponseTime,
        CommunicationFrequency           = r.CommunicationFrequency,
        RequirePeriodicUpdates           = r.RequirePeriodicUpdates == 1,
        UpdateFrequency                  = r.UpdateFrequency,
        UpdateContent                    = MentorPreferenceTokens.Split(r.UpdateContent),
        ClientInteractionFrequency       = r.ClientInteractionFrequency,
        ClientInteractionFrequencyCustom = r.ClientInteractionFrequencyCustom,
        MentorInClientInteraction        = r.MentorInClientInteraction,
        SubmissionLeadTime               = r.SubmissionLeadTime,
        SubmissionLeadTimeCustom         = r.SubmissionLeadTimeCustom,
        ReviewIterations                 = r.ReviewIterations,
        FeedbackType                     = r.FeedbackType,
        DecisionInvolvement              = MentorPreferenceTokens.Split(r.DecisionInvolvement),
        QualityDescription               = r.QualityDescription,
        QualityFocusAreas                = MentorPreferenceTokens.Split(r.QualityFocusAreas),
        RedLines                         = r.RedLines,
        AvailableTimes                   = r.AvailableTimes,
        MeetingFormat                    = r.MeetingFormat,
        SchedulingMethod                 = r.SchedulingMethod,
        PreferenceEvents                 = MentorPreferenceTokens.Split(r.PreferenceEvents),
        PreferenceFrequency              = r.PreferenceFrequency,
    };

    // Allowed-value sets — must mirror the constants in
    // Shared/AuthSharedModels/MentorPreferencesDto.cs.
    private static readonly HashSet<string> InvolvementLevels      = new() { "low", "medium", "high" };
    private static readonly HashSet<string> Channels               = new() { "email", "whatsapp", "slack", "other" };
    private static readonly HashSet<string> ResponseTimes          = new() { "24h", "1to3d", "week" };
    private static readonly HashSet<string> CommunicationFreqs     = new() { "weekly", "biweekly", "asneeded" };
    private static readonly HashSet<string> UpdateFreqs            = new() { "weekly", "biweekly" };
    private static readonly HashSet<string> ClientInteractionFreqs = new() { "monthly", "biweekly", "custom" };
    private static readonly HashSet<string> ClientInvolvement      = new() { "yes", "no", "key_milestones" };
    private static readonly HashSet<string> LeadTimes              = new() { "2d", "3to5d", "custom" };
    private static readonly HashSet<string> FeedbackTypes          = new() { "written", "verbal", "both" };
    private static readonly HashSet<string> MeetingFormats         = new() { "online", "in_person", "both" };
    private static readonly HashSet<string> SchedulingMethods      = new() { "student", "fixed" };
    private static readonly HashSet<string> DigestFrequencies      = new() { "immediate", "daily", "weekly" };

    private sealed class MentorPreferencesRow
    {
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
        public string?  PreferenceEvents                 { get; set; }
        public string?  PreferenceFrequency              { get; set; }
    }
}
