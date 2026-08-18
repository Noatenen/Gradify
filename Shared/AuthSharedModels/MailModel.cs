using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class MailModel
{
    public List<string> Recipients {  get; set; } = new List<string>();
    public string Subject { get; set; }
    public string Body { get; set; }

    /// <summary>
    /// Images to embed IN the message rather than link to, referenced from the
    /// HTML body as <c>src="cid:{ContentId}"</c>.
    ///
    /// <para><b>Optional, and empty by default.</b> Every existing caller
    /// constructs a MailModel with only Subject/Body/Recipients, so they all get
    /// an empty list and EmailHelper takes exactly the code path it took before
    /// this property existed. Nothing about password-reset, verification,
    /// approval or notification mail changes.</para>
    ///
    /// <para>Why embedding rather than a URL: a remote <c>&lt;img&gt;</c> renders
    /// only if the host is publicly reachable AND the recipient has chosen to
    /// load remote images, which most clients block by default. An embedded
    /// image has neither dependency.</para>
    /// </summary>
    public List<MailInlineImage> InlineImages { get; set; } = new List<MailInlineImage>();
}

/// <summary>
/// One embedded image.
///
/// <para><b>Deliberately plain strings.</b> This type lives in Shared, which is
/// compiled for the browser platform for the Blazor client, so it must not
/// reference <c>System.Net.Mail</c>. The translation to a LinkedResource happens
/// server-side, in EmailHelper.</para>
/// </summary>
public class MailInlineImage
{
    /// <summary>Path relative to the web root, e.g. "images/motiva-logo.png".
    /// Resolved through the host's web-root file provider, so an asset served
    /// from the Blazor client's wwwroot is found the same way the browser finds
    /// it.</summary>
    public string WebPath { get; set; } = "";

    /// <summary>The token the HTML references. Must match the <c>src="cid:…"</c>
    /// value exactly, with no angle brackets.</summary>
    public string ContentId { get; set; } = "";

    public string ContentType { get; set; } = "image/png";
}