namespace ExtraSpace.Api.Integrations.SiteLink;

/// <summary>
/// One <c>UnitsInformation_v3</c> row (demo 03 §4.1.2, booleans strict). A null property means the column was absent or a number was
/// blank, so the caller keeps its stored value (S1 spec §6.5, §6.7). The defaults for an absent column (type 0, type name "", size 0,
/// self-storage, rentable true, the other flags false) apply only when the refresh inserts a new unit.
/// </summary>
/// <param name="Description">Blank text is present and clears the stored text; only a missing column is null.</param>
internal sealed record SiteLinkUnit(
    int UnitId, int? UnitTypeId, string UnitName, string? TypeName, decimal? Width, decimal? Length,
    bool? Climate, bool? Inside, bool? Power, bool? Alarm, bool? Ada, bool? Mobile,
    bool? Rented, bool? Rentable, bool? WaitingListReserved,
    int? Floor, string? Description,
    decimal? StandardRate, decimal? WebRate, decimal? BoardRate, decimal? PushRate, decimal? PreferredRate, decimal? StandardSecurityDeposit,
    DateTime? DeletedAt)
{
    // Rounded as stored (numeric(12,2)), so an unchanged unit does not look changed on every refresh.
    public decimal? AreaSqft => Width is { } width && Length is { } length ? Math.Round(width * length, 2, MidpointRounding.AwayFromZero) : null;

    public static SiteLinkUnit From(SiteLinkRow row, TimeZoneInfo zone) => new(
        UnitId: row.Int("UnitID") ?? throw new FormatException("A unit row has no UnitID."),
        UnitTypeId: row.Int("UnitTypeID"),
        UnitName: row.Text("sUnitName")?.Trim() is { Length: > 0 } name ? name : throw new FormatException("A unit row has no sUnitName."),
        TypeName: row.Text("sTypeName"),
        Width: row.Decimal("dcWidth"),
        Length: row.Decimal("dcLength"),
        Climate: row.Bool("bClimate"),
        Inside: row.Bool("bInside"),
        Power: row.Bool("bPower"),
        Alarm: row.Bool("bAlarm"),
        Ada: row.Int("iADA") is { } ada ? ada == 1 : null,
        Mobile: row.Bool("bMobile"),
        Rented: row.Bool("bRented"),
        Rentable: row.Bool("bRentable"),
        WaitingListReserved: row.Bool("bWaitingListReserved"),
        Floor: row.Int("iFloor"),
        Description: row.Text("sUnitDesc"),
        StandardRate: row.Decimal("dcStdRate"),
        WebRate: row.Decimal("dcWebRate"),
        BoardRate: row.Decimal("dcBoardRate"),
        PushRate: row.Decimal("dcPushRate"),
        PreferredRate: row.Decimal("dcPreferredRate"),
        StandardSecurityDeposit: row.Decimal("dcStdSecDep"),
        DeletedAt: row.UtcDate("dDeleted", zone));
}

internal sealed record SiteLinkUnitsPage(IReadOnlyList<SiteLinkUnit> Units, long? NextTicks);
