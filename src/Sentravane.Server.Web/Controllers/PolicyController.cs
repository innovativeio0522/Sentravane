using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Sentravane.Server.Web.Data;
using Sentravane.Server.Web.Data.Entities;
using Sentravane.Shared.Models;

namespace Sentravane.Server.Web.Controllers;

[ApiController]
[Route("api/v1/policies")]
public class PolicyController(AppDbContext dbContext) : ControllerBase
{
    [HttpGet("{tenantId}")]
    public async Task<IActionResult> GetPolicy(string tenantId)
    {
        var policy = await dbContext.Policies.FirstOrDefaultAsync(p => p.TenantId == tenantId);
        if (policy == null)
        {
            policy = new Policy { TenantId = tenantId };
            dbContext.Policies.Add(policy);
            await dbContext.SaveChangesAsync();
        }

        return Ok(policy);
    }
}

