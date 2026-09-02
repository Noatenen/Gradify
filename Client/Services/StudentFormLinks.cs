namespace AuthWithAdmin.Client.Services;

/// <summary>
/// THE student-facing address of a form, in one place.
///
/// <para>WHY THIS EXISTS. Two admin surfaces now hand the Project Assignment
/// Form to students — the form editor's copy action and the Assignments
/// workspace's header action — and a third screen (the Assignments page) links
/// to it. Each of them writing "/student/assignment" for itself is three
/// sources of truth for one address, and the route only has to move once for
/// two of them to be quietly wrong. They all read this.</para>
///
/// <para>ROUTES ONLY, never absolute URLs. The absolute form is built by the
/// caller with <c>NavigationManager.ToAbsoluteUri</c>, which derives scheme and
/// host from the app's own base address — so a link copied on localhost, on the
/// staging host and in production is correct in all three without anything here
/// knowing a hostname.</para>
///
/// <para>NOT the team-registration link. That is a different student-facing
/// address (<c>/create-team</c>, anonymous, outside the app shell) with a
/// different purpose, and <c>StudentRegistrationLink</c> owns it. The two were
/// conflated on the old Management hub, where a tile labelled "לינק טופס
/// שיבוצים לסטודנטים" copied the registration URL.</para>
/// </summary>
public static class StudentFormLinks
{
    /// <summary>The Project Assignment Form. A system form with its own screen
    /// rather than a row in the generic renderer — it feeds the assignment
    /// algorithm, so it is answered at its own route.</summary>
    public const string AssignmentForm = "/student/assignment";

    /// <summary>Where a student answers <paramref name="formId"/>. The
    /// assignment form has its own screen; every other form is answered by the
    /// generic renderer at <c>/forms/{id}</c>.</summary>
    public static string For(int formId, bool isAssignmentForm) =>
        isAssignmentForm ? AssignmentForm : $"/forms/{formId}";
}
