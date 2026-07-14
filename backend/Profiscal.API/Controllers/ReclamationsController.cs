using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiscal.Application.Common.Interfaces;
using Profiscal.Contracts.Common;
using Profiscal.Contracts.Requests;
using Profiscal.Domain.Entities;
using Profiscal.Domain.Enums;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.API.Controllers;

/// <summary>
/// Reclamations: any user can report a bug/issue; admins triage the queue
/// and flip each item between Pending and Resolved.
/// </summary>
[ApiController]
[Route("api/reclamations")]
[Authorize]
[Produces("application/json")]
public class ReclamationsController(AppDbContext db, IRequestContext requestContext) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    /// <summary>Open a new reclamation.</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateReclamationRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Subject) || string.IsNullOrWhiteSpace(req.Description))
            return BadRequest(ApiResponse<object>.Fail("Subject and description are required."));

        var reclamation = new Reclamation
        {
            Subject     = req.Subject.Trim(),
            Description = req.Description.Trim(),
            Category    = Enum.TryParse<ReclamationCategory>(req.Category, true, out var c) ? c : ReclamationCategory.Bug,
            CreatedById = CurrentUserId
        };

        db.Reclamations.Add(reclamation);

        var me = await db.Users.FindAsync([CurrentUserId], ct);
        db.AuthAuditLogs.Add(new AuthAuditLog
        {
            AppUserId = CurrentUserId,
            Email     = me?.Email ?? string.Empty,
            Event     = AuthEvent.ReclamationOpened,
            Detail    = $"\"{reclamation.Subject}\"",
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent
        });

        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(reclamation, me)));
    }

    /// <summary>The reclamations I reported, newest first.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var rows = await db.Reclamations.AsNoTracking()
            .Include(r => r.CreatedBy)
            .Where(r => r.CreatedById == CurrentUserId)
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(rows.Select(r => ToDto(r, r.CreatedBy))));
    }

    /// <summary>Admin queue: every reclamation, filterable by status.</summary>
    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> All([FromQuery] string? status, CancellationToken ct)
    {
        var query = db.Reclamations.AsNoTracking().Include(r => r.CreatedBy).AsQueryable();
        if (Enum.TryParse<ReclamationStatus>(status, true, out var s))
            query = query.Where(r => r.Status == s);

        var rows = await query
            .OrderBy(r => r.Status)              // Pending first
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(rows.Select(r => ToDto(r, r.CreatedBy))));
    }

    /// <summary>Admin marks the reclamation solved (with an optional note shown to the reporter).</summary>
    [HttpPost("{id:guid}/resolve")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Resolve(Guid id, [FromBody] ResolveReclamationRequest req, CancellationToken ct)
    {
        var reclamation = await db.Reclamations.Include(r => r.CreatedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reclamation is null) return NotFound(ApiResponse<object>.Fail("Reclamation not found."));

        reclamation.Status       = ReclamationStatus.Resolved;
        reclamation.ResolvedAt   = DateTime.UtcNow;
        reclamation.ResolvedById = CurrentUserId;
        reclamation.AdminNote    = req.AdminNote?.Trim();

        db.AuthAuditLogs.Add(new AuthAuditLog
        {
            AppUserId = reclamation.CreatedById,
            Email     = reclamation.CreatedBy.Email ?? string.Empty,
            Event     = AuthEvent.ReclamationResolved,
            Detail    = $"\"{reclamation.Subject}\" (by {CurrentUserId})",
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent
        });

        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(reclamation, reclamation.CreatedBy)));
    }

    /// <summary>Admin flips a resolved reclamation back to pending.</summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        var reclamation = await db.Reclamations.Include(r => r.CreatedBy).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (reclamation is null) return NotFound(ApiResponse<object>.Fail("Reclamation not found."));

        reclamation.Status       = ReclamationStatus.Pending;
        reclamation.ResolvedAt   = null;
        reclamation.ResolvedById = null;

        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(reclamation, reclamation.CreatedBy)));
    }

    private static object ToDto(Reclamation r, AppUser? createdBy) => new
    {
        id = r.Id,
        subject = r.Subject,
        description = r.Description,
        category = r.Category.ToString(),
        status = r.Status.ToString(),
        createdAt = r.CreatedAt,
        resolvedAt = r.ResolvedAt,
        adminNote = r.AdminNote,
        createdBy = createdBy is null ? null : new
        {
            id = createdBy.Id,
            name = $"{createdBy.FirstName} {createdBy.LastName}".Trim(),
            email = createdBy.Email
        }
    };
}
