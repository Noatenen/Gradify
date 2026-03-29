using AuthWithAdmin.Server.AuthHelpers;
using AuthWithAdmin.Shared.AuthSharedModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AuthWithAdmin.Server.Controllers
{
    [Route("api/[controller]")]
    [ServiceFilter(typeof(AuthCheck))]
    [Authorize(Roles = Roles.User)]
    [ApiController]
    
    public class ChatController : ControllerBase
    {

        [HttpGet]
        public async Task<IActionResult> GetUser(int authUserId)
        {
            return Ok(authUserId);
        }
    }
}
