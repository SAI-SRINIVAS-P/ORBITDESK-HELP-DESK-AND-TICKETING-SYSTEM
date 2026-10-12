using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
namespace HelpDesk.Api.Data;
public sealed class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options) : IdentityDbContext<ApplicationUser, IdentityRole, string>(options) {
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<TicketComment> TicketComments => Set<TicketComment>();
    public DbSet<TicketHistory> TicketHistory => Set<TicketHistory>();
    public DbSet<EmailOutboxMessage> EmailOutbox => Set<EmailOutboxMessage>();
    protected override void OnModelCreating(ModelBuilder builder) {
        base.OnModelCreating(builder);
        builder.Entity<ApplicationUser>().Property(x => x.DisplayName).HasMaxLength(120);
        builder.Entity<Ticket>(entity => {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(10000).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Priority).HasConversion<string>().HasMaxLength(20);
            entity.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            entity.HasIndex(x => new { x.RequesterId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.Status, x.Priority, x.UpdatedAtUtc });
            entity.HasOne(x => x.Requester).WithMany().HasForeignKey(x => x.RequesterId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
            entity.HasMany(x => x.Comments).WithOne(x => x.Ticket).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
            entity.HasMany(x => x.History).WithOne(x => x.Ticket).HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TicketComment>(entity => {
            entity.Property(x => x.Body).HasMaxLength(5000).IsRequired();
            entity.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<TicketHistory>(entity => {
            entity.Property(x => x.Field).HasMaxLength(80).IsRequired();
            entity.Property(x => x.OldValue).HasMaxLength(500); entity.Property(x => x.NewValue).HasMaxLength(500);
            entity.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.TicketId, x.ChangedAtUtc });
        });
        builder.Entity<EmailOutboxMessage>(entity => {
            entity.Property(x => x.ToEmail).HasMaxLength(256).IsRequired(); entity.Property(x => x.Subject).HasMaxLength(240).IsRequired();
            entity.Property(x => x.Body).HasMaxLength(10000).IsRequired(); entity.HasIndex(x => new { x.SentAtUtc, x.CreatedAtUtc });
        });
    }
}
