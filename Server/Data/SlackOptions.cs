namespace AuthWithAdmin.Server.Data;

public class SlackOptions
{
    public const string SectionName = "Slack";

    public string ClientId     { get; set; } = "";
    public string ClientSecret { get; set; } = "";
    /// <summary>
    /// Must match exactly what is registered in the Slack app's OAuth redirect URLs.
    /// Example: https://localhost:5001/api/slack/callback
    /// </summary>
    public string RedirectUri  { get; set; } = "";
}
