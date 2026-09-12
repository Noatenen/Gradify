using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  תוצרי הגשה — the faculty's graduation-deliverable catalog.
//
//  READ is open to any authenticated user: the student workspace renders it,
//  and a mentor or lecturer looking at a team needs the same definitions.
//  WRITE is Admin/Staff only — this is course content, authored by faculty.
//
//  IT OWNS DEFINITIONS AND NOTHING ELSE. A team's progress
//  (ProjectSubmissionStatuses) and a team's own links (ProjectResources) live
//  in their own tables, keyed by the same Key string, and no endpoint here
//  reads or writes either. Deactivating or editing a deliverable therefore
//  cannot destroy a team's history.
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/deliverables")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize]
public class DeliverablesController : ControllerBase
{
    private readonly DbRepository _db;
    public DeliverablesController(DbRepository db) => _db = db;

    // ── GET /api/deliverables ────────────────────────────────────────────────
    // The student-facing catalog: ACTIVE only, in display order.
    [HttpGet]
    public async Task<IActionResult> GetActive()
        => Ok(await LoadAsync(activeOnly: true));

    // ── GET /api/deliverables/manage ─────────────────────────────────────────
    // Everything, including deactivated rows — a lecturer must be able to see
    // and re-activate what they switched off.
    [HttpGet("manage")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> GetAll()
        => Ok(await LoadAsync(activeOnly: false));

    // ── POST /api/deliverables ───────────────────────────────────────────────
    [HttpPost]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Create([FromBody] SaveDeliverableRequest req)
    {
        var title = (req.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest("יש להזין שם לתוצר");

        // THE KEY IS DERIVED, NEVER TYPED. A lecturer manages a title; the join
        // identifier is the system's business. Derived from the title where it
        // yields something usable and otherwise from a counter, then made unique
        // so two deliverables can never collide on the column that
        // ProjectSubmissionStatuses and ProjectResources join through.
        var key = await UniqueKeyAsync(Slug(title));

        var nextOrder = (await _db.GetRecordsAsync<int>(
            "SELECT COALESCE(MAX(OrderIndex), -1) + 1 FROM SubmissionDeliverables"))
            ?.FirstOrDefault() ?? 0;

        int id = await _db.InsertReturnIdAsync(@"
            INSERT INTO SubmissionDeliverables
                (Key, Title, IconPath, Intro, RequirementsLabel, OrderIndex, IsActive)
            VALUES (@Key, @Title, @IconPath, @Intro, @Label, @Order, @IsActive)",
            new
            {
                Key      = key,
                Title    = title,
                // No icon picker: the eight shipped icons stay with their rows and
                // a new deliverable gets a neutral document glyph. Building an
                // icon-management system is out of scope by instruction.
                IconPath = "M7 3h7l5 5v13H7zM10 12h7M10 16h5",
                Intro    = Clean(req.Intro),
                Label    = (req.RequirementsLabel ?? "").Trim(),
                Order    = nextOrder,
                IsActive = req.IsActive ? 1 : 0,
            });

        await ReplaceLinesAsync(id, req);
        return Ok(new { id, key });
    }

    // ── PUT /api/deliverables/{id} ───────────────────────────────────────────
    //
    // Key IS NOT IN THE UPDATE, and that is the point. Editing a title must not
    // move the join: ProjectSubmissionStatuses and ProjectResources rows already
    // point at the old key, and re-deriving it would silently reset every team's
    // progress on this deliverable to "not started".
    [HttpPut("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Update(int id, [FromBody] SaveDeliverableRequest req)
    {
        var title = (req.Title ?? "").Trim();
        if (title.Length == 0) return BadRequest("יש להזין שם לתוצר");

        int affected = await _db.SaveDataAsync(@"
            UPDATE SubmissionDeliverables
            SET    Title             = @Title,
                   Intro             = @Intro,
                   RequirementsLabel = @Label,
                   IsActive          = @IsActive
            WHERE  Id = @Id",
            new
            {
                Title    = title,
                Intro    = Clean(req.Intro),
                Label    = (req.RequirementsLabel ?? "").Trim(),
                IsActive = req.IsActive ? 1 : 0,
                Id       = id,
            });

        if (affected == 0) return NotFound("התוצר לא נמצא");

        await ReplaceLinesAsync(id, req);
        return Ok();
    }

    // ── PATCH /api/deliverables/{id}/active ──────────────────────────────────
    // Visibility only. No status or resource row is touched — a deactivated
    // deliverable simply leaves the student catalog, and everything a team
    // already recorded against it survives and returns if it is switched back on.
    [HttpPatch("{id:int}/active")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> SetActive(int id, [FromQuery] bool value)
    {
        int affected = await _db.SaveDataAsync(
            "UPDATE SubmissionDeliverables SET IsActive = @V WHERE Id = @Id",
            new { V = value ? 1 : 0, Id = id });
        return affected == 0 ? NotFound("התוצר לא נמצא") : Ok();
    }

    // ── PUT /api/deliverables/order ──────────────────────────────────────────
    // The whole order at once, so the list can never end up with duplicate or
    // gapped indexes the way per-row index edits allow.
    [HttpPut("order")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Reorder([FromBody] ReorderDeliverablesRequest req)
    {
        for (int i = 0; i < req.OrderedIds.Count; i++)
            await _db.SaveDataAsync(
                "UPDATE SubmissionDeliverables SET OrderIndex = @O WHERE Id = @Id",
                new { O = i, Id = req.OrderedIds[i] });
        return Ok();
    }

    // ── DELETE /api/deliverables/{id} ────────────────────────────────────────
    // Lines cascade. Progress and resources do NOT: their rows keep the key and
    // simply stop matching, which both tables already document as harmless.
    [HttpDelete("{id:int}")]
    [Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
    public async Task<IActionResult> Delete(int id)
    {
        int affected = await _db.SaveDataAsync(
            "DELETE FROM SubmissionDeliverables WHERE Id = @Id", new { Id = id });
        return affected == 0 ? NotFound("התוצר לא נמצא") : Ok();
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private async Task<List<DeliverableAdminDto>> LoadAsync(bool activeOnly)
    {
        var rows = (await _db.GetRecordsAsync<DeliverableRow>($@"
            SELECT Id, Key, Title, IconPath, Intro, RequirementsLabel, OrderIndex, IsActive
            FROM   SubmissionDeliverables
            {(activeOnly ? "WHERE IsActive = 1" : "")}
            ORDER  BY OrderIndex, Id"))?.ToList() ?? new();

        var lines = (await _db.GetRecordsAsync<LineRow>(@"
            SELECT DeliverableId, LineType, Text
            FROM   SubmissionDeliverableLines
            ORDER  BY DeliverableId, OrderIndex, Id"))?.ToList() ?? new();

        return rows.Select(r => new DeliverableAdminDto
        {
            Id                = r.Id,
            Key               = r.Key,
            Title             = r.Title,
            IconPath          = r.IconPath,
            Intro             = r.Intro,
            RequirementsLabel = r.RequirementsLabel,
            OrderIndex        = r.OrderIndex,
            IsActive          = r.IsActive == 1,
            Requirements      = lines.Where(l => l.DeliverableId == r.Id && l.LineType == "Requirement")
                                     .Select(l => l.Text).ToList(),
            Notes             = lines.Where(l => l.DeliverableId == r.Id && l.LineType == "Note")
                                     .Select(l => l.Text).ToList(),
        }).ToList();
    }

    /// <summary>Delete-and-reinsert, the same pattern TaskTemplatesController
    /// uses for its resource links. The client owns the order of both lists, so
    /// add / edit / remove / reorder are one save rather than four endpoints.</summary>
    private async Task ReplaceLinesAsync(int deliverableId, SaveDeliverableRequest req)
    {
        await _db.SaveDataAsync(
            "DELETE FROM SubmissionDeliverableLines WHERE DeliverableId = @Id",
            new { Id = deliverableId });

        await InsertLinesAsync(deliverableId, "Requirement", req.Requirements);
        await InsertLinesAsync(deliverableId, "Note",        req.Notes);
    }

    private async Task InsertLinesAsync(int deliverableId, string type, List<string>? texts)
    {
        if (texts is null) return;

        int order = 0;
        foreach (var raw in texts)
        {
            var text = (raw ?? "").Trim();
            if (text.Length == 0) continue;   // a blank row is a deletion, not an empty bullet

            await _db.SaveDataAsync(@"
                INSERT INTO SubmissionDeliverableLines (DeliverableId, LineType, Text, OrderIndex)
                VALUES (@Id, @Type, @Text, @Order)",
                new { Id = deliverableId, Type = type, Text = text, Order = order++ });
        }
    }

    /// <summary>A URL-safe key from a title. Hebrew titles yield nothing usable
    /// here, which is expected — those fall back to the counter in
    /// <see cref="UniqueKeyAsync"/>. The key is internal and never displayed, so
    /// a non-descriptive one is not a problem; a colliding one would be.</summary>
    private static string Slug(string title)
    {
        var chars = title.ToLowerInvariant()
                         .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
                         .ToArray();
        var slug = new string(chars).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length > 40 ? slug[..40].Trim('-') : slug;
    }

    private async Task<string> UniqueKeyAsync(string preferred)
    {
        var taken = (await _db.GetRecordsAsync<string>(
            "SELECT Key FROM SubmissionDeliverables"))?.ToHashSet() ?? new HashSet<string>();

        var baseKey = preferred.Length > 0 ? preferred : "deliverable";
        if (!taken.Contains(baseKey)) return baseKey;

        for (int i = 2; ; i++)
        {
            var candidate = $"{baseKey}-{i}";
            if (!taken.Contains(candidate)) return candidate;
        }
    }

    private static string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();

    private sealed class DeliverableRow
    {
        public int     Id                { get; set; }
        public string  Key               { get; set; } = "";
        public string  Title             { get; set; } = "";
        public string  IconPath          { get; set; } = "";
        public string? Intro             { get; set; }
        public string  RequirementsLabel { get; set; } = "";
        public int     OrderIndex        { get; set; }
        public int     IsActive          { get; set; }
    }

    private sealed class LineRow
    {
        public int    DeliverableId { get; set; }
        public string LineType      { get; set; } = "";
        public string Text          { get; set; } = "";
    }
}
