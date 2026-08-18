namespace AuthWithAdmin.Client.Pages.ProjectWorkspace;

// ═════════════════════════════════════════════════════════════════════════════
//  ⚠  DEVELOPMENT PLACEHOLDER CONTENT  ⚠
//
//  THE GUIDANCE TEXT IN THIS FILE IS NOT FACULTY GUIDANCE.
//
//  The graduation-submission requirements do not exist anywhere in this
//  repository — not in a table, not in a document, not in Tasks.
//  SubmissionInstructions (which holds mid-course milestone deliverables, a
//  different thing entirely). Rather than invent requirements and present them
//  as the course's, every Intro / Requirements / Notes string below is written
//  as an explicit placeholder, and the UI renders a visible notice above the
//  whole section for as long as IsPlaceholderContent is true.
//
//  ── HOW TO REPLACE THIS WITH REAL CONTENT ───────────────────────────────────
//  1. Fill in Intro / Requirements / Notes from the real faculty document.
//  2. Point ResourceTitles at real ResourceFiles rows (see the field's own
//     note) where the faculty document references a document Motiva already
//     hosts. Leave it empty where it does not — the UI omits the section.
//  3. Set IsPlaceholderContent to false. The notice disappears on its own.
//
//  Nothing else changes: the accordion, the status control, the persistence
//  layer (ProjectSubmissionStatuses) and the progress summary are all keyed on
//  Key and are completely independent of the text.
//
//  The CATEGORY NAMES themselves are not placeholders — they are the eight
//  categories named in the product brief.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// One submission category: a heading, whatever guidance content exists for it,
/// and a stable <see cref="Key"/> that the team's status is stored against.
///
/// <para>Every content section is OPTIONAL and independently absent. A category
/// with no notes renders no notes block; a category with no requirements
/// renders no requirements block. The design brief's own rule — "do not force
/// every category into an identical content template" — is enforced by this
/// shape rather than by discipline.</para>
/// </summary>
/// <param name="Key">Persistence key. NEVER change one once it has shipped: the
/// team's saved status is keyed on it, and a rename silently resets that
/// category to "not started" for every project.</param>
/// <param name="ResourceTitles">Titles of real Knowledge Center resources
/// (ResourceFiles) that belong to this category, matched case-insensitively
/// against KnowledgeResource.Title. This is the ONLY way a document reaches
/// this section — no URL is ever written here, so the page cannot show a link
/// to something the system does not actually host. Anything unmatched is
/// dropped silently, and a category whose list resolves to nothing renders no
/// documents block at all.</param>
public sealed record SubmissionDeliverable(
    string Key,
    string Title,
    string IconPath,
    string? Intro,
    string RequirementsLabel,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> ResourceTitles);

public static class SubmissionDeliverablesCatalog
{
    /// <summary>
    /// True while the guidance text below is placeholder. Drives the visible
    /// notice in SubmissionDeliverablesSection — flip it to false in the same
    /// commit that lands the real faculty content, and the notice goes away.
    /// </summary>
    public const bool IsPlaceholderContent = true;

    /// <summary>Placeholder body text. One constant, so replacing it is
    /// mechanical and so no placeholder line can be mistaken for a real
    /// requirement that someone half-edited.</summary>
    private const string PlaceholderIntro =
        "תוכן זמני לפיתוח. כאן יופיע ההסבר של הסגל על התוצר הזה.";

    private static readonly IReadOnlyList<string> PlaceholderRequirements = new[]
    {
        "תוכן זמני לפיתוח — כאן תופיע דרישת הסגל הראשונה לתוצר זה.",
        "תוכן זמני לפיתוח — כאן תופיע דרישת הסגל השנייה לתוצר זה.",
    };

    private static readonly IReadOnlyList<string> PlaceholderNotes = new[]
    {
        "תוכן זמני לפיתוח — כאן תופיע הערה של הסגל לתוצר זה.",
    };

    private static readonly IReadOnlyList<string> NoNotes         = Array.Empty<string>();
    private static readonly IReadOnlyList<string> NoResourceLinks = Array.Empty<string>();

    /// <summary>
    /// The eight categories, in the brief's own order.
    ///
    /// Notes are present on only some of them ON PURPOSE: the section has to
    /// prove it renders a category with requirements only, and a category with
    /// requirements plus notes, differently. Documents are absent from all of
    /// them because no ResourceFiles row reliably maps to a graduation
    /// deliverable today — omitting the block is the honest answer, and it is
    /// exactly what the code will keep doing for any category the faculty
    /// document leaves without a document.
    /// </summary>
    public static readonly IReadOnlyList<SubmissionDeliverable> All = new[]
    {
        new SubmissionDeliverable(
            Key: "disk-on-key",
            Title: "Disk on Key",
            IconPath: "M6 3h12v18H6zM9 7h6",
            Intro: PlaceholderIntro,
            RequirementsLabel: "מה כולל ההתקן",
            Requirements: PlaceholderRequirements,
            Notes: PlaceholderNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "telemview",
            Title: "TelemView",
            IconPath: "M4 5h16v11H4zM9 20h6M12 16v4",
            Intro: PlaceholderIntro,
            RequirementsLabel: "מה נדרש למלא במערכת",
            Requirements: PlaceholderRequirements,
            Notes: PlaceholderNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "info-sheet",
            Title: "דף מידע",
            IconPath: "M7 3h7l5 5v13H7zM10 12h7M10 16h5",
            Intro: PlaceholderIntro,
            RequirementsLabel: "מה כולל הדף",
            Requirements: PlaceholderRequirements,
            Notes: NoNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "booklet",
            Title: "חוברת",
            IconPath: "M5 4h6a3 3 0 0 1 3 3v13H8a3 3 0 0 0-3 3zM19 4h-5v16h5z",
            Intro: PlaceholderIntro,
            RequirementsLabel: "פרקי החוברת",
            Requirements: PlaceholderRequirements,
            Notes: PlaceholderNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "video",
            Title: "סרטון",
            IconPath: "M4 6h11v12H4zM15 10l5-3v10l-5-3",
            Intro: PlaceholderIntro,
            RequirementsLabel: "מה מציגים בסרטון",
            Requirements: PlaceholderRequirements,
            Notes: NoNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "poster",
            Title: "פוסטר",
            IconPath: "M4 4h16v16H4zM8 9h8M8 13h5",
            Intro: PlaceholderIntro,
            RequirementsLabel: "מה כולל הפוסטר",
            Requirements: PlaceholderRequirements,
            Notes: NoNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "model",
            Title: "מודל",
            IconPath: "M4 7.5 12 3.5l8 4v9L12 20.5l-8-4zM4 7.5l8 4 8-4M12 11.5v9",
            Intro: PlaceholderIntro,
            RequirementsLabel: "מה מציגים בהגשה",
            Requirements: PlaceholderRequirements,
            Notes: PlaceholderNotes,
            ResourceTitles: NoResourceLinks),

        new SubmissionDeliverable(
            Key: "faculty-server",
            Title: "שרת הפקולטה",
            IconPath: "M4 5h16v5H4zM4 14h16v5H4zM8 7.5h.01M8 16.5h.01",
            Intro: PlaceholderIntro,
            RequirementsLabel: "שלבי ההעלאה",
            Requirements: PlaceholderRequirements,
            Notes: NoNotes,
            ResourceTitles: NoResourceLinks),
    };

    /// <summary>
    /// The collapsed row's meta line — "2 דרישות · הערה אחת". Counts only the
    /// sections that actually exist, so it stays truthful when a category has
    /// no notes or no documents.
    /// </summary>
    public static string MetaLine(SubmissionDeliverable deliverable, int documentCount)
    {
        var parts = new List<string>(3);

        var requirements = deliverable.Requirements.Count;
        if (requirements == 1) parts.Add("דרישה אחת");
        else if (requirements > 1) parts.Add($"{requirements} דרישות");

        var notes = deliverable.Notes.Count;
        if (notes == 1) parts.Add("הערה אחת");
        else if (notes > 1) parts.Add($"{notes} הערות");

        if (documentCount == 1) parts.Add("מסמך אחד");
        else if (documentCount > 1) parts.Add($"{documentCount} מסמכים");

        return string.Join(" · ", parts);
    }
}
