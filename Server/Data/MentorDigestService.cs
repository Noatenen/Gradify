using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorDigestService — delivery for the one daily mentor digest.
//
//  ONE CODE PATH, TWO TRIGGERS. The scheduled 07:00 run and the admin manual
//  trigger both call SendForMentorAsync. There is no "test digest" variant:
//  a test that exercises different logic than production tests nothing.
//
//  ORDER OF OPERATIONS IS THE IDEMPOTENCY GUARANTEE
//      1. compose (pure)
//      2. bail if empty
//      3. CLAIM the day by INSERTing the ledger row — UNIQUE(MentorUserId,
//         RunDate) makes a duplicate attempt fail here
//      4. only then write the notification and send the email
//
//  Claiming before sending means a crash between 3 and 4 loses a digest, while
//  the reverse order would send two. Losing one morning's summary is a
//  non-event; emailing a mentor the same digest twice is the failure people
//  actually notice.
// ─────────────────────────────────────────────────────────────────────────────
public class MentorDigestService
{
    private readonly DbRepository           _db;
    private readonly MentorAttentionService _attention;
    private readonly EmailHelper            _email;
    private readonly IConfiguration         _config;
    private readonly IWebHostEnvironment    _env;
    private readonly ILogger<MentorDigestService> _log;

    public MentorDigestService(
        DbRepository db,
        MentorAttentionService attention,
        EmailHelper email,
        IConfiguration config,
        IWebHostEnvironment env,
        ILogger<MentorDigestService> log)
    {
        _db        = db;
        _attention = attention;
        _email     = email;
        _config    = config;
        _env       = env;
        _log       = log;
    }

    /// <summary>
    /// True when the Motiva logo is actually present in the web root.
    ///
    /// <para>Checked BEFORE composing, so the HTML only emits a cid reference
    /// for an image that will really be attached. A missing asset therefore
    /// yields a logo-less but otherwise complete digest, never a broken-image
    /// placeholder and never a failed send.</para>
    ///
    /// <para>Resolved through WebRootFileProvider because the file lives in the
    /// Blazor client's wwwroot and reaches the server via the static-web-assets
    /// manifest — it is not physically under Server/wwwroot.</para>
    /// </summary>
    private bool LogoAvailable
    {
        get
        {
            try
            {
                var f = _env.WebRootFileProvider.GetFileInfo(MentorDigestComposer.LogoWebPath);
                return f.Exists && !f.IsDirectory;
            }
            catch { return false; }
        }
    }

    /// <summary>Why a run happened. Recorded on the ledger row.</summary>
    public static class Triggers
    {
        public const string Scheduled = "Scheduled";
        public const string Manual    = "Manual";
    }

    /// <summary>Outcome of one mentor's digest attempt.</summary>
    /// <param name="Preview">The plain-text body — also what the in-app
    /// notification carries.</param>
    /// <param name="Html">The exact HTML that was (or would have been) mailed.
    /// Returned so the Admin-only manual trigger can verify the rendered email
    /// — layout, RTL, counts, limits and deep links — without waiting for the
    /// scheduled run or needing a mailbox. Null when nothing was composed.</param>
    public sealed record DigestResult(
        int    MentorUserId,
        string MentorName,
        bool   Sent,
        string Reason,
        int    Total,
        bool   EmailAttempted,
        bool   EmailSent,
        string? Preview,
        string? Html = null);

    // ── One mentor ───────────────────────────────────────────────────────────

    /// <summary>
    /// Composes and delivers this mentor's digest for today, unless it is empty
    /// or already sent.
    /// </summary>
    /// <param name="force">Manual-trigger escape hatch: bypasses the
    /// already-sent check so an admin can re-run a digest while testing. It does
    /// NOT bypass the empty check — there is never a reason to send "you have
    /// nothing". Never set by the scheduler.</param>
    public async Task<DigestResult> SendForMentorAsync(
        int mentorUserId,
        string trigger = Triggers.Scheduled,
        bool force = false)
    {
        string name = await GetMentorNameAsync(mentorUserId);

        var attention  = await _attention.GetAsync(mentorUserId);
        var extensions = await LoadExtensionContextAsync(attention);

        // The cid is passed only when the asset resolves — see LogoAvailable.
        bool withLogo  = LogoAvailable;
        var digest     = MentorDigestComposer.Compose(
            attention, BaseUrl, extensions,
            logoCid: withLogo ? MentorDigestComposer.LogoCid : null);

        // Rule: no digest when there is nothing to say. A daily "you have
        // nothing" email is how a digest gets filtered to spam, and once it is
        // filtered the days that DO matter are filtered with it.
        if (digest.IsEmpty)
            return new DigestResult(mentorUserId, name, false, "אין פריטים ממתינים", 0, false, false, null);

        string runDate = IsraelTime.Today.ToString("yyyy-MM-dd");

        if (force)
            await _db.SaveDataAsync(
                "DELETE FROM MentorDigestRuns WHERE MentorUserId = @UserId AND RunDate = @RunDate",
                new { UserId = mentorUserId, RunDate = runDate });

        if (!await TryClaimDayAsync(mentorUserId, runDate, digest.Total, trigger))
            return new DigestResult(mentorUserId, name, false, "כבר נשלח היום", digest.Total, false, false, null);

        // ── In-app ───────────────────────────────────────────────────────────
        // Written directly rather than through NotificationDispatcher because
        // the email here is a purpose-built HTML digest, not the dispatcher's
        // generic title+message wrapper. The preference check below is the same
        // one the dispatcher would have applied.
        await NotificationHelper.CreateAsync(
            _db, mentorUserId,
            title:   digest.Title,
            message: digest.PlainText,
            type:    NotificationTypes.MentorDailyDigest);

        // ── Email, per the mentor's own preference ───────────────────────────
        bool emailAttempted = false, emailSent = false;
        var (address, wantsEmail) = await ResolveEmailAsync(mentorUserId);

        if (wantsEmail && !string.IsNullOrWhiteSpace(address))
        {
            emailAttempted = true;
            try
            {
                emailSent = await _email.SendEmail(new MailModel
                {
                    Subject    = digest.Title,
                    Body       = digest.HtmlBody,
                    Recipients = new List<string> { address! },

                    // Embedded, not linked. The digest is the only sender in the
                    // application that supplies this; every other caller leaves
                    // InlineImages empty and takes EmailHelper's original path.
                    InlineImages = withLogo
                        ? new List<MailInlineImage>
                          {
                              new MailInlineImage
                              {
                                  WebPath     = MentorDigestComposer.LogoWebPath,
                                  ContentId   = MentorDigestComposer.LogoCid,
                                  ContentType = "image/png",
                              },
                          }
                        : new List<MailInlineImage>(),
                });
            }
            catch (Exception ex)
            {
                // Never fail the run over email: the in-app digest is already
                // written and is the primary channel.
                _log.LogError(ex, "Mentor digest email failed for user {UserId}", mentorUserId);
            }
        }

        return new DigestResult(
            mentorUserId, name, true, "נשלח", digest.Total,
            emailAttempted, emailSent, digest.PlainText, digest.HtmlBody);
    }

    // ── All mentors ──────────────────────────────────────────────────────────

    /// <summary>Runs the digest for every active mentor. One mentor's failure
    /// never stops the rest — a bad row in one caseload must not cost everyone
    /// else their morning summary.</summary>
    public async Task<List<DigestResult>> SendForAllMentorsAsync(
        string trigger = Triggers.Scheduled,
        bool force = false)
    {
        var results = new List<DigestResult>();

        foreach (int mentorId in await _attention.GetActiveMentorIdsAsync())
        {
            try
            {
                results.Add(await SendForMentorAsync(mentorId, trigger, force));
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Mentor digest failed for user {UserId}", mentorId);
                results.Add(new DigestResult(
                    mentorId, $"#{mentorId}", false, $"שגיאה: {ex.Message}", 0, false, false, null));
            }
        }

        return results;
    }

    // ── Internals ────────────────────────────────────────────────────────────

    /// <summary>
    /// Absolute origin for email links. Empty is a supported state — the
    /// composer then renders the digest without anchors rather than emitting
    /// relative hrefs, which a mail client would resolve against itself.
    /// </summary>
    private string BaseUrl => _config.GetValue<string>("App:BaseUrl") ?? "";

    /// <summary>
    /// Claims today for this mentor. Returns false when the day is already
    /// claimed.
    ///
    /// <para>The UNIQUE constraint does the work; this catches the resulting
    /// exception rather than pre-checking with a SELECT, because a check-then-act
    /// leaves the exact race it is meant to close — a restart landing two runs
    /// milliseconds apart, or two admins triggering at once.</para>
    /// </summary>
    private async Task<bool> TryClaimDayAsync(int mentorUserId, string runDate, int itemCount, string trigger)
    {
        try
        {
            int rows = await _db.SaveDataAsync(@"
                INSERT INTO MentorDigestRuns (MentorUserId, RunDate, ItemCount, Trigger)
                VALUES (@UserId, @RunDate, @ItemCount, @Trigger)",
                new { UserId = mentorUserId, RunDate = runDate, ItemCount = itemCount, Trigger = trigger });

            return rows > 0;
        }
        catch (Microsoft.Data.Sqlite.SqliteException ex)
            when (ex.SqliteErrorCode == 19)   // SQLITE_CONSTRAINT — already claimed
        {
            return false;
        }
    }

    /// <summary>
    /// The extension facts for the mentor's awaiting requests — current due
    /// date, requested due date, and the task or milestone they apply to.
    ///
    /// <para><b>Loaded here rather than in MentorAttentionService.</b> These
    /// fields are meaningful only for RequestType = Extension and only to the
    /// digest; putting them on the shared attention model would push
    /// request-type-specific columns into every mentor screen that reads it.
    /// The composer stays a pure function and simply receives them.</para>
    ///
    /// <para>One extra query per digest, bounded by the mentor's awaiting
    /// requests (a handful in practice). Returns an empty map on any failure,
    /// which degrades the two context lines rather than the whole send.</para>
    /// </summary>
    private async Task<IReadOnlyDictionary<int, ExtensionContext>> LoadExtensionContextAsync(
        MentorAttentionDto attention)
    {
        var ids = attention.Items
            .Where(i => i.Kind == MentorAttentionKind.Request)
            .Select(i => i.EntityId)
            .Distinct()
            .ToList();

        if (ids.Count == 0) return new Dictionary<int, ExtensionContext>();

        // LEFT JOINs throughout: an extension may target a task OR a milestone
        // and never both, so an inner join on either would drop half the rows.
        const string sql = @"
            SELECT  e.RequestId,
                    e.CurrentDueDate,
                    e.RequestedDueDate,
                    t.Title  AS TaskTitle,
                    mt.Title AS MilestoneTitle
            FROM    ProjectRequestExtensions e
            LEFT JOIN Tasks                    t   ON t.Id   = e.TaskId
            LEFT JOIN ProjectMilestones        pm  ON pm.Id  = e.ProjectMilestoneId
            LEFT JOIN AcademicYearMilestones   aym ON aym.Id = pm.AcademicYearMilestoneId
            LEFT JOIN MilestoneTemplates       mt  ON mt.Id  = aym.MilestoneTemplateId
            WHERE   e.RequestId IN @Ids";

        var rows = await _db.GetRecordsAsync<ExtensionRow>(sql, new { Ids = ids });
        if (rows is null) return new Dictionary<int, ExtensionContext>();

        return rows.ToDictionary(
            r => r.RequestId,
            r => new ExtensionContext(
                r.CurrentDueDate,
                r.RequestedDueDate,
                // A request with neither target is the "אחר / כללי" case the
                // schema explicitly allows — labelled, not left blank.
                !string.IsNullOrWhiteSpace(r.TaskTitle)      ? $"משימה: {r.TaskTitle}"
                : !string.IsNullOrWhiteSpace(r.MilestoneTitle) ? $"אבן דרך: {r.MilestoneTitle}"
                : "בקשה כללית"));
    }

    private sealed class ExtensionRow
    {
        public int       RequestId        { get; set; }
        public DateTime? CurrentDueDate   { get; set; }
        public DateTime? RequestedDueDate { get; set; }
        public string?   TaskTitle        { get; set; }
        public string?   MilestoneTitle   { get; set; }
    }

    private async Task<(string? Address, bool WantsEmail)> ResolveEmailAsync(int mentorUserId)
    {
        const string sql = @"
            SELECT  COALESCE(u.Email, '')      AS Email,
                    COALESCE(p.EmailEnabled, -1) AS EmailEnabledRaw
            FROM    users u
            LEFT JOIN UserNotificationPreferences p
                            ON p.UserId = u.Id AND p.NotificationType = @Type
            WHERE   u.Id = @UserId
              AND   u.IsActive = 1";

        var row = (await _db.GetRecordsAsync<EmailPrefRow>(
            sql, new { UserId = mentorUserId, Type = NotificationTypes.MentorDailyDigest }))
            ?.FirstOrDefault();

        if (row is null) return (null, false);

        // -1 = no saved row → fall back to the shared default for this type,
        // exactly as NotificationDispatcher does. Reusing that call rather than
        // hardcoding true keeps one answer to "what does a fresh account get".
        var (_, defaultEmail) = NotificationTypes.DefaultsForType(NotificationTypes.MentorDailyDigest);
        bool wants = row.EmailEnabledRaw == -1 ? defaultEmail : row.EmailEnabledRaw == 1;

        return (row.Email, wants);
    }

    private async Task<string> GetMentorNameAsync(int mentorUserId)
    {
        var rows = await _db.GetRecordsAsync<string>(
            "SELECT FirstName || ' ' || LastName FROM users WHERE Id = @Id",
            new { Id = mentorUserId });
        return rows?.FirstOrDefault() ?? $"#{mentorUserId}";
    }

    private sealed class EmailPrefRow
    {
        public string Email           { get; set; } = "";
        public int    EmailEnabledRaw { get; set; }
    }
}
