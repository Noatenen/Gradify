namespace AuthWithAdmin.Client.Pages.ProjectWorkspace;

/// <summary>
/// The kind of tool a project resource points at. Derived from the URL every
/// time it is rendered — never stored — so a resource's icon and type label can
/// never disagree with the link itself.
/// </summary>
public enum ProjectResourceKind
{
    Document,
    Drive,
    Figma,
    Code,
    Video,
    Board,
    Link
}

/// <summary>
/// URL → kind recognition, plus the Hebrew type label and the icon each kind
/// draws.
///
/// COLOUR: every kind uses the same brand tint. The System Master permits three
/// semantic colours (violet / teal / rose) and nothing here is a status, so the
/// kind is carried by the ICON, not by a per-vendor colour — inventing one hue
/// per SaaS product is exactly the "new arbitrary colours" the visual contract
/// rules out. The design export tints each card differently; that is the one
/// place this section knowingly departs from it.
/// </summary>
public static class ProjectResourceKinds
{
    /// <summary>
    /// Recognises the common tools a project team actually uses. Matching is on
    /// the HOST only — a path can contain anything — and anything unrecognised
    /// is an honest generic link rather than a guess.
    /// </summary>
    public static ProjectResourceKind Detect(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return ProjectResourceKind.Link;

        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
            return ProjectResourceKind.Link;

        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        if (host.Contains("docs.google."))
        {
            // One host serves documents, sheets, slides and forms; the first
            // path segment is what separates them. They are all "a document"
            // for this card's purposes except Drive folders, handled below.
            return ProjectResourceKind.Document;
        }

        if (host.Contains("drive.google.")) return ProjectResourceKind.Drive;

        if (host.Contains("figma.com"))    return ProjectResourceKind.Figma;

        if (host.Contains("github.com")
            || host.Contains("gitlab.com")
            || host.Contains("bitbucket.org")) return ProjectResourceKind.Code;

        if (host.Contains("youtube.com")
            || host.Contains("youtu.be")
            || host.Contains("vimeo.com")) return ProjectResourceKind.Video;

        if (host.Contains("trello.com")
            || host.Contains("notion.so")
            || host.Contains("miro.com")
            || host.Contains("atlassian.net")) return ProjectResourceKind.Board;

        // A Google Docs link that arrived on a non-docs host, e.g. a shortened
        // /document/ path on a workspace domain.
        if (path.StartsWith("/document/") || path.StartsWith("/spreadsheets/"))
            return ProjectResourceKind.Document;

        return ProjectResourceKind.Link;
    }

    /// <summary>The quiet second line on a resource card.</summary>
    public static string Label(ProjectResourceKind kind) => kind switch
    {
        ProjectResourceKind.Document => "מסמך",
        ProjectResourceKind.Drive    => "תיקייה",
        ProjectResourceKind.Figma    => "Figma",
        ProjectResourceKind.Code     => "מאגר קוד",
        ProjectResourceKind.Video    => "וידאו",
        ProjectResourceKind.Board    => "לוח עבודה",
        _                            => "קישור"
    };

    /// <summary>
    /// The icon's path data, drawn on a 24x24 viewBox with a 1.6 stroke — the
    /// same geometry every other icon in the student scope uses.
    /// </summary>
    public static string IconPath(ProjectResourceKind kind) => kind switch
    {
        ProjectResourceKind.Document => "M7 3h7l5 5v13H7zM10 12h7M10 16h5",
        ProjectResourceKind.Drive    => "M4 7a2 2 0 0 1 2-2h3.6l1.6 2H18a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2z",
        ProjectResourceKind.Figma    => "M9 3h3v18a3 3 0 1 1-3-3h6a3 3 0 1 0 0-6H9a3 3 0 1 1 0-6M15 3a3 3 0 1 1 0 6",
        ProjectResourceKind.Code     => "m9 8-4 4 4 4M15 8l4 4-4 4",
        ProjectResourceKind.Video    => "M4 6h11v12H4zM15 10l5-3v10l-5-3",
        ProjectResourceKind.Board    => "M4 4h16v16H4zM9 4v16M14 4v16",
        _                            => "M10 13a5 5 0 0 0 7 0l2-2a5 5 0 0 0-7-7l-1 1M14 11a5 5 0 0 0-7 0l-2 2a5 5 0 0 0 7 7l1-1"
    };

    /// <summary>
    /// The host, for the card's accessible name and title — so a student can
    /// tell two "מסמך אפיון" links apart without opening them.
    /// </summary>
    public static string? HostOf(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return null;
        return uri.Host;
    }
}
