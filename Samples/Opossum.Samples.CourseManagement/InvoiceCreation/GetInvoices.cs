using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Shared;

namespace Opossum.Samples.CourseManagement.InvoiceCreation;

public sealed record GetInvoicesQuery(
    int PageNumber = 1,
    int PageSize = 50,
    InvoiceSortField SortBy = InvoiceSortField.InvoiceNumber,
    SortOrder SortOrder = SortOrder.Ascending
) : PaginationQuery
{
    public new int PageNumber { get; init; } = PageNumber;
    public new int PageSize { get; init; } = PageSize;
}

public sealed record GetInvoiceQuery(int InvoiceNumber);

public static class GetInvoicesEndpoint
{
    public static void MapGetInvoicesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", async (
            int pageNumber,
            int pageSize,
            InvoiceSortField sortBy,
            SortOrder sortOrder,
            [FromServices] IMediator mediator) =>
        {
            var query = new GetInvoicesQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                SortBy: sortBy,
                SortOrder: sortOrder
            );

            var result = await mediator.InvokeAsync<CommandResult<PaginatedResponse<InvoiceReadModel>>>(query);

            return result.Success
                ? Results.Ok(result.Value)
                : Results.BadRequest(result.ErrorMessage);
        })
        .WithName("GetInvoices")
        .WithTags("Invoice")
        .WithSummary("List all invoices")
        .WithDescription("Returns all invoices ordered by invoice number ascending.");

        app.MapGet("/invoices/{invoiceNumber:int}", async (
            int invoiceNumber,
            [FromServices] IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<CommandResult<InvoiceReadModel>>(new GetInvoiceQuery(invoiceNumber));

            return result.Success
                ? Results.Ok(result.Value)
                : Results.NotFound(result.ErrorMessage);
        })
        .WithName("GetInvoiceByNumber")
        .WithTags("Invoice")
        .WithSummary("Get a single invoice by number")
        .WithDescription("Returns the invoice with the given consecutive invoice number.");
    }
}

public sealed class GetInvoicesQueryHandler()
{
    public async Task<CommandResult<PaginatedResponse<InvoiceReadModel>>> HandleAsync(
        GetInvoicesQuery query,
        IProjectionStore<InvoiceReadModel> projectionStore)
    {
        var all = await projectionStore.GetAllAsync();

        var sorted = query.SortBy switch
        {
            InvoiceSortField.Amount => query.SortOrder == SortOrder.Ascending
                ? all.OrderBy(i => i.Amount)
                : all.OrderByDescending(i => i.Amount),

            InvoiceSortField.IssuedAt => query.SortOrder == SortOrder.Ascending
                ? all.OrderBy(i => i.IssuedAt)
                : all.OrderByDescending(i => i.IssuedAt),

            _ => query.SortOrder == SortOrder.Ascending
                ? all.OrderBy(i => i.InvoiceNumber)
                : all.OrderByDescending(i => i.InvoiceNumber)
        };

        var list = sorted.ToList();
        var totalCount = list.Count;
        var items = list
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        return CommandResult<PaginatedResponse<InvoiceReadModel>>.Ok(new PaginatedResponse<InvoiceReadModel>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        });
    }
}

public sealed class GetInvoiceQueryHandler()
{
    public async Task<CommandResult<InvoiceReadModel>> HandleAsync(
        GetInvoiceQuery query,
        IProjectionStore<InvoiceReadModel> projectionStore)
    {
        var invoice = await projectionStore.GetAsync(query.InvoiceNumber.ToString());

        return invoice is null
            ? CommandResult<InvoiceReadModel>.Fail($"Invoice {query.InvoiceNumber} not found.")
            : CommandResult<InvoiceReadModel>.Ok(invoice);
    }
}
