using HelpDesk.Api.Models;
namespace HelpDesk.Api.Services;
public static class TicketWorkflow {
    public static bool CanTransition(TicketStatus current, TicketStatus next, bool isAdmin) {
        if (current == next) return false;
        return current switch {
            TicketStatus.New => next == TicketStatus.Open,
            TicketStatus.Open => next is TicketStatus.InProgress or TicketStatus.Resolved,
            TicketStatus.InProgress => next is TicketStatus.Open or TicketStatus.Resolved,
            // Closing is deliberately excluded. Only the requester can confirm closure.
            TicketStatus.Resolved => next == TicketStatus.InProgress,
            TicketStatus.Closed => isAdmin && next == TicketStatus.Open,
            _ => false
        };
    }
}
