namespace HelpDesk.Api.Models;
public enum TicketStatus { New, Open, InProgress, Resolved, Closed }
public enum TicketPriority { Low, Medium, High, Urgent }
public sealed class Ticket {
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = "General";
    public TicketPriority Priority { get; set; } = TicketPriority.Medium;
    public TicketStatus Status { get; set; } = TicketStatus.New;
    public string RequesterId { get; set; } = string.Empty;
    public ApplicationUser? Requester { get; set; }
    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<TicketComment> Comments { get; set; } = [];
    public List<TicketHistory> History { get; set; } = [];
}
