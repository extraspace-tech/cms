using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraSpace.Api.Features.Identity;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customer");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.HasIndex(c => c.Email).IsUnique();
        builder.Property(c => c.FirstName).HasMaxLength(100);
        builder.Property(c => c.LastName).HasMaxLength(100);
        builder.Property(c => c.Phone).HasMaxLength(32);
    }
}

internal sealed class CustomerLoginCodeConfiguration : IEntityTypeConfiguration<CustomerLoginCode>
{
    public void Configure(EntityTypeBuilder<CustomerLoginCode> builder)
    {
        builder.ToTable("customer_login_code");
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Email).HasMaxLength(254);
        builder.HasIndex(c => c.Email).IsUnique();
        builder.Property(c => c.CodeHash).HasMaxLength(64);
        builder.Property(c => c.RequestIp).HasMaxLength(45);
        builder.HasIndex(c => c.ExpiresAt);
    }
}

internal sealed class CustomerRefreshTokenConfiguration : IEntityTypeConfiguration<CustomerRefreshToken>
{
    public void Configure(EntityTypeBuilder<CustomerRefreshToken> builder)
    {
        builder.ToTable("customer_refresh_token");
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.TokenHash).HasMaxLength(64);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.HasIndex(t => t.ExpiresAt);
        builder.Property(t => t.UserAgent).HasMaxLength(256);
        builder.Property(t => t.Ip).HasMaxLength(45);
        builder.HasOne<Customer>().WithMany().HasForeignKey(t => t.CustomerId).OnDelete(DeleteBehavior.Cascade);
    }
}
