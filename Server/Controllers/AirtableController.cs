using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Airtable integration endpoints.
/// All actions are server-side only — the Airtable token never reaches the client.
/// </summary>
[Route("api/[controller]")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin + "," + Roles.Staff)]
public class AirtableController : ControllerBase
{
    private readonly AirtableService _airtable;

    public AirtableController(AirtableService airtable) => _airtable = airtable;

    // POST /api/airtable/sync-projects
    // Fetches all records from the configured Airtable base/table, maps them
    // to our internal project model, and upserts them into the local DB.
    // Returns a sync summary (fetched / inserted / updated / failed).
    [HttpPost("sync-projects")]
    public async Task<IActionResult> SyncProjects(int authUserId)
    {
        var result = await _airtable.SyncProjectsAsync();
        return Ok(result);
    }
}
