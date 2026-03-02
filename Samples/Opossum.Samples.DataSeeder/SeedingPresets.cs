namespace Opossum.Samples.DataSeeder;

/// <summary>
/// Factory methods that return pre-calibrated <see cref="SeedingConfiguration"/> instances
/// for the four standard dataset sizes.
/// </summary>
public static class SeedingPresets
{
    /// <summary>~620 events — explore the data model.</summary>
    public static SeedingConfiguration Small() => new()
    {
        PresetName      = "Small",
        StudentCount    = 40,
        CourseCount     = 8,
        CourseBookCount = 8,
        InvoiceCount    = 30,
        MultiBookOrders = 8
    };

    /// <summary>~104 000 events — growing business, a few months of data.</summary>
    public static SeedingConfiguration Medium() => new()
    {
        PresetName      = "Medium",
        StudentCount    = 7_000,
        CourseCount     = 1_400,
        CourseBookCount = 1_400,
        InvoiceCount    = 2_500,
        MultiBookOrders = 600
    };

    /// <summary>~1 030 000 events — established platform, 1-3 years of data.</summary>
    public static SeedingConfiguration Large() => new()
    {
        PresetName      = "Large",
        StudentCount    = 70_000,
        CourseCount     = 14_000,
        CourseBookCount = 14_000,
        InvoiceCount    = 15_000,
        MultiBookOrders = 7_000
    };

    /// <summary>~5 150 000 events — large-scale performance testing.</summary>
    public static SeedingConfiguration Prod() => new()
    {
        PresetName      = "Prod",
        StudentCount    = 350_000,
        CourseCount     = 70_000,
        CourseBookCount = 70_000,
        InvoiceCount    = 75_000,
        MultiBookOrders = 35_000
    };
}
