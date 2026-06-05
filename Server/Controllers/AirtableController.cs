using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

/// <summary>
/// Legacy Airtable endpoint. The unconfirmed sync surface is removed —
/// all imports MUST go through the preview→confirm flow at
/// /api/integrations/airtable/{id}/preview and /import. This controller
/// remains only to return a clear 410 Gone for any caller still pointing
/// at the old URL so the regression is loud, not silent.
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
    //
    // RETIRED 2026-06-04. This was an "import without confirmation" path:
    // it fetched all Airtable records and upserted them with no preview,
    // no admin review, no skip list. Per the import-confirmation rule, no
    // UI is allowed to write project data without an explicit admin
    // confirmation step.
    //
    // The replacement is two endpoints on AirtableIntegrationController:
    //   POST /api/integrations/airtable/{id}/preview  → analyse only
    //   POST /api/integrations/airtable/{id}/import   → admin-confirmed
    //
    // We return 410 Gone so any out-of-tree caller that still POSTs here
    // gets a loud failure instead of silently bypassing the preview.
    [HttpPost("sync-projects")]
    [Obsolete("Removed. Use /api/integrations/airtable/{id}/preview then /import. " +
              "Direct unconfirmed sync is no longer permitted.")]
    public IActionResult SyncProjects(int authUserId)
    {
        // Unused but retained so DI keeps wiring it without warnings.
        _ = _airtable;

        return StatusCode(StatusCodes.Status410Gone, new AirtableSyncResultDto
        {
            SyncError = "נקודת קצה זו הוסרה. ייבוא Airtable מחייב כעת תצוגה מקדימה ואישור: " +
                        "POST /api/integrations/airtable/{id}/preview ואז /import."
        });
    }
}
