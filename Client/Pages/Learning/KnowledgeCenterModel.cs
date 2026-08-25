using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Client.Pages.Learning;

/// <summary>
/// The Knowledge Center's four fixed categories.
///
/// <para><b>Why these four, and why they are fixed.</b> Neither ResourceFiles
/// nor any other table carries an editorial category column, and the lecturer
/// upload form (ResourceFilesPage) offers no category input — so the finalized
/// Design's seven sample categories (כלים / תבניות / מסמכים / צוות טכנולוגי /
/// מדריכים / סרטונים / עבודות לדוגמה) cannot be reconstructed from real data
/// and are deliberately not invented here.</para>
///
/// <para>What the data does carry is <c>ItemType</c> plus a <c>ContentType</c>
/// drawn from a closed set — the upload form's own accept list
/// (pdf/doc/docx/ppt/pptx/xls/xlsx/txt/zip/jpg/jpeg/png/gif). These four
/// categories partition that set into labels a student reads without knowing
/// the schema. Two of them (מסמכים, סרטונים) are literally names the Design
/// itself uses.</para>
///
/// <para>The taxonomy stays fixed even when a category is empty. That is the
/// Design's own behaviour, not a compromise: Motiva Resources.dc.html:261-263
/// ships a dedicated empty-category state ("אין עדיין משאבים בקטגוריה הזו"),
/// so a grid that never reshuffles as content arrives is the more faithful
/// reading than one that hides empty cards.</para>
/// </summary>
public enum KnowledgeCategoryKey
{
    /// <summary>pdf, doc, docx, txt.</summary>
    Documents,
    /// <summary>ItemType == "Video".</summary>
    Videos,
    /// <summary>ppt, pptx, xls, xlsx.</summary>
    Slides,
    /// <summary>Images, archives, and anything unmatched.</summary>
    Images,
}

/// <summary>What activating a resource row actually does.</summary>
public enum KnowledgeResourceAction
{
    /// <summary>Downloads from wwwroot/resources under the original filename.</summary>
    Download,
    /// <summary>Opens the video modal (YouTube URL normalized at play time).</summary>
    PlayVideo,
    /// <summary>Opens an absolute URL in a new tab.</summary>
    External,
    /// <summary>The row has no usable target — rendered inert rather than as a
    /// broken link. Reachable only for a malformed row (a File with no stored
    /// file and no URL); no such row exists today.</summary>
    None,
}

/// <summary>One category card in the browse grid.</summary>
/// <param name="Accent">Hex accent. Every value is drawn from the finalized
/// Knowledge Center's own declared palette (CATEGORY_ACCENT / BUCKET_META in
/// Motiva Resources.dc.html:444-457) — no new hue is introduced. These are
/// categorical metadata, not workflow status, which is why they are not
/// restricted to the Master's violet/teal/rose status triad and why they stay
/// local to this page instead of touching MStatusBadge.</param>
public sealed record KnowledgeCategory(
    KnowledgeCategoryKey Key,
    string Label,
    string Accent,
    /// <summary>Optional short form for the finalized Resources screen's filter
    /// chip row, where six pills share one line. Null means "no shorter honest
    /// name exists" and the full label is used — which is why this is a
    /// defaulted parameter and not a second required one.</summary>
    string? ShortLabel = null)
{
    /// <summary>What the filter chip says.</summary>
    public string ChipLabel => ShortLabel ?? Label;
}

/// <summary>One resource row, fully resolved for rendering. Everything on this
/// record comes from a real ResourceFileDto field — nothing is synthesized.</summary>
public sealed record KnowledgeResource(
    int Id,
    /// <summary>Display title with the file extension stripped; the type pill
    /// already carries the format.</summary>
    string Title,
    /// <summary>The untouched original filename, used for the download
    /// attribute so the file lands on disk under the name the lecturer
    /// uploaded.</summary>
    string DownloadName,
    KnowledgeCategoryKey Category,
    /// <summary>Pill text: PDF / Word / סרטון / מצגת / גיליון / תמונה /
    /// ארכיון / קובץ.</summary>
    string TypeLabel,
    string Accent,
    /// <summary>Milestone title, or "כללי" for MilestoneId 0.</summary>
    string MilestoneLabel,
    string UploadedLabel,
    KnowledgeResourceAction Action,
    string? Href,
    string? VideoUrl)
{
    /// <summary>The Design's row mark: "↖" when activating leaves Motiva,
    /// "‹" when it does not (Motiva Resources.dc.html:521).</summary>
    public string Mark => Action == KnowledgeResourceAction.External ? "↖" : "‹";

    // ── Added for the FINAL Resources screen ────────────────────────────────
    // The finalized design draws a card and a detail panel, not a one-line row,
    // so it asks the resource for more of itself than the row ever did:
    // a description, the tagged task, a format and a full date.
    //
    // Declared as init-only properties rather than positional parameters on
    // purpose — every one of them is OPTIONAL and every existing construction
    // site (the mentor library, the dashboard strip, the deliverables section)
    // keeps compiling and rendering unchanged.

    /// <summary>The lecturer's own description, when they wrote one. Empty on
    /// every row in the database today, which is exactly why the card and the
    /// panel render it conditionally instead of reserving space for it.</summary>
    public string? Description { get; init; }

    /// <summary>Title of the TaskTemplate the resource is pinned to, when the
    /// lecturer pinned it to one (ResourceFileDto.TaskName).</summary>
    public string? TaskLabel { get; init; }

    /// <summary>What the file (or link) actually is, in the detail panel's
    /// "פורמט" row: an uppercase extension for a stored file, the host for a
    /// video or external link. Null when neither can be derived — the row is
    /// then omitted rather than filled with a guess.</summary>
    public string? Format { get; init; }

    /// <summary>True when the resource is pinned to a real milestone. Distinct
    /// from <see cref="MilestoneLabel"/>, which reads "כללי" for MilestoneId 0
    /// — a label, not a flag, and not something to test with a string compare.</summary>
    public bool HasMilestone { get; init; }

    /// <summary>The raw upload timestamp, for the panel's full date.</summary>
    public DateTime UploadedAt { get; init; }

    /// <summary>The verb on the card's action link and the detail panel's
    /// primary button. It names the resource's REAL behaviour, which is why it
    /// is derived from <see cref="Action"/> and not from the category.</summary>
    public string ActionLabel => Action switch
    {
        KnowledgeResourceAction.Download  => "הורדה",
        KnowledgeResourceAction.PlayVideo => "צפייה",
        KnowledgeResourceAction.External  => "פתיחה",
        _                                 => "",
    };
}

/// <summary>
/// Maps the real ResourceFiles domain onto the finalized Design's presentation.
/// Pure functions over the DTO — no service calls, no state, no sample data.
/// </summary>
public static class KnowledgeCenterModel
{
    // ── Palette ──────────────────────────────────────────────────────────────
    // Every hex below appears verbatim in Motiva Resources.dc.html's own
    // CATEGORY_ACCENT / BUCKET_META tables. Kept as literals (not tokens)
    // because they are a categorical scale that exists only on this screen;
    // promoting them to --motiva-* would imply system-wide meaning they do
    // not have.
    private const string AccentDocs   = "#6366F1"; // CATEGORY_ACCENT.docs
    private const string AccentVideo  = "#6D28D9"; // CATEGORY_ACCENT.videos
    private const string AccentSlides = "#0F8F8C"; // CATEGORY_ACCENT.templates
    private const string AccentImages = "#14A090"; // CATEGORY_ACCENT.examples
    private const string AccentGuide  = "#6D5FD6"; // BUCKET_META.guide
    private const string AccentTool   = "#0D9C9A"; // BUCKET_META.tool

    /// <summary>The fixed four, in the Design's grid order.</summary>
    public static readonly IReadOnlyList<KnowledgeCategory> Categories = new[]
    {
        new KnowledgeCategory(KnowledgeCategoryKey.Documents, "מסמכים וטפסים",  AccentDocs,   "מסמכים"),
        new KnowledgeCategory(KnowledgeCategoryKey.Videos,    "סרטונים",        AccentVideo),
        new KnowledgeCategory(KnowledgeCategoryKey.Slides,    "מצגות וגיליונות", AccentSlides, "מצגות"),
        new KnowledgeCategory(KnowledgeCategoryKey.Images,    "תמונות וקבצים",  AccentImages),
    };

    public static string LabelFor(KnowledgeCategoryKey key) =>
        Categories.First(c => c.Key == key).Label;

    // Only extensions the upload form actually accepts are stripped from a
    // display title. A whitelist, not Path.GetExtension: a title like
    // "גרסה 1.2" must keep its ".2".
    private static readonly HashSet<string> StrippableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".ppt", ".pptx", ".xls", ".xlsx",
        ".txt", ".zip", ".jpg", ".jpeg", ".png", ".gif",
    };

    /// <summary>Projects the student resource feed onto row view-models,
    /// newest first within the order the API already returns.</summary>
    public static List<KnowledgeResource> ToResources(IEnumerable<ResourceFileDto> files) =>
        files.Select(ToResource).ToList();

    public static KnowledgeResource ToResource(ResourceFileDto file)
    {
        var isVideo = string.Equals(file.ItemType, ResourceItemType.Video, StringComparison.OrdinalIgnoreCase);
        var (category, typeLabel, accent) = Classify(file, isVideo);
        var (action, href) = ResolveAction(file, isVideo);

        return new KnowledgeResource(
            Id:             file.Id,
            Title:          StripExtension(file.FileName),
            DownloadName:   file.FileName,
            Category:       category,
            TypeLabel:      typeLabel,
            Accent:         accent,
            MilestoneLabel: string.IsNullOrWhiteSpace(file.MilestoneName) ? "כללי" : file.MilestoneName!,
            UploadedLabel:  file.UploadedAt.ToString("dd.MM.yy"),
            Action:         action,
            Href:           href,
            VideoUrl:       file.VideoUrl)
        {
            Description  = string.IsNullOrWhiteSpace(file.Description) ? null : file.Description!.Trim(),
            TaskLabel    = string.IsNullOrWhiteSpace(file.TaskName)    ? null : file.TaskName!.Trim(),
            Format       = FormatOf(file, isVideo, action),
            HasMilestone = file.MilestoneId > 0 && !string.IsNullOrWhiteSpace(file.MilestoneName),
            UploadedAt   = file.UploadedAt,
        };
    }

    /// <summary>The detail panel's "פורמט" value, derived and never invented:
    /// the stored file's own extension, or the host a video / external link
    /// actually points at. Null when the resource carries neither.</summary>
    private static string? FormatOf(ResourceFileDto file, bool isVideo, KnowledgeResourceAction action)
    {
        if (isVideo || action == KnowledgeResourceAction.External)
            return Uri.TryCreate(file.VideoUrl, UriKind.Absolute, out var uri)
                ? uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host
                : null;

        var ext = ExtensionOf(file.FileName);
        return ext.Length > 1 ? ext[1..].ToUpperInvariant() : null;
    }

    /// <summary>Client-side search across everything a student can actually see
    /// on a row, plus the original filename so searching "docx" still works.</summary>
    public static List<KnowledgeResource> Search(IEnumerable<KnowledgeResource> resources, string query)
    {
        var q = query.Trim();
        if (q.Length == 0) return resources.ToList();

        return resources
            .Where(r =>
                Contains(r.Title, q) ||
                Contains(r.Description, q) ||
                Contains(r.TaskLabel, q) ||
                Contains(r.DownloadName, q) ||
                Contains(r.TypeLabel, q) ||
                Contains(r.MilestoneLabel, q) ||
                Contains(LabelFor(r.Category), q))
            .ToList();
    }

    private static bool Contains(string? haystack, string needle) =>
        haystack is not null && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

    // ── Classification ───────────────────────────────────────────────────────
    // Keyed on ContentType, falling back to the filename extension. The
    // fallback is load-bearing: a Video row carries an empty ContentType, and
    // ResourceFiles rows predating a stricter upload path may too.
    private static (KnowledgeCategoryKey, string, string) Classify(ResourceFileDto file, bool isVideo)
    {
        if (isVideo) return (KnowledgeCategoryKey.Videos, "סרטון", AccentVideo);

        var ct  = file.ContentType ?? "";
        var ext = ExtensionOf(file.FileName);

        if (ct == "application/pdf" || ext == ".pdf")
            return (KnowledgeCategoryKey.Documents, "PDF", AccentGuide);

        if (ct.Contains("wordprocessingml") || ct == "application/msword" || ext is ".doc" or ".docx")
            return (KnowledgeCategoryKey.Documents, "Word", AccentDocs);

        if (ct == "text/plain" || ext == ".txt")
            return (KnowledgeCategoryKey.Documents, "טקסט", AccentGuide);

        if (ct.Contains("presentation") || ext is ".ppt" or ".pptx")
            return (KnowledgeCategoryKey.Slides, "מצגת", AccentSlides);

        if (ct.Contains("spreadsheet") || ct.Contains("excel") || ext is ".xls" or ".xlsx")
            return (KnowledgeCategoryKey.Slides, "גיליון", AccentTool);

        if (ct.StartsWith("image/") || ext is ".jpg" or ".jpeg" or ".png" or ".gif")
            return (KnowledgeCategoryKey.Images, "תמונה", AccentImages);

        if (ct is "application/zip" or "application/x-zip-compressed" || ext == ".zip")
            return (KnowledgeCategoryKey.Images, "ארכיון", AccentImages);

        // Anything unmatched lands in the catch-all category rather than
        // disappearing from the page.
        return (KnowledgeCategoryKey.Images, "קובץ", AccentImages);
    }

    private static (KnowledgeResourceAction, string?) ResolveAction(ResourceFileDto file, bool isVideo)
    {
        if (isVideo)
        {
            return string.IsNullOrWhiteSpace(file.VideoUrl)
                ? (KnowledgeResourceAction.None, null)
                : (KnowledgeResourceAction.PlayVideo, null);
        }

        // The real, existing download path — same one StudentFilesPage uses.
        if (!string.IsNullOrWhiteSpace(file.StoredFileName))
            return (KnowledgeResourceAction.Download, $"/resources/{file.StoredFileName}");

        // No ResourceFiles row is an external link today. This branch exists so
        // that if one ever arrives (or a File row loses its stored file), the
        // row opens safely instead of pointing at "/resources/".
        if (IsAbsoluteUrl(file.VideoUrl))
            return (KnowledgeResourceAction.External, file.VideoUrl);

        return (KnowledgeResourceAction.None, null);
    }

    /// <summary>
    /// Converts a YouTube watch/short/embed URL to an embeddable iframe src,
    /// or null when the URL is not recognisably YouTube (the caller then falls
    /// back to opening it externally).
    ///
    /// <para>This is StudentFilesPage's existing implementation, unchanged —
    /// including its handling of an already-embed URL and its query-string
    /// stripping, which the older LearningMaterialsPage copy lacked. It is
    /// duplicated rather than shared because folding the two together means
    /// editing StudentFilesPage, which Phase 4C is scoped out of; the duplicate
    /// is recorded so the /files cleanup can collapse it.</para>
    /// </summary>
    public static string? ToEmbedUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        if (url.Contains("youtube.com/embed/")) return url.Split('?')[0];

        if (url.Contains("youtu.be/"))
        {
            var id = url.Split("youtu.be/").Last().Split('?').First().Trim('/');
            return string.IsNullOrEmpty(id) ? null : $"https://www.youtube.com/embed/{id}";
        }

        if (url.Contains("youtube.com/watch"))
        {
            var vIndex = url.IndexOf("v=", StringComparison.Ordinal);
            if (vIndex < 0) return null;
            var id = url[(vIndex + 2)..].Split('&').First().Trim();
            return string.IsNullOrEmpty(id) ? null : $"https://www.youtube.com/embed/{id}";
        }

        return null;
    }

    private static bool IsAbsoluteUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string ExtensionOf(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "";
        var dot = fileName.LastIndexOf('.');
        return dot < 0 ? "" : fileName[dot..].ToLowerInvariant();
    }

    private static string StripExtension(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)) return "";

        var ext = ExtensionOf(fileName);
        return ext.Length > 0 && StrippableExtensions.Contains(ext)
            ? fileName[..^ext.Length].TrimEnd()
            : fileName;
    }
}
