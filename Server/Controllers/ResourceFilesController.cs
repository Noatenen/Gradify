using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

[Route("api/resourcefiles")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class ResourceFilesController : ControllerBase
{
    private readonly DbRepository        _db;
    private readonly FilesManage         _filesManage;
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<ResourceFilesController> _log;

    private const string Container = "resources";

    public ResourceFilesController(
        DbRepository db,
        FilesManage filesManage,
        IWebHostEnvironment env,
        ILogger<ResourceFilesController> log)
    {
        _db          = db;
        _filesManage = filesManage;
        _env         = env;
        _log         = log;
    }

    // ── GET /api/resourcefiles ───────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> GetAll(int authUserId)
    {
        try
        {
            var rows = await FetchAllRows();
            return Ok(MapRows(rows));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "GetAll failed");
            return StatusCode(500, DbError("שגיאה בטעינת הקבצים", ex));
        }
    }

    // ── GET /api/resourcefiles/milestones ────────────────────────────────────
    [HttpGet("milestones")]
    public async Task<IActionResult> GetMilestones(int authUserId)
    {
        const string sql = @"
            SELECT  Id, Title
            FROM    MilestoneTemplates
            WHERE   IsActive = 1
            ORDER   BY OrderIndex";

        var milestones = await _db.GetRecordsAsync<MilestoneOptionDto>(sql);
        return Ok(milestones ?? Enumerable.Empty<MilestoneOptionDto>());
    }

    // ── GET /api/resourcefiles/tasks/{milestoneTemplateId} ───────────────────
    [HttpGet("tasks/{milestoneId:int}")]
    public async Task<IActionResult> GetTasksForMilestone(int milestoneId, int authUserId)
    {
        const string sql = @"
            SELECT  Id, Title, MilestoneTemplateId AS MilestoneId
            FROM    TaskTemplates
            WHERE   MilestoneTemplateId = @MilestoneId AND IsActive = 1
            ORDER   BY Title";

        var tasks = await _db.GetRecordsAsync<TaskOptionDto>(sql, new { MilestoneId = milestoneId });
        return Ok(tasks ?? Enumerable.Empty<TaskOptionDto>());
    }

    // ── POST /api/resourcefiles ──────────────────────────────────────────────
    [HttpPost]
    public async Task<IActionResult> Upload([FromBody] UploadResourceFileRequest req, int authUserId)
    {
        // ── Validate ─────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(req.FileName))
            return BadRequest("חסר שם תצוגה");

        var isVideo = string.Equals(req.ItemType, ResourceItemType.Video, StringComparison.OrdinalIgnoreCase);
        var isFile  = !isVideo;

        if (isFile && string.IsNullOrWhiteSpace(req.FileBase64))
            return BadRequest("חסר תוכן קובץ");

        if (isVideo && string.IsNullOrWhiteSpace(req.VideoUrl))
            return BadRequest("חסר קישור וידאו");

        // ── Optional milestone validation ─────────────────────────────────────
        if (req.MilestoneId > 0)
        {
            try
            {
                var msRows  = await _db.GetRecordsAsync<int>(
                    "SELECT COUNT(1) FROM MilestoneTemplates WHERE Id = @Id AND IsActive = 1",
                    new { Id = req.MilestoneId });
                int msCount = msRows?.FirstOrDefault() ?? 0;
                if (msCount == 0) return BadRequest("אבן הדרך לא נמצאה");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Milestone validation query failed (MilestoneId={Id})", req.MilestoneId);
                return StatusCode(500, DbError("שגיאה בבדיקת אבן הדרך", ex));
            }
        }

        // ── Physical file save (File items only) ──────────────────────────────
        string storedFileName = "";
        string contentType    = "";

        if (isFile)
        {
            try
            {
                storedFileName = await _filesManage.SaveRawFile(req.FileBase64, req.FileName, Container);
                contentType    = string.IsNullOrWhiteSpace(req.ContentType)
                    ? "application/octet-stream"
                    : req.ContentType;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SaveRawFile failed for '{FileName}'", req.FileName);
                return StatusCode(500, DbError("שגיאה בשמירת הקובץ הפיזי", ex));
            }
        }

        // ── DB insert ─────────────────────────────────────────────────────────
        try
        {
            int newId = await _db.InsertReturnIdAsync(@"
                INSERT INTO ResourceFiles
                    (ItemType, FileName, StoredFileName, ContentType, UploadedAt, UploadedByUserId,
                     Description, MilestoneId, TaskId, ForTechnological, ForMethodological, VideoUrl)
                VALUES
                    (@ItemType, @FileName, @StoredFileName, @ContentType, datetime('now'), @UploadedByUserId,
                     @Description, @MilestoneId, @TaskId, @ForTechnological, @ForMethodological, @VideoUrl)",
                new
                {
                    ItemType          = isVideo ? ResourceItemType.Video : ResourceItemType.File,
                    FileName          = req.FileName.Trim(),
                    StoredFileName    = storedFileName,       // "" for video items
                    ContentType       = contentType,          // "" for video items
                    UploadedByUserId  = authUserId,
                    Description       = req.Description,
                    MilestoneId       = req.MilestoneId,
                    TaskId            = req.TaskId,           // int? — Dapper maps null correctly
                    ForTechnological  = req.ForTechnological  ? 1 : 0,
                    ForMethodological = req.ForMethodological ? 1 : 0,
                    VideoUrl          = isVideo ? req.VideoUrl!.Trim() : (string?)null,
                });

            if (newId == 0)
            {
                _log.LogError("InsertReturnIdAsync returned 0 rows for resource item '{FileName}'", req.FileName);
                return StatusCode(500, "שגיאה בשמירת הנתונים — לא הוחזר מזהה");
            }

            return Ok(new { id = newId });
        }
        catch (Exception ex)
        {
            _log.LogError(ex,
                "DB insert failed for resource item (ItemType={ItemType}, FileName='{FileName}')",
                req.ItemType, req.FileName);

            // Delete the physical file we may have already written so we don't leave orphans
            if (isFile && !string.IsNullOrEmpty(storedFileName))
            {
                try { _filesManage.DeleteFile(storedFileName, Container); } catch { /* best-effort */ }
            }

            return StatusCode(500, DbError("שגיאה בשמירת הנתונים במסד", ex));
        }
    }

    // ── PUT /api/resourcefiles/{id} ──────────────────────────────────────────
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateResourceFileRequest req, int authUserId)
    {
        if (string.IsNullOrWhiteSpace(req.FileName))
            return BadRequest("שם התצוגה לא יכול להיות ריק");

        var isVideo = string.Equals(req.ItemType, ResourceItemType.Video, StringComparison.OrdinalIgnoreCase);

        if (isVideo && string.IsNullOrWhiteSpace(req.VideoUrl))
            return BadRequest("חסר קישור וידאו");

        if (req.MilestoneId > 0)
        {
            try
            {
                var msRows  = await _db.GetRecordsAsync<int>(
                    "SELECT COUNT(1) FROM MilestoneTemplates WHERE Id = @Id AND IsActive = 1",
                    new { Id = req.MilestoneId });
                int msCount = msRows?.FirstOrDefault() ?? 0;
                if (msCount == 0) return BadRequest("אבן הדרך לא נמצאה");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Milestone validation query failed (MilestoneId={Id})", req.MilestoneId);
                return StatusCode(500, DbError("שגיאה בבדיקת אבן הדרך", ex));
            }
        }

        ExistingFileRow? existing;
        try
        {
            var rows = await _db.GetRecordsAsync<ExistingFileRow>(
                "SELECT StoredFileName, ContentType FROM ResourceFiles WHERE Id = @Id",
                new { Id = id });
            existing = rows?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fetch existing record failed (Id={Id})", id);
            return StatusCode(500, DbError("שגיאה בשליפת הפריט הקיים", ex));
        }

        if (existing is null) return NotFound("הפריט לא נמצא");

        string storedFileName = existing.StoredFileName;
        string contentType    = existing.ContentType;

        // Replace physical file only when updating a File item with new content
        if (!isVideo && !string.IsNullOrWhiteSpace(req.FileBase64))
        {
            try
            {
                if (!string.IsNullOrEmpty(existing.StoredFileName))
                    _filesManage.DeleteFile(existing.StoredFileName, Container);
                storedFileName = await _filesManage.SaveRawFile(req.FileBase64, req.FileName, Container);
                contentType    = string.IsNullOrWhiteSpace(req.ContentType)
                    ? "application/octet-stream"
                    : req.ContentType;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "SaveRawFile (replace) failed for '{FileName}'", req.FileName);
                return StatusCode(500, DbError("שגיאה בשמירת הקובץ החדש", ex));
            }
        }

        // When switching an existing File item to Video, remove the orphaned physical file
        if (isVideo && !string.IsNullOrEmpty(existing.StoredFileName))
        {
            try { _filesManage.DeleteFile(existing.StoredFileName, Container); } catch { /* best-effort */ }
            storedFileName = "";
            contentType    = "";
        }

        try
        {
            await _db.SaveDataAsync(@"
                UPDATE ResourceFiles
                SET ItemType          = @ItemType,
                    FileName          = @FileName,
                    StoredFileName    = @StoredFileName,
                    ContentType       = @ContentType,
                    Description       = @Description,
                    MilestoneId       = @MilestoneId,
                    TaskId            = @TaskId,
                    ForTechnological  = @ForTechnological,
                    ForMethodological = @ForMethodological,
                    VideoUrl          = @VideoUrl
                WHERE Id = @Id",
                new
                {
                    ItemType          = isVideo ? ResourceItemType.Video : ResourceItemType.File,
                    FileName          = req.FileName.Trim(),
                    StoredFileName    = storedFileName,
                    ContentType       = contentType,
                    Description       = req.Description,
                    MilestoneId       = req.MilestoneId,
                    TaskId            = req.TaskId,
                    ForTechnological  = req.ForTechnological  ? 1 : 0,
                    ForMethodological = req.ForMethodological ? 1 : 0,
                    VideoUrl          = isVideo ? req.VideoUrl!.Trim() : (string?)null,
                    Id                = id,
                });

            return Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DB update failed for resource item Id={Id}", id);
            return StatusCode(500, DbError("שגיאה בעדכון הנתונים", ex));
        }
    }

    // ── DELETE /api/resourcefiles/{id} ───────────────────────────────────────
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, int authUserId)
    {
        ExistingFileRow? row;
        try
        {
            var rows = await _db.GetRecordsAsync<ExistingFileRow>(
                "SELECT StoredFileName, ContentType FROM ResourceFiles WHERE Id = @Id",
                new { Id = id });
            row = rows?.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Fetch before delete failed (Id={Id})", id);
            return StatusCode(500, DbError("שגיאה בשליפת הפריט", ex));
        }

        if (row is null) return NotFound("הפריט לא נמצא");

        if (!string.IsNullOrEmpty(row.StoredFileName))
        {
            try { _filesManage.DeleteFile(row.StoredFileName, Container); }
            catch (Exception ex)
            {
                // Log but don't abort — the DB record should still be removed
                _log.LogWarning(ex, "Physical file delete failed for '{StoredFileName}'", row.StoredFileName);
            }
        }

        try
        {
            await _db.SaveDataAsync("DELETE FROM ResourceFiles WHERE Id = @Id", new { Id = id });
            return Ok();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "DB delete failed for resource item Id={Id}", id);
            return StatusCode(500, DbError("שגיאה במחיקת הנתונים", ex));
        }
    }

    // ── Shared helpers (also used by StudentController) ──────────────────────

    internal static async Task<List<ResourceFileRow>> FetchAllRowsAsync(DbRepository db)
    {
        const string sql = @"
            SELECT  rf.Id,
                    COALESCE(rf.ItemType, 'File') AS ItemType,
                    rf.FileName,
                    rf.StoredFileName,
                    rf.ContentType,
                    rf.UploadedAt,
                    rf.Description,
                    rf.MilestoneId,
                    CASE WHEN rf.MilestoneId > 0 THEN mt.Title ELSE NULL END AS MilestoneName,
                    rf.TaskId,
                    tt.Title              AS TaskName,
                    rf.ForTechnological,
                    rf.ForMethodological,
                    rf.VideoUrl
            FROM    ResourceFiles      rf
            LEFT JOIN MilestoneTemplates mt ON mt.Id = rf.MilestoneId AND rf.MilestoneId > 0
            LEFT JOIN TaskTemplates      tt ON tt.Id = rf.TaskId
            ORDER   BY rf.MilestoneId, rf.UploadedAt DESC";

        return (await db.GetRecordsAsync<ResourceFileRow>(sql))?.ToList() ?? new();
    }

    internal static List<ResourceFileDto> MapRows(List<ResourceFileRow> rows) =>
        rows.Select(r => new ResourceFileDto
        {
            Id                = r.Id,
            ItemType          = r.ItemType,
            FileName          = r.FileName,
            StoredFileName    = r.StoredFileName,
            ContentType       = r.ContentType,
            UploadedAt        = r.UploadedAt,
            Description       = r.Description,
            MilestoneId       = r.MilestoneId,
            MilestoneName     = r.MilestoneName,
            TaskId            = r.TaskId,
            TaskName          = r.TaskName,
            ForTechnological  = r.ForTechnological  == 1,
            ForMethodological = r.ForMethodological == 1,
            VideoUrl          = r.VideoUrl,
        }).ToList();

    private async Task<List<ResourceFileRow>> FetchAllRows() =>
        await FetchAllRowsAsync(_db);

    // Returns the error message — includes the exception detail in Development
    // so the actual SQL/column error is visible in the browser/client log.
    private string DbError(string hebrewMessage, Exception ex)
    {
        if (_env.IsDevelopment())
            return $"{hebrewMessage}: {ex.GetType().Name} — {ex.Message}";
        return hebrewMessage;
    }

    // ── Private Dapper row classes ───────────────────────────────────────────

    internal sealed class ResourceFileRow
    {
        public int      Id                { get; set; }
        public string   ItemType          { get; set; } = "File";
        public string   FileName          { get; set; } = "";
        public string   StoredFileName    { get; set; } = "";
        public string   ContentType       { get; set; } = "";
        public DateTime UploadedAt        { get; set; }
        public string?  Description       { get; set; }
        public int      MilestoneId       { get; set; }
        public string?  MilestoneName     { get; set; }
        public int?     TaskId            { get; set; }
        public string?  TaskName          { get; set; }
        public int      ForTechnological  { get; set; }
        public int      ForMethodological { get; set; }
        public string?  VideoUrl          { get; set; }
    }

    private sealed class ExistingFileRow
    {
        public string StoredFileName { get; set; } = "";
        public string ContentType    { get; set; } = "";
    }
}
