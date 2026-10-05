using EWasteManagement.API.Features.Processing.Entities;
using EWasteManagement.API.Shared.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EWasteManagement.API.Infrastructure.Persistence.Configurations;

public class InventoryItemConfiguration : IEntityTypeConfiguration<InventoryItem>
{
    public void Configure(EntityTypeBuilder<InventoryItem> builder)
    {
        builder.ToTable("inventory_items", t =>
        {
            t.HasCheckConstraint(
                "ck_inventory_items_status",
                "status IN ('received','sorting','dismantling','classified','readyforsale','exportonly','onhold','recovered')");
            t.HasCheckConstraint(
                "ck_inventory_items_origin_type",
                "origin_type IN ('jobcollection','extrawaste')");
            t.HasCheckConstraint(
                "ck_inventory_items_kind",
                "kind IN ('unit','component','material')");
            t.HasCheckConstraint("ck_inventory_items_quantity", "quantity >= 1");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");

        builder.Property(x => x.OriginType)
            .HasConversion(EnumStringConverter.Create<OriginType>())
            .HasColumnName("origin_type")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion(EnumStringConverter.Create<InventoryStatus>())
            .HasColumnName("status")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion(EnumStringConverter.Create<ItemKind>())
            .HasColumnName("kind")
            .HasMaxLength(20)
            .HasDefaultValue(ItemKind.Unit)
            .IsRequired();

        builder.Property(x => x.JobId).HasColumnName("job_id");
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id");
        builder.Property(x => x.SubmissionItemId).HasColumnName("submission_item_id");
        builder.Property(x => x.Quantity).HasColumnName("quantity").HasDefaultValue(1).IsRequired();
        builder.Property(x => x.ExtraWasteReceiptId).HasColumnName("extra_waste_receipt_id");
        builder.Property(x => x.ParentInventoryItemId).HasColumnName("parent_inventory_item_id");
        builder.Property(x => x.ItemType).HasColumnName("item_type").HasMaxLength(50).IsRequired();
        builder.Property(x => x.VerifiedWeightKg).HasColumnName("verified_weight_kg").HasColumnType("decimal(10,3)");
        builder.Property(x => x.CurrentLocationId).HasColumnName("current_location_id").IsRequired();
        // Each item of a job can only be received once, and a job received as a whole only once.
        // The service pre-checks this too, but these are the real backstop if two requests for the
        // same job land at the same instant.
        builder.HasIndex(x => new { x.JobId, x.SubmissionItemId })
            .IsUnique()
            .HasFilter("job_id IS NOT NULL AND submission_item_id IS NOT NULL")
            .HasDatabaseName("IX_inventory_items_job_id_submission_item_id");
        builder.HasIndex(x => x.JobId)
            .IsUnique()
            .HasFilter("job_id IS NOT NULL AND submission_item_id IS NULL")
            .HasDatabaseName("IX_inventory_items_job_id_whole_job");

        builder.HasOne(x => x.CurrentLocation)
            .WithMany()
            .HasForeignKey(x => x.CurrentLocationId)
            .OnDelete(DeleteBehavior.Restrict); // never let deleting a location cascade-delete inventory

        builder.HasOne(x => x.ParentInventoryItem)
            .WithMany()
            .HasForeignKey(x => x.ParentInventoryItemId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ExtraWasteReceipt)
            .WithMany()
            .HasForeignKey(x => x.ExtraWasteReceiptId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
