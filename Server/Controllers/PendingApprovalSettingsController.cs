using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  PendingApprovalSettingsController — /api/management/pending-approval-settings
//
//  Single-row config table. The page that reads it is admin-only, but the
//  GET endpoint is broadly available because non-admin code paths (reminder
//  dispatchers, project-health updaters, etc.) may want to read the same
//  knobs. The PUT is locked to Admin / Staff.
//
//  Validation:
//    • WarningAfter < EscalationAfter < CriticalAfter (strictly ascending)
//    • All thresholds 1..120 days
//    • IntervalDays 1..30 (only enforced when ReminderFrequency = EveryNDays)
//    • ReminderFrequency must be in the controlled vocabulary
// ─────────────────────────────────────────────────────────────────────────────

[Route("api/management/pending-approval-settings")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
public class PendingApprovalSettingsController : ControllerBase
{
    private readonly DbRepository _db;

    public PendingApprovalSettingsController(DbRepository db) => _db = db;

    // ── GET /api/management/pending-approval-settings ───────────────────────
    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Get(int authUserId)
    {
        const string sql = @"
            SELECT  s.WarningAfterDays, s.EscalationAfterDays, s.CriticalAfterDays,
                    s.ChannelEmail, s.ChannelInSystem, s.ChannelSlack,
                    s.ReminderFrequency, s.ReminderIntervalDays,
                    s.RecipientMentor, s.RecipientLecturer,
                    s.RecipientCoordinator, s.RecipientTeam,
                    s.EscalateNotifyLecturer, s.EscalateNotifyCoordinator,
                    s.MarkProjectAtRisk, s.ShowInManagementDashboard,
                    s.LecturerCanApproveWithoutMentor, s.LecturerCanRejectWithoutMentor,
                    s.LecturerCanReopenSubmissions, s.LecturerCanForceMilestone,
                    s.AutoEscalation, s.AutoProjectHealthUpdates,
                    s.AutoReminderScheduling, s.AutoNotificationCleanup,
                    s.UpdatedAt, s.UpdatedByUserId,
                    CASE
                        WHEN u.Id IS NULL THEN ''
                        ELSE TRIM(COALESCE(u.FirstName,'') || ' ' || COALESCE(u.LastName,''))
                    END                                AS UpdatedByName
            FROM    PendingApprovalSettings s
            LEFT JOIN users u ON u.Id = s.UpdatedByUserId
            WHERE   s.Id = 1
            LIMIT 1";

        var dto = (await _db.GetRecordsAsync<PendingApprovalSettingsDto>(sql))
                    ?.FirstOrDefault();

        // No row yet → return the DTO's compile-time defaults so the UI can
        // render immediately without a separate "no config" branch.
        dto ??= new PendingApprovalSettingsDto();
        return Ok(dto);
    }

    // ── PUT /api/management/pending-approval-settings ───────────────────────
    [HttpPut]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Save(int authUserId, [FromBody] PendingApprovalSettingsDto req)
    {
        if (req is null) return BadRequest("גוף בקשה ריק");

        // Threshold bounds + ordering.
        if (req.WarningAfterDays    is < 1 or > 120) return BadRequest("סף תזכורת חייב להיות בין 1 ל-120 ימים");
        if (req.EscalationAfterDays is < 1 or > 120) return BadRequest("סף אסקלציה חייב להיות בין 1 ל-120 ימים");
        if (req.CriticalAfterDays   is < 1 or > 120) return BadRequest("סף קריטי חייב להיות בין 1 ל-120 ימים");
        if (!(req.WarningAfterDays < req.EscalationAfterDays
              && req.EscalationAfterDays < req.CriticalAfterDays))
            return BadRequest("הספים חייבים להיות בסדר עולה: תזכורת < אסקלציה < קריטי");

        // Frequency must be in the allowed set.
        if (!ReminderFrequencies.All.Contains(req.ReminderFrequency))
            return BadRequest("תדירות תזכורת לא חוקית");

        // Interval bounds only relevant when EveryNDays is selected.
        if (req.ReminderFrequency == ReminderFrequencies.EveryNDays
            && req.ReminderIntervalDays is < 1 or > 30)
            return BadRequest("מרווח התזכורת חייב להיות בין 1 ל-30 ימים");

        const string upsert = @"
            INSERT INTO PendingApprovalSettings (
                Id,
                WarningAfterDays, EscalationAfterDays, CriticalAfterDays,
                ChannelEmail, ChannelInSystem, ChannelSlack,
                ReminderFrequency, ReminderIntervalDays,
                RecipientMentor, RecipientLecturer, RecipientCoordinator, RecipientTeam,
                EscalateNotifyLecturer, EscalateNotifyCoordinator,
                MarkProjectAtRisk, ShowInManagementDashboard,
                LecturerCanApproveWithoutMentor, LecturerCanRejectWithoutMentor,
                LecturerCanReopenSubmissions, LecturerCanForceMilestone,
                AutoEscalation, AutoProjectHealthUpdates,
                AutoReminderScheduling, AutoNotificationCleanup,
                UpdatedAt, UpdatedByUserId
            ) VALUES (
                1,
                @WarningAfterDays, @EscalationAfterDays, @CriticalAfterDays,
                @ChannelEmailI, @ChannelInSystemI, @ChannelSlackI,
                @ReminderFrequency, @ReminderIntervalDays,
                @RecipientMentorI, @RecipientLecturerI, @RecipientCoordinatorI, @RecipientTeamI,
                @EscalateNotifyLecturerI, @EscalateNotifyCoordinatorI,
                @MarkProjectAtRiskI, @ShowInManagementDashboardI,
                @LecturerCanApproveWithoutMentorI, @LecturerCanRejectWithoutMentorI,
                @LecturerCanReopenSubmissionsI, @LecturerCanForceMilestoneI,
                @AutoEscalationI, @AutoProjectHealthUpdatesI,
                @AutoReminderSchedulingI, @AutoNotificationCleanupI,
                datetime('now'), @UpdatedByUserId
            )
            ON CONFLICT(Id) DO UPDATE SET
                WarningAfterDays                = excluded.WarningAfterDays,
                EscalationAfterDays             = excluded.EscalationAfterDays,
                CriticalAfterDays               = excluded.CriticalAfterDays,
                ChannelEmail                    = excluded.ChannelEmail,
                ChannelInSystem                 = excluded.ChannelInSystem,
                ChannelSlack                    = excluded.ChannelSlack,
                ReminderFrequency               = excluded.ReminderFrequency,
                ReminderIntervalDays            = excluded.ReminderIntervalDays,
                RecipientMentor                 = excluded.RecipientMentor,
                RecipientLecturer               = excluded.RecipientLecturer,
                RecipientCoordinator            = excluded.RecipientCoordinator,
                RecipientTeam                   = excluded.RecipientTeam,
                EscalateNotifyLecturer          = excluded.EscalateNotifyLecturer,
                EscalateNotifyCoordinator       = excluded.EscalateNotifyCoordinator,
                MarkProjectAtRisk               = excluded.MarkProjectAtRisk,
                ShowInManagementDashboard       = excluded.ShowInManagementDashboard,
                LecturerCanApproveWithoutMentor = excluded.LecturerCanApproveWithoutMentor,
                LecturerCanRejectWithoutMentor  = excluded.LecturerCanRejectWithoutMentor,
                LecturerCanReopenSubmissions    = excluded.LecturerCanReopenSubmissions,
                LecturerCanForceMilestone       = excluded.LecturerCanForceMilestone,
                AutoEscalation                  = excluded.AutoEscalation,
                AutoProjectHealthUpdates        = excluded.AutoProjectHealthUpdates,
                AutoReminderScheduling          = excluded.AutoReminderScheduling,
                AutoNotificationCleanup         = excluded.AutoNotificationCleanup,
                UpdatedAt                       = excluded.UpdatedAt,
                UpdatedByUserId                 = excluded.UpdatedByUserId";

        await _db.SaveDataAsync(upsert, new
        {
            req.WarningAfterDays, req.EscalationAfterDays, req.CriticalAfterDays,
            ChannelEmailI            = req.ChannelEmail    ? 1 : 0,
            ChannelInSystemI         = req.ChannelInSystem ? 1 : 0,
            ChannelSlackI            = req.ChannelSlack    ? 1 : 0,
            req.ReminderFrequency, req.ReminderIntervalDays,
            RecipientMentorI         = req.RecipientMentor      ? 1 : 0,
            RecipientLecturerI       = req.RecipientLecturer    ? 1 : 0,
            RecipientCoordinatorI    = req.RecipientCoordinator ? 1 : 0,
            RecipientTeamI           = req.RecipientTeam        ? 1 : 0,
            EscalateNotifyLecturerI       = req.EscalateNotifyLecturer    ? 1 : 0,
            EscalateNotifyCoordinatorI    = req.EscalateNotifyCoordinator ? 1 : 0,
            MarkProjectAtRiskI            = req.MarkProjectAtRisk         ? 1 : 0,
            ShowInManagementDashboardI    = req.ShowInManagementDashboard ? 1 : 0,
            LecturerCanApproveWithoutMentorI = req.LecturerCanApproveWithoutMentor ? 1 : 0,
            LecturerCanRejectWithoutMentorI  = req.LecturerCanRejectWithoutMentor  ? 1 : 0,
            LecturerCanReopenSubmissionsI    = req.LecturerCanReopenSubmissions    ? 1 : 0,
            LecturerCanForceMilestoneI       = req.LecturerCanForceMilestone       ? 1 : 0,
            AutoEscalationI            = req.AutoEscalation           ? 1 : 0,
            AutoProjectHealthUpdatesI  = req.AutoProjectHealthUpdates ? 1 : 0,
            AutoReminderSchedulingI    = req.AutoReminderScheduling   ? 1 : 0,
            AutoNotificationCleanupI   = req.AutoNotificationCleanup  ? 1 : 0,
            UpdatedByUserId            = authUserId,
        });

        return await Get(authUserId);
    }
}