using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookPurchase;

/// <summary>Order multiple course books in a single transaction (Feature 3 — shopping cart).</summary>
public sealed record OrderCourseBooksRequest(Guid StudentId, IReadOnlyList<OrderCourseBookItem> Items);
public sealed record OrderCourseBookItem(Guid BookId, decimal DisplayedPrice);
public sealed record OrderCourseBooksCommand(Guid StudentId, IReadOnlyList<OrderCourseBookItem> Items);

public static class OrderCourseBooksEndpoint
{
    public static void MapOrderCourseBooksEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/course-books/order", async (
            [FromBody] OrderCourseBooksRequest request,
            [FromServices] IMediator mediator) =>
        {
            if (request.Items is null || request.Items.Count == 0)
                return Results.BadRequest("Order must contain at least one item.");

            var command = new OrderCourseBooksCommand(request.StudentId, request.Items);
            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Created($"/course-books/orders", null);
        })
        .WithName("OrderCourseBooks")
        .WithTags("Course Books (Dynamic Price)")
        .WithSummary("Order multiple course books — shopping cart (F3)")
        .WithDescription(
            "Demonstrates the DCB 'Dynamic Product Price' Feature 3. " +
            "Uses the N-ary BuildDecisionModelAsync overload to validate each book's price " +
            "in a single event-store read with one AppendCondition spanning all books. " +
            "A concurrent price change for any book in the cart invalidates the entire order.");
    }
}

public sealed class OrderCourseBooksCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        OrderCourseBooksCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryOrderAsync(command, store, ct));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult.Fail("The order could not be completed due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult> TryOrderAsync(
        OrderCourseBooksCommand command,
        IEventStore eventStore,
        CancellationToken cancellationToken)
    {
        // Build one PriceWithGracePeriod projection per cart item — N-ary overload issues
        // a single ReadAsync call and returns states[i] corresponding to projections[i].
        var projections = command.Items
            .Select(item => CourseBookPriceProjections.PriceWithGracePeriod(item.BookId))
            .ToList();

        var (states, appendCondition) = await eventStore.BuildDecisionModelAsync(
            (IReadOnlyList<IDecisionProjection<CourseBookPriceState>>)projections,
            cancellationToken);

        for (var i = 0; i < command.Items.Count; i++)
        {
            var item = command.Items[i];
            var state = states[i];

            if (state.CurrentPrice is null)
                return CommandResult.Fail($"Course book '{item.BookId}' does not exist.");

            if (!state.IsValidPrice(item.DisplayedPrice))
                return CommandResult.Fail(
                    $"The displayed price {item.DisplayedPrice:C} for book '{item.BookId}' is no longer valid. " +
                    "Please refresh and try again.");
        }

        var orderItems = command.Items
            .Select(item => new CourseBookOrderItem(item.BookId, item.DisplayedPrice))
            .ToList();

        var builder = new CourseBooksOrderedEvent(
                StudentId: command.StudentId,
                Items: orderItems)
            .ToDomainEvent()
            .WithTag("studentId", command.StudentId.ToString());

        foreach (var item in command.Items)
            builder = builder.WithTag("bookId", item.BookId.ToString());

        NewEvent orderedEvent = builder.WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(orderedEvent, appendCondition);
        return CommandResult.Ok();
    }
}
