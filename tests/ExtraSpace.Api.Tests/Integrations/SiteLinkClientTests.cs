using System.Net;
using ExtraSpace.Api.Integrations.SiteLink;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Integrations;

public sealed class SiteLinkClientTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<SiteLinkReply> CallAsync(string operation, string code, SiteLinkCallKind kind, SiteLinkFields? fields = null)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<SiteLinkClient>().CallAsync(operation, code, kind, fields ?? new SiteLinkFields(), Ct);
    }

    [Fact]
    public async Task A_call_sends_the_logon_block_first_then_the_fields_in_order()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("TestRead", code, SiteLinkXml.Reply("TestRead", ("Table", SiteLinkXml.Row(("sName", "first")))));

        var reply = await CallAsync("TestRead", code, SiteLinkCallKind.Read,
            new SiteLinkFields().Add("lngLastTimePolled", "0").Add("bFlag", true).Add("iCount", 3).Add("dcAmount", 12.50m));

        reply.Table("Table").Single().Text("sName").Should().Be("first");
        var request = api.SiteLink.RequestsFor(code).Single();
        request.Names.Should().Equal("sCorpCode", "sLocationCode", "sCorpUserName", "sCorpPassword", "lngLastTimePolled", "bFlag", "iCount", "dcAmount");
        request.Fields["sLocationCode"].Should().Be(code);
        request.Fields["sCorpUserName"].Should().Be("test-user:::test-key");
        request.Fields["bFlag"].Should().Be("true");
        request.Fields["dcAmount"].Should().Be("12.50");
        request.SoapAction.Should().Be("\"http://tempuri.org/CallCenterWs/CallCenterWs/TestRead\"");
    }

    [Fact]
    public async Task A_negative_return_code_is_a_refusal_and_the_call_is_logged_without_bodies()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("TestRefused", code, SiteLinkXml.Reply("TestRefused", ("RT", SiteLinkXml.Row(("Ret_Code", "-25"), ("Ret_Msg", "Invalid UnitID")))));

        var reply = await CallAsync("TestRefused", code, SiteLinkCallKind.Read);
        var refuse = () => reply.ThrowIfRefused("TestRefused");

        var error = refuse.Should().Throw<SiteLinkException>().Which;
        error.ReturnCode.Should().Be(-25);
        error.SiteLinkMessage.Should().Be("Invalid UnitID");
        var line = (await api.Logs.WaitForAsync(l => l.GetRawText().Contains("TestRefused") && l.GetRawText().Contains(code)))
            .Single().GetRawText();
        line.Should().Contain("refused").And.Contain("-25");
        line.Should().NotContain("test-password").And.NotContain("Invalid UnitID");
    }

    [Fact]
    public async Task A_read_is_retried_on_a_server_error_but_a_write_is_not()
    {
        const string code = "LSETUP";   // the only site a write may reach outside production
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var readOperation = "TestRetryRead" + suffix;
        var writeOperation = "TestRetryWrite" + suffix;
        var reads = 0;
        api.SiteLink.Reply(readOperation, code, _ => Interlocked.Increment(ref reads) == 1
            ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
            : FakeSiteLink.Ok(SiteLinkXml.Reply(readOperation)));
        api.SiteLink.Reply(writeOperation, code, _ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await CallAsync(readOperation, code, SiteLinkCallKind.Read);
        var write = () => CallAsync(writeOperation, code, SiteLinkCallKind.Write);

        (await write.Should().ThrowAsync<SiteLinkUnavailableException>()).Which.Status.Should().Be(503);
        api.SiteLink.RequestsFor(code, readOperation).Should().HaveCount(2);
        api.SiteLink.RequestsFor(code, writeOperation).Should().HaveCount(1);
        long counted = 0;
        await api.WithDbAsync(async db => counted = await db.Set<SiteLinkCallMonth>().Where(m => m.Operation == readOperation).SumAsync(m => m.Calls, Ct));
        counted.Should().Be(2);   // every attempt counts against SiteLink's budget
        // The retry is in our call line (attempts 2); Polly's retry and attempt events are Debug, so they add no line.
        api.Logs.All().Where(l => l.TryGetProperty("SourceContext", out var source) && source.GetString() == "Polly").Should().BeEmpty();
    }

    [Fact]
    public async Task An_unreadable_reply_is_unavailable_and_its_body_is_not_quoted()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("TestUnreadable", code, _ => FakeSiteLink.Ok("<NewDataSet><Table>test-body-text"));

        var call = () => CallAsync("TestUnreadable", code, SiteLinkCallKind.Read);

        var error = (await call.Should().ThrowAsync<SiteLinkUnavailableException>()).Which;
        error.InnerException.Should().BeNull();
        error.Message.Should().NotContain("test-body-text");
        (await api.Logs.WaitForAsync(l => l.GetRawText().Contains("TestUnreadable") && l.GetRawText().Contains(code)))
            .Single().GetProperty("outcome").GetString().Should().Be("unavailable");
    }

    [Fact]
    public async Task A_write_outside_the_allow_list_never_leaves_the_process()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("TestBlockedWrite", code, SiteLinkXml.Reply("TestBlockedWrite"));

        var write = () => CallAsync("TestBlockedWrite", code, SiteLinkCallKind.Write);

        (await write.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*WriteAllowList*");
        api.SiteLink.RequestsFor(code).Should().BeEmpty();
    }

    [Fact]
    public async Task Each_call_is_counted_for_the_month()
    {
        var operation = "TestCount" + Guid.NewGuid().ToString("N")[..8];
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply(operation, code, SiteLinkXml.Reply(operation));

        for (var i = 0; i < 3; i++) await CallAsync(operation, code, SiteLinkCallKind.Read);

        long calls = 0;
        await api.WithDbAsync(async db => calls = await db.Set<SiteLinkCallMonth>().Where(m => m.Operation == operation).SumAsync(m => m.Calls, Ct));
        calls.Should().Be(3);
    }

    [Theory]
    [InlineData(100, 1, 100, true)]
    [InlineData(101, 2, 100, true)]
    [InlineData(102, 1, 100, false)]
    [InlineData(99, 1, 100, false)]
    [InlineData(500, 1, 0, false)]
    public void The_monthly_alarm_fires_only_when_the_total_crosses_it(long total, int added, long alarm, bool fires) =>
        SiteLinkCallCounter.CrossedAlarm(total, added, alarm).Should().Be(fires);

    [Fact]
    public async Task Without_credentials_a_call_is_unavailable_and_sends_nothing()
    {
        await using var host = api.WithWebHostBuilder(b =>
        {
            b.UseSetting("App:Role", "Api");
            b.UseSetting("SiteLink:CorpPassword", "");
        });
        var code = SitesTestData.NewCode();
        await using var scope = host.Services.CreateAsyncScope();

        var call = () => scope.ServiceProvider.GetRequiredService<SiteLinkClient>().CallAsync("TestRead", code, SiteLinkCallKind.Read, new SiteLinkFields(), Ct);

        await call.Should().ThrowAsync<SiteLinkUnavailableException>();
        api.SiteLink.RequestsFor(code).Should().BeEmpty();
    }

    [Fact]
    public async Task A_user_name_without_the_licence_key_starts_the_app_but_is_not_configured()
    {
        await using var host = api.WithWebHostBuilder(b =>
        {
            b.UseSetting("App:Role", "Api");
            b.UseSetting("SiteLink:CorpUserName", "test-bare-user");
        });
        var code = SitesTestData.NewCode();
        var operation = "TestBareUser" + code;
        await using var scope = host.Services.CreateAsyncScope();

        var call = () => scope.ServiceProvider.GetRequiredService<SiteLinkClient>().CallAsync(operation, code, SiteLinkCallKind.Read, new SiteLinkFields(), Ct);

        (await call.Should().ThrowAsync<SiteLinkUnavailableException>()).Which.Status.Should().Be(503);
        api.SiteLink.RequestsFor(code).Should().BeEmpty();
        var line = (await api.Logs.WaitForAsync(l => l.GetRawText().Contains(operation))).Single();
        line.GetProperty("level").GetString().Should().Be("Warning");
        line.GetRawText().Should().NotContain("test-bare-user");
    }

    [Fact]
    public async Task Writing_to_every_site_is_refused_outside_production()
    {
        await using var host = api.WithWebHostBuilder(b =>
        {
            b.UseSetting("App:Role", "Api");
            b.UseSetting("SiteLink:AllowWritesToAllSites", "true");
        });
        var start = () => host.Services;
        start.Should().Throw<Exception>().WithMessage("*AllowWritesToAllSites*");
    }
}
