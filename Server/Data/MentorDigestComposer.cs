using System.Globalization;
using System.Net;
using System.Text;
using AuthWithAdmin.Shared.AuthSharedModels;

namespace AuthWithAdmin.Server.Data;

// ─────────────────────────────────────────────────────────────────────────────
//  MentorDigestComposer — turns a MentorAttentionDto into the one daily digest.
//
//  A PURE FUNCTION. No database, no HTTP, no clock. It receives a snapshot and
//  returns text. That is what lets the scheduled run and the admin manual
//  trigger share it exactly: there is no "test digest" code path, because there
//  is nothing here to stub. Anything the email needs that the attention model
//  does not carry — extension dates and targets — is passed IN, so this stays
//  a function of its arguments and MentorAttentionService is not touched.
//
//  WHAT THE EMAIL ANSWERS, IN THIS ORDER
//      1. what is waiting for me            → the headline count
//      2. what needs attention first        → section order + the model's own
//                                             NEEDS ATTENTION → WAITING → NEW
//      3. how long has it been waiting      → MentorAging.WaitingLabel
//      4. what exactly do I open in Motiva  → one deep link per named item
//
//  EVERY CTA IS NAVIGATION, NEVER AN ACTION. There is no approve, reject or
//  recommend here, and no link in this email changes state. Mail clients,
//  link-preview services and security scanners fetch URLs unattended, so a
//  state-changing GET would be fired by a machine before the mentor read the
//  sentence. Decisions stay inside Motiva behind the existing POST endpoints.
//
//  WHAT IS DELIBERATELY ABSENT
//    * terminal items — reviewed submissions, resolved/closed requests
//    * requests sitting with the lecturer or the team; the mentor cannot act
//    * team milestone and deliverable dates; they belong to the calendar, and
//      repeating them every morning is how a digest becomes noise
//    * personal-task titles — private, and nothing here is actionable by email
//    * project health, which is not computed in this product yet
//    * empty sections, and the whole digest when nothing is waiting — a daily
//      "you have nothing" email is how a digest gets filtered to spam
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The composed digest. <see cref="IsEmpty"/> callers must not send.</summary>
public sealed record MentorDigest(
    int    Total,
    string Title,
    string PlainText,
    string HtmlBody)
{
    public bool IsEmpty => Total == 0;
}

/// <summary>
/// The extension facts a mentor needs to understand a deferral request without
/// opening it: what the deadline is now, what is being asked for, and which
/// task or milestone it applies to.
///
/// <para>Passed in rather than read here, and NOT added to MentorAttentionDto:
/// this is the only surface that needs it, and widening the shared attention
/// model for one consumer's presentation would put request-type-specific fields
/// in front of every screen that reads it.</para>
/// </summary>
public sealed record ExtensionContext(
    DateTime? CurrentDueDate,
    DateTime? RequestedDueDate,
    string?   TargetLabel);

public static class MentorDigestComposer
{
    // ── Content limits ───────────────────────────────────────────────────────
    // A digest, not a second dashboard. Section headings always carry the REAL
    // total, so capping what is named never misrepresents the workload.

    private const int MaxNamedPerSection = 2;
    private const int MaxNamedOverall    = 4;

    /// <summary>The in-app notification title. Constant, so a mentor recognises
    /// the row instantly in the bell.</summary>
    public const string DigestTitle = "סיכום הפעילות היומי שלך";

    // ── Palette — the REAL Motiva tokens, not lookalikes ─────────────────────
    //
    // Every value below is copied from Client/wwwroot/css/motiva-tokens.css.
    // The previous version used generic greys (#111827/#6B7280/#E5E7EB) that
    // resemble Motiva without being it, which is precisely why the email read
    // as a stock HTML summary. Email cannot consume CSS custom properties —
    // there is no :root in a mail client — so the values are mirrored here, and
    // this comment is the contract that keeps them honest.
    //
    //   Ink / InkSecond / InkMuted   --g-text-primary / -secondary / -muted
    //   Paper / Surface              --g-bg-page ("Paper") / --g-bg-surface
    //   LineSoft / LineEdge          --g-border-light / --g-border
    //   Violet / Teal / RoseInk      the Master's three semantics; rose at TEXT
    //                                weight, which the Master reserves for
    //                                labels like "באיחור"
    private const string Ink       = "#1A1820";   // --g-text-primary
    private const string InkSecond = "#5B5568";   // --g-text-secondary
    private const string InkMuted  = "#8B8698";   // --g-text-muted
    private const string Paper     = "#FAF9F7";   // --g-bg-page  (Master "Paper")
    private const string Surface   = "#FFFFFF";   // --g-bg-surface
    private const string LineSoft  = "#F0EEF9";   // --g-border-light (row dividers)
    private const string LineEdge  = "#E7E4EC";   // --g-border       (strong edges)
    private const string Violet    = "#4F46E5";   // --motiva-color-violet
    private const string RoseInk   = "#B23256";   // --motiva-color-rose-ink
    private const string Radius    = "10px";      // --g-radius-sm, the CTA radius

    /// <summary>
    /// The product's signature sweep, spelled out because email has no tokens:
    /// <c>--motiva-gradient-signature</c> = purple → violet → blue → teal.
    ///
    /// <para>Always painted OVER a solid <see cref="Violet"/> background, never
    /// instead of it. Outlook's Word renderer ignores background-image entirely
    /// and keeps the solid; Gmail web and Apple Mail show the sweep. That makes
    /// the gradient a pure enhancement with no client where it can fail to a
    /// transparent or grey button.</para>
    /// </summary>
    private const string SignatureGradient =
        "linear-gradient(90deg,#6D0EE6,#4F46E5,#1C7FB8,#0D9C9A)";

    private const string Font = "'Assistant','Segoe UI',Arial,sans-serif";

    /// <summary>The real logo shipped with the app —
    /// <c>Client/wwwroot/images/motiva-logo.png</c>, the same file AppSideNav
    /// and LoginPage render. Served anonymously by UseStaticFiles, which runs
    /// before authentication.
    ///
    /// <para>The sibling <c>motiva-logo.svg</c> is NOT used: it is a checked-in
    /// placeholder whose own comment says "replace this file with the final
    /// Motiva logo asset", and SVG is unsupported in Gmail and Outlook
    /// regardless.</para></summary>
    public const string LogoWebPath = "images/motiva-logo.png";

    /// <summary>
    /// The cid token the HTML references and the sender attaches under.
    ///
    /// <para>The logo is EMBEDDED, not linked. It deliberately does NOT use
    /// App:BaseUrl: that value is localhost outside production, and even in
    /// production most clients block remote images by default — a masthead that
    /// disappears on first open is not a masthead. BaseUrl remains in use for
    /// every application deep link, where a real URL is the whole point.</para>
    /// </summary>
    public const string LogoCid = "motiva-logo";

    /// <summary>Natural size is 1566×338; 130×28 preserves that ratio and
    /// matches the 28px the app's own sidebar uses. Both attributes AND inline
    /// dimensions are set — Outlook honours the attributes, everything else the
    /// style.</summary>
    private const int LogoW = 130;
    private const int LogoH = 28;

    /// <summary>
    /// Composes the digest for one mentor.
    /// </summary>
    /// <param name="attention">The mentor's snapshot. Items arrive already in
    /// the model's canonical order (NEEDS ATTENTION → WAITING → NEW, oldest
    /// first) and are never re-sorted here — there is exactly one priority
    /// model in this product.</param>
    /// <param name="baseUrl">Absolute origin for email links. Empty is
    /// supported and renders the email without anchors: unclickable text is
    /// degraded, whereas a relative href in a mail client resolves against the
    /// mail client and is simply broken.</param>
    /// <param name="extensions">Extension facts by request id. Missing entries
    /// degrade to omitting those two lines rather than guessing.</param>
    public static MentorDigest Compose(
        MentorAttentionDto attention,
        string? baseUrl = null,
        IReadOnlyDictionary<int, ExtensionContext>? extensions = null,
        string? logoCid = null)
    {
        var c = attention.Counts;

        if (c.Total == 0)
            return new MentorDigest(0, DigestTitle, "", "");

        // ── Selection ────────────────────────────────────────────────────────
        // Take from the top of the model's order. Two caps apply: per section,
        // and overall — the overall cap is what stops a mentor with a heavy
        // caseload receiving a wall of text on a busy morning.
        var requests = attention.Items
            .Where(i => i.Kind == MentorAttentionKind.Request)
            .Take(MaxNamedPerSection)
            .ToList();

        var submissions = attention.Items
            .Where(i => i.Kind == MentorAttentionKind.Submission)
            .Take(Math.Max(0, Math.Min(MaxNamedPerSection, MaxNamedOverall - requests.Count)))
            .ToList();

        string headline = c.Total == 1
            ? "יש לך היום פריט אחד שממתין לטיפול"
            : $"יש לך היום {c.Total} פריטים שממתינים לטיפול";

        return new MentorDigest(
            Total:     c.Total,
            Title:     DigestTitle,
            PlainText: BuildPlainText(headline, c, requests, submissions, extensions),
            HtmlBody:  BuildHtml(headline, attention.AsOfLocalDate, c, requests, submissions,
                                 extensions, baseUrl, logoCid));
    }

    // ── Item wording ─────────────────────────────────────────────────────────

    /// <summary>"בקשת דחייה — צוות Motiva". The request TYPE leads, because a
    /// student-authored title is far less informative than knowing this is a
    /// deferral; the team says whose it is.</summary>
    private static string RequestHeading(MentorAttentionItemDto i)
    {
        var type  = string.IsNullOrWhiteSpace(i.RequestType)
            ? "בקשה"
            : RequestTypes.Label(i.RequestType!);
        var owner = Owner(i);
        return owner is null ? type : $"{type} — {owner}";
    }

    /// <summary>"בדיקת אבטיפוס — צוות הספרייה". The task title leads here,
    /// because that IS the thing being reviewed.</summary>
    private static string SubmissionHeading(MentorAttentionItemDto i)
    {
        var owner = Owner(i);
        return owner is null ? i.Title : $"{i.Title} — {owner}";
    }

    /// <summary>Team first, project as the fallback — a mentor thinks in teams,
    /// and the project title is often the long formal one.</summary>
    private static string? Owner(MentorAttentionItemDto i) =>
        !string.IsNullOrWhiteSpace(i.TeamName)     ? i.TeamName
        : !string.IsNullOrWhiteSpace(i.ProjectTitle) ? i.ProjectTitle
        : null;

    /// <summary>"30.06.2026 ← 15.09.2026" — current on the reading start (the
    /// right, in RTL) and the requested date after the arrow. Null when the
    /// extension row carried no dates.</summary>
    private static string? DateShift(ExtensionContext? ext)
    {
        if (ext?.RequestedDueDate is not { } requested) return null;

        var to = requested.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        return ext.CurrentDueDate is { } current
            ? $"{current:dd.MM.yyyy} ← {to}"
            : $"מועד מבוקש: {to}";
    }

    // ── Plain text ───────────────────────────────────────────────────────────
    //
    // Also the in-app notification body, so it carries NO urls — the bell row
    // already navigates on click. Kept short for the same reason.

    private static string BuildPlainText(
        string headline,
        MentorAttentionCountsDto c,
        List<MentorAttentionItemDto> requests,
        List<MentorAttentionItemDto> submissions,
        IReadOnlyDictionary<int, ExtensionContext>? extensions)
    {
        var sb = new StringBuilder();
        sb.AppendLine(headline);

        if (c.AwaitingRequests > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"בקשות · {c.AwaitingRequests}");
            foreach (var r in requests)
            {
                sb.AppendLine($"• {RequestHeading(r)} — {r.WaitingLabel}");
                var ext = Lookup(extensions, r.EntityId);
                if (DateShift(ext) is { } shift) sb.AppendLine($"  {shift}");
                if (!string.IsNullOrWhiteSpace(ext?.TargetLabel)) sb.AppendLine($"  {ext!.TargetLabel}");
            }
        }

        if (c.PendingSubmissions > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"הגשות לבדיקה · {c.PendingSubmissions}");
            foreach (var s in submissions)
                sb.AppendLine($"• {SubmissionHeading(s)} — {s.WaitingLabel}");
        }

        if (c.PersonalTasksDueToday > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"משימות אישיות · {c.PersonalTasksDueToday}");
            sb.AppendLine(PersonalSummary(c));
        }

        return sb.ToString().TrimEnd();
    }

    private static ExtensionContext? Lookup(
        IReadOnlyDictionary<int, ExtensionContext>? map, int id) =>
        map is not null && map.TryGetValue(id, out var v) ? v : null;

    // ── HTML ─────────────────────────────────────────────────────────────────
    //
    // EMAIL-CLIENT RULES THIS FILE FOLLOWS, and why each matters:
    //   * Tables for layout. Outlook renders with Word, which has no flexbox
    //     and no grid.
    //   * Inline styles only. Gmail strips <style> blocks outright.
    //   * dir="rtl" AND align="right" AND text-align:right on every cell.
    //     Outlook does not reliably inherit direction, so it is restated.
    //   * Padding on <td>, never on <div>. Word ignores div padding.
    //   * Hairlines drawn as a <td> with border-top and zero font-size, not
    //     <hr>, which Outlook renders with its own margins.
    //   * A fixed 600px content table — the safe width across clients.
    //   * No background images, no gradients, no web fonts, no position.

    private static string BuildHtml(
        string headline,
        DateTime asOfLocalDate,
        MentorAttentionCountsDto c,
        List<MentorAttentionItemDto> requests,
        List<MentorAttentionItemDto> submissions,
        IReadOnlyDictionary<int, ExtensionContext>? extensions,
        string? baseUrl,
        string? logoCid)
    {
        string root = (baseUrl ?? "").TrimEnd('/');
        var sb = new StringBuilder();

        // Outer: full-bleed Paper. There is deliberately NO outer white card —
        // a card inside a card is what made the previous version read as a
        // document. Content sits on Paper and only the ITEMS lift onto white.
        sb.Append($@"<div dir=""rtl"" style=""margin:0;padding:0;background:{Paper};"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl"" style=""background:{Paper};"">
<tr><td align=""center"" style=""padding:32px 16px;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl"" style=""width:100%;max-width:600px;"">");

        // ── Masthead ─────────────────────────────────────────────────────────
        sb.Append($@"
<tr><td align=""right"" dir=""rtl"" style=""padding:0 4px 22px 4px;text-align:right;"">{Logo(logoCid)}</td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:0 4px;text-align:right;font-family:{Font};font-size:22px;font-weight:700;line-height:1.35;color:{Ink};"">{Enc(DigestTitle)}</td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:5px 4px 0 4px;text-align:right;font-family:{Font};font-size:13px;color:{InkMuted};"">{Enc(ShortHebrewDate(asOfLocalDate))}</td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:20px 4px 0 4px;text-align:right;font-family:{Font};font-size:15px;font-weight:600;color:{Ink};"">בוקר טוב,</td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:3px 4px 0 4px;text-align:right;font-family:{Font};font-size:14px;line-height:1.6;color:{InkSecond};"">ריכזנו עבורך את הדברים שממתינים לטיפולך.</td></tr>");

        // ── Summary block ────────────────────────────────────────────────────
        // Restrained on purpose: a white surface with ONE violet edge and a
        // large numeral. No gradient wash, no full-bleed hero — it states the
        // number and gets out of the way.
        sb.Append($@"
<tr><td style=""padding:20px 4px 0 4px;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl"" style=""width:100%;background:{Surface};border:1px solid {LineSoft};border-radius:{Radius};"">
<tr>
<td width=""4"" bgcolor=""{Violet}"" style=""width:4px;font-size:0;line-height:0;border-radius:0 {Radius} {Radius} 0;"">&nbsp;</td>
<td align=""right"" dir=""rtl"" style=""padding:16px 18px;text-align:right;font-family:{Font};"">
<div style=""font-size:13px;color:{InkMuted};"">יש לך היום</div>
<div style=""font-size:32px;font-weight:800;line-height:1.15;color:{Violet};padding:2px 0;"">{c.Total}</div>
<div style=""font-size:14px;font-weight:600;color:{Ink};"">פריטים שממתינים לטיפול</div>
</td>
</tr>
</table>
</td></tr>");

        // ── Section 1 — בקשות ────────────────────────────────────────────────
        if (c.AwaitingRequests > 0)
        {
            sb.Append(SectionHeading("בקשות", c.AwaitingRequests));
            foreach (var r in requests)
            {
                var ext = Lookup(extensions, r.EntityId);
                sb.Append(ItemCard(
                    title:     RequestTypeOnly(r),
                    owner:     Owner(r),
                    waiting:   r.WaitingLabel,
                    urgent:    r.Age == MentorAttentionAge.NeedsAttention,
                    metaLines: new[] { DateShift(ext), ext?.TargetLabel },
                    ctaText:   "פתיחת הבקשה",
                    // The exact request. MentorRequestsPage reads ?requestId=
                    // and expands that row — this deep link already works.
                    ctaHref:   r.Href,
                    root:      root));
            }
        }

        // ── Section 2 — הגשות לבדיקה ─────────────────────────────────────────
        if (c.PendingSubmissions > 0)
        {
            sb.Append(SectionHeading("הגשות לבדיקה", c.PendingSubmissions));
            foreach (var s in submissions)
            {
                sb.Append(ItemCard(
                    title:     s.Title,
                    owner:     Owner(s),
                    waiting:   s.WaitingLabel,
                    urgent:    s.Age == MentorAttentionAge.NeedsAttention,
                    metaLines: new[] { string.IsNullOrWhiteSpace(s.MilestoneTitle) ? null : $"אבן דרך: {s.MilestoneTitle}" },
                    ctaText:   "לבדיקת ההגשה",
                    // Best destination that exists today: the project workspace
                    // with its pending-submissions list. There is no
                    // per-submission route yet — see the report.
                    ctaHref:   s.Href,
                    root:      root));
            }
        }

        // ── Section 3 — משימות אישיות ────────────────────────────────────────
        // One quiet line, no card and no button: these are the mentor's own
        // private notes, and the email neither names nor acts on them.
        if (c.PersonalTasksDueToday > 0)
        {
            sb.Append(SectionHeading("משימות אישיות", c.PersonalTasksDueToday));
            sb.Append($@"
<tr><td align=""right"" dir=""rtl"" style=""padding:0 4px;text-align:right;font-family:{Font};font-size:14px;color:{InkSecond};"">{Enc(PersonalSummary(c))}</td></tr>");
        }

        // ── Secondary CTA ────────────────────────────────────────────────────
        // A text link, not a button. The item CTAs are the primary actions and
        // must stay louder than the catch-all.
        sb.Append($@"
<tr><td align=""right"" dir=""rtl"" style=""padding:26px 4px 0 4px;text-align:right;"">{TextLink("לכל הפריטים שממתינים לטיפול ←", "/mentor/tasks", root)}</td></tr>");

        // ── Brand footer ─────────────────────────────────────────────────────
        // The ONE rule in the whole email. Everything above is separated by
        // spacing; this line exists because the footer is genuinely a different
        // register, not another section.
        //
        // The descriptor is the approved product wording from LoginPage
        // ("המרחב החכם לניהול, מעקב ושיתוף פעולה בפרויקט הגמר."), not a new
        // slogan. Both sentences end in Hebrew on purpose: a sentence ending in
        // a Latin word plus a period renders the period on the wrong side under
        // bidi in Gmail and Outlook alike.
        sb.Append($@"
<tr><td style=""padding:30px 4px 0 4px;""><table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td style=""border-top:1px solid {LineEdge};font-size:0;line-height:0;"">&nbsp;</td></tr></table></td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:20px 4px 0 4px;text-align:right;"">{Logo(logoCid, small: true)}</td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:8px 4px 0 4px;text-align:right;font-family:{Font};font-size:13px;line-height:1.6;color:{InkSecond};"">המרחב החכם לניהול, מעקב ושיתוף פעולה בפרויקט הגמר.</td></tr>
<tr><td align=""right"" dir=""rtl"" style=""padding:14px 4px 8px 4px;text-align:right;font-family:{Font};font-size:12px;line-height:1.6;color:{InkMuted};"">סיכום זה נשלח בהתאם להעדפות ההתראות שלך.<br />ניתן לשנות אותן בעמוד ההגדרות, תחת העדפות התראות.</td></tr>
</table>
</td></tr>
</table>
</div>");

        return sb.ToString();
    }

    /// <summary>
    /// The real logo asset, referenced as an embedded (cid) image.
    ///
    /// <para>Emitted only when <paramref name="logoCid"/> is supplied. The
    /// sender passes it exactly when the asset actually resolved on disk, so a
    /// missing file yields NO img tag rather than a broken-image placeholder —
    /// which is the graceful degradation this needs. Everything else in the
    /// email is unaffected either way.</para>
    /// </summary>
    private static string Logo(string? logoCid, bool small = false)
    {
        if (string.IsNullOrWhiteSpace(logoCid)) return "";

        int w = small ? 104 : LogoW;
        int h = small ? 22  : LogoH;

        return $@"<img src=""cid:{Enc(logoCid!)}"" width=""{w}"" height=""{h}"" alt=""Motiva"" style=""display:block;width:{w}px;height:{h}px;border:0;outline:none;text-decoration:none;font-family:{Font};font-size:15px;font-weight:700;color:{Violet};"" />";
    }

    /// <summary>Request rows lead with the TYPE — "בקשת דחייה" tells a mentor
    /// more than a student-authored title does.</summary>
    private static string RequestTypeOnly(MentorAttentionItemDto i) =>
        string.IsNullOrWhiteSpace(i.RequestType) ? "בקשה" : RequestTypes.Label(i.RequestType!);

    /// <summary>
    /// Section heading: label at the reading start, real total at the far end.
    ///
    /// <para>The count is ALWAYS the true total even when fewer items are named
    /// below it — a capped list under a true count is a summary; a capped list
    /// under a capped count is a lie.</para>
    /// </summary>
    private static string SectionHeading(string label, int total) => $@"
<tr><td style=""padding:30px 4px 12px 4px;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl"" style=""width:100%;"">
<tr>
<td align=""right"" dir=""rtl"" style=""text-align:right;font-family:{Font};font-size:15px;font-weight:700;color:{Ink};"">{Enc(label)}</td>
<td align=""left"" dir=""rtl"" style=""text-align:left;font-family:{Font};font-size:14px;font-weight:700;color:{Violet};"">{total}</td>
</tr>
</table>
</td></tr>";

    /// <summary>
    /// One named item on a soft surface.
    ///
    /// <para>White on Paper with a 1px hairline and a 10px radius — a lift, not
    /// a card: no shadow, no thick border, no tint. The only colour that means
    /// anything is the waiting line, which turns rose ONLY at NEEDS ATTENTION,
    /// so a busy morning does not arrive as a wall of red.</para>
    /// </summary>
    private static string ItemCard(
        string title, string? owner, string waiting, bool urgent,
        string?[] metaLines, string ctaText, string ctaHref, string root)
    {
        var sb = new StringBuilder();

        sb.Append($@"
<tr><td style=""padding:0 4px 10px 4px;"">
<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" dir=""rtl"" style=""width:100%;background:{Surface};border:1px solid {LineSoft};border-radius:{Radius};"">
<tr><td align=""right"" dir=""rtl"" style=""padding:16px 18px;text-align:right;font-family:{Font};"">
<div style=""font-size:15px;font-weight:700;line-height:1.45;color:{Ink};"">{Enc(title)}</div>");

        if (!string.IsNullOrWhiteSpace(owner))
            sb.Append($@"
<div style=""font-size:13px;line-height:1.5;color:{InkMuted};padding-top:2px;"">{Enc(owner!)}</div>");

        sb.Append($@"
<div style=""font-size:13px;font-weight:{(urgent ? "700" : "400")};color:{(urgent ? RoseInk : InkSecond)};padding-top:10px;"">{Enc(waiting)}</div>");

        foreach (var line in metaLines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            sb.Append($@"
<div style=""font-size:13px;line-height:1.6;color:{InkSecond};padding-top:4px;"">{Enc(line!)}</div>");
        }

        sb.Append($@"
<div style=""padding-top:14px;"">{PrimaryButton(ctaText, ctaHref, root)}</div>
</td></tr>
</table>
</td></tr>");

        return sb.ToString();
    }

    // ── Buttons ──────────────────────────────────────────────────────────────
    //
    // Table-cell buttons, not padded <a> tags: Outlook ignores padding on an
    // inline anchor, which collapses a styled link into bare text.
    // mso-padding-alt gives Word the box it understands while every other
    // client uses the real padding.
    //
    // The fill mirrors the app's .motiva-cta: the signature sweep at
    // --g-radius-sm with white ink. bgcolor carries the solid violet for
    // Outlook, background-image adds the sweep everywhere it is supported, so
    // there is no client where the button loses its fill.

    private static string PrimaryButton(string text, string path, string root)
    {
        if (root.Length == 0)
            return $@"<span style=""font-family:{Font};font-size:13px;font-weight:700;color:{Violet};"">{Enc(text)}</span>";

        return $@"<table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" align=""right"" dir=""rtl"" style=""margin:0;""><tr>
<td bgcolor=""{Violet}"" style=""background-color:{Violet};background-image:{SignatureGradient};border-radius:{Radius};mso-padding-alt:11px 20px;"">
<a href=""{Enc(root + path)}"" style=""display:inline-block;padding:11px 20px;font-family:{Font};font-size:13px;font-weight:700;color:#FFFFFF;text-decoration:none;border-radius:{Radius};"">{Enc(text)}</a>
</td></tr></table>";
    }

    /// <summary>The catch-all link. Deliberately NOT a button — it must read as
    /// secondary to the per-item actions above it.</summary>
    private static string TextLink(string text, string path, string root)
    {
        if (root.Length == 0)
            return $@"<span style=""font-family:{Font};font-size:14px;font-weight:600;color:{Violet};"">{Enc(text)}</span>";

        return $@"<a href=""{Enc(root + path)}"" style=""font-family:{Font};font-size:14px;font-weight:600;color:{Violet};text-decoration:none;"">{Enc(text)}</a>";
    }

    /// <summary>"יום שלישי, 11 באוגוסט". Falls back to a numeric date if the
    /// host has no Hebrew culture data (globalization-invariant mode), so a
    /// misconfigured container degrades the date rather than throwing on every
    /// send.</summary>
    private static string ShortHebrewDate(DateTime date)
    {
        try
        {
            return date.ToString("dddd, d 'ב'MMMM", new CultureInfo("he-IL"));
        }
        catch (CultureNotFoundException)
        {
            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }
    }

    private static string PersonalSummary(MentorAttentionCountsDto c)
    {
        // "באיחור" is honest here and ONLY here: a personal task carries a real
        // due date the mentor set. A waiting submission or request has no
        // deadline and must never be described as late.
        var due = c.PersonalTasksDueToday - c.PersonalTasksOverdue;
        var parts = new List<string>();
        if (due > 0)                  parts.Add(due == 1 ? "אחת להיום" : $"{due} להיום");
        if (c.PersonalTasksOverdue > 0) parts.Add(c.PersonalTasksOverdue == 1 ? "אחת באיחור" : $"{c.PersonalTasksOverdue} באיחור");
        return string.Join(" · ", parts);
    }

    private static string Enc(string s) => WebUtility.HtmlEncode(s);
}
