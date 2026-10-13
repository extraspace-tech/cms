using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraSpace.Api.Features.Admin;

internal sealed class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable("staff_user");
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Email).HasMaxLength(254);
        builder.HasIndex(s => s.Email).IsUnique();
        builder.Property(s => s.DisplayName).HasMaxLength(200);
        builder.HasOne(s => s.Role).WithMany().HasForeignKey(s => s.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(s => s.Version).IsRowVersion();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    private static readonly DateTime Seeded = new(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc);

    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("role");
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Name).HasMaxLength(100);   // unique on lower(name): raw SQL in the AdminTables migration
        builder.Property(r => r.Version).IsRowVersion();
        builder.HasData(new Role { Id = Role.OwnerId, Name = "Owner", IsSystem = true, CreatedAt = Seeded, UpdatedAt = Seeded });
    }
}

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");
        builder.Property(a => a.Id).ValueGeneratedNever();
        builder.Property(a => a.Action).HasMaxLength(200);
        builder.Property(a => a.EntityType).HasMaxLength(100);
        builder.Property(a => a.Changes).HasColumnType("jsonb");
        builder.HasOne<StaffUser>().WithMany().HasForeignKey(a => a.StaffUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.StaffUserId);
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.OccurredAt).IsDescending();
    }
}
