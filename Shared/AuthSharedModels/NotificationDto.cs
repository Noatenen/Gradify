using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>One notification row for the current user's feed.</summary>
public class NotificationDto
{
    public int       Id                { get; set; }
    public string    Title             { get; set; } = "";
    public string    Message           { get; set; } = "";
    /// <summary>"SubmissionReceived" | "SubmissionReturned" | "General"</summary>
    public string    Type              { get; set; } = "General";
    public string?   RelatedEntityType { get; set; }
    public int?      RelatedEntityId   { get; set; }
    public bool      IsRead            { get; set; }
    public DateTime  CreatedAt         { get; set; }
    public DateTime? ReadAt            { get; set; }
}
