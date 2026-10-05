using System.Data.Common;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Kreyora.Application.Abstractions;
using Kreyora.Application.Audit;
using Kreyora.Application.Authorization;
using Kreyora.Application.Integrations;
using Kreyora.Application.Integrations.Instagram;
using Kreyora.Application.Tenancy;
using Kreyora.Domain.Abstractions;
using Kreyora.Domain.Integrations;
using Kreyora.Domain.Tenancy;
using Kreyora.Infrastructure.Audit;
using Kreyora.Infrastructure.Authorization;
using Kreyora.Infrastructure.Integrations;
using Kreyora.Infrastructure.Integrations.Instagram;
using Kreyora.Infrastructure.Persistence;
using Kreyora.Infrastructure.Tenancy;
using Kreyora.IntegrationTests.Fixtures;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit.Abstractions;

namespace Kreyora.IntegrationTests.Integrations;

/// <summary>
/// M08-S03 corrective reproductions (findings B1–B4). Each test asserts the required behavior;
/// while the corresponding production fix is unapproved the test is skipped with the finding ID.
/// See docs/plan/M08-S03_WEBHOOK_NORMALIZATION_PLAN.md (Corrective scope). Signed fixtures only;
/// no real identifiers or secrets.
/// </summary>
public sealed class InstagramWebhookCorrectiveReproTests : IClassFixture<PostgresFixture>
{
    private const string TestAppSecret = "repro_app_secret_ig";
    private const string TestVerifyToken = "repro_app_verify_token";

    private readonly PostgresFixture fixture;
    private readonly ITestOutputHelper output;

    public InstagramWebhookCorrectiveReproTests(PostgresFixture fixture, ITestOutputHelper output)
    {
        this.fixture = fixture;
        this.output = output;
    }

    // ---------- B1: app-level verification challenge at the documented callback ----------

    [Fact]
    public async Task B1_Http_ChallengeAtDocumentedCallback_EchoesChallengeForAppVerifyToken()
    {
        await MigrateAsync();
        await using var factory = new InstagramWebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            $"/v1/webhooks/instagram?hub.mode=subscribe&hub.verify_token={TestVerifyToken}&hub.challenge=1158201444");
        var body = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET documented callback -> {(int)response.StatusCode} {body}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1158201444", body);
    }

    [Fact]
    public async Task B1_Http_ChallengeAtDocumentedCallback_RejectsWrongToken()
    {
        await MigrateAsync();
        await using var factory = new InstagramWebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(
            "/v1/webhooks/instagram?hub.mode=subscribe&hub.verify_token=not_the_token&hub.challenge=42");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---------- B2: connection ownership — validation inputs and account-to-tenant ambiguity ----------

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task B2_CreateInstagramConnection_WithoutCompleteValidationInputs_IsRejected(
        bool withSecret, bool withOptions)
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-b2-val");
        using var scope = accessor.BeginScope(OwnerContext(tenant.Id));

        var request = new CreateChannelConnectionRequest(
            Channel: ChannelType.Instagram,
            ExternalAccountId: "igid_b2_val_" + Guid.NewGuid().ToString("N")[..8],
            DisplayName: "B2 validation",
            PlainTextSecret: withSecret ? "test_page_token_not_real" : null)
        {
            Instagram = withOptions ? new InstagramConnectOptions("page_b2", "igid_b2") : null
        };

        var result = await CreateConnectionService(db, accessor).CreateConnectionAsync(request);
        output.WriteLine($"secret={withSecret} options={withOptions} -> success={result.IsSuccess}");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, await db.ChannelConnections.CountAsync(c => c.ExternalAccountId == request.ExternalAccountId));
    }

    [Fact]
    public async Task B2_SameInstagramAccount_InSecondTenant_IsRejectedByService()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenantA = await CreateTenantAsync(db, "ig-b2-own-a");
        var tenantB = await CreateTenantAsync(db, "ig-b2-own-b");
        var igid = "igid_b2_own_" + Guid.NewGuid().ToString("N")[..8];
        await InsertConnectionAsync(db, accessor, tenantA.Id, igid);

        using var scope = accessor.BeginScope(OwnerContext(tenantB.Id));
        // Complete validation inputs, so the request reaches the ownership check (before any Graph call).
        var result = await CreateConnectionService(db, accessor).CreateConnectionAsync(
            new CreateChannelConnectionRequest(ChannelType.Instagram, igid, "B claims A's account",
                PlainTextSecret: "test_page_token_not_real")
            {
                Instagram = new InstagramConnectOptions("page_b2_b", igid)
            });
        output.WriteLine($"tenant B create for tenant A's account -> success={result.IsSuccess}");

        Assert.False(result.IsSuccess);
        Assert.Equal(409, result.Error!.Status);
        Assert.DoesNotContain(tenantA.Id, result.Error.Detail ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task B2_SameInstagramAccount_InSecondTenant_IsRejectedByDatabase()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenantA = await CreateTenantAsync(db, "ig-b2-db-a");
        var tenantB = await CreateTenantAsync(db, "ig-b2-db-b");
        var igid = "igid_b2_db_" + Guid.NewGuid().ToString("N")[..8];
        await InsertConnectionAsync(db, accessor, tenantA.Id, igid);

        Exception? failure = null;
        try
        {
            await InsertConnectionAsync(db, accessor, tenantB.Id, igid);
        }
        catch (Exception ex)
        {
            failure = ex;
        }

        output.WriteLine($"second-tenant insert -> {(failure is null ? "persisted" : failure.GetType().Name)}");
        Assert.IsType<DbUpdateException>(failure);
    }

    [Fact]
    public async Task B2_Http_ForgedAccountHeader_CannotRedirectSignedPayloadToAnotherTenant()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenantA = await CreateTenantAsync(db, "ig-b2-hdr-a");
        var tenantB = await CreateTenantAsync(db, "ig-b2-hdr-b");
        var igidA = "igid_hdr_a_" + Guid.NewGuid().ToString("N")[..8];
        var igidB = "igid_hdr_b_" + Guid.NewGuid().ToString("N")[..8];
        await InsertConnectionAsync(db, accessor, tenantA.Id, igidA);
        var connB = await InsertConnectionAsync(db, accessor, tenantB.Id, igidB);

        await using var factory = new InstagramWebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var body = MessageBody(igidA, "mid_hdr_" + Guid.NewGuid().ToString("N")[..8], "for tenant A");
        using var request = SignedPost("/v1/webhooks/instagram", body);
        request.Headers.Add("X-External-Account-Id", igidB);

        var response = await client.SendAsync(request);
        var rowsUnderB = await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.ConnectionId == connB);
        output.WriteLine($"forged header -> {(int)response.StatusCode}; rows under tenant B = {rowsUnderB}");

        Assert.Equal(0, rowsUnderB);
    }

    [Fact]
    public async Task B2_Http_RouteConnectionId_CannotClaimSignedPayloadForAnotherAccount()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenantA = await CreateTenantAsync(db, "ig-b2-rt-a");
        var tenantB = await CreateTenantAsync(db, "ig-b2-rt-b");
        var igidA = "igid_rt_a_" + Guid.NewGuid().ToString("N")[..8];
        var igidB = "igid_rt_b_" + Guid.NewGuid().ToString("N")[..8];
        await InsertConnectionAsync(db, accessor, tenantA.Id, igidA);
        var connB = await InsertConnectionAsync(db, accessor, tenantB.Id, igidB);

        await using var factory = new InstagramWebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var body = MessageBody(igidA, "mid_rt_" + Guid.NewGuid().ToString("N")[..8], "for tenant A");
        using var request = SignedPost($"/v1/webhooks/instagram/{connB}", body);

        var response = await client.SendAsync(request);
        var rowsUnderB = await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.ConnectionId == connB);
        output.WriteLine($"route connectionId of tenant B -> {(int)response.StatusCode}; rows under tenant B = {rowsUnderB}");

        Assert.Equal(0, rowsUnderB);
    }

    // ---------- B3: multiple accounts / entries in one signed delivery ----------

    [Fact]
    public async Task B3_MultiAccountDelivery_EachEntryReachesOnlyItsOwnTenant()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenantA = await CreateTenantAsync(db, "ig-b3-a");
        var tenantB = await CreateTenantAsync(db, "ig-b3-b");
        var igidA = "igid_b3_a_" + Guid.NewGuid().ToString("N")[..8];
        var igidB = "igid_b3_b_" + Guid.NewGuid().ToString("N")[..8];
        var connA = await InsertConnectionAsync(db, accessor, tenantA.Id, igidA);
        var connB = await InsertConnectionAsync(db, accessor, tenantB.Id, igidB);
        var midA = "mid_b3_a_" + Guid.NewGuid().ToString("N")[..8];
        var midB = "mid_b3_b_" + Guid.NewGuid().ToString("N")[..8];

        var body = "{\"object\":\"instagram\",\"entry\":[" +
            Entry(igidA, MessageItem(midA, "igsid_cust_a", "hello A")) + "," +
            Entry(igidB, MessageItem(midB, "igsid_cust_b", "hello B")) + "]}";

        await IngestAndProcessAllAsync(db, accessor, body);

        var midsUnderA = await InboundMidsAsync(db, connA);
        var midsUnderB = await InboundMidsAsync(db, connB);
        output.WriteLine($"tenant A inbound: [{string.Join(",", midsUnderA)}]; tenant B inbound: [{string.Join(",", midsUnderB)}]");

        Assert.DoesNotContain(midB, midsUnderA);
        Assert.Contains(midA, midsUnderA);
        Assert.Contains(midB, midsUnderB);
    }

    // ---------- B4: event identity — repeated messages vs distinct reactions/receipts ----------

    [Fact]
    public async Task B4_SameMessageInTwoDifferentDeliveries_ProducesOneInboundEvent()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-b4-rep");
        var igid = "igid_b4_rep_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);
        var mid = "mid_b4_rep_" + Guid.NewGuid().ToString("N")[..8];

        // Same message, different wrapper bytes (entry.time differs) => distinct body hashes.
        await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, MessageItem(mid, "igsid_c", "same"), time: 1729500000000)));
        await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, MessageItem(mid, "igsid_c", "same"), time: 1729500009999)));

        Assert.Equal(2, await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.ConnectionId == conn));
        Assert.Single(await InboundMidsAsync(db, conn));
    }

    [Fact]
    public async Task B4_SeenThenReactThenUnreact_OnOneMessage_AcrossDeliveries_AreAllRecorded()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-b4-seq");
        var igid = "igid_b4_seq_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);
        var businessMid = "mid_biz_" + Guid.NewGuid().ToString("N")[..8];

        await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, SeenItem(businessMid, 1729500001000))));
        await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, ReactionItem(businessMid, "react", 1729500002000))));
        await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, ReactionItem(businessMid, "unreact", 1729500003000))));

        var types = await db.InboundEvents.IgnoreQueryFilters()
            .Where(i => i.ConnectionId == conn).Select(i => i.EventType).ToListAsync();
        output.WriteLine($"inbound event types: [{string.Join(",", types)}]");

        Assert.Equal(3, types.Count);
        Assert.Equal(1, types.Count(t => t == "status"));
        Assert.Equal(2, types.Count(t => t == "reaction"));
    }

    [Fact]
    public async Task B4_SeenAndReactionOnOneMessage_InSameDelivery_ProcessesBoth()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-b4-batch");
        var igid = "igid_b4_batch_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);
        var businessMid = "mid_biz_" + Guid.NewGuid().ToString("N")[..8];

        var body = Wrap(Entry(igid,
            SeenItem(businessMid, 1729500001000) + "," + ReactionItem(businessMid, "react", 1729500002000)));

        var outcome = await IngestAndProcessAllAsync(db, accessor, body);
        var status = await EventStatusAsync(conn);
        var count = await db.InboundEvents.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == conn);
        output.WriteLine($"outcome={outcome}; status={status}; inbound={count}");

        Assert.Equal(WebhookProcessingStatus.Processed, status);
        Assert.Equal(2, count);
    }

    [Fact]
    public async Task B4_SameMessageTwiceInSameDelivery_ProcessesOnce()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-b4-dup");
        var igid = "igid_b4_dup_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);
        var mid = "mid_b4_dup_" + Guid.NewGuid().ToString("N")[..8];
        var item = MessageItem(mid, "igsid_c", "twice");

        var outcome = await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, item + "," + item)));
        var status = await EventStatusAsync(conn);
        var count = await db.InboundEvents.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == conn);
        output.WriteLine($"outcome={outcome}; status={status}; inbound={count}");

        Assert.Equal(WebhookProcessingStatus.Processed, status);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task B4_PersistenceFailureDuringProcessing_LeavesEventRetryable_NotStuckInProcessing()
    {
        var accessor = new TenantContextAccessor();
        await using (var setup = fixture.CreateDbContext(accessor))
        {
            await setup.Database.MigrateAsync();
        }

        await using var db = CreateDbContextWithInterceptor(accessor, new FailInboundInsertInterceptor());
        var tenant = await CreateTenantAsync(db, "ig-b4-rec");
        var igid = "igid_b4_rec_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);

        var outcome = await IngestAndProcessAllAsync(db, accessor,
            Wrap(Entry(igid, MessageItem("mid_b4_rec_" + Guid.NewGuid().ToString("N")[..8], "igsid_c", "x"))));
        var status = await EventStatusAsync(conn);
        output.WriteLine($"outcome={outcome}; status={status}");

        Assert.DoesNotContain("threw", outcome, StringComparison.Ordinal);
        Assert.Contains(status, new[] { WebhookProcessingStatus.Failed, WebhookProcessingStatus.DeadLetter });
    }

    // ---------- B5: provider message IDs longer than the 128-character column ----------

    [Fact]
    public async Task B5_MessageIdLongerThan128Characters_IsProcessed_NotDeadLettered()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-b5-len");
        var igid = "igid_b5_len_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);
        // Synthetic opaque ID; Meta documents no maximum mid length.
        var longMid = "aWdfZAG1f" + string.Concat(Enumerable.Repeat(Guid.NewGuid().ToString("N"), 6));

        var outcome = await IngestAndProcessAllAsync(db, accessor, Wrap(Entry(igid, MessageItem(longMid, "igsid_c", "long id"))));
        var status = await EventStatusAsync(conn);
        output.WriteLine($"mid length={longMid.Length}; outcome={outcome}; status={status}");

        Assert.Equal(WebhookProcessingStatus.Processed, status);
        Assert.Equal(1, await db.InboundEvents.IgnoreQueryFilters().CountAsync(i => i.ConnectionId == conn));
    }

    // ---------- C4: stranded-event reclaim ----------

    [Fact]
    public async Task C4_Job_ReclaimsEventStrandedInProcessing_ButNotOneStillWithinLease()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-c4-lease");
        var igid = "igid_c4_lease_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);

        var staleId = await IngestOnlyAsync(db, accessor, Wrap(Entry(igid, MessageItem("mid_c4_stale_" + Guid.NewGuid().ToString("N")[..8], "igsid_c", "a"))));
        var freshId = await IngestOnlyAsync(db, accessor, Wrap(Entry(igid, MessageItem("mid_c4_fresh_" + Guid.NewGuid().ToString("N")[..8], "igsid_c", "b"))));
        var staleAt = DateTimeOffset.UtcNow - WebhookProcessingJob.ProcessingLease - TimeSpan.FromMinutes(1);
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE webhook_events SET processing_status = 'Processing', modified_at = {staleAt} WHERE id = {staleId}");
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE webhook_events SET processing_status = 'Processing', modified_at = {DateTimeOffset.UtcNow} WHERE id = {freshId}");

        // The raw updates bypass EF (new xmin); production jobs use a fresh scoped context, so mirror that.
        db.ChangeTracker.Clear();

        var authorizer = new TenantPermissionAuthorizer(accessor);
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddSingleton<ITenantContextAccessor>(accessor);
        services.AddSingleton<ITenantPermissionAuthorizer>(authorizer);
        services.AddSingleton<IAuditEventService>(new AuditEventService(db, accessor, new Correlation("ig-c4"), authorizer));
        services.AddSingleton<IChannelProviderRegistry>(Registry());
        services.AddSingleton<IWebhookProcessingService, WebhookProcessingService>();
        services.AddSingleton<ITimeProvider, UtcTimeProvider>();
        services.AddLogging();
        await using var provider = services.BuildServiceProvider();
        var job = new WebhookProcessingJob(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<WebhookProcessingJob>.Instance);

        var processed = await job.ProcessTenantWebhooksAsync(provider, tenant.Id);

        await using var fresh = fixture.CreateDbContext(new TenantContextAccessor());
        var statuses = await fresh.WebhookEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.ConnectionId == conn)
            .ToDictionaryAsync(e => e.Id, e => e.ProcessingStatus);
        output.WriteLine($"processed={processed}; stale={statuses[staleId]}; fresh={statuses[freshId]}");

        Assert.Equal(1, processed);
        Assert.Equal(WebhookProcessingStatus.Processed, statuses[staleId]);
        Assert.Equal(WebhookProcessingStatus.Processing, statuses[freshId]);
    }

    // ---------- C5: documented delivery semantics and connection-status policy ----------

    [Fact]
    public async Task C5_Http_SignedPostAtDocumentedCallback_Returns200Accepted_InvalidSignatureReturns401()
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-c5-http");
        var igid = "igid_c5_http_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid);

        await using var factory = new InstagramWebhookFactory(fixture.ConnectionString);
        using var client = factory.CreateClient();
        var body = MessageBody(igid, "mid_c5_" + Guid.NewGuid().ToString("N")[..8], "hello");

        using var accepted = SignedPost("/v1/webhooks/instagram", body);
        var acceptedResponse = await client.SendAsync(accepted);
        var acceptedBody = await acceptedResponse.Content.ReadAsStringAsync();

        using var forged = SignedPost("/v1/webhooks/instagram", body.Replace("hello", "tampered", StringComparison.Ordinal));
        forged.Headers.Remove(InstagramChannelProvider.SignatureHeader);
        forged.Headers.Add(InstagramChannelProvider.SignatureHeader, Sign(body));
        var forgedResponse = await client.SendAsync(forged);

        var rows = await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.ConnectionId == conn);
        output.WriteLine($"signed -> {(int)acceptedResponse.StatusCode} {acceptedBody}; tampered -> {(int)forgedResponse.StatusCode}; rows={rows}");

        Assert.Equal(HttpStatusCode.OK, acceptedResponse.StatusCode);
        Assert.Equal("{\"status\":\"accepted\"}", acceptedBody);
        Assert.Equal(HttpStatusCode.Unauthorized, forgedResponse.StatusCode);
        Assert.Equal(1, rows);
    }

    [Theory]
    [InlineData(ChannelConnectionStatus.Active, true)]
    [InlineData(ChannelConnectionStatus.Degraded, true)]
    [InlineData(ChannelConnectionStatus.Expired, true)]
    [InlineData(ChannelConnectionStatus.Disabled, false)]
    [InlineData(ChannelConnectionStatus.Revoked, false)]
    [InlineData(ChannelConnectionStatus.Pending, false)]
    public async Task C5_ConnectionStatusPolicy_DecidesWhetherVerifiedDeliveriesAreStored(
        ChannelConnectionStatus status, bool stored)
    {
        var (db, accessor) = await NewDbAsync();
        await using var _ = db;
        var tenant = await CreateTenantAsync(db, "ig-c5-status");
        var igid = "igid_c5_st_" + Guid.NewGuid().ToString("N")[..8];
        var conn = await InsertConnectionAsync(db, accessor, tenant.Id, igid, status);

        var outcome = await IngestAndProcessAllAsync(db, accessor, MessageBody(igid, "mid_c5_st_" + Guid.NewGuid().ToString("N")[..8], "hi"));
        var rows = await db.WebhookEvents.IgnoreQueryFilters().CountAsync(e => e.ConnectionId == conn);
        output.WriteLine($"status={status}; outcome={outcome}; rows={rows}");

        Assert.Equal(stored ? 1 : 0, rows);
        if (!stored)
        {
            Assert.Equal("ingress:200:duplicate=False", outcome);
        }
    }

    // ---------- C4: migration backfill uses the same identity hash as the application ----------

    [Theory]
    [InlineData("aWdfZAG1faXRlbToxOklHTWVzc2FnZA")]
    [InlineData("mid_with_unicode_नमस्ते")]
    public async Task C4_MigrationBackfillHash_MatchesApplicationHash(string identity)
    {
        var (db, _) = await NewDbAsync();
        await using var __ = db;

        var sqlHash = await db.Database
            .SqlQuery<string>($"SELECT encode(sha256(convert_to({identity}, 'UTF8')), 'hex') AS \"Value\"")
            .SingleAsync();

        Assert.Equal(InboundEvent.HashIdentity(identity), sqlHash);
    }

    // ---------- helpers ----------

    private async Task MigrateAsync()
    {
        await using var db = fixture.CreateDbContext(new TenantContextAccessor());
        await db.Database.MigrateAsync();
    }

    private async Task<(AppDbContext Db, TenantContextAccessor Accessor)> NewDbAsync()
    {
        var accessor = new TenantContextAccessor();
        var db = fixture.CreateDbContext(accessor);
        await db.Database.MigrateAsync();
        return (db, accessor);
    }

    private AppDbContext CreateDbContextWithInterceptor(ITenantContextAccessor accessor, IInterceptor interceptor)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(fixture.ConnectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .AddInterceptors(interceptor)
            .Options;
        return new AppDbContext(options, accessor);
    }

    private async Task<WebhookProcessingStatus> EventStatusAsync(string connectionId)
    {
        await using var fresh = fixture.CreateDbContext(new TenantContextAccessor());
        return await fresh.WebhookEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.ConnectionId == connectionId)
            .Select(e => e.ProcessingStatus)
            .SingleAsync();
    }

    private static async Task<List<string?>> InboundMidsAsync(AppDbContext db, string connectionId) =>
        await db.InboundEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(i => i.ConnectionId == connectionId)
            .Select(i => i.ProviderMessageId)
            .ToListAsync();

    /// <summary>Ingests through the real ingress service, then processes every event it created.</summary>
    private static async Task<string> IngestAndProcessAllAsync(AppDbContext db, TenantContextAccessor accessor, string body)
    {
        var ingress = new WebhookIngressService(
            db, Registry(), CreateEncryptionService(), accessor, NullLogger<WebhookIngressService>.Instance);
        var correlationId = Guid.NewGuid().ToString("N");
        var accepted = await ingress.HandleWebhookAsync(new WebhookIngressCommand(
            Channel: ChannelType.Instagram,
            ConnectionId: null,
            Method: "POST",
            Path: "/v1/webhooks/instagram",
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["content-type"] = "application/json",
                [InstagramChannelProvider.SignatureHeader] = Sign(body)
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes(body),
            ContentType: "application/json",
            CorrelationId: correlationId,
            ReceivedAt: DateTimeOffset.UtcNow));

        if (!accepted.IsSuccess || accepted.IsDuplicate || accepted.EventId is null)
        {
            return $"ingress:{accepted.StatusCode}:duplicate={accepted.IsDuplicate}";
        }

        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("ig-repro"), authorizer);
        var processing = new WebhookProcessingService(
            db, accessor, authorizer, audit, Registry(), NullLogger<WebhookProcessingService>.Instance);

        // A multi-account delivery creates one event per account; process all of them.
        var eventIds = await db.WebhookEvents.IgnoreQueryFilters().AsNoTracking()
            .Where(e => e.CorrelationId == correlationId)
            .Select(e => e.Id)
            .ToListAsync();

        var outcomes = new List<string>();
        foreach (var eventId in eventIds)
        {
            try
            {
                var result = await processing.ProcessWebhookEventAsync(eventId);
                outcomes.Add($"processed:succeeded={result.Succeeded}");
            }
            catch (Exception ex)
            {
                outcomes.Add($"threw:{ex.GetType().Name}");
            }
        }

        return string.Join(";", outcomes);
    }

    private static ChannelProviderRegistry Registry() => new(new IChannelProvider[]
    {
        new InstagramChannelProvider(Options.Create(new InstagramWebhookOptions { AppSecret = TestAppSecret, VerifyToken = TestVerifyToken }))
    });

    private static ChannelConnectionService CreateConnectionService(AppDbContext db, TenantContextAccessor accessor)
    {
        var authorizer = new TenantPermissionAuthorizer(accessor);
        var audit = new AuditEventService(db, accessor, new Correlation("ig-repro"), authorizer);
        return new ChannelConnectionService(
            db, accessor, authorizer, CreateEncryptionService(), audit,
            new ChannelProviderRegistry(Array.Empty<IChannelProvider>()), new RefusingInstagramGraphClient());
    }

    private static async Task<string> IngestOnlyAsync(AppDbContext db, TenantContextAccessor accessor, string body)
    {
        var ingress = new WebhookIngressService(
            db, Registry(), CreateEncryptionService(), accessor, NullLogger<WebhookIngressService>.Instance);
        var result = await ingress.HandleWebhookAsync(new WebhookIngressCommand(
            Channel: ChannelType.Instagram,
            ConnectionId: null,
            Method: "POST",
            Path: "/v1/webhooks/instagram",
            Headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["content-type"] = "application/json",
                [InstagramChannelProvider.SignatureHeader] = Sign(body)
            },
            QueryParameters: new Dictionary<string, string>(),
            RawBody: Encoding.UTF8.GetBytes(body),
            ContentType: "application/json",
            CorrelationId: Guid.NewGuid().ToString("N"),
            ReceivedAt: DateTimeOffset.UtcNow));

        Assert.True(result.IsSuccess && result.EventId is not null);
        return result.EventId!;
    }

    private static async Task<string> InsertConnectionAsync(
        AppDbContext db,
        TenantContextAccessor accessor,
        string tenantId,
        string externalAccountId,
        ChannelConnectionStatus status = ChannelConnectionStatus.Active)
    {
        using var scope = accessor.BeginScope(OwnerContext(tenantId));
        var connection = ChannelConnection.Create(tenantId, ChannelType.Instagram, externalAccountId, "Repro IG", status: status);
        db.ChannelConnections.Add(connection);
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            db.Entry(connection).State = EntityState.Detached;
            throw;
        }

        return connection.Id;
    }

    private static HttpRequestMessage SignedPost(string path, string body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = new ByteArrayContent(Encoding.UTF8.GetBytes(body))
        };
        request.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add(InstagramChannelProvider.SignatureHeader, Sign(body));
        return request;
    }

    private static string Wrap(string entry) => "{\"object\":\"instagram\",\"entry\":[" + entry + "]}";

    private static string Entry(string igid, string messagingItems, long time = 1729500000000) =>
        "{\"id\":\"" + igid + "\",\"time\":" + time + ",\"messaging\":[" + messagingItems + "]}";

    private static string MessageBody(string igid, string mid, string text) =>
        Wrap(Entry(igid, MessageItem(mid, "igsid_cust", text)));

    private static string MessageItem(string mid, string sender, string text) =>
        "{\"sender\":{\"id\":\"" + sender + "\"},\"timestamp\":1729500000123,\"message\":{\"mid\":\"" + mid + "\",\"text\":\"" + text + "\"}}";

    private static string SeenItem(string businessMid, long timestamp) =>
        "{\"sender\":{\"id\":\"igsid_reader\"},\"timestamp\":" + timestamp + ",\"read\":{\"mid\":\"" + businessMid + "\"}}";

    private static string ReactionItem(string businessMid, string action, long timestamp) =>
        "{\"sender\":{\"id\":\"igsid_reactor\"},\"timestamp\":" + timestamp + ",\"reaction\":{\"mid\":\"" + businessMid +
        "\",\"action\":\"" + action + "\"" + (action == "react" ? ",\"reaction\":\"love\",\"emoji\":\"\\u2764\"" : "") + "}}";

    private static string Sign(string body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(TestAppSecret));
        return "sha256=" + Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(body)));
    }

    private static AesGcmSecretEncryptionService CreateEncryptionService() =>
        new(Options.Create(new SecretEncryptionOptions
        {
            MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            DefaultKeyVersion = "v1",
            VersionedKeys = []
        }));

    private static TenantContext OwnerContext(string tenantId) =>
        new(tenantId, "01J00000000000000000000001", "01J00000000000000000000002", TenantRole.Owner);

    private static async Task<Tenant> CreateTenantAsync(AppDbContext db, string prefix)
    {
        var tenant = Tenant.Create($"{prefix} tenant", $"{prefix}-{Guid.NewGuid():N}"[..Math.Min(40, prefix.Length + 1 + 32)]);
        db.Tenants.Add(tenant);
        await db.SaveChangesAsync();
        return tenant;
    }

    private sealed class UtcTimeProvider : ITimeProvider
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class Correlation(string correlationId) : ICorrelationContext
    {
        public string CorrelationId => correlationId;
        public void SetCorrelationId(string value) { }
    }

    /// <summary>Simulates a persistent database failure on every inbound-event insert.</summary>
    private sealed class FailInboundInsertInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO inbound_events", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated persistence failure for inbound_events.");
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class InstagramWebhookFactory(string connectionString) : WebApplicationFactory<Kreyora.WebApi.Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("Database:ConnectionString", connectionString);
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = connectionString,
                    ["InstagramWebhook:AppSecret"] = TestAppSecret,
                    ["InstagramWebhook:VerifyToken"] = TestVerifyToken,
                    ["SecretEncryption:MasterKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    ["PublicStorefront:PlatformBaseDomain"] = "kreyora.test",
                    ["PublicStorefront:EnableDevelopmentSlugRoutes"] = "true",
                    ["Email:Smtp:ApplicationName"] = "Kreyora Test",
                    ["Email:Smtp:Host"] = "smtp.kreyora.test",
                    ["Email:Smtp:Port"] = "587",
                    ["Email:Smtp:Security"] = "StartTls",
                    ["Email:Smtp:SenderEmail"] = "no-reply@kreyora.test",
                    ["Email:Smtp:SenderDisplayName"] = "Kreyora Test",
                    ["Email:Smtp:ApplicationPublicUrl"] = "https://seller.kreyora.test"
                });
            });
        }
    }
}
