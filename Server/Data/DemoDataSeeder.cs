namespace AuthWithAdmin.Server.Data;

using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Shared.AuthSharedModels;

// ─────────────────────────────────────────────────────────────────────────────
//  DemoDataSeeder — Development-only, idempotent demo dataset.
//
//  Builds one coherent end-to-end Motiva story (see design/demo-story.md and
//  design/demo-data-spec.md for the narrative and the exact field-by-field
//  contract this class implements) so the Dashboard, My Tasks, Project Stages,
//  Requests and Notifications screens can be evaluated against consistent,
//  connected data instead of ad-hoc manual records.
//
//  SAFETY — this class must never run in Production:
//    - It is only ever invoked from DemoDataController, whose every action
//      re-checks IHostEnvironment.IsDevelopment() itself (belt-and-suspenders —
//      do not remove that check from the controller even though callers here
//      are already expected to be Development-only).
//    - Nothing in this file reads or writes outside the three reserved
//      ProjectNumbers (9001/9002/9003) and the eight fixed demo emails below.
//
//  IDEMPOTENCY — the master marker is ProjectNumber = 9001. SeedIfMissingAsync
//  is a complete no-op if that project already exists: it never patches,
//  merges, or upserts. ResetAsync deletes everything tied to the three
//  reserved ProjectNumbers and eight demo emails, in explicit reverse-
//  dependency order, then SeedIfMissingAsync can run again from a clean slate.
// ─────────────────────────────────────────────────────────────────────────────
public class DemoDataSeeder
{
    private readonly DbRepository _db;
    private readonly PasswordService _passwordService;

    public const string DemoPassword = "MotivaDemo2026!";

    private static readonly string[] DemoEmails =
    {
        "noa.demo@motiva.local",
        "ofir.demo@motiva.local",
        "yanai.mentor.demo@motiva.local",
        "jenny.staff.demo@motiva.local",
        "daniel.demo@motiva.local",
        "maya.demo@motiva.local",
        "idan.demo@motiva.local",
        "shira.demo@motiva.local",
        "merav.mentor.demo@motiva.local",
        "roni.demo@motiva.local",
        "tal.demo@motiva.local",
        "yuval.demo@motiva.local",
        "avigail.demo@motiva.local",
    };

    private static readonly int[] DemoProjectNumbers = { 9001, 9002, 9003, 9004, 9005 };

    public DemoDataSeeder(DbRepository db, PasswordService passwordService)
    {
        _db = db;
        _passwordService = passwordService;
    }

    // Captured once per seed run so every relative date in the story is
    // computed against the same instant, then stored as an absolute value —
    // re-seeding later recomputes everything fresh against the new "now".
    private DateTime _now;
    private DateTime Day(int offsetDays) => _now.AddDays(offsetDays);

    // ═════════════════════════════════════════════════════════════════════
    //  SEED
    // ═════════════════════════════════════════════════════════════════════
    public async Task<string> SeedIfMissingAsync()
    {
        var existing = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM Projects WHERE ProjectNumber = @Num", new { Num = 9001 });
        if (existing is not null && existing.Any())
            return "Demo data already present (ProjectNumber 9001 exists) — no-op.";

        _now = DateTime.Now;

        int academicYearId = await EnsureCurrentAcademicYearIdAsync();
        int projectTypeId = await GetOrCreateProjectTypeIdAsync("Technological");

        // ── Users ────────────────────────────────────────────────────────
        int noaId = await CreateUserAsync("noa.demo@motiva.local", "נועה", "כהן");
        int ofirId = await CreateUserAsync("ofir.demo@motiva.local", "אופיר", "שרעבי");
        int yanaiId = await CreateUserAsync("yanai.mentor.demo@motiva.local", "ינאי", "כרמי");
        int jennyId = await CreateUserAsync("jenny.staff.demo@motiva.local", "ג'ני", "אלון");
        int danielId = await CreateUserAsync("daniel.demo@motiva.local", "דניאל", "אבני");
        int mayaId = await CreateUserAsync("maya.demo@motiva.local", "מאיה", "גולן");
        int idanId = await CreateUserAsync("idan.demo@motiva.local", "עידן", "ברק");
        int shiraId = await CreateUserAsync("shira.demo@motiva.local", "שירה", "מזרחי");
        int meravId = await CreateUserAsync("merav.mentor.demo@motiva.local", "מירב", "שגיא");
        int roniId = await CreateUserAsync("roni.demo@motiva.local", "רוני", "אשכנזי");
        int talId = await CreateUserAsync("tal.demo@motiva.local", "טל", "ברקוביץ'");
        int yuvalId = await CreateUserAsync("yuval.demo@motiva.local", "יובל", "שני");
        int avigailId = await CreateUserAsync("avigail.demo@motiva.local", "אביגיל", "נוי");

        await AddRoleAsync(noaId, Roles.Student);
        await AddRoleAsync(ofirId, Roles.Student);
        await AddRoleAsync(yanaiId, Roles.Mentor);
        await AddRoleAsync(jennyId, Roles.Staff);
        await AddRoleAsync(jennyId, Roles.Admin); // Staff⇒Admin invariant enforced elsewhere in the app
        await AddRoleAsync(danielId, Roles.Student);
        await AddRoleAsync(mayaId, Roles.Student);
        await AddRoleAsync(idanId, Roles.Student);
        await AddRoleAsync(shiraId, Roles.Student);
        await AddRoleAsync(meravId, Roles.Mentor);
        await AddRoleAsync(roniId, Roles.Student);
        await AddRoleAsync(talId, Roles.Student);
        await AddRoleAsync(yuvalId, Roles.Student);
        await AddRoleAsync(avigailId, Roles.Student);

        // ── Project 9001 — the primary story ────────────────────────────
        int team1 = await CreateTeamAsync(academicYearId, "צוות Motiva");
        await AddTeamMemberAsync(team1, noaId);
        await AddTeamMemberAsync(team1, ofirId);
        int project1 = await CreateProjectAsync(9001, academicYearId, team1, projectTypeId,
            "Motiva — Final Project Management Platform");
        await AddProjectMentorAsync(project1, yanaiId);

        var stageIds1 = await GetStageIdsAsync(academicYearId);

        // Stages 1–3: one completed milestone each, purely so "current stage"
        // derivation lands correctly on Specification (see BuildProgressAsync
        // in RoadmapStagesController — a stage is Current only if it's the
        // first, in DisplayOrder, with ≥1 linked milestone that isn't 100% done).
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project1, stageIds1["Selection"],
            "בחירת נושא ואישור פרויקט", Day(-80), Day(-81));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project1, stageIds1["Kickoff"],
            "פגישת פתיחה עם הלקוח", Day(-70), Day(-71));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project1, stageIds1["Definition"],
            "מסמך הגדרת בעיה ומטרות", Day(-55), Day(-56));

        // Specification stage — the 8 deliverables. 5 completed / 8 total =
        // round(100*5/8) = 62% exactly (see demo-data-spec.md for the math).
        int msResearchPlan = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "תוכנית מחקר UX", Day(-49), "Completed", Day(-50));
        int msInterviews = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "סיכום ראיונות משתמשים", Day(-35), "Completed", Day(-36));
        int msPersonas = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "הגדרת פרסונות", Day(-28), "Completed", Day(-29));
        int msFlows = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "מיפוי תהליכי משתמש", Day(-25), "Completed", Day(-26));
        int msLoFiWireframes = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "Wireframes ברמת דיוק נמוכה", Day(-21), "Completed", Day(-22));
        int msUxSpec = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "מסמך אפיון UX", Day(-16), "InProgress", null);
        int msHiFiWireframes = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "Wireframes ברמת דיוק גבוהה", Day(2), "InProgress", null);
        int msUsabilityPlan = await CreateFullMilestoneAsync(academicYearId, project1, stageIds1["Specification"],
            "תוכנית בדיקות שימושיות", Day(10), "NotStarted", null);

        // Stages 5–7: one not-started milestone each — reads as genuine
        // upcoming stages ("Future") rather than "NotApplicable".
        await SeedSimpleFutureMilestoneAsync(academicYearId, project1, stageIds1["Development"],
            "אב-טיפוס ראשוני", Day(30));
        await SeedSimpleFutureMilestoneAsync(academicYearId, project1, stageIds1["Evaluation"],
            "תוכנית הערכת משתמשים", Day(45));
        await SeedSimpleFutureMilestoneAsync(academicYearId, project1, stageIds1["SubmissionGrading"],
            "הגשה סופית", Day(60));

        // ── Tasks ────────────────────────────────────────────────────────
        int taskResearchPlan = await CreateTaskAsync(project1, msResearchPlan, "כתיבת תוכנית מחקר UX", noaId, yanaiId,
            "Done", Day(-49), isSubmission: true);
        await CreateTaskAsync(project1, msPersonas, "ניתוח ראיונות והגדרת פרסונות", noaId, yanaiId,
            "Done", Day(-28), isSubmission: false);
        await CreateTaskAsync(project1, msFlows, "מיפוי תהליכי משתמש עיקריים", ofirId, yanaiId,
            "Done", Day(-25), isSubmission: false);
        await CreateTaskAsync(project1, msLoFiWireframes, "עיצוב Wireframes ברמת דיוק נמוכה", ofirId, yanaiId,
            "Done", Day(-21), isSubmission: false);

        int taskUxSpec = await CreateTaskAsync(project1, msUxSpec, "כתיבת מסמך אפיון UX", noaId, yanaiId,
            "ReturnedForRevision", Day(-14), isSubmission: true);
        int taskHiFiWireframes = await CreateTaskAsync(project1, msHiFiWireframes, "בניית Wireframes ברמת דיוק גבוהה",
            ofirId, yanaiId, "InProgress", Day(2), isSubmission: true);
        // A second, smaller submission under the same milestone — genuinely
        // awaiting the mentor's review right now (MentorStatus='Pending').
        // Without this, Yanai's Submissions queue would be empty for an
        // "ongoing" project — nothing sitting in every state is what breaks
        // the cross-screen story, not just the returned/overdue items.
        int taskWireframesCheckin = await CreateTaskAsync(project1, msHiFiWireframes,
            "שיתוף גרסת ביניים של Wireframes לבדיקת המנחה", ofirId, yanaiId,
            "SubmittedToMentor", Day(-1), isSubmission: true);
        await CreateTaskAsync(project1, msUsabilityPlan, "תכנון תוכנית בדיקות שימושיות", null, yanaiId,
            "Open", Day(10), isSubmission: false);
        await CreateTaskAsync(project1, msInterviews, "עדכון סיכום ראיונות משתמשים לפי הערות המנחה", ofirId, yanaiId,
            "Open", Day(-4), isSubmission: false);
        await CreateTaskAsync(project1, null, "תיאום ישיבת סטטוס שבועית עם המנחה", null, noaId,
            "InProgress", Day(5), isSubmission: false, isMandatory: false);

        // ── Submissions — one of each MentorStatus, so Submissions screens
        //    (student + mentor) show a believable history, not one incident ──
        await CreateSubmissionAsync(taskResearchPlan, noaId,
            submittedAt: Day(-49),
            driveUrl: RandomDriveUrl(),
            status: "Reviewed", mentorStatus: "Approved",
            mentorFeedback: "עבודה טובה, אפשר להמשיך הלאה.", mentorReviewedAt: Day(-47),
            moodleSubmittedAt: Day(-46));

        string uxSpecFeedback =
            "1. הפרסונה המרכזית סותרת יעד שהוגדר קודם — יש ליישב את הסתירה.\n" +
            "2. תהליך ה-Onboarding חסר מסלול למקרה של שגיאה.\n" +
            "3. לא מטופלות דרישות נגישות עבור מסכי המנחה.\n" +
            "4. פרק מדדי ההצלחה לא מקושר בחזרה להגדרת הבעיה המקורית.";
        await CreateSubmissionAsync(taskUxSpec, noaId,
            submittedAt: Day(-14),
            driveUrl: RandomDriveUrl(),
            status: "NeedsRevision", mentorStatus: "Returned",
            mentorFeedback: uxSpecFeedback, mentorReviewedAt: Day(-3));

        await CreateSubmissionAsync(taskWireframesCheckin, ofirId,
            submittedAt: Day(-1),
            driveUrl: RandomDriveUrl(),
            status: "Submitted", mentorStatus: "Pending",
            mentorFeedback: null, mentorReviewedAt: null);

        // ── Extension request (pending) ─────────────────────────────────
        int request1 = await CreateRequestAsync(project1, noaId, RequestTypes.Extension,
            "בקשת דחייה להגשת Wireframes ברמת דיוק גבוהה",
            "בעקבות הצורך לתקן את מסמך האפיון לפי הערות המנחה, נבקש דחייה קלה במועד הגשת ה-Wireframes ברמת דיוק גבוהה.",
            RequestStatuses.PendingMentorRecommendation, RequestPriorities.Normal, Day(-2));
        await CreateRequestExtensionAsync(request1, taskHiFiWireframes, currentDueDate: Day(2), requestedDueDate: Day(5),
            reason: "עומס עבודה עקב תיקון מסמך האפיון.");
        await CreateRequestEventAsync(request1, yanaiId, "ראיתי, אבדוק ואחזור אליכם בקרוב.", Day(-1));

        // ── Notifications ────────────────────────────────────────────────
        await CreateNotificationAsync(noaId, NotificationTypes.SubmissionFeedbackReceived,
            "משוב חדש על מסמך אפיון UX", "המנחה החזיר משוב על מסמך אפיון UX — נדרש תיקון.", Day(-3), isRead: false);
        await CreateNotificationAsync(ofirId, NotificationTypes.TaskOverdue,
            "משימה באיחור", "המשימה 'עדכון סיכום ראיונות משתמשים לפי הערות המנחה' באיחור.", Day(-1), isRead: false);
        await CreateNotificationAsync(noaId, NotificationTypes.RequestStatusChanged,
            "בקשת הדחייה נשלחה", "בקשת הדחייה שלכם ממתינה להמלצת המנחה.", Day(-2), isRead: false);
        await CreateNotificationAsync(ofirId, NotificationTypes.RequestStatusChanged,
            "בקשת הדחייה נשלחה", "בקשת הדחייה שלכם ממתינה להמלצת המנחה.", Day(-2), isRead: true);
        // "General" stands in for a course announcement — there is no dedicated
        // Announcement entity in the backend today (see the honesty note in
        // design/demo-story.md). This is a documented workaround, not a real feature.
        await CreateNotificationAsync(noaId, "General",
            "עדכון קורס", "תזכורת: בדיקת ההתקדמות באמצע הסמסטר מתקרבת.", Day(-1), isRead: false);
        await CreateNotificationAsync(ofirId, "General",
            "עדכון קורס", "תזכורת: בדיקת ההתקדמות באמצע הסמסטר מתקרבת.", Day(-1), isRead: false);
        await CreateNotificationAsync(ofirId, NotificationTypes.TaskDueSoon,
            "משימה מתקרבת", "המשימה 'בניית Wireframes ברמת דיוק גבוהה' מתקרבת למועד ההגשה.", Day(-1), isRead: false);
        // Mentor-side notification — completes the cross-screen story: Yanai's
        // own bell/inbox should reflect the same submission Noa/Ofir just made.
        await CreateNotificationAsync(yanaiId, NotificationTypes.SubmissionSubmitted,
            "הגשה חדשה ממתינה לבדיקה", "אופיר שיתף גרסת ביניים של Wireframes לבדיקה.", Day(-1), isRead: false);

        // ── Project 9002 — lightweight, "on track" ──────────────────────
        int team2 = await CreateTeamAsync(academicYearId, "צוות הספרייה הדיגיטלית");
        await AddTeamMemberAsync(team2, danielId);
        await AddTeamMemberAsync(team2, mayaId);
        int project2 = await CreateProjectAsync(9002, academicYearId, team2, projectTypeId,
            "מערכת ניהול ספרייה דיגיטלית");
        await AddProjectMentorAsync(project2, yanaiId);

        await SeedSimpleCompletedMilestoneAsync(academicYearId, project2, stageIds1["Selection"],
            "בחירת נושא ואישור פרויקט", Day(-90), Day(-91));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project2, stageIds1["Kickoff"],
            "פגישת פתיחה עם הלקוח", Day(-75), Day(-76));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project2, stageIds1["Definition"],
            "מסמך הגדרת בעיה ומטרות", Day(-60), Day(-61));
        int msLibSpec = await CreateFullMilestoneAsync(academicYearId, project2, stageIds1["Specification"],
            "מסמך אפיון ראשוני", Day(-20), "Completed", Day(-21));
        await SeedSimpleFutureMilestoneAsync(academicYearId, project2, stageIds1["Specification"],
            "Wireframes", Day(10));

        int taskLibSpec = await CreateTaskAsync(project2, msLibSpec, "כתיבת מסמך אפיון ראשוני", danielId, yanaiId,
            "Done", Day(-20), isSubmission: true);
        await CreateTaskAsync(project2, null, "בחירת ספריית UI לפרויקט", mayaId, yanaiId,
            "Done", Day(-15), isSubmission: false);
        await CreateTaskAsync(project2, null, "תכנון מבנה בסיס הנתונים", danielId, yanaiId,
            "InProgress", Day(8), isSubmission: false);
        await CreateSubmissionAsync(taskLibSpec, danielId,
            submittedAt: Day(-20), driveUrl: RandomDriveUrl(),
            status: "Reviewed", mentorStatus: "Approved",
            mentorFeedback: "מסמך מסודר וברור, מאושר להמשך.", mentorReviewedAt: Day(-19),
            moodleSubmittedAt: Day(-18));

        int req2 = await CreateRequestAsync(project2, mayaId, RequestTypes.Meeting,
            "בקשה לפגישת ייעוץ נוספת",
            "נשמח לפגישה קצרה לגבי בחירת הטכנולוגיה למערכת החיפוש.",
            RequestStatuses.Resolved, RequestPriorities.Low, Day(-30));
        await CreateRequestEventAsync(req2, yanaiId, "נקבעה פגישה, סוכם על ElasticSearch.", Day(-28));

        // ── Project 9003 — lightweight, "at risk" ───────────────────────
        int team3 = await CreateTeamAsync(academicYearId, "צוות המסחר האלקטרוני");
        await AddTeamMemberAsync(team3, idanId);
        await AddTeamMemberAsync(team3, shiraId);
        int project3 = await CreateProjectAsync(9003, academicYearId, team3, projectTypeId,
            "פלטפורמת מסחר אלקטרוני לעסקים קטנים");
        await AddProjectMentorAsync(project3, yanaiId);

        await SeedSimpleCompletedMilestoneAsync(academicYearId, project3, stageIds1["Selection"],
            "בחירת נושא ואישור פרויקט", Day(-95), Day(-96));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project3, stageIds1["Kickoff"],
            "פגישת פתיחה עם הלקוח", Day(-85), Day(-86));

        // Definition stage is still open, 25 days past due — drives
        // ProjectHealthController's computed status to Red ("at risk").
        int msDefRisk = await CreateFullMilestoneAsync(academicYearId, project3, stageIds1["Definition"],
            "מסמך הגדרת בעיה", Day(-25), "InProgress", null);
        int taskDefRisk = await CreateTaskAsync(project3, msDefRisk, "כתיבת מסמך הגדרת בעיה", idanId, yanaiId,
            "ReturnedForRevision", Day(-25), isSubmission: true);
        await CreateSubmissionAsync(taskDefRisk, idanId,
            submittedAt: Day(-24),
            driveUrl: RandomDriveUrl(),
            status: "NeedsRevision", mentorStatus: "Returned",
            mentorFeedback: "הניתוח לא מכסה את קהל היעד המרכזי, יש לעדכן.", mentorReviewedAt: Day(-15));

        await CreateTaskAsync(project3, null, "מחקר מתחרים בתחום המסחר האלקטרוני", idanId, yanaiId,
            "Open", Day(-10), isSubmission: false);

        int req3 = await CreateRequestAsync(project3, shiraId, RequestTypes.TechnicalSupport,
            "בעיה בהתחברות ל-API של ספק התשלומים",
            "אנחנו נתקעים באינטגרציה מול ה-API של ספק הסליקה ונדרשת עזרה טכנית.",
            RequestStatuses.InProgress, RequestPriorities.High, Day(-6));
        await CreateRequestEventAsync(req3, yanaiId, "פניתי לצוות התמיכה הטכנית של הקורס, ממתין לתשובה.", Day(-4));

        // ── Project 9004 — lightweight, "moderately behind" (Orange) ────
        // ProjectHealthController buckets by max milestone delay: 0=Green,
        // ≤14 days=Orange, >14=Red. A 7-day-late open milestone lands here.
        int team4 = await CreateTeamAsync(academicYearId, "צוות הבריאות הדיגיטלית");
        await AddTeamMemberAsync(team4, roniId);
        await AddTeamMemberAsync(team4, talId);
        int project4 = await CreateProjectAsync(9004, academicYearId, team4,
            await GetOrCreateProjectTypeIdAsync("Methodological"),
            "אפליקציית מעקב בריאות דיגיטלית לקשישים");
        await AddProjectMentorAsync(project4, meravId);

        await SeedSimpleCompletedMilestoneAsync(academicYearId, project4, stageIds1["Selection"],
            "בחירת נושא ואישור פרויקט", Day(-70), Day(-71));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project4, stageIds1["Kickoff"],
            "פגישת פתיחה עם הלקוח", Day(-60), Day(-61));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project4, stageIds1["Definition"],
            "מסמך הגדרת בעיה ומטרות", Day(-45), Day(-46));

        int msDevOrange = await CreateFullMilestoneAsync(academicYearId, project4, stageIds1["Development"],
            "אב-טיפוס עובד ראשוני", Day(-7), "InProgress", null);
        int taskDevOrange = await CreateTaskAsync(project4, msDevOrange, "פיתוח אב-טיפוס ראשוני", roniId, meravId,
            "SubmittedToMentor", Day(-7), isSubmission: true);
        await CreateSubmissionAsync(taskDevOrange, roniId,
            submittedAt: Day(-2), driveUrl: RandomDriveUrl(),
            status: "Submitted", mentorStatus: "Pending",
            mentorFeedback: null, mentorReviewedAt: null);
        await CreateTaskAsync(project4, null, "בדיקות נגישות למשתמשים מבוגרים", talId, meravId,
            "Open", Day(4), isSubmission: false);

        int req4 = await CreateRequestAsync(project4, roniId, RequestTypes.Extension,
            "בקשת דחייה למסירת אב-הטיפוס",
            "נתקלנו בקושי טכני בלתי צפוי באינטגרציית חיישן קצב הלב.",
            RequestStatuses.PendingLecturerDecision, RequestPriorities.Normal, Day(-5));
        await CreateRequestExtensionAsync(req4, taskDevOrange, currentDueDate: Day(-7), requestedDueDate: Day(3),
            reason: "קושי טכני באינטגרציית חיישן.");
        await CreateRequestEventAsync(req4, meravId, "ממליצה לאשר, העיכוב מוצדק טכנית.", Day(-4));

        // ── Project 9005 — lightweight, further along, healthy (Green) ──
        int team5 = await CreateTeamAsync(academicYearId, "צוות הכושר החכם");
        await AddTeamMemberAsync(team5, yuvalId);
        await AddTeamMemberAsync(team5, avigailId);
        int project5 = await CreateProjectAsync(9005, academicYearId, team5, projectTypeId,
            "אפליקציית אימונים אישית מבוססת AI");
        await AddProjectMentorAsync(project5, yanaiId);

        await SeedSimpleCompletedMilestoneAsync(academicYearId, project5, stageIds1["Selection"],
            "בחירת נושא ואישור פרויקט", Day(-110), Day(-111));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project5, stageIds1["Kickoff"],
            "פגישת פתיחה עם הלקוח", Day(-100), Day(-101));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project5, stageIds1["Definition"],
            "מסמך הגדרת בעיה ומטרות", Day(-85), Day(-86));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project5, stageIds1["Specification"],
            "מסמך אפיון UX", Day(-60), Day(-61));
        await SeedSimpleCompletedMilestoneAsync(academicYearId, project5, stageIds1["Development"],
            "גרסה עובדת ראשונה", Day(-25), Day(-26));

        int msEvalGreen = await CreateFullMilestoneAsync(academicYearId, project5, stageIds1["Evaluation"],
            "תוכנית הערכת משתמשים", Day(10), "InProgress", null);
        int taskEvalGreen = await CreateTaskAsync(project5, msEvalGreen, "כתיבת תוכנית הערכת משתמשים", yuvalId, yanaiId,
            "Done", Day(-3), isSubmission: true);
        await CreateSubmissionAsync(taskEvalGreen, yuvalId,
            submittedAt: Day(-3), driveUrl: RandomDriveUrl(),
            status: "Reviewed", mentorStatus: "Approved",
            mentorFeedback: "תוכנית מקיפה, מוכנים להתחיל בבדיקות.", mentorReviewedAt: Day(-1),
            moodleSubmittedAt: Day(0));
        await CreateTaskAsync(project5, null, "גיוס משתתפים לבדיקות שימושיות", avigailId, yanaiId,
            "InProgress", Day(7), isSubmission: false);

        int req5 = await CreateRequestAsync(project5, avigailId, RequestTypes.SpecialEvent,
            "בקשה להצגת הפרויקט באירוע הקורס",
            "נשמח להציג את האפליקציה באירוע הסיום השנתי של הקורס.",
            RequestStatuses.Resolved, RequestPriorities.Low, Day(-12));
        await CreateRequestEventAsync(req5, jennyId, "אושר, נשריין לכם 10 דקות הצגה.", Day(-10));

        return "Demo data seeded: projects 9001-9005; 15 users (2 mentors); rich cross-screen story with varied health states.";
    }

    // ═════════════════════════════════════════════════════════════════════
    //  RESET — deletes only the fixed demo identifiers, explicit dependency order
    // ═════════════════════════════════════════════════════════════════════
    public async Task<string> ResetAsync()
    {
        var projectIds = (await _db.GetRecordsAsync<int>(
            $"SELECT Id FROM Projects WHERE ProjectNumber IN ({string.Join(",", DemoProjectNumbers)})"))
            ?.ToList() ?? new List<int>();

        var userIds = (await _db.GetRecordsAsync<int>(
            $"SELECT Id FROM users WHERE Email IN ({string.Join(",", DemoEmails.Select(e => $"'{e}'"))})"))
            ?.ToList() ?? new List<int>();

        if (projectIds.Count == 0 && userIds.Count == 0)
            return "No demo data found — nothing to reset.";

        string projIdsCsv = projectIds.Count > 0 ? string.Join(",", projectIds) : "-1";
        string userIdsCsv = userIds.Count > 0 ? string.Join(",", userIds) : "-1";

        // Task ids belonging to demo projects (needed to scope TaskSubmissions).
        var taskIds = (await _db.GetRecordsAsync<int>(
            $"SELECT Id FROM Tasks WHERE ProjectId IN ({projIdsCsv})"))?.ToList() ?? new List<int>();
        string taskIdsCsv = taskIds.Count > 0 ? string.Join(",", taskIds) : "-1";

        // ProjectMilestone ids, and the AcademicYearMilestone / MilestoneTemplate
        // ids they reference — deleted only if not referenced by any OTHER
        // project's milestones (defensive, in case of future manual reuse).
        var pmIds = (await _db.GetRecordsAsync<int>(
            $"SELECT Id FROM ProjectMilestones WHERE ProjectId IN ({projIdsCsv})"))?.ToList() ?? new List<int>();
        string pmIdsCsv = pmIds.Count > 0 ? string.Join(",", pmIds) : "-1";

        var aymIds = (await _db.GetRecordsAsync<int>(
            $"SELECT DISTINCT AcademicYearMilestoneId FROM ProjectMilestones WHERE Id IN ({pmIdsCsv})"))
            ?.ToList() ?? new List<int>();
        string aymIdsCsv = aymIds.Count > 0 ? string.Join(",", aymIds) : "-1";

        var templateIds = (await _db.GetRecordsAsync<int>(
            $"SELECT DISTINCT MilestoneTemplateId FROM AcademicYearMilestones WHERE Id IN ({aymIdsCsv})"))
            ?.ToList() ?? new List<int>();
        string templateIdsCsv = templateIds.Count > 0 ? string.Join(",", templateIds) : "-1";

        var requestIds = (await _db.GetRecordsAsync<int>(
            $"SELECT Id FROM ProjectRequests WHERE ProjectId IN ({projIdsCsv})"))?.ToList() ?? new List<int>();
        string requestIdsCsv = requestIds.Count > 0 ? string.Join(",", requestIds) : "-1";

        // Resolve TeamIds now, while Projects.TeamId still exists — Projects
        // must be deleted before Teams (Projects.TeamId references Teams.Id),
        // so this list has to be captured up front, not re-derived after.
        var teamIds = (await _db.GetRecordsAsync<int>(
            $"SELECT TeamId FROM Projects WHERE Id IN ({projIdsCsv}) AND TeamId IS NOT NULL"))
            ?.ToList() ?? new List<int>();
        string teamIdsCsv = teamIds.Count > 0 ? string.Join(",", teamIds) : "-1";

        // ── Delete children before parents, explicit order (not relying
        //    solely on ON DELETE CASCADE) ─────────────────────────────────
        await _db.SaveDataAsync($"DELETE FROM Notifications WHERE UserId IN ({userIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM ProjectRequestEvents WHERE RequestId IN ({requestIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM ProjectRequestExtensions WHERE RequestId IN ({requestIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM ProjectRequests WHERE Id IN ({requestIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM TaskSubmissions WHERE TaskId IN ({taskIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM Tasks WHERE Id IN ({taskIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM ProjectMentors WHERE ProjectId IN ({projIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM ProjectMilestones WHERE Id IN ({pmIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM AcademicYearMilestones WHERE Id IN ({aymIdsCsv})");
        // Only drop templates that no OTHER (non-demo) AcademicYearMilestone still references.
        await _db.SaveDataAsync($@"
            DELETE FROM MilestoneTemplates
            WHERE Id IN ({templateIdsCsv})
              AND Id NOT IN (SELECT MilestoneTemplateId FROM AcademicYearMilestones)");
        // Projects (references Teams) must go before Teams.
        await _db.SaveDataAsync($"DELETE FROM Projects WHERE Id IN ({projIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM TeamMembers WHERE TeamId IN ({teamIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM Teams WHERE Id IN ({teamIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM UserRoles WHERE UserId IN ({userIdsCsv})");
        await _db.SaveDataAsync($"DELETE FROM users WHERE Id IN ({userIdsCsv})");

        return $"Demo data reset: removed {projectIds.Count} project(s), {userIds.Count} user(s).";
    }

    // ═════════════════════════════════════════════════════════════════════
    //  VERIFY — read-only summary
    // ═════════════════════════════════════════════════════════════════════
    public async Task<object> VerifyAsync()
    {
        var projects = await _db.GetRecordsAsync<dynamic>($@"
            SELECT ProjectNumber, Title, Id FROM Projects
            WHERE ProjectNumber IN ({string.Join(",", DemoProjectNumbers)})");

        var users = await _db.GetRecordsAsync<dynamic>($@"
            SELECT Email, FirstName, LastName FROM users
            WHERE Email IN ({string.Join(",", DemoEmails.Select(e => $"'{e}'"))})");

        int projectIdPrimary = 0;
        var primary = projects?.FirstOrDefault(p => (long)p.ProjectNumber == 9001);
        if (primary is not null) projectIdPrimary = (int)(long)primary.Id;

        object? taskCounts = null;
        object? requestCount = null;
        if (projectIdPrimary > 0)
        {
            taskCounts = (await _db.GetRecordsAsync<dynamic>(
                "SELECT Status, COUNT(*) AS Count FROM Tasks WHERE ProjectId = @P GROUP BY Status",
                new { P = projectIdPrimary }))?.ToList();
            requestCount = (await _db.GetRecordsAsync<int>(
                "SELECT COUNT(*) FROM ProjectRequests WHERE ProjectId = @P", new { P = projectIdPrimary }))
                ?.FirstOrDefault();
        }

        return new
        {
            Projects = projects?.ToList() ?? new List<dynamic>(),
            Users = users?.ToList() ?? new List<dynamic>(),
            Project9001TaskStatusCounts = taskCounts,
            Project9001RequestCount = requestCount,
            Note = "For the computed 'current stage' and progress %, hit GET /api/roadmap-stages/projects/{projectId}/progress as the demo mentor — this endpoint reuses the app's own real computation rather than duplicating it here.",
        };
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═════════════════════════════════════════════════════════════════════

    private async Task<int> EnsureCurrentAcademicYearIdAsync()
    {
        var current = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM AcademicYears WHERE IsCurrent = 1 LIMIT 1");
        var id = current?.FirstOrDefault() ?? 0;
        if (id > 0) return id;

        // Fallback for a completely fresh DB with no cycle at all — create one
        // and seed the same 7 default stages DatabaseMigrator would seed for
        // any other cycle (duplicated here deliberately rather than modifying
        // DatabaseMigrator.cs, which we're not touching per the safety rules).
        int newYearId = await _db.InsertReturnIdAsync(@"
            INSERT INTO AcademicYears (Name, StartDate, EndDate, IsActive, IsCurrent, Status)
            VALUES ('Demo', @Start, @End, 1, 1, 'Active')",
            new { Start = Day(-365), End = Day(365) });

        var defaults = new (string Code, string Name, string Description, int Order)[]
        {
            ("Selection", "בחירה ושיבוצים", "תהליך בחירת פרויקטים ושיבוץ סטודנטים לצוותים", 1),
            ("Kickoff", "התנעה", "מפגשי פתיחה, היכרות עם הלקוח והגדרת אופן העבודה", 2),
            ("Definition", "הגדרת הבעיה וכיוון ראשוני לפתרון", "ניסוח הצורך, מטרות ויעדים ראשוניים", 3),
            ("Specification", "אפיון", "סקירות, מחקר משתמשים ואפיון הפתרון", 4),
            ("Development", "פיתוח", "מוקאפים, אבי-טיפוס וגרסאות עובדות של המוצר", 5),
            ("Evaluation", "הערכה", "תכנון, ביצוע וסיכום הערכת הפתרון מול משתמשים", 6),
            ("SubmissionGrading", "הגשות וציונים", "הגשות סופיות, הגנה ומתן ציונים", 7),
        };
        foreach (var s in defaults)
        {
            await _db.SaveDataAsync(@"
                INSERT OR IGNORE INTO RoadmapStages (AcademicYearId, Code, Name, Description, DisplayOrder, IsActive)
                VALUES (@Y, @Code, @Name, @Description, @Order, 1)",
                new { Y = newYearId, s.Code, s.Name, s.Description, s.Order });
        }
        return newYearId;
    }

    private async Task<int> GetOrCreateProjectTypeIdAsync(string name)
    {
        var existing = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM ProjectTypes WHERE Name = @Name", new { Name = name });
        var id = existing?.FirstOrDefault() ?? 0;
        if (id > 0) return id;
        return await _db.InsertReturnIdAsync(
            "INSERT INTO ProjectTypes (Name) VALUES (@Name)", new { Name = name });
    }

    // Real Google Drive share links use an opaque, random-looking file ID —
    // a descriptive slug (e.g. "...UXSpec...") is actually LESS realistic
    // than a proper-looking random string, so that's what this generates.
    private static readonly Random _rng = new();
    private static string RandomDriveUrl()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-";
        var id = new char[33];
        id[0] = '1';
        for (int i = 1; i < id.Length; i++) id[i] = chars[_rng.Next(chars.Length)];
        return $"https://drive.google.com/file/d/{new string(id)}/view?usp=sharing";
    }

    private class StageIdRow
    {
        public string Code { get; set; } = "";
        public int Id { get; set; }
    }

    private async Task<Dictionary<string, int>> GetStageIdsAsync(int academicYearId)
    {
        var rows = await _db.GetRecordsAsync<StageIdRow>(
            "SELECT Code, Id FROM RoadmapStages WHERE AcademicYearId = @Y",
            new { Y = academicYearId });
        return rows!.ToDictionary(r => r.Code, r => r.Id);
    }

    private async Task<int> CreateUserAsync(string email, string firstName, string lastName)
    {
        var existing = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM users WHERE Email = @Email", new { Email = email });
        var id = existing?.FirstOrDefault() ?? 0;
        if (id > 0) return id;

        string hash = _passwordService.HashPassword(new MinimalUser { Email = email }, DemoPassword);
        return await _db.InsertReturnIdAsync(@"
            INSERT INTO Users (Email, PasswordHash, FirstName, LastName, IsVerified)
            VALUES (@Email, @PasswordHash, @FirstName, @LastName, 1)",
            new { Email = email, PasswordHash = hash, FirstName = firstName, LastName = lastName });
    }

    private async Task AddRoleAsync(int userId, string role)
    {
        var existing = await _db.GetRecordsAsync<int>(
            "SELECT Id FROM UserRoles WHERE UserId = @U AND Role = @R", new { U = userId, R = role });
        if (existing is not null && existing.Any()) return;
        await _db.SaveDataAsync(
            "INSERT INTO UserRoles (UserId, Role) VALUES (@U, @R)", new { U = userId, R = role });
    }

    private async Task<int> CreateTeamAsync(int academicYearId, string teamName) =>
        await _db.InsertReturnIdAsync(
            "INSERT INTO Teams (AcademicYearId, TeamName) VALUES (@Y, @Name)",
            new { Y = academicYearId, Name = teamName });

    private async Task AddTeamMemberAsync(int teamId, int userId) =>
        await _db.SaveDataAsync(
            "INSERT INTO TeamMembers (TeamId, UserId, IsActive) VALUES (@T, @U, 1)",
            new { T = teamId, U = userId });

    private async Task<int> CreateProjectAsync(int projectNumber, int academicYearId, int teamId,
        int projectTypeId, string title) =>
        await _db.InsertReturnIdAsync(@"
            INSERT INTO Projects (ProjectNumber, AcademicYearId, TeamId, Title, ProjectTypeId, Status, SourceType)
            VALUES (@Num, @Year, @Team, @Title, @Type, 'Active', 'Manual')",
            new { Num = projectNumber, Year = academicYearId, Team = teamId, Title = title, Type = projectTypeId });

    private async Task AddProjectMentorAsync(int projectId, int mentorUserId) =>
        await _db.SaveDataAsync(
            "INSERT INTO ProjectMentors (ProjectId, UserId) VALUES (@P, @U)",
            new { P = projectId, U = mentorUserId });

    private async Task<int> CreateMilestoneTemplateAsync(string title, int orderIndex) =>
        await _db.InsertReturnIdAsync(@"
            INSERT INTO MilestoneTemplates (Title, Description, OrderIndex, IsRequired, IsActive, ProjectTypeId)
            VALUES (@Title, @Title, @Order, 1, 1, NULL)",
            new { Title = title, Order = orderIndex });

    private async Task<int> CreateAcademicYearMilestoneAsync(int academicYearId, int templateId,
        DateTime dueDate, int? roadmapStageId)
    {
        int aymId = await _db.InsertReturnIdAsync(@"
            INSERT INTO AcademicYearMilestones (AcademicYearId, MilestoneTemplateId, DueDate, IsActive)
            VALUES (@Y, @T, @Due, 1)",
            new { Y = academicYearId, T = templateId, Due = dueDate });
        if (roadmapStageId.HasValue)
        {
            await _db.SaveDataAsync(
                "UPDATE AcademicYearMilestones SET RoadmapStageId = @S WHERE Id = @Id",
                new { S = roadmapStageId.Value, Id = aymId });
        }
        return aymId;
    }

    private async Task<int> CreateProjectMilestoneAsync(int projectId, int aymId, string status, DateTime? completedAt) =>
        await _db.InsertReturnIdAsync(@"
            INSERT INTO ProjectMilestones (ProjectId, AcademicYearMilestoneId, Status, CompletedAt)
            VALUES (@P, @A, @Status, @Completed)",
            new { P = projectId, A = aymId, Status = status, Completed = completedAt });

    // Every milestone template created by this seeder previously hardcoded
    // OrderIndex=0 — ordering only happened to look right because SQL's
    // secondary sort key (Id) coincided with narrative creation order. This
    // counter gives each one a real, distinct, monotonically increasing value
    // instead, so ordering is correct by design, not by insertion-order luck.
    private int _msOrderCounter = 0;

    /// <summary>Creates template + AYM + ProjectMilestone in one call for a
    /// fully-specified deliverable (used for the Specification-stage story
    /// items and the two at-risk-project milestones).</summary>
    private async Task<int> CreateFullMilestoneAsync(int academicYearId, int projectId, int stageId,
        string title, DateTime dueDate, string status, DateTime? completedAt)
    {
        int templateId = await CreateMilestoneTemplateAsync(title, ++_msOrderCounter);
        int aymId = await CreateAcademicYearMilestoneAsync(academicYearId, templateId, dueDate, stageId);
        return await CreateProjectMilestoneAsync(projectId, aymId, status, completedAt);
    }

    private async Task<int> SeedSimpleCompletedMilestoneAsync(int academicYearId, int projectId, int stageId,
        string title, DateTime dueDate, DateTime completedAt) =>
        await CreateFullMilestoneAsync(academicYearId, projectId, stageId, title, dueDate, "Completed", completedAt);

    private async Task<int> SeedSimpleFutureMilestoneAsync(int academicYearId, int projectId, int stageId,
        string title, DateTime dueDate) =>
        await CreateFullMilestoneAsync(academicYearId, projectId, stageId, title, dueDate, "NotStarted", null);

    // IsMandatory defaults true — every real curriculum/deliverable task should
    // be mandatory. LecturerDashboardController's overdue-count, missing-
    // submission, and health-score queries ALL filter on IsMandatory = 1; a
    // task left at the schema default (0) is invisible to every one of them,
    // which is exactly the bug that made every mentor project show "0 overdue,
    // 100% healthy" regardless of real data. Pass isMandatory: false only for
    // genuinely informal, non-deliverable tasks (e.g. a coordination meeting).
    private async Task<int> CreateTaskAsync(int projectId, int? projectMilestoneId, string title,
        int? assignedToUserId, int createdByUserId, string status, DateTime dueDate, bool isSubmission,
        bool isMandatory = true) =>
        await _db.InsertReturnIdAsync(@"
            INSERT INTO Tasks (ProjectId, ProjectMilestoneId, Title, TaskType, Status, DueDate,
                                CreatedByUserId, AssignedToUserId, IsSubmission, IsMandatory)
            VALUES (@Project, @Milestone, @Title, 'Mentor', @Status, @Due, @CreatedBy, @AssignedTo, @IsSubmission, @IsMandatory)",
            new
            {
                Project = projectId,
                Milestone = projectMilestoneId,
                Title = title,
                Status = status,
                Due = dueDate,
                CreatedBy = createdByUserId,
                AssignedTo = assignedToUserId,
                IsSubmission = isSubmission ? 1 : 0,
                IsMandatory = isMandatory ? 1 : 0,
            });

    // moodleSubmittedAt should be set for any submission that's meant to read
    // as GENUINELY, fully closed (typically MentorStatus=Approved). Every
    // "IsComplete" check across the app (Dashboard hero, ActionCenterCard,
    // UpcomingSubmissionsCard) requires Approved AND Moodle-confirmed — leaving
    // this null on an Approved submission leaves it permanently "stuck",
    // wrongly resurfacing as the most urgent overdue item everywhere.
    private async Task<int> CreateSubmissionAsync(int taskId, int submittedByUserId, DateTime submittedAt,
        string driveUrl, string status, string mentorStatus, string? mentorFeedback, DateTime? mentorReviewedAt,
        DateTime? moodleSubmittedAt = null) =>
        await _db.InsertReturnIdAsync(@"
            INSERT INTO TaskSubmissions (TaskId, SubmittedByUserId, SubmittedAt, DriveUrl, Status,
                                          MentorStatus, MentorFeedback, MentorReviewedAt, MoodleSubmittedAt)
            VALUES (@Task, @SubmittedBy, @SubmittedAt, @DriveUrl, @Status, @MentorStatus, @Feedback, @ReviewedAt, @MoodleAt)",
            new
            {
                Task = taskId,
                SubmittedBy = submittedByUserId,
                SubmittedAt = submittedAt,
                DriveUrl = driveUrl,
                Status = status,
                MentorStatus = mentorStatus,
                Feedback = mentorFeedback,
                ReviewedAt = mentorReviewedAt,
                MoodleAt = moodleSubmittedAt,
            });

    private async Task<int> CreateRequestAsync(int projectId, int createdByUserId, string requestType,
        string title, string description, string status, string priority, DateTime createdAt) =>
        await _db.InsertReturnIdAsync(@"
            INSERT INTO ProjectRequests (ProjectId, CreatedByUserId, RequestType, Title, Description,
                                          Status, Priority, CreatedAt)
            VALUES (@Project, @CreatedBy, @Type, @Title, @Description, @Status, @Priority, @CreatedAt)",
            new
            {
                Project = projectId,
                CreatedBy = createdByUserId,
                Type = requestType,
                Title = title,
                Description = description,
                Status = status,
                Priority = priority,
                CreatedAt = createdAt,
            });

    private async Task CreateRequestExtensionAsync(int requestId, int taskId, DateTime currentDueDate,
        DateTime requestedDueDate, string reason) =>
        await _db.SaveDataAsync(@"
            INSERT INTO ProjectRequestExtensions (RequestId, TaskId, CurrentDueDate, RequestedDueDate, Reason)
            VALUES (@Request, @Task, @Current, @Requested, @Reason)",
            new { Request = requestId, Task = taskId, Current = currentDueDate, Requested = requestedDueDate, Reason = reason });

    private async Task CreateRequestEventAsync(int requestId, int userId, string comment, DateTime createdAt) =>
        await _db.SaveDataAsync(@"
            INSERT INTO ProjectRequestEvents (RequestId, UserId, EventType, Content, CreatedAt)
            VALUES (@Request, @User, 'Comment', @Content, @CreatedAt)",
            new { Request = requestId, User = userId, Content = comment, CreatedAt = createdAt });

    private async Task CreateNotificationAsync(int userId, string type, string title, string message,
        DateTime createdAt, bool isRead) =>
        await _db.SaveDataAsync(@"
            INSERT INTO Notifications (UserId, Title, Message, Type, IsRead, CreatedAt, ReadAt)
            VALUES (@User, @Title, @Message, @Type, @IsRead, @CreatedAt, @ReadAt)",
            new
            {
                User = userId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = isRead ? 1 : 0,
                CreatedAt = createdAt,
                ReadAt = isRead ? (DateTime?)createdAt : null,
            });
}
