using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Server.Data;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers;

// ─────────────────────────────────────────────────────────────────────────────
//  DemoDataController — /api/demo-data
//
//  Development-only. Every action re-checks IWebHostEnvironment.IsDevelopment()
//  itself, in addition to [Authorize(Roles = Roles.Admin)] and this class never
//  being reachable without a valid Bearer token — three independent reasons
//  this can never run in Production, not just one. See design/demo-data-spec.md
//  and design/demo-story.md for what gets seeded and why.
// ─────────────────────────────────────────────────────────────────────────────
[Route("api/demo-data")]
[ApiController]
[ServiceFilter(typeof(AuthCheck))]
[Authorize(Roles = Roles.Admin)]
public class DemoDataController : ControllerBase
{
    private readonly DemoDataSeeder _seeder;
    private readonly IWebHostEnvironment _env;

    public DemoDataController(DemoDataSeeder seeder, IWebHostEnvironment env)
    {
        _seeder = seeder;
        _env = env;
    }

    private bool BlockOutsideDevelopment(out IActionResult? result)
    {
        if (!_env.IsDevelopment())
        {
            result = NotFound(); // deliberately not Forbid() — don't reveal this route exists outside Development
            return true;
        }
        result = null;
        return false;
    }

    // POST /api/demo-data/seed
    [HttpPost("seed")]
    public async Task<IActionResult> Seed()
    {
        if (BlockOutsideDevelopment(out var blocked)) return blocked!;
        string message = await _seeder.SeedIfMissingAsync();
        return Ok(new { message });
    }

    // POST /api/demo-data/reset
    [HttpPost("reset")]
    public async Task<IActionResult> Reset()
    {
        if (BlockOutsideDevelopment(out var blocked)) return blocked!;
        string message = await _seeder.ResetAsync();
        return Ok(new { message });
    }

    // GET /api/demo-data/verify
    [HttpGet("verify")]
    public async Task<IActionResult> Verify()
    {
        if (BlockOutsideDevelopment(out var blocked)) return blocked!;
        var report = await _seeder.VerifyAsync();
        return Ok(report);
    }
}
