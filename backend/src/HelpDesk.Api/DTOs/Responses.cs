using HelpDesk.Api.Models;
namespace HelpDesk.Api.DTOs;
public sealed record TicketListItemDto(Guid Id, string Number, string Title, string Category, TicketPriority Priority, TicketStatus Status, string RequesterId, string RequesterName, string? AssigneeId, string? AssigneeName, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc);
public sealed record CommentDto(Guid Id, string AuthorId, string AuthorName, string Body, bool IsInternal, DateTimeOffset CreatedAtUtc);
public sealed record HistoryDto(Guid Id, string ActorName, string Field, string? OldValue, string? NewValue, DateTimeOffset ChangedAtUtc);
public sealed record TicketDetailDto(Guid Id, string Number, string Title, string Description, string Category, TicketPriority Priority, TicketStatus Status, string RequesterId, string RequesterName, string? AssigneeId, string? AssigneeName, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, IReadOnlyList<CommentDto> Comments, IReadOnlyList<HistoryDto> History);
public sealed record DashboardSummaryDto(int Total, int New, int InProgress, int Resolved, int Urgent, int AssignedToMe);
public sealed record UserAdminDto(string Id, string DisplayName, string Email, IReadOnlyList<string> Roles, DateTimeOffset CreatedAtUtc);
public sealed record AgentDto(string Id, string DisplayName, string Email);
