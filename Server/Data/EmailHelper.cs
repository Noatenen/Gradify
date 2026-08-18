namespace AuthWithAdmin.Server.Data;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using Shared.AuthSharedModels;

//לא לגעת - ניהול טוקנים
//
// ON THAT MARKER, because it matters before anyone edits this file:
// the same "//לא לגעת - ניהול טוקנים" line sits on AuthCheck.cs,
// TokenService.cs and FilesManage.cs, all from the initial scaffolding commit.
// EmailHelper does no token work at all — it holds SMTP credentials, loads a
// template and sends. The marker is a blanket "this is original auth-adjacent
// infrastructure" note, and the real risk it guards is REAL: password reset and
// email verification (UsersController) both send through this method, so a
// regression here breaks sign-in recovery for every role.
//
// That argues for strict backward compatibility, not for never touching it.
// The inline-image support below is therefore additive only: when a caller
// supplies no InlineImages — which is every existing caller — the message is
// built exactly as before.

public class EmailHelper
{
    private readonly string username;
    private readonly string password;
    private readonly IWebHostEnvironment _env;

    /// <summary>Decides which recipients may actually be relayed. See
    /// EmailDeliveryPolicy — this is the single choke point every sender in the
    /// application already passes through, so the rule lives here once rather
    /// than being repeated by the digest, the dispatcher and the auth flows.</summary>
    private readonly EmailDeliveryPolicy _policy;


    public EmailHelper(IConfiguration config, IWebHostEnvironment env)
    {
        username = config.GetValue<string>("Email:UserName");
        password = config.GetValue<string>("Email:Password");
        _env = env;
        _policy = new EmailDeliveryPolicy(config, env);
    }

    public async Task<string> GetEmailTemplateAsync(string templateName, Dictionary<string, string> placeholders)
    {
        // Get the full path to the template file
        string templatePath = Path.Combine(_env.WebRootPath, "Emails", templateName + ".html");
        Console.WriteLine(templatePath);

        if (!File.Exists(templatePath))
            return null;

        // Read the file content
        string emailBody = await File.ReadAllTextAsync(templatePath);

        // Replace placeholders with actual values
        foreach (var placeholder in placeholders)
        {
            emailBody = emailBody.Replace($"{{{placeholder.Key}}}", placeholder.Value);
        }

        return emailBody;
    }


    public async Task <bool> SendEmail(MailModel mail)
    {
        // ── Deliverability gate ──────────────────────────────────────────────
        // Applied BEFORE the relay, because SMTP cannot report this failure
        // afterwards: Gmail accepts a message for an impossible domain, this
        // method returns true, and the rejection arrives minutes later as a
        // bounce to the sending mailbox. Nothing downstream can detect that,
        // so the only correct place to stop it is here.
        var decision = _policy.Resolve(mail.Recipients);

        if (decision.Skipped.Count > 0)
        {
            Console.WriteLine(decision.Redirected
                ? $"Email redirected (Development): intended for {string.Join(", ", decision.Skipped)} → {string.Join(", ", decision.Recipients)}"
                : $"Email skipped: {string.Join(", ", decision.Skipped)} — non-deliverable placeholder domain (Email:NonDeliverableDomains)");
        }

        if (decision.Recipients.Count == 0)
        {
            // Not an error — there was simply nobody real to send to. Callers
            // treat false as "not sent", which is exactly what happened; the
            // in-app notification they wrote first is unaffected.
            return false;
        }

        using var smtpClient = new SmtpClient("smtp.gmail.com")
        {
            Port = 587, // or 465 for SSL
            Credentials = new NetworkCredential(username, password),
            EnableSsl = true, // Enabling SSL
        };

        using var mailMessage = new MailMessage
        {
            From = new MailAddress(username),
            Subject = mail.Subject,
            Body = mail.Body,
            IsBodyHtml = true, // true if you are sending HTML content
        };

        foreach (string recipient in decision.Recipients) {
            mailMessage.To.Add(recipient);
        }

        // ── Optional inline images ───────────────────────────────────────────
        // Only runs when a caller asked for it. With no InlineImages the
        // message above is already complete and is sent exactly as it always
        // has been — same Body, same IsBodyHtml, same single-part structure.
        //
        // With images, the body is re-expressed as an AlternateView so the
        // LinkedResources can hang off it; .NET then emits multipart/related
        // with the HTML as the root part, which is what a mail client needs in
        // order to resolve a cid: reference.
        //
        // A missing file is skipped rather than fatal, and if NOTHING resolves
        // the plain Body already on the message is what goes out. A digest is
        // worth more than its logo.
        AttachInlineImages(mailMessage, mail.InlineImages);

        try
        {
            await smtpClient.SendMailAsync(mailMessage);
            Console.WriteLine("Email sent successfully!");
            return true;

        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to send email: {ex.Message}");
            return false;
        }
        // mailMessage.Dispose() (via `using`) cascades to its AlternateViews,
        // their LinkedResources and the file streams those hold, so no asset
        // stays open after the send. The streams below are never disposed by
        // hand for that reason — doing so would close them before SmtpClient
        // reads them.
    }

    /// <summary>
    /// Converts <see cref="MailInlineImage"/> entries into a multipart/related
    /// body. No-op when the list is empty, which is the case for every caller
    /// that existed before this method.
    /// </summary>
    private void AttachInlineImages(MailMessage message, List<MailInlineImage>? images)
    {
        if (images is null || images.Count == 0) return;

        var resources = new List<LinkedResource>();

        foreach (var img in images)
        {
            if (string.IsNullOrWhiteSpace(img.WebPath) || string.IsNullOrWhiteSpace(img.ContentId))
                continue;

            // Resolved through WebRootFileProvider, not Path.Combine on
            // WebRootPath: this asset lives in the Blazor CLIENT's wwwroot and
            // reaches the server through the static-web-assets manifest, so it
            // is not physically under Server/wwwroot. The file provider is the
            // same one UseStaticFiles serves from, so if the browser can fetch
            // the image, this finds it.
            var file = _env.WebRootFileProvider.GetFileInfo(img.WebPath);

            if (!file.Exists || file.IsDirectory)
            {
                Console.WriteLine($"Email inline image skipped: '{img.WebPath}' not found in the web root.");
                continue;
            }

            try
            {
                var resource = new LinkedResource(file.CreateReadStream(), new ContentType(img.ContentType))
                {
                    ContentId        = img.ContentId,
                    TransferEncoding = System.Net.Mime.TransferEncoding.Base64,
                };
                resources.Add(resource);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Email inline image skipped: '{img.WebPath}' — {ex.Message}");
            }
        }

        // Nothing resolved → leave the plain HTML body untouched. The message
        // still sends; only the embedded art is missing.
        if (resources.Count == 0) return;

        // Encoding.UTF8 is passed EXPLICITLY and must stay that way. With a null
        // encoding CreateAlternateViewFromString falls back to us-ascii, which
        // silently mangles every Hebrew character in the body — verified by
        // dumping the generated MIME, where the inline-image variant came out
        // "text/html; charset=us-ascii" while the plain path was correctly
        // utf-8. MailMessage.Body auto-detects; an AlternateView does not.
        var view = AlternateView.CreateAlternateViewFromString(
            message.Body, System.Text.Encoding.UTF8, MediaTypeNames.Text.Html);

        foreach (var r in resources) view.LinkedResources.Add(r);

        message.AlternateViews.Add(view);
    }

}