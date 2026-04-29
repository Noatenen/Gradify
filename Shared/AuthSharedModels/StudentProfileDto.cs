using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

public class StudentProfileDto
{
    public int     Id              { get; set; }
    public string  FirstName       { get; set; } = "";
    public string  LastName        { get; set; } = "";
    public string  Email           { get; set; } = "";
    public string  Phone           { get; set; } = "";
    public string  AcademicYear    { get; set; } = "";
    public string  IdNumber        { get; set; } = "";
    public string? ProfileImageUrl { get; set; }       // null = no image, show initials

    // Linked project/team (nullable — student may not be assigned yet)
    public string? ProjectTitle  { get; set; }
    public int?    ProjectNumber { get; set; }
    public string? TeamName      { get; set; }

    public StudentPreferencesDto Preferences     { get; set; } = new();
    public SlackConnectionDto    SlackConnection { get; set; } = new();
}

public class StudentPreferencesDto
{
    public bool   NotifyOnTasks           { get; set; } = true;
    public bool   NotifyOnDeadlines       { get; set; } = true;
    public bool   NotifyOnFeedback        { get; set; } = true;
    public bool   NotifyOnSubmissions     { get; set; } = true;
    public bool   NotifyOnMentorUpdates   { get; set; } = true;
    public bool   GoogleCalendarConnected { get; set; } = false;
    public string ThemePreference         { get; set; } = "system"; // "light" | "dark" | "system"
}

public class SlackConnectionDto
{
    public bool   IsConnected { get; set; }
    public string TeamName    { get; set; } = "";
    public string ConnectedAt { get; set; } = "";
}

public class UpdateStudentProfileRequest
{
    public string Phone { get; set; } = "";
}

public class UploadAvatarRequest
{
    public string ImageBase64 { get; set; } = "";
    public string Extension   { get; set; } = "";
}
