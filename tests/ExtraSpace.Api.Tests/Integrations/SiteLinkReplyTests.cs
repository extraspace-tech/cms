using ExtraSpace.Api.Integrations.SiteLink;
using ExtraSpace.Api.Tests.TestSupport;

namespace ExtraSpace.Api.Tests.Integrations;

public sealed class SiteLinkReplyTests
{
    private static readonly TimeZoneInfo Sg = TimeZoneInfo.FindSystemTimeZoneById("Asia/Singapore");

    [Fact]
    public void Rows_are_grouped_by_table_and_absent_columns_stay_absent()
    {
        var reply = SiteLinkReply.Parse(SiteLinkXml.Reply("Op",
            ("Table", SiteLinkXml.Row(("UnitID", "7"), ("dcWebRate", null), ("dcStdRate", "120.50"))),
            ("Table", SiteLinkXml.Row(("UnitID", "8"), ("dcWebRate", ""))),
            ("Table1", SiteLinkXml.Row(("lngLastTimePolled", "638000000000000001")))));

        reply.Table("Table").Should().HaveCount(2);
        var first = reply.Table("Table")[0];
        first.Has("dcWebRate").Should().BeFalse();
        first.Decimal("dcWebRate").Should().BeNull();
        first.Decimal("dcStdRate").Should().Be(120.50m);
        reply.Table("Table")[1].Decimal("dcWebRate").Should().BeNull();   // blank number = absent
        reply.Table("Table1")[0].Long("lngLastTimePolled").Should().Be(638000000000000001L);
        reply.Table("Missing").Should().BeEmpty();
        reply.RetCode.Should().BeNull();
    }

    [Fact]
    public void The_first_RT_row_gives_the_return_code()
    {
        var reply = SiteLinkReply.Parse(SiteLinkXml.Reply("Op", ("RT", SiteLinkXml.Row(("Ret_Code", "-95"), ("Ret_Msg", "Invalid Tenant ID")))));
        reply.RetCode.Should().Be(-95);
        reply.RetMessage.Should().Be("Invalid Tenant ID");
        SiteLinkReply.Parse(SiteLinkXml.Reply("Op", ("RT", SiteLinkXml.Row(("Ret_Code", "abc"))))).RetCode.Should().BeNull();
    }

    [Theory]
    [InlineData("Ret_Code", "0", true)]
    [InlineData("Ret_Code", "-1", true)]
    [InlineData("Ret_Cod", "-1", true)]
    [InlineData("Ret_Code", "1", false)]
    [InlineData("Ret_Code", "abc", false)]
    public void A_return_code_of_zero_or_below_is_a_refusal(string column, string value, bool refused)
    {
        var reply = SiteLinkReply.Parse(SiteLinkXml.Reply("Op", ("RT", SiteLinkXml.Row((column, value), ("Ret_Msg", "test refusal")))));
        var check = () => reply.ThrowIfRefused("Op");

        reply.IsRefused.Should().Be(refused);
        if (refused) check.Should().Throw<SiteLinkException>().Which.ReturnCode.Should().Be(int.Parse(value));
        else check.Should().NotThrow();
    }

    [Fact]
    public void A_reply_without_a_data_set_has_no_tables() =>
        SiteLinkReply.Parse("<Envelope><Body><OpResponse/></Body></Envelope>").Table("Table").Should().BeEmpty();

    [Theory]
    [InlineData("true", true)]
    [InlineData(" True ", true)]
    [InlineData("1", true)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("0", false)]
    public void Booleans_are_read_strictly(string text, bool expected) => SiteLinkValues.ParseBool(text).Should().Be(expected);

    [Fact]
    public void Anything_else_is_not_a_boolean()
    {
        var parse = () => SiteLinkValues.ParseBool("yes-secret-value");
        // The message may reach logs and the sync state: it names the kind, never the raw value.
        parse.Should().Throw<FormatException>().Which.Message.Should().NotContain("yes-secret-value");
    }

    [Fact]
    public void A_bad_number_names_the_column_but_not_the_value()
    {
        var row = SiteLinkReply.Parse(SiteLinkXml.Reply("Op", ("Table", SiteLinkXml.Row(("dcStdRate", "12x5"), ("bRentable", "maybe"))))).Table("Table")[0];

        var number = () => row.Decimal("dcStdRate");
        var flag = () => row.Bool("bRentable");

        number.Should().Throw<FormatException>().Which.Message.Should().Contain("dcStdRate").And.NotContain("12x5");
        flag.Should().Throw<FormatException>().Which.Message.Should().Contain("bRentable").And.NotContain("maybe");
    }

    [Fact]
    public void A_bad_date_is_not_echoed()
    {
        var parse = () => SiteLinkValues.ParseDate("2026-13-45T99:00:00", Sg);
        parse.Should().Throw<FormatException>().Which.Message.Should().NotContain("2026-13-45");
    }

    [Theory]
    [InlineData("2026-09-30T23:59:59", "2026-09-30T15:59:59Z")]          // site-local, no offset
    [InlineData("2026-09-30T23:59:59+08:00", "2026-09-30T15:59:59Z")]    // an offset is taken as sent
    [InlineData("2026-09-30T15:59:59Z", "2026-09-30T15:59:59Z")]
    [InlineData("2026-09-30T23:59:59.123", "2026-09-30T15:59:59.123Z")]
    public void Dates_are_site_local_unless_they_carry_an_offset(string text, string utc) =>
        SiteLinkValues.ParseDate(text, Sg).Should().Be(DateTime.Parse(utc, null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1700-01-01T00:00:00")]
    [InlineData("1799-12-31T23:59:59")]
    public void Blank_and_pre_1800_dates_are_null(string? text) => SiteLinkValues.ParseDate(text, Sg).Should().BeNull();

    [Fact]
    public void A_parsed_date_is_UTC() => SiteLinkValues.ParseDate("2026-09-30T23:59:59", Sg)!.Value.Kind.Should().Be(DateTimeKind.Utc);
}
