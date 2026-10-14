using ExtraSpace.Api.Features.Sites;
using ExtraSpace.Api.Integrations.SiteLink;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExtraSpace.Api.Tests.Features.Sites;

public sealed class UnitRefreshTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private const string Op = "UnitsInformation_v3";

    private async Task<UnitRefreshResult> RefreshAsync(Guid siteId, UnitRefreshMode mode)
    {
        await using var scope = api.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<UnitRefreshService>().RefreshAsync(siteId, mode, Ct);
    }

    private async Task<(List<Unit> Units, UnitSyncState? State)> ReadAsync(Guid siteId)
    {
        List<Unit> units = [];
        UnitSyncState? state = null;
        await api.WithDbAsync(async db =>
        {
            units = await db.Set<Unit>().AsNoTracking().Where(u => u.SiteId == siteId).OrderBy(u => u.SiteLinkUnitId).ToListAsync(Ct);
            state = await db.Set<UnitSyncState>().AsNoTracking().SingleOrDefaultAsync(s => s.SiteId == siteId, Ct);
        });
        return (units, state);
    }

    // The holder of the site's refresh lock (a bigint advisory key is stored as classid = high half, objid = low half).
    private const string LockHolderSql = """
        SELECT a.state, a.xact_start IS NULL FROM pg_locks AS l JOIN pg_stat_activity AS a ON a.pid = l.pid
        WHERE l.locktype = 'advisory' AND l.granted AND l.objsubid = 1
          AND ((l.classid::bigint << 32) | l.objid::bigint) = hashtextextended(@key, 0)
        """;

    private List<(string State, bool NoTransaction)> LockHolders(Guid siteId)
    {
        using var connection = new NpgsqlConnection(api.ConnectionString);
        connection.Open();
        using var command = new NpgsqlCommand(LockHolderSql, connection);
        command.Parameters.AddWithValue("key", UnitRefreshService.LockKey(siteId));
        using var reader = command.ExecuteReader();
        var holders = new List<(string, bool)>();
        while (reader.Read()) holders.Add((reader.GetString(0), reader.GetBoolean(1)));
        return holders;
    }

    /// <summary>A full unit row; <paramref name="more"/> replaces a column, and a null value leaves it out of the reply.</summary>
    private static Dictionary<string, string?> UnitRow(int id, string name, params (string Column, string? Value)[] more)
    {
        var row = SiteLinkXml.Row(("UnitID", id.ToString()), ("UnitTypeID", "7"), ("sUnitName", name), ("sTypeName", "Walk In"),
            ("dcWidth", "5"), ("dcLength", "10"), ("bRentable", "true"), ("bRented", "false"));
        foreach (var (column, value) in more) row[column] = value;
        return row;
    }

    [Fact]
    public async Task The_first_refresh_pulls_every_unit_and_saves_the_cursor()
    {
        var site = await api.CreateSiteAsync();
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op,
            ("Table", UnitRow(1, "A1", ("dcWebRate", "100"))),
            ("Table", UnitRow(2, "A2", ("bRented", "true"))),
            ("Table", UnitRow(3, "W1", ("sTypeName", " BizPlus "))),
            ("Table1", SiteLinkXml.Row(("lngLastTimePolled", "638900000000000001")))));

        var result = await RefreshAsync(site.Id, UnitRefreshMode.Incremental);   // no cursor yet: a full pull

        result.Should().Be(new UnitRefreshResult(UnitRefreshOutcome.Refreshed, 3));
        var request = api.SiteLink.RequestsFor(site.LocationCode, Op).Single();
        request.Fields["lngLastTimePolled"].Should().Be("0");
        request.Fields["bReturnExcludedFromWebsiteUnits"].Should().Be("true");
        var (units, state) = await ReadAsync(site.Id);
        units.Select(u => (u.UnitName, u.Available)).Should().Equal(("A1", true), ("A2", false), ("W1", true));
        units[0].WebRate.Should().Be(100m);
        units[0].AreaSqft.Should().Be(50m);
        units[0].StorageTypeId.Should().Be(StorageType.SelfStorageId);
        units[2].StorageTypeId.Should().Be(StorageType.WorkspaceId);
        units.Should().AllSatisfy(u => u.LastSyncedAt.Should().NotBeNull());
        state!.LastTimePolledTicks.Should().Be(638900000000000001);
        state.LastSyncedAt.Should().NotBeNull();
        state.LastError.Should().BeNull();
        LockHolders(site.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task SiteLink_is_called_with_the_site_lock_held_and_no_transaction_open()
    {
        // The database ends a session idle in a transaction for 60 s, and one SiteLink read may take that long.
        var site = await api.CreateSiteAsync();
        List<(string State, bool NoTransaction)>? during = null;
        api.SiteLink.Reply(Op, site.LocationCode, _ =>
        {
            during = LockHolders(site.Id);
            return FakeSiteLink.Ok(SiteLinkXml.Reply(Op, ("Table", UnitRow(1, "A1"))));
        });

        (await RefreshAsync(site.Id, UnitRefreshMode.Incremental)).Outcome.Should().Be(UnitRefreshOutcome.Refreshed);

        during.Should().Equal(("idle", true));
        LockHolders(site.Id).Should().BeEmpty();
        (await ReadAsync(site.Id)).Units.Should().ContainSingle();
    }

    [Fact]
    public async Task An_incremental_refresh_keeps_absent_columns_and_marks_deleted_units()
    {
        var site = await api.CreateSiteAsync();
        var unit = await api.CreateUnitAsync(site.Id, u =>
        {
            u.SiteLinkUnitId = 41;
            u.WebRate = 50m;
            u.Description = "old";
        });
        await api.WithDbAsync(async db =>
        {
            db.Add(new UnitSyncState { SiteId = site.Id, LastTimePolledTicks = 638800000000000000 });
            await db.SaveChangesAsync(Ct);
        });
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op,
            ("Table", UnitRow(41, unit.UnitName, ("dDeleted", "2026-10-01T10:00:00")))));   // no dcWebRate, no sUnitDesc, no Table1

        await RefreshAsync(site.Id, UnitRefreshMode.Incremental);

        var request = api.SiteLink.RequestsFor(site.LocationCode, Op).Single();
        request.Fields["lngLastTimePolled"].Should().Be("638800000000000000");
        request.Fields["bReturnExcludedFromWebsiteUnits"].Should().Be("true");   // every poll: our copy holds all units
        var (units, state) = await ReadAsync(site.Id);
        var row = units.Single();
        row.WebRate.Should().Be(50m);
        row.Description.Should().Be("old");
        row.DeletedAt.Should().Be(new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc));
        row.Available.Should().BeFalse();
        state!.LastTimePolledTicks.Should().Be(638800000000000000);   // no Table1: the cursor stays
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task An_incremental_row_without_rented_width_or_type_name_keeps_the_stored_values(string? typeName)
    {
        var site = await api.CreateSiteAsync();
        var unit = await api.CreateUnitAsync(site.Id, u =>
        {
            u.SiteLinkUnitId = 51;
            u.SiteLinkUnitTypeId = 9;
            u.Rented = true;
            u.StorageTypeId = StorageType.WorkspaceId;
        });
        await api.WithDbAsync(async db =>
        {
            db.Add(new UnitSyncState { SiteId = site.Id, LastTimePolledTicks = 638800000000000000 });
            await db.SaveChangesAsync(Ct);
        });
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op, ("Table", UnitRow(51, unit.UnitName,
            ("UnitTypeID", null), ("bRented", null), ("dcWidth", null), ("sTypeName", typeName)))));

        await RefreshAsync(site.Id, UnitRefreshMode.Incremental);

        var row = (await ReadAsync(site.Id)).Units.Single();
        row.SiteLinkUnitTypeId.Should().Be(9);
        row.Rented.Should().BeTrue();
        row.Available.Should().BeFalse();
        (row.Width, row.Length, row.AreaSqft).Should().Be((5m, 10m, 50m));
        row.StorageTypeId.Should().Be(StorageType.WorkspaceId);
    }

    [Fact]
    public async Task A_new_unit_from_a_row_without_flags_or_sizes_gets_the_defaults()
    {
        var site = await api.CreateSiteAsync();
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op, ("Table", UnitRow(1, "N1",
            ("UnitTypeID", null), ("sTypeName", null), ("dcWidth", null), ("dcLength", null), ("bRentable", null), ("bRented", null)))));

        await RefreshAsync(site.Id, UnitRefreshMode.Incremental);

        var row = (await ReadAsync(site.Id)).Units.Single();
        row.Rentable.Should().BeTrue();
        row.Rented.Should().BeFalse();
        row.WaitingListReserved.Should().BeFalse();
        row.Available.Should().BeTrue();
        (row.SiteLinkUnitTypeId, row.Width, row.Length, row.AreaSqft).Should().Be((0, 0m, 0m, 0m));
        row.StorageTypeId.Should().Be(StorageType.SelfStorageId);
    }

    [Fact]
    public async Task A_full_refresh_with_no_units_fails_and_keeps_the_cursor()
    {
        var site = await api.CreateSiteAsync();
        await api.WithDbAsync(async db =>
        {
            db.Add(new UnitSyncState { SiteId = site.Id, LastTimePolledTicks = 638800000000000000 });
            await db.SaveChangesAsync(Ct);
        });
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op));

        var refresh = () => RefreshAsync(site.Id, UnitRefreshMode.Full);

        await refresh.Should().ThrowAsync<UnitRefreshException>();
        var (units, state) = await ReadAsync(site.Id);
        units.Should().BeEmpty();
        state!.LastTimePolledTicks.Should().Be(638800000000000000);
        state.LastError.Should().Be("SiteLink returned no units for a full refresh.");
        state.LastAttemptAt.Should().NotBeNull();
        LockHolders(site.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failing_row_rolls_back_the_whole_batch()
    {
        var site = await api.CreateSiteAsync();
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op,
            ("Table", UnitRow(1, "GOOD")),
            ("Table", UnitRow(2, new string('X', 101))),   // longer than unit_name allows: the save fails
            ("Table1", SiteLinkXml.Row(("lngLastTimePolled", "638900000000000009")))));

        var refresh = () => RefreshAsync(site.Id, UnitRefreshMode.Incremental);

        await refresh.Should().ThrowAsync<DbUpdateException>();
        var (units, state) = await ReadAsync(site.Id);
        units.Should().BeEmpty();
        state!.LastTimePolledTicks.Should().BeNull();
        state.LastError.Should().Be("Saving the units failed.");
        LockHolders(site.Id).Should().BeEmpty();
    }

    [Fact]
    public async Task A_second_refresh_of_the_same_site_is_skipped()
    {
        var site = await api.CreateSiteAsync();
        await using var holder = new NpgsqlConnection(api.ConnectionString);
        await holder.OpenAsync(Ct);
        await using var transaction = await holder.BeginTransactionAsync(Ct);
        await using (var lockCommand = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", holder, transaction))
        {
            lockCommand.Parameters.AddWithValue("key", $"unit-refresh:{site.Id}");
            await lockCommand.ExecuteNonQueryAsync(Ct);
        }

        var result = await RefreshAsync(site.Id, UnitRefreshMode.Incremental);

        result.Outcome.Should().Be(UnitRefreshOutcome.Skipped);
        api.SiteLink.RequestsFor(site.LocationCode).Should().BeEmpty();
    }

    [Fact]
    public async Task The_nightly_run_checks_the_currency_decimals()
    {
        var site = await api.CreateSiteAsync("SG");
        api.SiteLink.Reply("UnitTypePriceList_v2", site.LocationCode, SiteLinkXml.Reply("UnitTypePriceList_v2",
            ("Table", SiteLinkXml.Row(("iCurrencyDecimals", "0")))));

        var refresh = () => RefreshAsync(site.Id, UnitRefreshMode.Nightly);

        (await refresh.Should().ThrowAsync<UnitRefreshException>()).WithMessage("*0 currency decimals*SGD has 2*");
        api.SiteLink.RequestsFor(site.LocationCode, Op).Should().BeEmpty();
        var failed = (await ReadAsync(site.Id)).State!;
        failed.LastError.Should().Be("SiteLink uses 0 currency decimals at this site, but SGD has 2.");
        failed.LastAttemptAt.Should().NotBeNull();
        failed.LastSyncedAt.Should().BeNull();

        api.SiteLink.Reply("UnitTypePriceList_v2", site.LocationCode, SiteLinkXml.Reply("UnitTypePriceList_v2",
            ("Table", SiteLinkXml.Row(("iCurrencyDecimals", "2")))));
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op, ("Table", UnitRow(1, "A1"))));
        (await RefreshAsync(site.Id, UnitRefreshMode.Nightly)).Outcome.Should().Be(UnitRefreshOutcome.Refreshed);
        (await ReadAsync(site.Id)).State!.LastError.Should().BeNull();
    }

    [Fact]
    public async Task A_nightly_run_without_currency_decimals_in_the_reply_goes_ahead()
    {
        var site = await api.CreateSiteAsync("KR");
        api.SiteLink.Reply("UnitTypePriceList_v2", site.LocationCode, SiteLinkXml.Reply("UnitTypePriceList_v2"));   // no Table at all
        api.SiteLink.Reply(Op, site.LocationCode, SiteLinkXml.Reply(Op, ("Table", UnitRow(1, "A1"))));

        (await RefreshAsync(site.Id, UnitRefreshMode.Nightly)).Outcome.Should().Be(UnitRefreshOutcome.Refreshed);

        api.SiteLink.RequestsFor(site.LocationCode, "UnitTypePriceList_v2").Should().ContainSingle();
        (await ReadAsync(site.Id)).State!.LastError.Should().BeNull();
    }

    public static TheoryData<Exception, string> Failures => new()
    {
        { new SiteLinkException("UnitsInformation_v3", -7, "test vendor text"), "SiteLink refused the call (code -7)." },
        { new SiteLinkUnavailableException("UnitsInformation_v3", "HTTP 500"), "SiteLink could not be reached." },
        { new UnitRefreshException("SiteLink returned no units for a full refresh."), "SiteLink returned no units for a full refresh." },
        { new FormatException("A unit row has no UnitID."), "SiteLink sent a value we could not read: A unit row has no UnitID." },
        { new DbUpdateException("x", new PostgresException("test detail", "ERROR", "ERROR", PostgresErrorCodes.StringDataRightTruncation)),
            "Saving the units failed." },
        { new PostgresException("test detail", "FATAL", "FATAL", "25P03"), "The database could not complete the refresh." },
        { new NpgsqlException("test detail"), "The database could not complete the refresh." },
        { new InvalidOperationException("test detail"), "InvalidOperationException" },
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void Staff_see_our_own_words_for_a_failure(Exception exception, string expected) =>
        UnitRefreshService.Describe(exception).Should().Be(expected);

    [Theory]
    [InlineData("Asia/Singapore", "2026-10-14T19:00:00Z", true)]     // 03:00 SGT
    [InlineData("Asia/Singapore", "2026-10-14T19:59:00Z", true)]
    [InlineData("Asia/Singapore", "2026-10-14T18:59:00Z", false)]
    [InlineData("Asia/Kuala_Lumpur", "2026-10-14T19:30:00Z", true)]
    [InlineData("Asia/Seoul", "2026-10-14T18:00:00Z", true)]         // 03:00 KST
    [InlineData("Asia/Seoul", "2026-10-14T19:00:00Z", false)]
    public void Each_site_is_due_at_3am_in_its_own_zone(string zone, string utc, bool due) =>
        UnitRefreshJob.IsDue(zone, DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture)).Should().Be(due);
}
