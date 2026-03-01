using Opossum.Core;
using Opossum.DecisionModel;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseBookPurchase;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.CourseBookManagement;

public sealed record DefineCourseBookRequest(string Title, string Author, string Isbn, decimal Price);
public sealed record DefineCourseBookCommand(Guid BookId, string Title, string Author, string Isbn, decimal Price);

public static class DefineCourseBookEndpoint
{
    public static void MapDefineCourseBookEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapPost("/course-books", async (
            [FromBody] DefineCourseBookRequest request,
            [FromServices] IMediator mediator) =>
        {
            var bookId = Guid.NewGuid();
            var command = new DefineCourseBookCommand(bookId, request.Title, request.Author, request.Isbn, request.Price);
            var result = await mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
                return Results.BadRequest(result.ErrorMessage);

            return Results.Created($"/course-books/{bookId}", new { id = bookId });
        })
        .WithName("DefineCourseBook")
        .WithTags("Course Books (Dynamic Price)")
        .WithSummary("Define a new course book (admin)")
        .WithDescription("Adds a new course book to the catalog with an initial price.");
    }
}

public sealed class DefineCourseBookCommandHandler
{
    public async Task<CommandResult> HandleAsync(
        DefineCourseBookCommand command,
        IEventStore eventStore)
    {
        var (bookExists, appendCondition) = await eventStore.BuildDecisionModelAsync(
            CourseBookPriceProjections.BookExists(command.BookId));

        if (bookExists)
            return CommandResult.Fail($"Course book '{command.BookId}' already exists.");

        NewEvent newEvent = new CourseBookDefinedEvent(
                BookId: command.BookId,
                Title: command.Title,
                Author: command.Author,
                Isbn: command.Isbn,
                Price: command.Price)
            .ToDomainEvent()
            .WithTag("bookId", command.BookId.ToString())
            .WithTimestamp(DateTimeOffset.UtcNow);

        await eventStore.AppendAsync(newEvent, appendCondition);
        return CommandResult.Ok();
    }
}
