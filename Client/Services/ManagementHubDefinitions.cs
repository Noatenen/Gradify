using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Services;

/// <summary>
/// THE Management hub's information architecture, as data.
///
/// <para>Visual source: <c>design/design-system/Motiva Admin Home.dc.html</c> —
/// named sections, each a grid of one-line destination tiles.</para>
///
/// <para>WHY THIS IS A FILE AND NOT A PRIVATE RECORD ON THE PAGE. The hub is a
/// list of destinations, and the product adds destinations. Held on the page it
/// was 21 entries interleaved with a two-column balancing algorithm and the
/// four inline panels, so "add a management area" meant editing a Razor page
/// that also renders four editors. Here it is a data change: one
/// <see cref="ManagementEntry"/> in one <see cref="ManagementSection"/>, and
/// nothing else in the application moves.</para>
///
/// <para>It sits beside <see cref="NavDefinitions"/> on purpose — that file is
/// already the place this codebase keeps "which destinations does this role
/// see", and the hub is the same kind of statement about the same product.</para>
///
/// <para>NOTHING HERE IS A SECURITY BOUNDARY. <see cref="ManagementEntry.RequiresFlag"/>
/// hides a tile from a viewer whose role has the flag off; the server's
/// authorization is unchanged and is broader (see the note above
/// <c>LecturerManagementPage.CanSee</c>). Every route below already exists and
/// keeps its own <c>[Authorize]</c> attribute.</para>
/// </summary>
public static class ManagementHubDefinitions
{
    /// <summary>The tile's accent — the icon's stroke colour, and the only
    /// colour a tile carries. Three roles, matching the design reference's own
    /// rotation across a section, so a grid row reads as a row rather than as
    /// eight identical violet chips. The chip's tri-tone tint background is the
    /// same on every tile; only the glyph changes hue.</summary>
    public enum TileAccent { Violet, Blue, Teal }

    /// <summary>
    /// One destination on the hub.
    /// </summary>
    /// <param name="Title">The module's name, as the product already calls it.</param>
    /// <param name="Description">One line saying what the module holds.</param>
    /// <param name="IconMarkup">Lucide 24×24 geometry, as raw SVG children.
    /// Raw markup rather than a path array because several of these glyphs are
    /// <c>rect</c>/<c>circle</c>/<c>ellipse</c> in the reference and flattening
    /// them to paths would redraw the icon by hand.</param>
    /// <param name="Route">Where the tile navigates. Mutually exclusive with
    /// <paramref name="SectionKey"/>; a tile with a route renders as a real
    /// anchor, so middle-click, open-in-new-tab and Back all behave.</param>
    /// <param name="SectionKey">Set instead of <paramref name="Route"/> for the
    /// four quick-edit panels that open in place on this page.</param>
    /// <param name="RequiresFlag">A <c>RoleFeatures</c> flag that must be on for
    /// the viewer's role. UI visibility only — see the type-level note.</param>
    ///
    /// <remarks>There is no slot here for an ACTION. A hub entry is a place you
    /// go, and the one action this grid used to carry — the student form link —
    /// was a copy button wearing a tile's clothes. It moved to the screens that
    /// own the workflow; see <see cref="StudentFormLinks"/>.</remarks>
    public record ManagementEntry(
        string     Title,
        string     Description,
        string     IconMarkup,
        TileAccent Accent,
        string?    Route        = null,
        string?    SectionKey   = null,
        string?    RequiresFlag = null);

    /// <summary>A named group of destinations. <paramref name="Id"/> is a stable
    /// slug — it is the <c>aria-labelledby</c> target, so it must not change
    /// between renders.</summary>
    public record ManagementSection(string Id, string Title, string Note, ManagementEntry[] Entries);

    // ── Glyphs (Lucide 24×24) ───────────────────────────────────────────────
    // The eight the reference draws are transcribed from it verbatim. The rest
    // are the Lucide icons for modules the reference has no card for, chosen at
    // the same weight so a grid row does not mix two icon families.
    //
    // ELEVEN OF THESE ARE CURRENTLY UNREFERENCED — one for each entry in
    // WithdrawnFromHub (Folder, Target, UserPlus, Stages, CalCheck, Inbox,
    // ClipCheck, Book, Eye, Sliders, Database). They are kept on purpose: that
    // list promises re-showing a destination is one ManagementEntry moved back
    // into Sections, and deleting its glyph would make the promise false.
    private const string IcoUsers      = """<path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M22 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>""";
    private const string IcoCatalog    = """<path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01"/>""";
    private const string IcoFolder     = """<path d="M2 7v10a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2V9a2 2 0 0 0-2-2h-8L10 5H4a2 2 0 0 0-2 2Z"/>""";
    private const string IcoTarget     = """<circle cx="12" cy="12" r="9"/><circle cx="12" cy="12" r="5"/><circle cx="12" cy="12" r="1.4"/>""";
    private const string IcoUserPlus   = """<path d="M15 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2"/><circle cx="8.5" cy="7" r="4"/><path d="M19 8v6M22 11h-6"/>""";
    private const string IcoCalendar   = """<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/>""";
    private const string IcoStages     = """<path d="M6 3v12"/><circle cx="18" cy="6" r="3"/><circle cx="6" cy="18" r="3"/><path d="M15 6a9 9 0 0 1-9 9"/>""";
    private const string IcoFlag       = """<path d="M4 22V4a1 1 0 0 1 1-1h12l-3 4.5L17 12H5"/>""";
    private const string IcoCalCheck   = """<rect x="3" y="4" width="18" height="18" rx="2"/><path d="M16 2v4M8 2v4M3 10h18"/><path d="m9 16 2 2 4-4"/>""";
    private const string IcoCheckbox   = """<rect x="3" y="3" width="18" height="18" rx="3"/><path d="m8.5 12 2.5 2.5 5-5"/>""";
    private const string IcoFileText   = """<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8Z"/><path d="M14 2v6h6"/><path d="M9 13h6M9 17h4"/>""";
    private const string IcoMessage    = """<path d="M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2Z"/><path d="M9 9h6M9 12.5h4"/>""";
    private const string IcoInbox      = """<path d="m6 14 1.45-2.9A2 2 0 0 1 9.24 10H20a2 2 0 0 1 1.94 2.5l-1.55 6a2 2 0 0 1-1.94 1.5H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h3.9a2 2 0 0 1 1.69.9l.81 1.2a2 2 0 0 0 1.67.9H18a2 2 0 0 1 2 2v2"/>""";
    private const string IcoClipCheck  = """<path d="M16 4h2a2 2 0 0 1 2 2v14a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V6a2 2 0 0 1 2-2h2"/><rect x="8" y="2" width="8" height="4" rx="1"/><path d="m9 14 2 2 4-4"/>""";
    private const string IcoShield     = """<path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1Z"/><path d="m9 12 2 2 4-4"/>""";
    private const string IcoBook       = """<path d="M4 19.5v-15A2.5 2.5 0 0 1 6.5 2H19a1 1 0 0 1 1 1v18a1 1 0 0 1-1 1H6.5a2.5 2.5 0 0 1 0-5H20"/>""";
    private const string IcoEye        = """<path d="M2.06 12.35a1 1 0 0 1 0-.7 10.75 10.75 0 0 1 19.88 0 1 1 0 0 1 0 .7 10.75 10.75 0 0 1-19.88 0"/><circle cx="12" cy="12" r="3"/>""";
    private const string IcoLock       = """<rect x="3" y="11" width="18" height="11" rx="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>""";
    private const string IcoSliders    = """<path d="M21 4h-7M10 4H3M21 12h-9M8 12H3M21 20h-5M12 20H3M14 2v4M8 10v4M16 18v4"/>""";
    private const string IcoLink       = """<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/><path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>""";
    private const string IcoDatabase   = """<ellipse cx="12" cy="5" rx="9" ry="3"/><path d="M3 5v14a9 3 0 0 0 18 0V5"/><path d="M3 12a9 3 0 0 0 18 0"/>""";

    /// <summary>
    /// The hub, in reading order. TEN destinations in the reference's four
    /// sections — a curated map of the current management architecture, not an
    /// index of every administrative route that exists.
    ///
    /// <para>THE REFERENCE IS THE CURATION, and its card subtitles say so
    /// literally: "ניהול פרויקטים — קטלוג, פרויקטים פעילים ושיוכים" is one card
    /// over three old hub entries, and "מחזורים אקדמיים — מחזורים, תוכנית
    /// המחזור ואבני הדרך שלו" is one card over three more. The previous pass
    /// read those as mock labels to be expanded back into 21 tiles. They are
    /// not: they are the consolidation.</para>
    ///
    /// <para>ELEVEN ENTRIES WERE WITHDRAWN FROM THE HUB — see
    /// <see cref="WithdrawnFromHub"/>, which names every one of them and why.
    /// NOTHING WAS DELETED. Every route still exists, still resolves, still
    /// carries its own [Authorize], and is still reachable by URL and from the
    /// screens that own the concept. This file decides what the Home SHOWS.</para>
    ///
    /// <para>The accent rotates violet → blue → teal from the start of each
    /// section, which is what the reference does across its own cards.</para>
    /// </summary>
    public static readonly ManagementSection[] Sections =
    {
        // ── ניהול שוטף ──────────────────────────────────────────────────────
        new("lmg-s-daily", "ניהול שוטף", "משתמשים ופרויקטים", new[]
        {
            new ManagementEntry("משתמשים", "משתמשים, תפקידים, מחזורים וסטטוסים",
                IcoUsers, TileAccent.Violet,
                Route: "/management/users", RequiresFlag: RoleFeatures.CanManageUsers),

            // THE project-management entry. Catalog is the broader pool /
            // proposal experience; "פרויקטים פעילים" (/management/projects) is a
            // lifecycle VIEW over the same Projects domain, and the lecturer's
            // own redesigned הצוותים שלי (/projects) is where active teams are
            // actually worked. Two equal cards for one domain is the thing this
            // pass removes.
            //
            // TITLED FOR ITS DESTINATION, not for the reference. The reference
            // calls this card "ניהול פרויקטים"; the page it opens is titled
            // "קטלוג פרויקטים", and renaming that page is out of scope here. A
            // tile should predict the screen it opens, so the tile takes the
            // page's name and the DESCRIPTION carries the consolidation.
            new ManagementEntry("קטלוג פרויקטים", "הצעות פרויקט, זמינות, שיוך למחזור ופרויקטים פעילים",
                IcoCatalog, TileAccent.Blue,
                Route: "/management/catalog"),
        }),

        // ── תוכנית אקדמית ───────────────────────────────────────────────────
        new("lmg-s-program", "תוכנית אקדמית", "מחזורים ותבניות התוכנית", new[]
        {
            // "שלבי תוכנית" is not a second card here. The cycles screen draws
            // an always-visible stages button on every cycle row
            // (→ /management/cycles/{id}/stages), so the stage editor is
            // reached through the cycle it belongs to. The old hub's
            // "?focus=stages" tile only added a guidance banner to this same
            // page — a shortcut to a screen one click away.
            new ManagementEntry("מחזורים אקדמיים", "מחזורים, תוכנית המחזור ואבני הדרך שלו",
                IcoCalendar, TileAccent.Violet,
                Route: "/management/cycles"),

            new ManagementEntry("תבניות אבני דרך", "ספריית אבני הדרך לתוכניות המחזורים",
                IcoFlag, TileAccent.Blue,
                Route: "/management/milestones"),

            new ManagementEntry("תבניות משימות", "משימות מוכנות לשיבוץ באבני דרך",
                IcoCheckbox, TileAccent.Teal,
                Route: "/management/tasks"),
        }),

        // ── טפסים ותהליכים ──────────────────────────────────────────────────
        // CONFIGURATION ONLY. The two operational queues that used to sit here
        // — תור הבקשות and תור אישורי מנחה — are work, not settings, and בקשות
        // is already a primary nav destination. A hub that mixes "define the
        // request types" with "answer today's requests" teaches neither.
        new("lmg-s-forms", "טפסים ותהליכים", "טפסים ומערך הבקשות", new[]
        {
            new ManagementEntry("טפסים", "בניית טפסים, סעיפים ושדות למשימות ובקשות",
                IcoFileText, TileAccent.Violet,
                Route: "/management/forms"),

            new ManagementEntry("סוגי בקשות", "הגדרת סוגי הבקשות והטפסים המשויכים להן",
                IcoMessage, TileAccent.Blue,
                Route: "/management/request-types"),
        }),

        // ── הגדרות ──────────────────────────────────────────────────────────
        new("lmg-s-system", "הגדרות", "תצורת המערכת", new[]
        {
            new ManagementEntry("הגדרות אישורים", "ספי תזכורת, ערוצי שליחה, תדירות והרשאת עקיפה",
                IcoShield, TileAccent.Violet,
                Route: "/management/pending-approval-settings"),

            // Not drawn by the reference, kept deliberately: permissions are a
            // current, non-superseded management area, and this screen is the
            // only way to reach the role feature-flag matrix — including the
            // flags THIS file reads in RequiresFlag.
            new ManagementEntry("הרשאות לפי תפקיד", "מה מותר לכל תפקיד במערכת",
                IcoLock, TileAccent.Blue,
                Route: "/management/role-settings"),

            // Also not drawn by the reference, also kept: the integrations hub
            // is the only entry point to Slack, Airtable, external forms and
            // the innovation webhook. Airtable itself is NOT a second tile —
            // this hub lists it, and the catalog's import button navigates
            // straight to it.
            new ManagementEntry("ניהול אינטגרציות", "Slack, Airtable, טפסים חיצוניים ו-webhook החדשנות",
                IcoLink, TileAccent.Teal,
                Route: "/management/integrations"),
        }),
    };

    /// <summary>
    /// The eleven destinations the hub no longer shows, and why — kept as data
    /// so the decision is auditable and reversible rather than living in a
    /// commit message.
    ///
    /// <para>NONE OF THESE WAS DELETED. Every route resolves, every page
    /// renders, every [Authorize] is unchanged, and each one is reachable by
    /// URL and from the screen that owns the concept. Re-showing one is a
    /// single <see cref="ManagementEntry"/> moved back into
    /// <see cref="Sections"/>.</para>
    ///
    /// <para>The four with no route are the in-place quick-edit panels
    /// LecturerManagementPage still renders; their code, CSS and services are
    /// untouched and they open again the moment an entry carrying their
    /// SectionKey returns to the hub.</para>
    /// </summary>
    public static readonly (string Title, string Target, string Reason)[] WithdrawnFromHub =
    {
        ("פרויקטים פעילים",      "/management/projects",
            "A lifecycle view over the same Projects domain as the catalog; active teams are worked in the lecturer's redesigned /projects."),
        ("שיבוץ צוותים לפרויקטים", "/assignments",
            "Already a primary nav destination (שיבוצים). The hub was its second entry point."),
        ("שיוך מנחים לצוותים",   "section:teams",
            "Mentor assignment is owned by /management/projects and the shared ProjectMentorsEditor."),
        ("שלבי תוכנית",          "/management/cycles?focus=stages",
            "The cycles screen draws a stages button on every cycle row; this tile only added a banner to that same page."),
        ("עדכון מועדי אבני דרך", "section:milestones",
            "Dates belong to the milestone-template editor, which now owns them in the redesigned /management/milestones."),
        ("תור הבקשות",           "/management/requests",
            "Already a primary nav destination (בקשות), and an operational queue rather than configuration."),
        ("תור אישורי מנחה",      "/management/pending-mentor-approvals",
            "An operational queue. Its own settings page names it as such and this hub keeps the settings."),
        ("חומרי עזר",            "/management/resources",
            "The redesigned /resource-files workspace — itself a primary nav item — links straight to it."),
        ("נראות משאבים",         "section:resources",
            "The per-resource visibility flags are edited on /management/resources."),
        ("עריכה מהירה — סטודנט ומנחה", "section:settings",
            "A subset of the role feature-flag matrix the הרשאות לפי תפקיד tile opens."),
        ("Airtable",             "/management/integrations/airtable",
            "Listed by the integrations hub, and the catalog's import button navigates straight to it."),
    };
}
