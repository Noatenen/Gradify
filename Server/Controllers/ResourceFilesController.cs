using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

[Route("api/[controller]")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class ResourceFilesController : ControllerBase
{
    private readonly DbRepository       _db;
    private readonly FilesManage        _filesManage;
    private readonly IWebHostEnvironment _env;

    private const string Container = "resources";

    public ResourceFilesController(DbRepository db, FilesManage filesManage, IWebHostEnvironment env)
    {
        _db          = db;
        _filesManage = filesManage;
        _env         = env;
    }

    // ── GET /api/resourcefiles ───────────────────────────────────────────────
    // Returns all uploaded files with milestone and task names resolved.
    [HttpGet]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        const string sql = @"
            SELECT  rf.Id,
                    rf.FileName,
                    rf.StoredFileName,
                    rf.ContentType,
                    rf.UploadedAt,
                    rf.Description,
                    rf.MilestoneId,
                    mt.Title || ' — פרויקט ' || p.ProjectNumber AS MilestoneName,
                    rf.TaskId,
                    t.Title AS TaskName
            FROM    ResourceFiles           rf
            JOIN    ProjectMilestones       pm  ON rf.MilestoneId = pm.Id
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            JOIN    Projects                p   ON pm.ProjectId = p.Id
            LEFT JOIN Tasks                 t   ON rf.TaskId = t.Id
            ORDER   BY rf.UploadedAt DESC";

        var rows = await _db.GetRecordsAsync<ResourceFileRow>(sql);

        var result = rows?.Select(r => new ResourceFileDto
        {
            Id            = r.Id,
            FileName      = r.FileName,
            StoredFileName = r.StoredFileName,
            ContentType   = r.ContentType,
            UploadedAt    = r.UploadedAt,
            Description   = r.Description,
            MilestoneId   = r.MilestoneId,
            MilestoneName = r.MilestoneName,
            TaskId        = r.TaskId,
            TaskName      = r.TaskName,
        }).ToList() ?? new List<ResourceFileDto>();

        return Ok(result);
    }

    // ── GET /api/resourcefiles/milestones ────────────────────────────────────
    // Returns all project milestones across all projects for the upload form dropdown.
    [HttpGet("milestones")]
    public async Task<IActionResult> GetMilestones(int authUserId)
    {
        const string sql = @"
            SELECT  pm.Id,
                    mt.Title || ' — פרויקט ' || p.ProjectNumber AS Title
            FROM    ProjectMilestones       pm
            JOIN    AcademicYearMilestones  aym ON pm.AcademicYearMilestoneId = aym.Id
            JOIN    MilestoneTemplates      mt  ON aym.MilestoneTemplateId    = mt.Id
            JOIN    Projects                p   ON pm.ProjectId = p.Id
            ORDER   BY p.ProjectNumber, mt.OrderIndex";

        var milestones = await _db.GetRecordsAsync<MilestoneOptionDto>(sql);
        return Ok(milestones ?? Enumerable.Empty<MilestoneOptionDto>());
    }

    // ── GET /api/resourcefiles/tasks/{milestoneId} ───────────────────────────
    // Returns tasks belonging to the given project milestone (for the optional task dropdown).
    [HttpGet("tasks/{milestoneId:int}")]
    public async Task<IActionResult> GetTasksForMilestone(int milestoneId, int authUserId)
    {
        const string sql = @"
            SELECT  t.Id,
                    t.Title,
                    t.ProjectMilestoneId AS MilestoneId
            FROM    Tasks t
            WHERE   t.ProjectMilestoneId = @MilestoneId
            ORDER   BY t.Title";

        var tasks = await _db.GetRecordsAsync<TaskOptionDto>(sql, new { MilestoneId = milestoneId });
        return Ok(tasks ?? Enumerable.Empty<TaskOptionDto>());
    }

    // ── POST /api/resourcefiles ──────────────────────────────────────────────
    // Saves the file to wwwroot/resources/ and stores metadata in DB.
    [HttpPost]
    public async Task<IActionResult> Upload([FromBody] UploadResourceFileRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.FileName) || string.IsNullOrWhiteSpace(req.FileBase64))
            return BadRequest("חסרים שם קובץ או תוכן");

        if (req.MilestoneId <= 0)
            return BadRequest("יש לבחור אבן דרך");

        // Verify milestone exists
        const string msSql = "SELECT COUNT(1) FROM ProjectMilestones WHERE Id = @Id";
        var msCount = (await _db.GetRecordsAsync<int>(msSql, new { Id = req.MilestoneId })).FirstOrDefault();
        if (msCount == 0)
            return BadRequest("אבן הדרך לא נמצאה");

        string storedFileName;
        try
        {
            storedFileName = await _filesManage.SaveRawFile(req.FileBase64, req.FileName, Container);
        }
        catch
        {
            return StatusCode(500, "שגיאה בשמירת הקובץ");
        }

        const string insertSql = @"
            INSERT INTO ResourceFiles
                (FileName, StoredFileName, ContentType, UploadedAt, UploadedByUserId, Description, MilestoneId, TaskId)
            VALUES
                (@FileName, @StoredFileName, @ContentType, datetime('now'), @UploadedByUserId, @Description, @MilestoneId, @TaskId)";

        int newId = await _db.InsertReturnIdAsync(insertSql, new
        {
            FileName         = req.FileName,
            StoredFileName   = storedFileName,
            ContentType      = string.IsNullOrWhiteSpace(req.ContentType) ? "application/octet-stream" : req.ContentType,
            UploadedByUserId = authUserId,
            Description      = req.Description,
            MilestoneId      = req.MilestoneId,
            TaskId           = req.TaskId,
        });

        if (newId == 0)
            return StatusCode(500, "שגיאה בשמירת נתוני הקובץ");

        return Ok(new { id = newId });
    }

    // ── DELETE /api/resourcefiles/{id} ───────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, int authUserId)
    {
        const string selectSql = "SELECT StoredFileName FROM ResourceFiles WHERE Id = @Id";
        var rows = await _db.GetRecordsAsync<StoredFileRow>(selectSql, new { Id = id });
        var row = rows?.FirstOrDefault();

        if (row is null)
            return NotFound("הקובץ לא נמצא");

        _filesManage.DeleteFile(row.StoredFileName, Container);

        const string deleteSql = "DELETE FROM ResourceFiles WHERE Id = @Id";
        await _db.SaveDataAsync(deleteSql, new { Id = id });

        return Ok();
    }

    // ── Private Dapper row classes ───────────────────────────────────────────

    private sealed class ResourceFileRow
    {
        public int      Id             { get; set; }
        public string   FileName       { get; set; } = "";
        public string   StoredFileName { get; set; } = "";
        public string   ContentType    { get; set; } = "";
        public DateTime UploadedAt     { get; set; }
        public string?  Description    { get; set; }
        public int      MilestoneId    { get; set; }
        public string   MilestoneName  { get; set; } = "";
        public int?     TaskId         { get; set; }
        public string?  TaskName       { get; set; }
    }

    private sealed class StoredFileRow
    {
        public string StoredFileName { get; set; } = "";
    }
}
