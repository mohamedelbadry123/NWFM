namespace Workflow.Infrastructure.Persistence.Configurations;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Workflow.Domain.Entities;

public sealed class WorkflowFormReceiptReservationConfiguration : IEntityTypeConfiguration<WorkflowFormReceiptReservation>
{
    public void Configure(EntityTypeBuilder<WorkflowFormReceiptReservation> builder)
    {
        builder.ToTable("form_receipt_reservations", "Workflow");
        builder.HasKey(x => x.ReceiptKey);
        builder.HasIndex(x => x.WorkItemId);
    }
}
