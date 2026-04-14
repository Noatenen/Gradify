namespace AuthWithAdmin.Shared.AuthSharedModels;

/// <summary>One uploaded resource file — full display record returned by the API.</summary>
public class ResourceFileDto
{
    public int      Id             { get; set; }
    public string   FileName       { get; set; } = "";
    /// <summary>GUID-based stored filename inside wwwroot/resources/. Used to build the download URL.</summary>
    public string   StoredFileName { get; set; } = "";
    public string   ContentType    { get; set; } = "";
    public DateTime UploadedAt     { get; set; }
    public string?  Description    { get; set; }
    public int      MilestoneId    { get; set; }
    public string   MilestoneName  { get; set; } = "";
    public int?     TaskId         { get; set; }
    public string?  TaskName       { get; set; }
}

/// <summary>Request payload sent from the client when uploading a new resource file.</summary>
public class UploadResourceFileRequest
{
    public string  FileName    { get; set; } = "";
    /// <summary>Base64-encoded file content.</summary>
    public string  FileBase64  { get; set; } = "";
    public string  ContentType { get; set; } = "";
    public int     MilestoneId { get; set; }
    public int?    TaskId      { get; set; }
    public string? Description { get; set; }
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
