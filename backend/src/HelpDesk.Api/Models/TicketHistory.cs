namespace HelpDesk.Api.Models;
public sealed class TicketHistory {
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Ticket? Ticket { get; set; }
    public string ActorId { get; set; } = string.Empty;
    public ApplicationUser? Actor { get; set; }
    public string Field { get; set; } = string.Empty;
    public string? OldValue { get; set; }
    public string? NewValue { get; set; }
    public DateTimeOffset ChangedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
