namespace Opossum.Samples.DataSeeder;

/// <summary>
/// Factory methods that return pre-calibrated <see cref="SeedingConfiguration"/> instances
/// for the four standard dataset sizes.
/// </summary>
public static class SeedingPresets
{
    /// <summary>~1 700 events — explore the data model.</summary>
    public static SeedingConfiguration Small() => new()
    {
        PresetName      = "Small",
        StudentCount    = 40,
        CourseCount     = 25,
        CourseBookCount = 25,
        InvoiceCount    = 30,
        MultiBookOrders = 8
    };

    /// <summary>~270 000 events — growing business, a few months of data.</summary>
    public static SeedingConfiguration Medium() => new()
    {
        PresetName      = "Medium",
        StudentCount    = 7_000,
        CourseCount     = 4_000,
        CourseBookCount = 4_000,
        InvoiceCount    = 2_500,
        MultiBookOrders = 600
    };

    /// <summary>~2 700 000 events — established platform, 1-3 years of data.</summary>
    public static SeedingConfiguration Large() => new()
    {
        PresetName      = "Large",
        StudentCount    = 70_000,
        CourseCount     = 40_000,
        CourseBookCount = 40_000,
        InvoiceCount    = 15_000,
        MultiBookOrders = 7_000
    };

    /// <summary>~13 000 000 events — large-scale performance testing.</summary>
    public static SeedingConfiguration Prod() => new()
    {
        PresetName      = "Prod",
        StudentCount    = 350_000,
        CourseCount     = 200_000,
        CourseBookCount = 200_000,
        InvoiceCount    = 75_000,
        MultiBookOrders = 35_000
    };
}
