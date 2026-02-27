using Opossum.Core;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.InvoiceCreation;

/// <summary>
/// Materialized view of a single invoice.
/// </summary>
public sealed record InvoiceReadModel(
    int InvoiceNumber,
    Guid CustomerId,
    decimal Amount,
    DateTimeOffset IssuedAt);

[ProjectionDefinition("Invoice")]
public sealed class InvoiceProjection : IProjectionDefinition<InvoiceReadModel>
{
    public string ProjectionName => "Invoice";

    public string[] EventTypes => [nameof(InvoiceCreatedEvent)];

    public string KeySelector(SequencedEvent evt)
    {
        var numberTag = evt.Event.Tags.FirstOrDefault(t => t.Key == "invoiceNumber")
            ?? throw new InvalidOperationException(
                $"Event {evt.Event.EventType} at position {evt.Position} is missing invoiceNumber tag");

        return numberTag.Value;
    }

    public InvoiceReadModel? Apply(InvoiceReadModel? current, SequencedEvent evt) =>
        evt.Event.Event switch
        {
            InvoiceCreatedEvent created => new InvoiceReadModel(
                InvoiceNumber: created.InvoiceNumber,
                CustomerId: created.CustomerId,
                Amount: created.Amount,
                IssuedAt: created.IssuedAt),
            _ => current
        };
}
