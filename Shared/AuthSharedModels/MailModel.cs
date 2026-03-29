namespace AuthWithAdmin.Shared.AuthSharedModels;

public class MailModel
{
    public List<string> Recipients {  get; set; } = new List<string>();
    public string Subject { get; set; }
    public string Body { get; set; }
}