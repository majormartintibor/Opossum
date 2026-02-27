using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;

namespace Opossum.Samples.CourseManagement.InvoiceCreation;

public sealed record GetInvoicesQuery;
public sealed record GetInvoiceQuery(int InvoiceNumber);

public static class GetInvoicesEndpoint
{
    public static void MapGetInvoicesEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/invoices", async ([FromServices] IMediator mediator) =>
        {
            var result = await mediator.InvokeAsync<CommandResult<IReadOnlyList<InvoiceReadModel>>>(new GetInvoicesQuery());

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
    public async Task<CommandResult<IReadOnlyList<InvoiceReadModel>>> HandleAsync(
        GetInvoicesQuery _,
        IProjectionStore<InvoiceReadModel> projectionStore)
    {
        var all = await projectionStore.GetAllAsync();
        var sorted = all.OrderBy(i => i.InvoiceNumber).ToList();
        return CommandResult<IReadOnlyList<InvoiceReadModel>>.Ok(sorted);
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
