using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;

namespace Opossum.Samples.DataSeeder.Generators;

/// <summary>
/// Generates <see cref="InvoiceCreatedEvent"/> records with sequential invoice numbers.
/// The counter is maintained purely in-memory — no event-store reads required.
/// </summary>
public sealed class InvoiceGenerator : ISeedGenerator
{
    public IReadOnlyList<SeedEvent> Generate(SeedContext context, SeedingConfiguration config)
    {
        if (config.InvoiceCount <= 0) return [];

        var random = context.Random;
        var events = new List<SeedEvent>(config.InvoiceCount);

        for (var i = 0; i < config.InvoiceCount; i++)
        {
            var invoiceNumber = i + 1; // Sequential starting at 1 — no store read needed.
            var customerId    = context.Students.Count > 0
                ? context.Students[random.Next(context.Students.Count)].StudentId
                : Guid.NewGuid();
            var amount   = Math.Round(random.Next(1000, 999999) / 100m, 2);
            var issuedAt = GeneratorHelper.RandomTimestamp(random, 90, 1);

            Tag[] tags = [new("invoiceNumber", invoiceNumber.ToString())];

            events.Add(GeneratorHelper.CreateSeedEvent(
                new InvoiceCreatedEvent(invoiceNumber, customerId, amount, issuedAt),
                tags,
                issuedAt));
        }

        return events;
    }
}
