using System;
using System.Collections.Generic;
using System.Linq;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Management.Forms;

/// <summary>
/// The sample context the student assignment form is PREVIEWED against.
///
/// <para><b>What this is not.</b> It is not a copy of the form. Nothing here
/// draws a section, a label, a control or a validation hint — all of that is
/// <c>StudentAssignmentPage</c>, rendered as it is. This file only supplies the
/// two things that page normally gets from a signed-in student: an
/// <see cref="AssignmentContextDto"/> (who I am, who my team is, what the
/// catalogue holds, how the form is configured) and a set of answers to open
/// with. That split is what makes the preview incapable of drifting from the
/// student's form: there is no second design to drift.</para>
///
/// <para><b>The form half is real, the people half is invented.</b> The layout —
/// every title, helper text, required flag, option label and admin-added
/// question — is projected from the live <see cref="FormDetailDto"/> the admin
/// is editing, so previewing after a wording change shows that change. Only the
/// two students, their strengths and the note are sample values, and the modal
/// says so on the surface.</para>
/// </summary>
internal static class StudentFormPreviewData
{
    public const string Label = "תצוגה מקדימה";

    /// <summary>The context that used to sit under the heading as a paragraph.
    /// It says the same two things — what this screen IS, and which half of it
    /// is invented — in the one place a reader goes looking for them.</summary>
    public const string Hint =
        "כך נראה המסלול של הסטודנטים: עיון בקטלוג הפרויקטים, ולאחריו טופס ההגשה — שבו נבחרות ומדורגות שלוש ההעדפות ונענות שאר השאלות. הקטלוג, הפרויקטים וניסוח השאלות הם ההגדרות והנתונים הנוכחיים; הסטודנטיות, החוזקות וההערה הם נתוני דוגמה. שום דבר אינו נשמר, ואין הגשה.";

    /// <summary>Ids for the invented people. Negative, so a preview id can never
    /// be mistaken for — or collide with — a real Users.Id, and so anything that
    /// did try to persist one would fail loudly rather than quietly write.</summary>
    private const int MeId      = -9001;
    private const int PartnerId = -9002;

    private sealed record Person(int Id, string Name, string[] Strengths);

    private static readonly Person Me =
        new(MeId, "מיכל אורן", new[] { "Technology", "ProjectManagement" });

    private static readonly Person Partner =
        new(PartnerId, "יובל דגן", new[] { "Design", "Content" });

    private const string SampleNote =
        "אנחנו מעדיפות פרויקט עם ממשק משתמש משמעותי, ויכולות להתחיל כבר בתחילת הסמסטר.";

    public sealed record Preview(AssignmentContextDto Context, ExistingAssignmentDto Answers);

    // ─────────────────────────────────────────────────────────────────────────
    //  The catalogue the preview browses
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Maps the admin's own catalogue rows onto the shape the student
    /// catalogue renders.
    ///
    /// <para><b>Why a mapping and not the student endpoint.</b>
    /// <c>api/student/catalog</c> is <c>[Authorize(Roles = Student)]</c> — an
    /// admin cannot call it, and widening it for a preview would be trading a
    /// real authorisation boundary for a convenience. <c>api/catalog</c> is the
    /// admin's own read of the same Projects rows, so this is the same data
    /// reached through the door the reader already has.</para>
    ///
    /// <para><b>It mirrors the student query's rules, not the admin one's.</b>
    /// The student endpoint returns only <c>Status = 'Available'</c> and marks a
    /// project Taken when its team has active members; the admin list returns
    /// everything. Filtering and re-deriving here is what keeps the preview
    /// honest — a lecturer must not be shown projects in the student catalogue
    /// that students cannot see.</para>
    ///
    /// <para>Nothing internal crosses over: <c>InternalNotes</c> and
    /// <c>Priority</c> exist on the admin DTO and have no field on the student
    /// one, so they cannot arrive on a card by accident.</para></summary>
    public static List<StudentCatalogProjectDto> ToStudentCatalog(
        IEnumerable<CatalogProjectListDto>? rows)
    {
        if (rows is null) return new List<StudentCatalogProjectDto>();

        return rows
            .Where(r => string.Equals(r.Status, "Available", StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => r.ProjectNumber)
            .Select(r => new StudentCatalogProjectDto
            {
                Id               = r.Id,
                ProjectNumber    = r.ProjectNumber,
                Title            = r.Title,
                Description      = r.Description ?? "",
                ProjectType      = r.ProjectType,
                OrganizationName = r.OrganizationName,

                // The student query calls a project Taken when its team has
                // active members; IsAssigned is the admin list's answer to the
                // same question.
                Availability     = r.IsAssigned ? "Taken" : "Available",

                // A preview has no favourites: they belong to a person, and the
                // person here is looking at somebody else's screen.
                IsFavorite       = false
            })
            .ToList();
    }

    /// <summary>Builds the preview.
    ///
    /// <param name="form">The form as it stands RIGHT NOW — in the editor that
    /// means the admin's unsaved edits, which is the version they want to
    /// look at. Null falls back to the page's own built-in wording, exactly as
    /// it does for a student on an install with no Forms row.</param>
    /// <param name="catalogue">The WHOLE catalogue, as the browse view shows
    /// it. The form's three slots choose FROM it, so this is the list of
    /// options rather than a set of picks — the preview arrives with nothing
    /// chosen, exactly as a student does.</param>
    /// </summary>
    public static Preview Build(FormDetailDto? form, IReadOnlyList<StudentCatalogProjectDto>? catalogue)
    {
        var catalog = (catalogue ?? Array.Empty<StudentCatalogProjectDto>())
            .Select(p => new AssignmentCatalogItemDto
            {
                Id               = p.Id,
                ProjectNumber    = p.ProjectNumber,
                Title            = p.Title,
                ProjectType      = p.ProjectType,
                Availability     = p.Availability,
                Description      = p.Description,
                OrganizationName = p.OrganizationName,

                // Starting false, and the modal flips it when the reader stars
                // something in the catalogue view — so inside the preview the
                // two screens share one favourite state exactly as the
                // student's two screens share StudentProjectFavorites.
                IsFavorite       = p.IsFavorite
            })
            .ToList();

        var ctx = new AssignmentContextDto
        {
            Me      = new StudentBasicDto { Id = Me.Id, FullName = Me.Name },

            // An approved team, not a partner search. The preview's job is the
            // REVIEW-AND-SUBMIT step — what a student sees once they have a team
            // and three ranked projects — and an unapproved team would replace
            // that step's first section with a partner picker over a student
            // list this preview has no business inventing.
            HasTeam = true,
            TeamMembers = new List<TeamMemberBasicDto>
            {
                new() { UserId = Me.Id,      FullName = Me.Name,      Strengths = Me.Strengths.ToList() },
                new() { UserId = Partner.Id, FullName = Partner.Name, Strengths = Partner.Strengths.ToList() }
            },

            AvailableStudents  = new List<StudentBasicDto>(),
            Catalog            = catalog,
            ExistingSubmission = null,   // → the editable form, not the summary
            FormStatus         = BuildStatus(form),
            FormLayout         = BuildLayout(form)
        };

        var answers = new ExistingAssignmentDto
        {
            // NOTHING pre-ranked. Choosing three projects is what the form is
            // for, and filling the slots in advance would hide the one control
            // a lecturer most needs to look at.
            Preferences           = new List<ProjectPreferenceDto>(),
            HasOwnProject         = false,
            OwnProjectDescription = "",
            Notes                 = SampleNote,
            SubmittedAt           = "",

            // The admin's own questions open EMPTY. Pre-filling them would hide
            // the thing the preview is most often opened to check: what an
            // unanswered required question actually looks like to a student.
            ExtraAnswers = new List<FormAnswerInputDto>()
        };

        return new Preview(ctx, answers);
    }

    /// <summary>The form's own settings, with ONE deliberate override:
    /// <c>CanSubmit</c> is always true.
    ///
    /// <para>The gate is about whether a student may submit today; the preview
    /// is about what the form looks like. On a draft or closed form the real
    /// page correctly replaces the whole thing with "הטופס סגור להגשה", and an
    /// admin previewing a form they are still building would see that instead of
    /// the form they are building. Submission is blocked in the preview by
    /// StudentAssignmentPage.Preview regardless, so nothing is loosened.</para></summary>
    private static AssignmentFormStatusDto BuildStatus(FormDetailDto? form) => new()
    {
        IsOpen               = form?.IsOpen ?? true,
        OpensAt              = form?.OpensAt,
        ClosesAt             = form?.ClosesAt,
        AllowEditAfterSubmit = form?.AllowEditAfterSubmit ?? true,
        Instructions         = form?.Instructions ?? "",
        Status               = form?.Status ?? FormStatuses.Open,
        CanSubmit            = true,
        ClosedReason         = null,
        ClosedMessage        = null
    };

    /// <summary>Projects the admin's block list into the layout the student page
    /// reads. The three system blocks are matched by BlockKey — the same keys the
    /// server anchors them with — and everything else falls through to
    /// ExtraQuestions in its own SortOrder, which is exactly what the real
    /// endpoint does. A form with no blocks yields null, so the page uses its
    /// built-in fallback wording rather than rendering empty headings.</summary>
    private static AssignmentFormLayoutDto? BuildLayout(FormDetailDto? form)
    {
        if (form is null || form.Blocks.Count == 0) return null;

        var ordered = form.Blocks.OrderBy(b => b.SortOrder).ThenBy(b => b.Id).ToList();

        FormBlockDto? ByKey(string key) =>
            ordered.FirstOrDefault(b => string.Equals(b.BlockKey, key, StringComparison.Ordinal));

        return new AssignmentFormLayoutDto
        {
            FormId      = form.Id,
            Strengths   = ByKey(FormBlockKeys.Strengths),
            Preferences = ByKey(FormBlockKeys.ProjectPreferences),
            Notes       = ByKey(FormBlockKeys.Notes),
            ExtraQuestions = ordered
                .Where(b => string.IsNullOrEmpty(b.BlockKey))
                .ToList()
        };
    }
}
