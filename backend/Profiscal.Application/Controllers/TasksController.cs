using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Profiscal.Domain.Abstractions;
using Profiscal.Domain.Contracts.Common;
using Profiscal.Domain.Contracts.Requests;
using Profiscal.Domain.Entities;
using Profiscal.Domain.Enums;
using Profiscal.Infrastructure.Persistence;

namespace Profiscal.Application.Controllers;

/// <summary>
/// Manager ↔ consultant task workflow: a manager assigns a consultation task,
/// the consultant (plus optional collaborators) works on it, links the generated
/// consultation and submits it back for review.
/// </summary>
[ApiController]
[Route("api/tasks")]
[Authorize]
[Produces("application/json")]
public class TasksController(
    AppDbContext db,
    UserManager<AppUser> userManager,
    IRequestContext requestContext) : ControllerBase
{
    private Guid CurrentUserId => Guid.Parse(
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub")!);

    private bool IsManager => User.IsInRole("Manager") || User.IsInRole("Admin");

    private bool IsAdminRole => User.IsInRole("Admin");

    // ───────────────────────── MANAGER SIDE ─────────────────────────

    /// <summary>Create a task for one of my consultants, optionally inviting collaborators by email.</summary>
    [HttpPost]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Create([FromBody] CreateWorkTaskRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req.Title))
            return BadRequest(ApiResponse<object>.Fail("A task title is required."));

        var consultant = await db.Users.FirstOrDefaultAsync(u => u.Id == req.ConsultantId, ct);
        if (consultant is null)
            return BadRequest(ApiResponse<object>.Fail("Consultant not found."));

        if (!User.IsInRole("Admin") && consultant.ManagerId != CurrentUserId)
            return BadRequest(ApiResponse<object>.Fail("You can only assign tasks to your own consultants."));

        var task = new WorkTask
        {
            Title        = req.Title.Trim(),
            Description  = req.Description?.Trim() ?? string.Empty,
            ClientName   = req.ClientName?.Trim() ?? string.Empty,
            Priority     = Enum.TryParse<WorkTaskPriority>(req.Priority, true, out var p) ? p : WorkTaskPriority.Medium,
            DueDate      = req.DueDate,
            ManagerId    = CurrentUserId,
            ConsultantId = consultant.Id
        };

        var skipped = new List<string>();
        foreach (var raw in (req.CollaboratorEmails ?? []).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var email = raw.Trim();
            if (email.Length == 0) continue;
            var user = await userManager.FindByEmailAsync(email);
            if (user is null || user.Id == consultant.Id) { skipped.Add(email); continue; }
            task.Collaborators.Add(new WorkTaskCollaborator { WorkTaskId = task.Id, UserId = user.Id });
        }

        db.WorkTasks.Add(task);
        await db.SaveChangesAsync(ct);

        // Tell the consultant (and any collaborators) they have new work.
        Notify(consultant.Id, "Nouvelle tâche assignée", task.Title, "/app/tasks");
        foreach (var c in task.Collaborators)
            Notify(c.UserId, "Nouvelle tâche (collaboration)", task.Title, "/app/tasks");

        await AuditAsync(consultant.Id, consultant.Email!, AuthEvent.TaskAssigned,
            $"\"{task.Title}\" by manager {CurrentUserId}", ct);

        var created = await LoadTaskAsync(task.Id, ct);
        return Ok(ApiResponse<object>.Ok(new { task = ToDto(created!), skippedCollaborators = skipped }));
    }

    /// <summary>
    /// Tasks I assigned (manager view), newest first. Admins see every task on the
    /// platform. Filter by consultant and status.
    /// </summary>
    [HttpGet("assigned")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Assigned(
        [FromQuery] Guid? consultantId, [FromQuery] string? status, CancellationToken ct)
    {
        var query = User.IsInRole("Admin")
            ? TaskQuery()
            : TaskQuery().Where(t => t.ManagerId == CurrentUserId);
        if (consultantId is not null) query = query.Where(t => t.ConsultantId == consultantId);
        if (Enum.TryParse<WorkTaskStatus>(status, true, out var s)) query = query.Where(t => t.Status == s);

        var tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync(ct);
        return Ok(ApiResponse<object>.Ok(tasks.Select(t => ToDto(t))));
    }

    /// <summary>My team: consultants attached to me, with task and consultation stats.</summary>
    [HttpGet("consultants")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Consultants(CancellationToken ct)
    {
        var consultants = await db.Users.AsNoTracking()
            .Where(u => u.ManagerId == CurrentUserId)
            .OrderBy(u => u.FirstName)
            .ToListAsync(ct);

        var ids = consultants.Select(c => c.Id).ToList();

        var taskStats = await db.WorkTasks.AsNoTracking()
            .Where(t => ids.Contains(t.ConsultantId))
            .GroupBy(t => new { t.ConsultantId, t.Status })
            .Select(g => new { g.Key.ConsultantId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        var consultationCounts = await db.FiscalConsultations.AsNoTracking()
            .Where(c => c.OwnerUserId != null && ids.Contains(c.OwnerUserId.Value))
            .GroupBy(c => c.OwnerUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

        return Ok(ApiResponse<object>.Ok(consultants.Select(c => new
        {
            id = c.Id,
            firstName = c.FirstName,
            lastName = c.LastName,
            email = c.Email,
            lastLoginAt = c.LastLoginAt,
            createdAt = c.CreatedAt,
            consultations = consultationCounts.GetValueOrDefault(c.Id),
            tasks = new
            {
                pending    = taskStats.Where(t => t.ConsultantId == c.Id && t.Status == WorkTaskStatus.Pending).Sum(t => t.Count),
                inProgress = taskStats.Where(t => t.ConsultantId == c.Id && t.Status == WorkTaskStatus.InProgress).Sum(t => t.Count),
                submitted  = taskStats.Where(t => t.ConsultantId == c.Id && t.Status == WorkTaskStatus.Submitted).Sum(t => t.Count),
                approved   = taskStats.Where(t => t.ConsultantId == c.Id && t.Status == WorkTaskStatus.Approved).Sum(t => t.Count),
            }
        })));
    }

    /// <summary>One consultant's profile + every task I assigned them (manager drill-down).</summary>
    [HttpGet("consultants/{id:guid}")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> ConsultantDetail(Guid id, CancellationToken ct)
    {
        var consultant = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);
        if (consultant is null)
            return NotFound(ApiResponse<object>.Fail("Consultant not found."));
        if (!User.IsInRole("Admin") && consultant.ManagerId != CurrentUserId)
            return NotFound(ApiResponse<object>.Fail("This consultant is not in your team."));

        var tasks = await TaskQuery()
            .Where(t => t.ConsultantId == id)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(new
        {
            consultant = new
            {
                id = consultant.Id,
                firstName = consultant.FirstName,
                lastName = consultant.LastName,
                email = consultant.Email,
                lastLoginAt = consultant.LastLoginAt,
                createdAt = consultant.CreatedAt,
            },
            tasks = tasks.Select(t => ToDto(t))
        }));
    }

    /// <summary>Manager validates a submitted task.</summary>
    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Approve(Guid id, CancellationToken ct)
    {
        var task = await LoadTaskAsync(id, ct);
        if (task is null || (!IsAdminRole && task.ManagerId != CurrentUserId))
            return NotFound(ApiResponse<object>.Fail("Task not found."));
        if (task.Status != WorkTaskStatus.Submitted)
            return BadRequest(ApiResponse<object>.Fail("Only submitted tasks can be approved."));

        task.Status = WorkTaskStatus.Approved;
        task.ApprovedAt = DateTime.UtcNow;
        Notify(task.ConsultantId, "Tâche validée ✓", task.Title, "/app/tasks");
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(task)));
    }

    /// <summary>Send a submitted task back to the consultant for rework.</summary>
    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Reopen(Guid id, CancellationToken ct)
    {
        var task = await LoadTaskAsync(id, ct);
        if (task is null || (!IsAdminRole && task.ManagerId != CurrentUserId))
            return NotFound(ApiResponse<object>.Fail("Task not found."));

        task.Status = WorkTaskStatus.InProgress;
        task.SubmittedAt = null;
        task.ApprovedAt = null;
        Notify(task.ConsultantId, "Tâche renvoyée pour révision", task.Title, "/app/tasks");
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(task)));
    }

    /// <summary>Delete a task I created (only while it hasn't been submitted).</summary>
    [HttpDelete("{id:guid}")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var task = await db.WorkTasks.FirstOrDefaultAsync(
            t => t.Id == id && (IsAdminRole || t.ManagerId == CurrentUserId), ct);
        if (task is null) return NotFound(ApiResponse<object>.Fail("Task not found."));
        if (task.Status is WorkTaskStatus.Submitted or WorkTaskStatus.Approved)
            return BadRequest(ApiResponse<object>.Fail("Submitted work can't be deleted."));

        db.WorkTasks.Remove(task);
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(new { deleted = true }));
    }

    /// <summary>Invite another consultant (by email) to collaborate on an existing task.</summary>
    [HttpPost("{id:guid}/collaborators")]
    [Authorize(Roles = "Manager,Admin")]
    public async Task<IActionResult> AddCollaborator(Guid id, [FromBody] AddCollaboratorRequest req, CancellationToken ct)
    {
        var task = await LoadTaskAsync(id, ct);
        if (task is null || task.ManagerId != CurrentUserId)
            return NotFound(ApiResponse<object>.Fail("Task not found."));

        var user = await userManager.FindByEmailAsync(req.Email.Trim());
        if (user is null)
            return BadRequest(ApiResponse<object>.Fail($"No account found for {req.Email}."));
        if (user.Id == task.ConsultantId || task.Collaborators.Any(c => c.UserId == user.Id))
            return BadRequest(ApiResponse<object>.Fail("This user is already on the task."));

        task.Collaborators.Add(new WorkTaskCollaborator { WorkTaskId = task.Id, UserId = user.Id, User = user });
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(task)));
    }

    /// <summary>One task, visible to its manager, consultant or collaborators (document-view page).</summary>
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOne(Guid id, CancellationToken ct)
    {
        var me = CurrentUserId;
        var task = await TaskQuery().FirstOrDefaultAsync(
            t => t.Id == id && (t.ManagerId == me || t.ConsultantId == me ||
                                t.Collaborators.Any(c => c.UserId == me) || User.IsInRole("Admin")), ct);
        if (task is null) return NotFound(ApiResponse<object>.Fail("Task not found."));
        return Ok(ApiResponse<object>.Ok(ToDto(task)));
    }

    // ───────────────────────── CONSULTANT SIDE ─────────────────────────

    /// <summary>My work queue: tasks assigned to me plus tasks I collaborate on.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> Mine(CancellationToken ct)
    {
        var me = CurrentUserId;
        var tasks = await TaskQuery()
            .Where(t => t.ConsultantId == me || t.Collaborators.Any(c => c.UserId == me))
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync(ct);

        return Ok(ApiResponse<object>.Ok(tasks.Select(t => ToDto(t, isCollaboration: t.ConsultantId != me))));
    }

    /// <summary>Consultant picks the task up (Pending → InProgress).</summary>
    [HttpPost("{id:guid}/start")]
    public async Task<IActionResult> Start(Guid id, CancellationToken ct)
    {
        var task = await LoadOwnedTaskAsync(id, ct);
        if (task is null) return NotFound(ApiResponse<object>.Fail("Task not found."));
        if (task.Status != WorkTaskStatus.Pending)
            return BadRequest(ApiResponse<object>.Fail("This task has already been started."));

        task.Status = WorkTaskStatus.InProgress;
        task.StartedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(ApiResponse<object>.Ok(ToDto(task)));
    }

    /// <summary>Submit the finished work to the manager, linking the consultation produced.</summary>
    [HttpPost("{id:guid}/submit")]
    public async Task<IActionResult> Submit(Guid id, [FromBody] SubmitWorkTaskRequest req, CancellationToken ct)
    {
        var task = await LoadOwnedTaskAsync(id, ct);
        if (task is null) return NotFound(ApiResponse<object>.Fail("Task not found."));
        if (task.Status == WorkTaskStatus.Approved)
            return BadRequest(ApiResponse<object>.Fail("This task is already approved."));

        if (req.ConsultationId is not null)
        {
            var exists = await db.FiscalConsultations.AnyAsync(c => c.Id == req.ConsultationId, ct);
            if (!exists)
                return BadRequest(ApiResponse<object>.Fail("The linked consultation doesn't exist."));
        }

        task.ConsultationId = req.ConsultationId;
        task.SubmitNote     = req.Note?.Trim();
        task.Status         = WorkTaskStatus.Submitted;
        task.SubmittedAt    = DateTime.UtcNow;
        task.StartedAt    ??= task.SubmittedAt;

        // Ping the manager that work is ready for validation.
        Notify(task.ManagerId, "Tâche soumise pour validation", task.Title,
            $"/manager/consultants/{task.ConsultantId}");
        await db.SaveChangesAsync(ct);

        var me = await db.Users.FindAsync([CurrentUserId], ct);
        await AuditAsync(CurrentUserId, me?.Email ?? "", AuthEvent.TaskSubmitted, $"\"{task.Title}\"", ct);

        return Ok(ApiResponse<object>.Ok(ToDto(task)));
    }

    /* ───────────────────────── helpers ───────────────────────── */

    private IQueryable<WorkTask> TaskQuery() => db.WorkTasks
        .Include(t => t.Manager)
        .Include(t => t.Consultant)
        .Include(t => t.Collaborators).ThenInclude(c => c.User);

    private Task<WorkTask?> LoadTaskAsync(Guid id, CancellationToken ct) =>
        TaskQuery().FirstOrDefaultAsync(t => t.Id == id, ct);

    /// <summary>A task the current user can work on: theirs, or one they collaborate on.</summary>
    private async Task<WorkTask?> LoadOwnedTaskAsync(Guid id, CancellationToken ct)
    {
        var me = CurrentUserId;
        return await TaskQuery().FirstOrDefaultAsync(
            t => t.Id == id && (t.ConsultantId == me || t.Collaborators.Any(c => c.UserId == me)), ct);
    }

    private object ToDto(WorkTask t, bool isCollaboration = false)
    {
        var consultation = t.ConsultationId is null ? null
            : db.FiscalConsultations.AsNoTracking()
                .Where(c => c.Id == t.ConsultationId)
                .Select(c => new { id = c.Id, c.Reference, c.ClientName, c.FiscalQuestion, createdAt = c.CreatedAt })
                .FirstOrDefault();

        return new
        {
            id = t.Id,
            title = t.Title,
            description = t.Description,
            clientName = t.ClientName,
            status = t.Status.ToString(),
            priority = t.Priority.ToString(),
            dueDate = t.DueDate,
            createdAt = t.CreatedAt,
            startedAt = t.StartedAt,
            submittedAt = t.SubmittedAt,
            approvedAt = t.ApprovedAt,
            submitNote = t.SubmitNote,
            consultationId = t.ConsultationId,
            consultation,
            isCollaboration,
            manager = new { id = t.Manager.Id, name = $"{t.Manager.FirstName} {t.Manager.LastName}".Trim(), email = t.Manager.Email },
            consultant = new { id = t.Consultant.Id, name = $"{t.Consultant.FirstName} {t.Consultant.LastName}".Trim(), email = t.Consultant.Email },
            collaborators = t.Collaborators.Select(c => new
            {
                id = c.UserId,
                name = $"{c.User.FirstName} {c.User.LastName}".Trim(),
                email = c.User.Email
            }),
        };
    }

    /// <summary>Queue an in-app notification (saved with the caller's next SaveChanges).</summary>
    private void Notify(Guid recipientId, string title, string body, string? link) =>
        db.Notifications.Add(new Notification
        {
            RecipientId = recipientId,
            Type        = "task",
            Title       = title,
            Body        = body,
            LinkUrl     = link
        });

    private async Task AuditAsync(Guid? userId, string email, AuthEvent evt, string? detail, CancellationToken ct)
    {
        db.AuthAuditLogs.Add(new AuthAuditLog
        {
            AppUserId = userId,
            Email     = email,
            Event     = evt,
            Detail    = detail,
            IpAddress = requestContext.IpAddress,
            UserAgent = requestContext.UserAgent
        });
        await db.SaveChangesAsync(ct);
    }
}
