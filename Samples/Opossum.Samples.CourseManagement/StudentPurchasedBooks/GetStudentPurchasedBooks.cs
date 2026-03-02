using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.StudentPurchasedBooks;

public sealed record GetStudentPurchasedBooksQuery(Guid StudentId);

public static class GetStudentPurchasedBooksEndpoint
{
    public static void MapGetStudentPurchasedBooksEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/students/{studentId:guid}/purchased-books", async (
            Guid studentId,
            [FromServices] IMediator mediator) =>
        {
            var query = new GetStudentPurchasedBooksQuery(studentId);
            var result = await mediator.InvokeAsync<CommandResult<StudentPurchasedBooksState>>(query);

            return result.Success
                ? Results.Ok(result.Value)
                : Results.NotFound(result.ErrorMessage);
        })
        .WithName("GetStudentPurchasedBooks")
        .WithTags("Queries")
        .WithSummary("Get all books purchased by a student")
        .WithDescription(
            "Returns all books the given student has ever purchased via individual purchases or " +
            "cart orders, deduplicated by bookId. " +
            "Each entry aggregates TotalPaid and PurchaseCount across all transactions for that book.");
    }
}

public sealed class GetStudentPurchasedBooksQueryHandler
{
    public async Task<CommandResult<StudentPurchasedBooksState>> HandleAsync(
        GetStudentPurchasedBooksQuery query,
        IProjectionStore<StudentPurchasedBooksState> projectionStore)
    {
        var state = await projectionStore.GetAsync(query.StudentId.ToString());

        if (state is null)
        {
            return CommandResult<StudentPurchasedBooksState>.Fail(
                $"No purchase history found for student {query.StudentId}.");
        }

        return CommandResult<StudentPurchasedBooksState>.Ok(state);
    }
}
