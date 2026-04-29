using System;
using System.Collections.Generic;
namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>Discriminator constants for LearningMaterials.MaterialType.</summary>
public static class LearningMaterialType
{
    public const string Video = "Video";
    public const string File  = "File";
    public const string Link  = "Link";
}

/// <summary>
/// A single learning material item returned to the student.
/// Internal targeting fields (ProjectId / ProjectType) are included so the
/// client can debug or display context if needed, but are not required for rendering.
/// </summary>
public class LearningMaterialDto
{
    public int      Id           { get; set; }
    public string   Title        { get; set; } = "";
    public string?  Description  { get; set; }

    /// <summary>"Video" | "File" | "Link"</summary>
    public string   MaterialType { get; set; } = LearningMaterialType.Link;

    /// <summary>
    /// For Video: YouTube watch/embed URL.
    /// For File:  relative server path or absolute URL to the file.
    /// For Link:  the external URL to open.
    /// </summary>
    public string?  Url          { get; set; }

    /// <summary>Display name for File items (original filename shown to user).</summary>
    public string?  FileName     { get; set; }

    /// <summary>Null = applies globally or by project type only.</summary>
    public int?     ProjectId    { get; set; }

    /// <summary>Null = applies globally or by specific project only.</summary>
    public string?  ProjectType  { get; set; }

    public DateTime CreatedAt    { get; set; }
}
