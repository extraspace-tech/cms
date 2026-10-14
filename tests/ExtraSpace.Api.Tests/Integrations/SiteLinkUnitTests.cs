using ExtraSpace.Api.Integrations.SiteLink;
using ExtraSpace.Api.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace ExtraSpace.Api.Tests.Integrations;

public sealed class SiteLinkUnitTests(ApiFactory api)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly TimeZoneInfo Sg = TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore");

    private static SiteLinkUnit Map(params (string, string?)[] columns) =>
        SiteLinkUnit.From(SiteLinkReply.Parse(SiteLinkXml.Reply("Op", ("Table", SiteLinkXml.Row(columns)))).Table("Table")[0], Sg);

    [Fact]
    public void A_full_row_maps_every_field()
    {
        var unit = Map(("UnitID", "101"), ("UnitTypeID", "7"), ("sUnitName", " A101 "), ("sTypeName", "Walk In"),
            ("dcWidth", "7.5"), ("dcLength", "10"), ("bClimate", "true"), ("bInside", "false"), ("bPower", "true"), ("bAlarm", "false"),
            ("iADA", "1"), ("bMobile", "false"), ("bRented", "true"), ("bRentable", "false"), ("bWaitingListReserved", "true"),
            ("iFloor", "2"), ("sUnitDesc", "near lift"), ("dcStdRate", "120.00"), ("dcWebRate", "100.00"), ("dcBoardRate", "130"),
            ("dcPushRate", "95.5"), ("dcPreferredRate", "90"), ("dcStdSecDep", "50"), ("dDeleted", "2026-09-30T08:00:00"));

        unit.Should().BeEquivalentTo(new SiteLinkUnit(
            UnitId: 101, UnitTypeId: 7, UnitName: "A101", TypeName: "Walk In", Width: 7.5m, Length: 10m,
            Climate: true, Inside: false, Power: true, Alarm: false, Ada: true, Mobile: false,
            Rented: true, Rentable: false, WaitingListReserved: true,
            Floor: 2, Description: "near lift",
            StandardRate: 120.00m, WebRate: 100.00m, BoardRate: 130m, PushRate: 95.5m, PreferredRate: 90m, StandardSecurityDeposit: 50m,
            DeletedAt: new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc)));
        unit.AreaSqft.Should().Be(75m);   // C8
    }

    [Fact]
    public void The_area_is_rounded_as_stored_and_absent_without_both_sides()
    {
        Map(("UnitID", "104"), ("sUnitName", "D1"), ("dcWidth", "3.333"), ("dcLength", "3")).AreaSqft.Should().Be(10.00m);
        Map(("UnitID", "105"), ("sUnitName", "D2"), ("dcWidth", "5")).AreaSqft.Should().BeNull();
    }

    [Fact]
    public void Absent_and_false_values_map_strictly()
    {
        var unit = Map(("UnitID", "102"), ("sUnitName", "B1"), ("bRented", "false"), ("dcWebRate", ""), ("dDeleted", "1700-01-01T00:00:00"));

        unit.Rented.Should().BeFalse();                // C9: "false" is false
        unit.Rentable.Should().BeNull();               // absent: the refresh keeps the stored flag (C10 default only on insert)
        unit.WaitingListReserved.Should().BeNull();
        unit.Climate.Should().BeNull();
        unit.Ada.Should().BeNull();
        unit.WebRate.Should().BeNull();                // blank = absent: the refresh keeps the stored rate
        unit.StandardRate.Should().BeNull();
        unit.Description.Should().BeNull();
        unit.Floor.Should().BeNull();
        unit.DeletedAt.Should().BeNull();              // SiteLink's null date
        unit.Width.Should().BeNull();
        unit.Length.Should().BeNull();
        unit.AreaSqft.Should().BeNull();               // a missing size must not remap the unit
        unit.UnitTypeId.Should().BeNull();
        unit.TypeName.Should().BeNull();               // a missing type name must not change the storage type
    }

    [Fact]
    public void A_deleted_date_is_site_local() =>
        Map(("UnitID", "103"), ("sUnitName", "C1"), ("dDeleted", "2026-10-01T10:00:00")).DeletedAt
            .Should().Be(new DateTime(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc));

    [Theory]
    [InlineData(null, "A1")]
    [InlineData("5", null)]
    [InlineData("5", " ")]
    public void A_row_without_an_id_or_a_name_is_unreadable(string? unitId, string? name)
    {
        var map = () => Map(("UnitID", unitId), ("sUnitName", name));
        map.Should().Throw<FormatException>();
    }

    [Fact]
    public async Task UnitsInformation_sends_the_cursor_and_reads_the_next_one()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("UnitsInformation_v3", code, SiteLinkXml.Reply("UnitsInformation_v3",
            ("Table", SiteLinkXml.Row(("UnitID", "1"), ("sUnitName", "A1"))),
            ("Table", SiteLinkXml.Row(("UnitID", "2"), ("sUnitName", "A2"))),
            ("Table1", SiteLinkXml.Row(("lngLastTimePolled", "638900000000000000"), ("dLatestUpdated", "2026-10-14T03:00:00")))));
        await using var scope = api.Services.CreateAsyncScope();
        var client = scope.ServiceProvider.GetRequiredService<SiteLinkClient>();

        var page = await client.UnitsInformationAsync(code, 638800000000000000, includeExcludedFromWebsite: false, Sg, Ct);

        page.Units.Select(u => u.UnitName).Should().Equal("A1", "A2");
        page.NextTicks.Should().Be(638900000000000000);
        var request = api.SiteLink.RequestsFor(code).Single();
        request.Names.Skip(4).Should().Equal("lngLastTimePolled", "bReturnExcludedFromWebsiteUnits");
        request.Fields["lngLastTimePolled"].Should().Be("638800000000000000");
        request.Fields["bReturnExcludedFromWebsiteUnits"].Should().Be("false");
    }

    [Fact]
    public async Task A_first_pull_sends_zero_and_a_missing_cursor_row_is_null()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("UnitsInformation_v3", code, SiteLinkXml.Reply("UnitsInformation_v3", ("Table", SiteLinkXml.Row(("UnitID", "1"), ("sUnitName", "A1")))));
        await using var scope = api.Services.CreateAsyncScope();

        var page = await scope.ServiceProvider.GetRequiredService<SiteLinkClient>().UnitsInformationAsync(code, null, true, Sg, Ct);

        page.NextTicks.Should().BeNull();
        api.SiteLink.RequestsFor(code).Single().Fields["lngLastTimePolled"].Should().Be("0");
        api.SiteLink.RequestsFor(code).Single().Fields["bReturnExcludedFromWebsiteUnits"].Should().Be("true");
    }

    [Fact]
    public async Task The_price_list_gives_the_currency_decimals()
    {
        var code = SitesTestData.NewCode();
        api.SiteLink.Reply("UnitTypePriceList_v2", code, SiteLinkXml.Reply("UnitTypePriceList_v2",
            ("Table", SiteLinkXml.Row(("SiteID", "9"), ("iCurrencyDecimals", "2"), ("UnitTypeID", "7")))));
        await using var scope = api.Services.CreateAsyncScope();

        (await scope.ServiceProvider.GetRequiredService<SiteLinkClient>().CurrencyDecimalsAsync(code, Ct)).Should().Be(2);
        api.SiteLink.RequestsFor(code).Single().Names.Should().HaveCount(4);   // the logon block only
    }
}
