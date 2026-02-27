namespace Opossum.Samples.CourseManagement.Events;

/// <summary>
/// Raised whenever a new invoice is created with a unique, consecutive invoice number.
/// The unbroken sequence is enforced by the DCB read → decide → append pattern using
/// <see cref="Opossum.IEventStore.ReadLastAsync"/> — see <c>CreateInvoiceCommandHandler</c>.
/// </summary>
public sealed record InvoiceCreatedEvent(
    int InvoiceNumber,
    Guid CustomerId,
    decimal Amount,
    DateTimeOffset IssuedAt) : IEvent;
