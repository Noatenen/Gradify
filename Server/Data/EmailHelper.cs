namespace AuthWithAdmin.Server.Data;
using System.Net;
using System.Net.Mail;
using Shared.AuthSharedModels;

//לא לגעת - ניהול טוקנים

public class EmailHelper
{
    private readonly string username;
    private readonly string password;
    private readonly IWebHostEnvironment _env;


    public EmailHelper(IConfiguration config, IWebHostEnvironment env)
    {
        username = config.GetValue<string>("Email:UserName");
        password = config.GetValue<string>("Email:Password");
        _env = env;
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
        var smtpClient = new SmtpClient("smtp.gmail.com")
        {
            Port = 587, // or 465 for SSL
            Credentials = new NetworkCredential(username, password),
            EnableSsl = true, // Enabling SSL
        };

        var mailMessage = new MailMessage
        {
            From = new MailAddress(username),
            Subject = mail.Subject,
            Body = mail.Body,
            IsBodyHtml = true, // true if you are sending HTML content
        };

        foreach (string recipient in mail.Recipients) { 
            mailMessage.To.Add(recipient);
        }


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

    }

}