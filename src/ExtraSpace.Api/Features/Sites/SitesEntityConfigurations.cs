using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraSpace.Api.Features.Sites;

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("site", t =>
        {
            t.HasCheckConstraint("ck_site_country", "country IN ('SG', 'MY', 'KR')");
            t.HasCheckConstraint("ck_site_currency", "currency IN ('SGD', 'MYR', 'KRW')");
            t.HasCheckConstraint("ck_site_country_currency", "(country, currency) IN (('SG', 'SGD'), ('MY', 'MYR'), ('KR', 'KRW'))");
            t.HasCheckConstraint("ck_site_tax_percentage", "tax_percentage >= 0 AND tax_percentage <= 100");
        });
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.LocationCode).HasMaxLength(20);
        builder.HasIndex(s => s.LocationCode).IsUnique();
        builder.Property(s => s.SiteLinkSiteId).HasColumnName("sitelink_site_id");
        builder.HasIndex(s => s.SiteLinkSiteId).IsUnique().HasDatabaseName("ix_site_sitelink_site_id");   // NULLs never clash in PostgreSQL
        builder.Property(s => s.Name).HasMaxLength(200);
        builder.Property(s => s.DisplayName).HasMaxLength(200);
        builder.Property(s => s.Description).HasMaxLength(4000);
        builder.Property(s => s.HeroImageUrl).HasMaxLength(1000);
        builder.Property(s => s.Address1).HasMaxLength(300);
        builder.Property(s => s.City).HasMaxLength(100);
        builder.Property(s => s.Region).HasMaxLength(100);
        builder.Property(s => s.PostalCode).HasMaxLength(20);
        builder.Property(s => s.Country).HasMaxLength(2).IsFixedLength();
        builder.Property(s => s.Phone).HasMaxLength(50);
        builder.Property(s => s.Latitude).HasPrecision(9, 6);
        builder.Property(s => s.Longitude).HasPrecision(9, 6);
        builder.Property(s => s.TimeZone).HasMaxLength(64);
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(s => s.TaxPercentage).HasPrecision(5, 2);
        builder.Property(s => s.TaxLabel).HasMaxLength(20);
        builder.Property(s => s.FacilityType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.StripeAccountId).HasMaxLength(100);
        builder.Property(s => s.Version).IsRowVersion();
    }
}

internal sealed class StorageTypeConfiguration : IEntityTypeConfiguration<StorageType>
{
    private static readonly DateTime Seeded = new(2026, 10, 14, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<StorageType> builder)
    {
        builder.ToTable("storage_type");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.Key).HasMaxLength(50);
        builder.HasIndex(t => t.Key).IsUnique();
        builder.Property(t => t.Name).HasMaxLength(100);
        builder.Property(t => t.DisplayName).HasMaxLength(100);
        builder.Property(t => t.IconPath).HasMaxLength(500);
        builder.Property(t => t.ImagePath).HasMaxLength(500);
        builder.Property(t => t.Version).IsRowVersion();
        builder.HasData(
            new StorageType { Id = StorageType.SelfStorageId, Key = StorageType.SelfStorageKey, Name = "Self Storage", SortOrder = 1, Active = true, CreatedAt = Seeded, UpdatedAt = Seeded },
            new StorageType { Id = StorageType.WorkspaceId, Key = StorageType.WorkspaceKey, Name = "Workspace", SortOrder = 2, Active = false, CreatedAt = Seeded, UpdatedAt = Seeded });
    }
}

internal sealed class UnitSizeConfiguration : IEntityTypeConfiguration<UnitSize>
{
    public void Configure(EntityTypeBuilder<UnitSize> builder)
    {
        builder.ToTable("unit_size", t => t.HasCheckConstraint("ck_unit_size_range",
            "min_area_sqft IS NULL OR max_area_sqft IS NULL OR min_area_sqft <= max_area_sqft"));
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Key).HasMaxLength(50);
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Label).HasMaxLength(100);
        builder.Property(s => s.DisplayName).HasMaxLength(100);
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.UnitGroup).HasMaxLength(100);
        builder.Property(s => s.ImagePath).HasMaxLength(500);
        builder.Property(s => s.MinAreaSqft).HasPrecision(10, 2);
        builder.Property(s => s.MaxAreaSqft).HasPrecision(10, 2);
        builder.HasOne<StorageType>().WithMany().HasForeignKey(s => s.StorageTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.Version).IsRowVersion();
    }
}

internal sealed class UnitConfiguration : IEntityTypeConfiguration<Unit>
{
    public void Configure(EntityTypeBuilder<Unit> builder)
    {
        builder.ToTable("unit");
        builder.Property(u => u.Id).ValueGeneratedNever();
        builder.HasOne(u => u.Site).WithMany().HasForeignKey(u => u.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.StorageType).WithMany().HasForeignKey(u => u.StorageTypeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(u => u.UnitSize).WithMany().HasForeignKey(u => u.UnitSizeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(u => u.SiteLinkUnitId).HasColumnName("sitelink_unit_id");
        builder.Property(u => u.SiteLinkUnitTypeId).HasColumnName("sitelink_unit_type_id");
        builder.HasIndex(u => new { u.SiteId, u.SiteLinkUnitId }).IsUnique().HasDatabaseName("ix_unit_site_id_sitelink_unit_id");
        builder.HasIndex(u => new { u.SiteId, u.UnitName });   // S1-2 matches ledgers by (site, unit name)
        builder.Property(u => u.UnitName).HasMaxLength(100);
        builder.Property(u => u.Description).HasMaxLength(1000);
        builder.Property(u => u.Width).HasPrecision(10, 2);
        builder.Property(u => u.Length).HasPrecision(10, 2);
        builder.Property(u => u.Available)
            .HasComputedColumnSql("rentable AND NOT rented AND NOT waiting_list_reserved AND deleted_at IS NULL", stored: true);
    }
}

internal sealed class UnitSyncStateConfiguration : IEntityTypeConfiguration<UnitSyncState>
{
    public void Configure(EntityTypeBuilder<UnitSyncState> builder)
    {
        builder.ToTable("unit_sync_state");
        builder.HasKey(s => s.SiteId);
        builder.Property(s => s.SiteId).ValueGeneratedNever();
        builder.HasOne<Site>().WithOne().HasForeignKey<UnitSyncState>(s => s.SiteId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(s => s.LastError).HasMaxLength(1000);
    }
}
