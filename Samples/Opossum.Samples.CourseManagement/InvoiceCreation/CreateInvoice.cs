using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.InvoiceCreation;

public sealed record CreateInvoiceRequest(Guid CustomerId, decimal Amount);
public sealed record CreateInvoiceCommand(Guid CustomerId, decimal Amount);

public static class CreateInvoiceEndpoint
{
    public static void MapCreateInvoiceEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/invoices", async (
            [FromBody] CreateInvoiceRequest request,
            [FromServices] IMediator mediator) =>
        {
            if (request.CustomerId == Guid.Empty)
                return Results.BadRequest("'customerId' must be a valid non-empty GUID.");

            if (request.Amount <= 0)
                return Results.BadRequest("'amount' must be greater than zero.");

            var command = new CreateInvoiceCommand(
                CustomerId: request.CustomerId,
                Amount: request.Amount);

            var commandResult = await mediator.InvokeAsync<CommandResult<int>>(command);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }

            return Results.Created($"/invoices/{commandResult.Value}", new { invoiceNumber = commandResult.Value });
        })
        .WithName("CreateInvoice")
        .WithTags("Invoice")
        .WithSummary("Create an invoice with a consecutive invoice number")
        .WithDescription(
            "Appends an InvoiceCreatedEvent with a unique, gap-free invoice number. " +
            "Uses ReadLastAsync to find the current highest number, then guards the append " +
            "with an AppendCondition so concurrent requests are detected and retried " +
            "automatically — guaranteeing an unbroken sequence even under parallel load.");
    }
}

public sealed class CreateInvoiceCommandHandler()
{
    // The invoice query has NO tag filter — it spans all InvoiceCreatedEvents globally.
    // This is intentional: any new invoice created by anyone invalidates our "last number"
    // read, so the consistency boundary is exactly "the set of all invoice creation events".
    private static readonly Query _invoiceQuery =
        Query.FromEventTypes(nameof(InvoiceCreatedEvent));

    public async Task<CommandResult<int>> HandleAsync(
        CreateInvoiceCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryCreateInvoiceAsync(command, store, ct));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult<int>.Fail(
                "Failed to create invoice due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult<int>> TryCreateInvoiceAsync(
        CreateInvoiceCommand command,
        IEventStore eventStore,
        CancellationToken cancellationToken)
    {
        // Step 1 — Read: find the most recently created invoice (O(1) file reads).
        // Returns null when the store contains no invoices yet.
        var last = await eventStore.ReadLastAsync(_invoiceQuery, cancellationToken);

        // Step 2 — Decide: derive the next consecutive number.
        var nextNumber = last is null
            ? 1
            : ((InvoiceCreatedEvent)last.Event.Event).InvoiceNumber + 1;

        // Step 3 — Append with a guard that rejects the write if any InvoiceCreatedEvent
        // appeared since our read. AfterSequencePosition = null on first invoice means
        // "reject if ANY invoice already exists", closing the bootstrap race.
        var condition = new AppendCondition
        {
            FailIfEventsMatch = _invoiceQuery,
            AfterSequencePosition = last?.Position
        };

        NewEvent newEvent = new InvoiceCreatedEvent(
                InvoiceNumber: nextNumber,
                CustomerId: command.CustomerId,
                Amount: command.Amount,
                IssuedAt: DateTimeOffset.UtcNow)
            .ToDomainEvent()
            .WithTag("invoiceNumber", nextNumber.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, condition, cancellationToken);

        return CommandResult<int>.Ok(nextNumber);
    }
}
