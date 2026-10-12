using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Xunit;
namespace HelpDesk.Api.Tests;
public sealed class TicketFlowTests(ApiFactory factory) : IClassFixture<ApiFactory> {
    private readonly HttpClient _client = factory.CreateClient();
    [Fact]
    public async Task Register_CreateTicket_AndReadTicket_Works() {
        var unique = Guid.NewGuid().ToString("N")[..8];
        var register = await _client.PostAsJsonAsync("/api/auth/register", new { displayName = "Integration Tester", email = $"tester-{unique}@example.com", password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        Assert.Equal(1, await CountOutboxAsync($"tester-{unique}@example.com", "Welcome to OrbitDesk"));
        using var authJson = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var token = authJson.RootElement.GetProperty("token").GetString();
        Assert.False(string.IsNullOrWhiteSpace(token));
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var create = await _client.PostAsJsonAsync("/api/tickets", new { title = "VPN access needed", description = "I cannot connect to the company VPN after the latest update.", category = "Access", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var ticketJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = ticketJson.RootElement.GetProperty("id").GetGuid();
        Assert.StartsWith("HD-", ticketJson.RootElement.GetProperty("number").GetString());
        var detail = await _client.GetAsync($"/api/tickets/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Equal("VPN access needed", detailJson.RootElement.GetProperty("title").GetString());
        Assert.Equal("New", detailJson.RootElement.GetProperty("status").GetString());
        Assert.True(detailJson.RootElement.GetProperty("history").GetArrayLength() > 0, "Ticket creation should be recorded in change history.");
    }

    [Fact]
    public async Task AgentCanWorkTicket_ButUserCannotWriteInternalNotesOrChangeStatus() {
        var userClient = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var register = await userClient.PostAsJsonAsync("/api/auth/register", new { displayName = "Requester", email = $"requester-{unique}@example.com", password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var authJson = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        userClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", authJson.RootElement.GetProperty("token").GetString());
        var create = await userClient.PostAsJsonAsync("/api/tickets", new { title = "Cannot access reports", description = "Reports page returns an error after I sign in to the application.", category = "Technical issue", priority = "High" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = createdJson.RootElement.GetProperty("id").GetGuid();

        var forbiddenStatus = await userClient.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Open", note = "Requester cannot change workflow state" });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenStatus.StatusCode);
        var forbiddenInternal = await userClient.PostAsJsonAsync($"/api/tickets/{id}/comments", new { body = "This should stay private", isInternal = true });
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenInternal.StatusCode);

        var agentClient = factory.CreateClient();
        var agentLogin = await agentClient.PostAsJsonAsync("/api/auth/login", new { email = "agent-integration@example.com", password = "Agent-Test-Password-123!" });
        Assert.Equal(HttpStatusCode.OK, agentLogin.StatusCode);
        using var agentAuth = JsonDocument.Parse(await agentLogin.Content.ReadAsStringAsync());
        agentClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentAuth.RootElement.GetProperty("token").GetString());
        var status = await agentClient.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Open", note = "Picked up by support" });
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
        var internalNote = await agentClient.PostAsJsonAsync($"/api/tickets/{id}/comments", new { body = "Internal troubleshooting details", isInternal = true });
        Assert.Equal(HttpStatusCode.OK, internalNote.StatusCode);
        var publicReply = await agentClient.PostAsJsonAsync($"/api/tickets/{id}/comments", new { body = "We are looking into this for you.", isInternal = false });
        Assert.Equal(HttpStatusCode.OK, publicReply.StatusCode);

        var requesterDetail = await userClient.GetAsync($"/api/tickets/{id}");
        Assert.Equal(HttpStatusCode.OK, requesterDetail.StatusCode);
        using var detail = JsonDocument.Parse(await requesterDetail.Content.ReadAsStringAsync());
        var comments = detail.RootElement.GetProperty("comments").EnumerateArray().ToArray();
        Assert.Single(comments);
        Assert.Equal("We are looking into this for you.", comments[0].GetProperty("body").GetString());
    }

    [Fact]
    public async Task RoleChangesApplyToAnExistingJwtOnTheNextRequest() {
        var client = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var register = await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Role Test", email = $"role-{unique}@example.com", password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var auth = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        var userId = auth.RootElement.GetProperty("userId").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.RootElement.GetProperty("token").GetString());

        var admin = factory.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin-integration@example.com", password = "Admin-Test-Password-123!", loginAs = "Admin" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var adminAuth = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.RootElement.GetProperty("token").GetString());
        var promote = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/role", new { role = "Agent" });
        Assert.Equal(HttpStatusCode.OK, promote.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/agents")).StatusCode);

        var demote = await admin.PutAsJsonAsync($"/api/admin/users/{userId}/role", new { role = "User" });
        Assert.Equal(HttpStatusCode.OK, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/agents")).StatusCode);
    }

    [Fact]
    public async Task LoginChoiceMustMatchAccountRoleAndAdminCannotRegisterThroughPublicSignup() {
        var client = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var register = await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Choice Tester", email = $"choice-{unique}@example.com", password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        var userAsAdmin = await client.PostAsJsonAsync("/api/auth/login", new { email = $"choice-{unique}@example.com", password = "GoodPassword-123!", loginAs = "Admin" });
        Assert.Equal(HttpStatusCode.Unauthorized, userAsAdmin.StatusCode);
        var adminAsUser = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin-integration@example.com", password = "Admin-Test-Password-123!", loginAs = "User" });
        Assert.Equal(HttpStatusCode.Unauthorized, adminAsUser.StatusCode);
        var adminAsAdmin = await client.PostAsJsonAsync("/api/auth/login", new { email = "admin-integration@example.com", password = "Admin-Test-Password-123!", loginAs = "Admin" });
        Assert.Equal(HttpStatusCode.OK, adminAsAdmin.StatusCode);
    }

    [Fact]
    public async Task DeleteAccountRequiresPasswordAndTypedConfirmationAndRevokesSession() {
        var client = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"delete-{unique}@example.com";
        const string password = "GoodPassword-123!";
        var register = await client.PostAsJsonAsync("/api/auth/register", new { displayName = "Delete Tester", email, password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var auth = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.RootElement.GetProperty("token").GetString());

        var badConfirmation = await client.DeleteAsyncWithJson("/api/auth/me", new { password, confirmation = "not-delete" });
        Assert.Equal(HttpStatusCode.BadRequest, badConfirmation.StatusCode);
        var badPassword = await client.DeleteAsyncWithJson("/api/auth/me", new { password = "Wrong-Password-123!", confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.BadRequest, badPassword.StatusCode);
        var deleted = await client.DeleteAsyncWithJson("/api/auth/me", new { password, confirmation = "DELETE" });
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(1, await CountOutboxAsync(email, "Your OrbitDesk account has been deleted"));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/auth/me")).StatusCode);

        var loginAfterDeletion = await client.PostAsJsonAsync("/api/auth/login", new { email, password, loginAs = "User" });
        Assert.Equal(HttpStatusCode.Unauthorized, loginAfterDeletion.StatusCode);
    }

    [Fact]
    public async Task AdminTicketStatusUpdateQueuesEmailForRequester()
    {
        var requester = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"admin-notify-{unique}@example.com";
        var title = $"Admin reviewed issue {unique}";
        var register = await requester.PostAsJsonAsync("/api/auth/register", new { displayName = "Admin Notification Requester", email, password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var requesterAuth = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        requester.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", requesterAuth.RootElement.GetProperty("token").GetString());
        var create = await requester.PostAsJsonAsync("/api/tickets", new { title, description = "A long enough description for the admin notification test ticket.", category = "Technical issue", priority = "Medium" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var ticketJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = ticketJson.RootElement.GetProperty("id").GetGuid();

        var admin = factory.CreateClient();
        var login = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin-integration@example.com", password = "Admin-Test-Password-123!", loginAs = "Admin" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var adminAuth = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.RootElement.GetProperty("token").GetString());
        var update = await admin.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Open", note = "Administrator reviewed this ticket." });
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal(1, await CountOutboxAsync(email, $"Administrator update: {title}"));
    }

    [Fact]
    public async Task StaffCannotCreateCustomerTicketsOrCloseWithoutRequesterConfirmation()
    {
        var requester = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"closure-{unique}@example.com";
        var register = await requester.PostAsJsonAsync("/api/auth/register", new { displayName = "Closure Requester", email, password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var userAuth = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        requester.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAuth.RootElement.GetProperty("token").GetString());
        var create = await requester.PostAsJsonAsync("/api/tickets", new { title = $"Close flow {unique}", description = "Testing the requester-approved ticket closure workflow.", category = "Technical issue", priority = "Medium" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var ticketJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = ticketJson.RootElement.GetProperty("id").GetGuid();

        var agent = factory.CreateClient();
        var agentLogin = await agent.PostAsJsonAsync("/api/auth/login", new { email = "agent-integration@example.com", password = "Agent-Test-Password-123!" });
        Assert.Equal(HttpStatusCode.OK, agentLogin.StatusCode);
        using var agentAuth = JsonDocument.Parse(await agentLogin.Content.ReadAsStringAsync());
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentAuth.RootElement.GetProperty("token").GetString());

        var staffCreate = await agent.PostAsJsonAsync("/api/tickets", new { title = "Staff must not create", description = "This is not a customer request ticket.", category = "Other", priority = "Low" });
        Assert.Equal(HttpStatusCode.Forbidden, staffCreate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Open", note = "Acknowledged" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Resolved", note = "Fix deployed; please verify" })).StatusCode);

        var directClose = await agent.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Closed", note = "Trying to close directly" });
        Assert.Equal(HttpStatusCode.Conflict, directClose.StatusCode);

        var admin = factory.CreateClient();
        var adminLogin = await admin.PostAsJsonAsync("/api/auth/login", new { email = "admin-integration@example.com", password = "Admin-Test-Password-123!", loginAs = "Admin" });
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
        using var adminAuth = JsonDocument.Parse(await adminLogin.Content.ReadAsStringAsync());
        admin.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.RootElement.GetProperty("token").GetString());
        var adminCreate = await admin.PostAsJsonAsync("/api/tickets", new { title = "Admin-created ticket", description = "Admins should only manage the queue.", category = "Other", priority = "Low" });
        Assert.Equal(HttpStatusCode.Forbidden, adminCreate.StatusCode);
        var adminDirectClose = await admin.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Closed", note = "Admin cannot bypass approval" });
        Assert.Equal(HttpStatusCode.Conflict, adminDirectClose.StatusCode);
        Assert.Equal(2, await CountOutboxAsync(email, $"Support update: Close flow {unique}"));

        var detail = await requester.GetAsync($"/api/tickets/{id}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using var detailJson = JsonDocument.Parse(await detail.Content.ReadAsStringAsync());
        Assert.Contains(detailJson.RootElement.GetProperty("history").EnumerateArray(), h => h.GetProperty("field").GetString() == "Closure request" && h.GetProperty("newValue").GetString() == "Pending");

        var confirmation = await requester.PostAsJsonAsync($"/api/tickets/{id}/closure-response", new { confirm = true, note = (string?)null });
        Assert.Equal(HttpStatusCode.OK, confirmation.StatusCode);
        using var confirmationJson = JsonDocument.Parse(await confirmation.Content.ReadAsStringAsync());
        Assert.Equal("Closed", confirmationJson.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, await CountOutboxAsync(email, $"Ticket closed with your confirmation: Close flow {unique}"));
    }

    [Fact]
    public async Task RequesterCanDeclineClosureAndTicketReopens()
    {
        var requester = factory.CreateClient();
        var unique = Guid.NewGuid().ToString("N")[..8];
        var email = $"decline-{unique}@example.com";
        var title = $"Needs more work {unique}";
        var register = await requester.PostAsJsonAsync("/api/auth/register", new { displayName = "Declining Requester", email, password = "GoodPassword-123!" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);
        using var userAuth = JsonDocument.Parse(await register.Content.ReadAsStringAsync());
        requester.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", userAuth.RootElement.GetProperty("token").GetString());
        var create = await requester.PostAsJsonAsync("/api/tickets", new { title, description = "Testing that unsatisfied requesters can reopen support tickets.", category = "Technical issue", priority = "Medium" });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var ticketJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var id = ticketJson.RootElement.GetProperty("id").GetGuid();

        var agent = factory.CreateClient();
        var login = await agent.PostAsJsonAsync("/api/auth/login", new { email = "agent-integration@example.com", password = "Agent-Test-Password-123!" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var agentJson = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        agent.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", agentJson.RootElement.GetProperty("token").GetString());
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Open", note = "Picked up" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await agent.PatchAsJsonAsync($"/api/tickets/{id}/status", new { status = "Resolved", note = "Please check" })).StatusCode);

        var decline = await requester.PostAsJsonAsync($"/api/tickets/{id}/closure-response", new { confirm = false, note = "I can still reproduce the issue." });
        Assert.Equal(HttpStatusCode.OK, decline.StatusCode);
        using var result = JsonDocument.Parse(await decline.Content.ReadAsStringAsync());
        Assert.Equal("Open", result.RootElement.GetProperty("status").GetString());
        Assert.Equal(1, await CountOutboxAsync(email, $"Ticket reopened: {title}"));
    }

    private async Task<int> CountOutboxAsync(string email, string subject)
    {
        await using var connection = new SqlConnection(factory.TestConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT COUNT(*) FROM [EmailOutbox] WHERE [ToEmail] = @email AND [Subject] = @subject", connection);
        command.Parameters.AddWithValue("@email", email);
        command.Parameters.AddWithValue("@subject", subject);
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task HealthEndpoint_IsAnonymous() {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

internal static class HttpClientJsonDeleteExtensions {
    public static Task<HttpResponseMessage> DeleteAsyncWithJson<T>(this HttpClient client, string uri, T value) {
        var request = new HttpRequestMessage(HttpMethod.Delete, uri) { Content = JsonContent.Create(value) };
        return client.SendAsync(request);
    }
}
