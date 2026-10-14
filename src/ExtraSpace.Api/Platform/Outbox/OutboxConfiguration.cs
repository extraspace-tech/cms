using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ExtraSpace.Api.Platform.Outbox;

internal sealed class OutboxConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_message");
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Kind).HasMaxLength(100);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.IdempotencyKey).HasMaxLength(200);
        builder.HasIndex(m => m.IdempotencyKey).IsUnique();
        builder.Property(m => m.CorrelationId).HasMaxLength(64);
        builder.Property(m => m.LastError).HasMaxLength(1000);
        builder.Property(m => m.ResolutionNote).HasMaxLength(1000);
        // The dispatcher's only query: due rows that are neither done nor failed.
        builder.HasIndex(m => m.AvailableAt).HasFilter("processed_at IS NULL AND failed_at IS NULL");
    }
}
