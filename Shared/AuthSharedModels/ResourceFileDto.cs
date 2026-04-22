namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>Discriminator constants for ResourceFiles.ItemType.</summary>
public static class ResourceItemType
{
    public const string File  = "File";
    public const string Video = "Video";
}

/// <summary>One resource item (uploaded file or YouTube video link) returned by the API.</summary>
public class ResourceFileDto
{
    public int      Id             { get; set; }
    /// <summary>"File" or "Video" — determines how the item is stored and rendered.</summary>
    public string   ItemType       { get; set; } = ResourceItemType.File;
    public string   FileName       { get; set; } = "";
    /// <summary>GUID-based stored filename inside wwwroot/resources/. Empty for Video items.</summary>
    public string   StoredFileName { get; set; } = "";
    public string   ContentType    { get; set; } = "";
    public DateTime UploadedAt     { get; set; }
    public string?  Description    { get; set; }
    /// <summary>0 means "no milestone" (general). Maps to MilestoneTemplates.Id otherwise.</summary>
    public int      MilestoneId    { get; set; }
    public string?  MilestoneName  { get; set; }
    public int?     TaskId         { get; set; }
    public string?  TaskName       { get; set; }
    /// <summary>True when the item applies to Technological projects.</summary>
    public bool     ForTechnological  { get; set; }
    /// <summary>True when the item applies to Methodological projects.</summary>
    public bool     ForMethodological { get; set; }
    /// <summary>YouTube watch or embed URL. Populated only when ItemType = "Video".</summary>
    public string?  VideoUrl       { get; set; }
}

/// <summary>Request payload sent from the client when creating a new resource item.</summary>
public class UploadResourceFileRequest
{
    /// <summary>"File" or "Video".</summary>
    public string  ItemType          { get; set; } = ResourceItemType.File;
    public string  FileName          { get; set; } = "";
    /// <summary>Base64-encoded file content. Required when ItemType = "File".</summary>
    public string  FileBase64        { get; set; } = "";
    public string  ContentType       { get; set; } = "";
    /// <summary>0 = no milestone (general section).</summary>
    public int     MilestoneId       { get; set; }
    public int?    TaskId            { get; set; }
    public string? Description       { get; set; }
    public bool    ForTechnological  { get; set; }
    public bool    ForMethodological { get; set; }
    /// <summary>YouTube URL. Required when ItemType = "Video".</summary>
    public string? VideoUrl          { get; set; }
}

/// <summary>Full update request — metadata and optional file/video replacement.</summary>
public class UpdateResourceFileRequest
{
    /// <summary>"File" or "Video".</summary>
    public string  ItemType          { get; set; } = ResourceItemType.File;
    public string  FileName          { get; set; } = "";
    public string? Description       { get; set; }
    public int     MilestoneId       { get; set; }
    public int?    TaskId            { get; set; }
    public bool    ForTechnological  { get; set; }
    public bool    ForMethodological { get; set; }
    /// <summary>Base64-encoded replacement file. Null = keep existing file. Only for File items.</summary>
    public string? FileBase64        { get; set; }
    public string? ContentType       { get; set; }
    /// <summary>Updated YouTube URL. Only for Video items.</summary>
    public string? VideoUrl          { get; set; }
}

/// <summary>Slim milestone option used to populate the upload-form dropdown.</summary>
public class MilestoneOptionDto
{
    public int    Id    { get; set; }
    public string Title { get; set; } = "";
}

/// <summary>Slim task option used to populate the upload-form task dropdown.</summary>
public class TaskOptionDto
{
    public int    Id          { get; set; }
    public string Title       { get; set; } = "";
    public int    MilestoneId { get; set; }
}
