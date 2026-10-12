using HelpDesk.Api.Data;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.EntityFrameworkCore;
using MimeKit;
namespace HelpDesk.Api.Services;
public sealed class EmailNotificationBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<EmailNotificationBackgroundService> logger) : BackgroundService {
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        try {
            while (!stoppingToken.IsCancellationRequested) {
                if (string.IsNullOrWhiteSpace(configuration["Smtp:Host"])) {
                    await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                    continue;
                }
                try { await DeliverBatchAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception ex) { logger.LogError(ex, "Email outbox batch failed"); }
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        } catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
            // Normal host shutdown; the outbox remains durable for the next process.
        }
    }
    private async Task DeliverBatchAsync(CancellationToken token) {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HelpDeskDbContext>();
        var batch = await db.EmailOutbox.Where(x => x.SentAtUtc == null).OrderBy(x => x.CreatedAtUtc).Take(10).ToListAsync(token);
        var smtpHost = configuration["Smtp:Host"];
        if (string.IsNullOrWhiteSpace(smtpHost)) {
            logger.LogDebug("SMTP host is not configured; email delivery is skipped for this cycle.");
            return;
        }
        foreach (var item in batch) {
            item.AttemptCount++; item.LastAttemptAtUtc = DateTimeOffset.UtcNow;
            try {
                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(configuration["Smtp:FromName"] ?? "OrbitDesk", configuration["Smtp:FromEmail"] ?? "no-reply@localhost"));
                message.To.Add(MailboxAddress.Parse(item.ToEmail)); message.Subject = item.Subject; message.Body = new TextPart("plain") { Text = item.Body };
                using var client = new SmtpClient();
                var port = configuration.GetValue("Smtp:Port", 587);
                var secure = configuration.GetValue<bool>("Smtp:UseSsl") ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTlsWhenAvailable;
                await client.ConnectAsync(smtpHost, port, secure, token);
                var username = configuration["Smtp:Username"];
                var password = configuration["Smtp:Password"];
                if (!string.IsNullOrWhiteSpace(username)) {
                    if (string.IsNullOrWhiteSpace(password))
                        throw new InvalidOperationException("Smtp:Password is required when Smtp:Username is configured.");
                    await client.AuthenticateAsync(username, password, token);
                }
                await client.SendAsync(message, token); await client.DisconnectAsync(true, token);
                item.SentAtUtc = DateTimeOffset.UtcNow; item.LastError = null;
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                item.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                logger.LogWarning(ex, "Could not deliver outbox email {EmailOutboxId}; it will be retried", item.Id);
            }
        }
        await db.SaveChangesAsync(token);
    }
}
