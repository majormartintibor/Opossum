using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Exceptions;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookPurchase;

/// <summary>Purchase a single course book (Features 1 &amp; 2).</summary>
public sealed record PurchaseCourseBookRequest(Guid StudentId, decimal DisplayedPrice);
public sealed record PurchaseCourseBookCommand(Guid BookId, Guid StudentId, decimal DisplayedPrice);

public static class PurchaseCourseBookEndpoint
{
    public static void MapPurchaseCourseBookEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/course-books/{bookId:guid}/purchase", async (
            Guid bookId,
            [FromBody] PurchaseCourseBookRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new PurchaseCourseBookCommand(bookId, request.StudentId, request.DisplayedPrice);
            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Created($"/course-books/orders", null);
        })
        .WithName("PurchaseCourseBook")
        .WithTags("Course Books (Dynamic Price)")
        .WithSummary("Purchase a single course book (F1/F2)")
        .WithDescription(
            "Demonstrates the DCB 'Dynamic Product Price' pattern (Features 1 & 2). " +
            "The displayed price must match the current price (or the previous price within the grace period). " +
            "A concurrent price change invalidates the purchase — the client must retry.");
    }
}

public sealed class PurchaseCourseBookCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        PurchaseCourseBookCommand command,
        IEventStore eventStore)
    {
        try
        {
            return await eventStore.ExecuteDecisionAsync(
                (store, ct) => TryPurchaseAsync(command, store, ct));
        }
        catch (AppendConditionFailedException)
        {
            return CommandResult.Fail("The purchase could not be completed due to concurrent updates. Please try again.");
        }
    }

    private static async Task<CommandResult> TryPurchaseAsync(
        PurchaseCourseBookCommand command,
        IEventStore eventStore,
        CancellationToken cancellationToken)
    {
        var (priceState, courseId, appendCondition) = await eventStore.BuildDecisionModelAsync(
            CourseBookPriceProjections.PriceWithGracePeriod(command.BookId),
            CourseBookPriceProjections.CourseIdForBook(command.BookId),
            cancellationToken);

        if (priceState.CurrentPrice is null)
            return CommandResult.Fail($"Course book '{command.BookId}' does not exist.");

        if (!priceState.IsValidPrice(command.DisplayedPrice))
            return CommandResult.Fail(
                $"The displayed price {command.DisplayedPrice:C} is no longer valid. Please refresh and try again.");

        var builder = new CourseBookPurchasedEvent(
                BookId: command.BookId,
                StudentId: command.StudentId,
                PricePaid: command.DisplayedPrice)
            .ToDomainEvent()
            .WithTag("bookId", command.BookId.ToString())
            .WithTag("studentId", command.StudentId.ToString());

        if (courseId is not null)
            builder = builder.WithTag("courseId", courseId.Value.ToString());

        NewEvent purchaseEvent = builder.WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(purchaseEvent, appendCondition);
        return CommandResult.Ok();
    }
}
