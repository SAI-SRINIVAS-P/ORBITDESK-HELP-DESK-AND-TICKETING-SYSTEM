using System.Security.Claims;
using FluentValidation;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Models;
using HelpDesk.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace HelpDesk.Api.Controllers;

[Authorize, Route("api/tickets")]
public sealed class TicketsController(HelpDeskDbContext db, UserManager<ApplicationUser> users,
    IValidator<CreateTicketRequest> createValidator, IValidator<AddCommentRequest> commentValidator,
    IValidator<UpdateTicketStatusRequest> statusValidator, ILogger<TicketsController> logger) : ValidationControllerBase {
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    private bool IsAgent => User.IsInRole("Agent") || User.IsInRole("Admin");
    private bool IsAdmin => User.IsInRole("Admin");

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] string? q, [FromQuery] TicketStatus? status,
        [FromQuery] TicketPriority? priority, CancellationToken token) {
        var query = db.Tickets.AsNoTracking().Include(x => x.Requester).Include(x => x.Assignee).AsQueryable();
        if (!IsAgent) query = query.Where(x => x.RequesterId == UserId);
        if (status.HasValue) query = query.Where(x => x.Status == status.Value);
        if (priority.HasValue) query = query.Where(x => x.Priority == priority.Value);
        if (!string.IsNullOrWhiteSpace(q)) { var term = q.Trim(); query = query.Where(x => x.Title.Contains(term) || x.Description.Contains(term) || x.Category.Contains(term)); }
        var rows = await query.OrderByDescending(x => x.UpdatedAtUtc).Take(200).ToListAsync(token);
        return Ok(rows.Select(ticket => ToListItem(ticket)).ToList());
    }

    [HttpGet("summary")]
    public async Task<ActionResult<DashboardSummaryDto>> Summary(CancellationToken token) {
        var query = db.Tickets.AsNoTracking();
        if (!IsAgent) query = query.Where(x => x.RequesterId == UserId);
        var total = await query.CountAsync(token);
        var newCount = await query.CountAsync(x => x.Status == TicketStatus.New, token);
        var inProgress = await query.CountAsync(x => x.Status == TicketStatus.InProgress, token);
        var resolved = await query.CountAsync(x => x.Status == TicketStatus.Resolved || x.Status == TicketStatus.Closed, token);
        var urgent = await query.CountAsync(x => x.Priority == TicketPriority.Urgent && x.Status != TicketStatus.Closed && x.Status != TicketStatus.Resolved, token);
        var assigned = IsAgent ? await query.CountAsync(x => x.AssigneeId == UserId && x.Status != TicketStatus.Closed, token) : 0;
        return Ok(new DashboardSummaryDto(total, newCount, inProgress, resolved, urgent, assigned));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateTicketRequest request, CancellationToken token) {
        // Agents/admins work the queue; only regular users may submit customer tickets.
        if (IsAgent) return Forbid();
        var validation = await ValidateRequestAsync(request, createValidator, token); if (validation is not null) return validation;
        var user = await db.Users.SingleAsync(x => x.Id == UserId, token);
        var ticket = new Ticket { Title = request.Title.Trim(), Description = request.Description.Trim(), Category = request.Category.Trim(), Priority = request.Priority, Status = TicketStatus.New, RequesterId = UserId, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.Tickets.Add(ticket);
        db.TicketHistory.Add(new TicketHistory { TicketId = ticket.Id, ActorId = UserId, Field = "Ticket", NewValue = "Created" });
        AddEmail(user.Email, "Ticket received: " + ticket.Title, $"Hi {user.DisplayName},\n\nYour ticket '{ticket.Title}' has been created with status New.\n\nOrbitDesk Support");
        await db.SaveChangesAsync(token);
        logger.LogInformation("Ticket {TicketId} created by {UserId}", ticket.Id, UserId);
        return CreatedAtAction(nameof(GetById), new { id = ticket.Id }, ToListItem(ticket, user));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken token) {
        var ticket = await DetailQuery().FirstOrDefaultAsync(x => x.Id == id, token);
        if (ticket is null) return NotFound(); if (!CanRead(ticket)) return Forbid();
        return Ok(ToDetail(ticket, IsAgent));
    }

    [HttpPost("{id:guid}/comments")]
    public async Task<IActionResult> AddComment(Guid id, AddCommentRequest request, CancellationToken token) {
        var validation = await ValidateRequestAsync(request, commentValidator, token); if (validation is not null) return validation;
        var ticket = await db.Tickets.Include(x => x.Requester).FirstOrDefaultAsync(x => x.Id == id, token);
        if (ticket is null) return NotFound(); if (!CanRead(ticket)) return Forbid(); if (request.IsInternal && !IsAgent) return Forbid();
        var comment = new TicketComment { TicketId = id, AuthorId = UserId, Body = request.Body.Trim(), IsInternal = request.IsInternal };
        db.TicketComments.Add(comment);
        db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Comment", NewValue = request.IsInternal ? "Internal note added" : "Public reply added" });
        ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;
        if (!request.IsInternal && ticket.RequesterId != UserId) AddEmail(ticket.Requester?.Email, $"New reply on {ticket.Title}", $"A new reply was added to your ticket '{ticket.Title}'.\n\n{request.Body.Trim()}");
        await db.SaveChangesAsync(token);
        return Ok(new CommentDto(comment.Id, UserId, User.FindFirstValue(ClaimTypes.Name) ?? "User", comment.Body, comment.IsInternal, comment.CreatedAtUtc));
    }

    [Authorize(Roles = "Agent,Admin"), HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(Guid id, UpdateTicketStatusRequest request, CancellationToken token) {
        var validation = await ValidateRequestAsync(request, statusValidator, token); if (validation is not null) return validation;
        var ticket = await db.Tickets.Include(x => x.Requester).Include(x => x.Assignee).FirstOrDefaultAsync(x => x.Id == id, token);
        if (ticket is null) return NotFound();
        if (request.Status == TicketStatus.Closed)
            return Conflict(new { message = "A ticket cannot be closed by support directly. Mark it Resolved and ask the requester to confirm closure." });
        var previous = ticket.Status;
        if (!TicketWorkflow.CanTransition(previous, request.Status, IsAdmin)) return Conflict(new { message = $"Transition from {previous} to {request.Status} is not allowed." });
        ticket.Status = request.Status; ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Status", OldValue = previous.ToString(), NewValue = request.Status.ToString() });
        if (!string.IsNullOrWhiteSpace(request.Note)) db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Workflow note", NewValue = request.Note.Trim() });

        // Resolve automatically starts a customer-confirmation step. A later move back to InProgress cancels it.
        if (request.Status == TicketStatus.Resolved)
            db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Closure request", OldValue = null, NewValue = "Pending" });
        else if (previous == TicketStatus.Resolved && request.Status == TicketStatus.InProgress)
        {
            var currentClosure = await LatestClosureRequestAsync(id, token);
            if (currentClosure?.NewValue == "Pending")
                db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Closure request", OldValue = "Pending", NewValue = "Cancelled" });
        }

        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? (IsAdmin ? "Administrator" : "Support team");
        var updateKind = IsAdmin ? "Administrator update" : "Support update";
        var closePrompt = request.Status == TicketStatus.Resolved
            ? "\n\nPlease sign in to OrbitDesk and confirm whether the issue is fixed. Choose Confirm & close if you are satisfied, or ‘I still need help’ to reopen the ticket. The ticket will not close until you confirm."
            : string.Empty;
        AddEmail(ticket.Requester?.Email, $"{updateKind}: {ticket.Title}",
            $"Hi {ticket.Requester?.DisplayName ?? "there"},\n\nYour ticket '{ticket.Title}' was updated by {actorName} ({(IsAdmin ? "Admin" : "Support team")}).\nStatus: {previous} → {request.Status}." +
            (string.IsNullOrWhiteSpace(request.Note) ? "" : $"\n\nNote: {request.Note.Trim()}") + closePrompt + "\n\nOrbitDesk Support");
        await db.SaveChangesAsync(token);
        return Ok(new { ticket.Id, Status = ticket.Status, ticket.UpdatedAtUtc, ClosureConfirmationRequired = request.Status == TicketStatus.Resolved });
    }

    [HttpPost("{id:guid}/closure-response")]
    public async Task<IActionResult> RespondToClosure(Guid id, RespondToClosureRequest request, CancellationToken token)
    {
        var ticket = await db.Tickets.Include(x => x.Requester).Include(x => x.Assignee)
            .FirstOrDefaultAsync(x => x.Id == id, token);
        if (ticket is null) return NotFound();
        if (!User.IsInRole("User") || IsAgent || ticket.RequesterId != UserId) return Forbid();
        if (ticket.Status != TicketStatus.Resolved)
            return Conflict(new { message = "Only a resolved ticket can be confirmed or reopened." });

        var latestClosure = await LatestClosureRequestAsync(id, token);
        if (latestClosure?.NewValue != "Pending")
            return Conflict(new { message = "There is no pending closure request for this ticket." });

        var now = DateTimeOffset.UtcNow;
        var requesterName = User.FindFirstValue(ClaimTypes.Name) ?? ticket.Requester?.DisplayName ?? "Requester";
        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = id, ActorId = UserId, Field = "Closure request",
            OldValue = "Pending", NewValue = request.Confirm ? "Confirmed" : "Declined", ChangedAtUtc = now
        });

        var previous = ticket.Status;
        ticket.Status = request.Confirm ? TicketStatus.Closed : TicketStatus.Open;
        ticket.UpdatedAtUtc = now;
        db.TicketHistory.Add(new TicketHistory
        {
            TicketId = id, ActorId = UserId, Field = "Status",
            OldValue = previous.ToString(), NewValue = ticket.Status.ToString(), ChangedAtUtc = now
        });

        if (!request.Confirm && !string.IsNullOrWhiteSpace(request.Note))
        {
            var comment = new TicketComment { TicketId = id, AuthorId = UserId, Body = request.Note.Trim(), IsInternal = false, CreatedAtUtc = now };
            db.TicketComments.Add(comment);
            db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Comment", NewValue = "Requester asked for more help", ChangedAtUtc = now });
        }

        if (request.Confirm)
        {
            AddEmail(ticket.Requester?.Email, $"Ticket closed with your confirmation: {ticket.Title}",
                $"Hi {requesterName},\n\nYou confirmed that '{ticket.Title}' is resolved. The ticket is now Closed.\n\nThank you for confirming.\n\nOrbitDesk Support");
            AddEmail(ticket.Assignee?.Email, $"Requester confirmed closure: {ticket.Title}",
                $"The requester confirmed that ticket '{ticket.Title}' is resolved. Its status is now Closed.");
        }
        else
        {
            AddEmail(ticket.Requester?.Email, $"Ticket reopened: {ticket.Title}",
                $"Hi {requesterName},\n\nYou indicated that '{ticket.Title}' still needs work. The ticket has been reopened with status Open so support can continue.\n\nOrbitDesk Support");
            AddEmail(ticket.Assignee?.Email, $"Ticket reopened by requester: {ticket.Title}",
                $"{requesterName} indicated that ticket '{ticket.Title}' still needs work. It has been reopened with status Open." +
                (string.IsNullOrWhiteSpace(request.Note) ? string.Empty : $"\n\nRequester note: {request.Note.Trim()}") );
        }

        await db.SaveChangesAsync(token);
        return Ok(new { ticket.Id, Status = ticket.Status, ticket.UpdatedAtUtc, ClosureConfirmed = request.Confirm });
    }

    private Task<TicketHistory?> LatestClosureRequestAsync(Guid ticketId, CancellationToken token) =>
        db.TicketHistory.Where(x => x.TicketId == ticketId && x.Field == "Closure request")
            .OrderByDescending(x => x.ChangedAtUtc).FirstOrDefaultAsync(token);

    [Authorize(Roles = "Agent,Admin"), HttpPut("{id:guid}/assignee")]
    public async Task<IActionResult> Assign(Guid id, AssignTicketRequest request, CancellationToken token) {
        var ticket = await db.Tickets.FirstOrDefaultAsync(x => x.Id == id, token); if (ticket is null) return NotFound();
        var previousId = ticket.AssigneeId;
        var previous = previousId is null ? null : await users.FindByIdAsync(previousId);
        ApplicationUser? target = null;
        if (!string.IsNullOrWhiteSpace(request.AgentId)) {
            target = await users.FindByIdAsync(request.AgentId);
            if (target is null) return BadRequest(new { message = "Agent account not found." });
            var roles = await users.GetRolesAsync(target);
            if (!roles.Contains("Agent") && !roles.Contains("Admin")) return BadRequest(new { message = "Tickets can only be assigned to an Agent or Admin." });
        }
        ticket.AssigneeId = target?.Id; ticket.UpdatedAtUtc = DateTimeOffset.UtcNow;
        db.TicketHistory.Add(new TicketHistory { TicketId = id, ActorId = UserId, Field = "Assignee", OldValue = previous?.DisplayName, NewValue = target?.DisplayName ?? "Unassigned" });
        var requester = await db.Users.FindAsync(new object?[] { ticket.RequesterId }, token);
        if (target is not null) AddEmail(target.Email, $"Ticket assigned: {ticket.Title}", $"Ticket '{ticket.Title}' has been assigned to you.");
        var assignmentText = target is null ? "is now unassigned" : $"has been assigned to {target.DisplayName}";
        var actorName = User.FindFirstValue(ClaimTypes.Name) ?? (IsAdmin ? "Administrator" : "Support team");
        AddEmail(requester?.Email, $"Ticket assignment updated: {ticket.Title}",
            $"Your ticket '{ticket.Title}' was updated by {actorName} ({(IsAdmin ? "Admin" : "Support team")}) and {assignmentText}.\n\nOrbitDesk Support");
        await db.SaveChangesAsync(token);
        return Ok(new { ticket.Id, ticket.AssigneeId, AssigneeName = target?.DisplayName });
    }

    [Authorize(Roles = "Agent,Admin"), HttpGet("/api/agents")]
    public async Task<IActionResult> Agents(CancellationToken token) {
        var agentUsers = await users.GetUsersInRoleAsync("Agent");
        var adminUsers = await users.GetUsersInRoleAsync("Admin");
        var list = agentUsers.Concat(adminUsers).DistinctBy(x => x.Id).OrderBy(x => x.DisplayName)
            .Select(user => new AgentDto(user.Id, user.DisplayName, user.Email ?? string.Empty)).ToList();
        return Ok(list);
    }

    private IQueryable<Ticket> DetailQuery() => db.Tickets.AsNoTracking().Include(x => x.Requester).Include(x => x.Assignee).Include(x => x.Comments).ThenInclude(x => x.Author).Include(x => x.History).ThenInclude(x => x.Actor);
    private bool CanRead(Ticket ticket) => IsAgent || ticket.RequesterId == UserId;
    private void AddEmail(string? email, string subject, string body) { if (!string.IsNullOrWhiteSpace(email)) db.EmailOutbox.Add(new EmailOutboxMessage { ToEmail = email, Subject = subject, Body = body }); }
    private static string TicketNumber(Ticket ticket) => $"HD-{ticket.CreatedAtUtc:yyyy}-{ticket.Id.ToString("N")[..8].ToUpperInvariant()}";
    private static TicketListItemDto ToListItem(Ticket ticket, ApplicationUser? requester = null) => new(ticket.Id, TicketNumber(ticket), ticket.Title, ticket.Category, ticket.Priority, ticket.Status, ticket.RequesterId, (requester ?? ticket.Requester)?.DisplayName ?? "Unknown user", ticket.AssigneeId, ticket.Assignee?.DisplayName, ticket.CreatedAtUtc, ticket.UpdatedAtUtc);
    private static TicketDetailDto ToDetail(Ticket ticket, bool canSeeInternal) => new(ticket.Id, TicketNumber(ticket), ticket.Title, ticket.Description, ticket.Category, ticket.Priority, ticket.Status, ticket.RequesterId, ticket.Requester?.DisplayName ?? "Unknown user", ticket.AssigneeId, ticket.Assignee?.DisplayName, ticket.CreatedAtUtc, ticket.UpdatedAtUtc,
        ticket.Comments.Where(x => canSeeInternal || !x.IsInternal).OrderBy(x => x.CreatedAtUtc).Select(x => new CommentDto(x.Id, x.AuthorId, x.Author?.DisplayName ?? "Unknown user", x.Body, x.IsInternal, x.CreatedAtUtc)).ToList(),
        ticket.History.OrderByDescending(x => x.ChangedAtUtc).Select(x => new HistoryDto(x.Id, x.Actor?.DisplayName ?? "System", x.Field, x.OldValue, x.NewValue, x.ChangedAtUtc)).ToList());
}
