using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseBookPurchase;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookManagement;

public sealed record ChangeCourseBookPriceRequest(decimal NewPrice);
public sealed record ChangeCourseBookPriceCommand(Guid BookId, decimal NewPrice);

public static class ChangeCourseBookPriceEndpoint
{
    public static void MapChangeCourseBookPriceEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPatch("/course-books/{bookId:guid}/price", async (
            Guid bookId,
            [FromBody] ChangeCourseBookPriceRequest request,
            [FromServices] IMediator mediator) =>
        {
            var command = new ChangeCourseBookPriceCommand(bookId, request.NewPrice);
            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Ok();
        })
        .WithName("ChangeCourseBookPrice")
        .WithTags("Course Books (Dynamic Price)")
        .WithSummary("Change the price of a course book (admin)")
        .WithDescription("Updates the price. The previous price remains valid for a grace period.");
    }
}

public sealed class ChangeCourseBookPriceCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        ChangeCourseBookPriceCommand command,
        IEventStore eventStore)
    {
        var (bookExists, appendCondition) = await eventStore.BuildDecisionModelAsync(
            CourseBookPriceProjections.BookExists(command.BookId));

        if (!bookExists)
            return CommandResult.Fail($"Course book '{command.BookId}' does not exist.");

        NewEvent newEvent = new CourseBookPriceChangedEvent(
                BookId: command.BookId,
                NewPrice: command.NewPrice)
            .ToDomainEvent()
            .WithTag("bookId", command.BookId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, appendCondition);
        return CommandResult.Ok();
    }
}
