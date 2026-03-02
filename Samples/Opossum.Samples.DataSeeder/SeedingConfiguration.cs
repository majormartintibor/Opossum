namespace Opossum.Samples.DataSeeder;

/// <summary>
/// Configuration for a seeding run. Select a preset via <see cref="SeedingPresets"/> or
/// set individual properties to customise the run.
/// </summary>
public class SeedingConfiguration
{
    /// <summary>Display name for the selected preset (e.g. "Small", "Medium"). Set by <see cref="SeedingPresets"/>.</summary>
    public string PresetName { get; set; } = "Custom";

    // --- Entity counts (set by preset or overridden by user) ---
    public int StudentCount    { get; set; } = 10_000;
    public int CourseCount     { get; set; } = 2_000;
    public int CourseBookCount { get; set; } = 2_000;
    public int InvoiceCount    { get; set; } = 1_000;
    public int MultiBookOrders { get; set; } = 200;

    // --- Per-entity multipliers (shared across all presets) ---
    public int AnnouncementsPerCourse           { get; set; } = 3;
    public int AnnouncementRetractionPercentage { get; set; } = 20;
    public int ExamsPerCourse                   { get; set; } = 2;
    public int TokensPerExam                    { get; set; } = 5;
    public int TokenRedemptionPercentage        { get; set; } = 70;
    public int TokenRevocationPercentage        { get; set; } = 10;
    public int TierUpgradePercentage            { get; set; } = 30;
    public int CapacityChangePercentage         { get; set; } = 20;
    public int PriceChangePercentage            { get; set; } = 40;
    public int SingleBookPurchasesPerBook       { get; set; } = 20;

    // --- Tier distribution ---
    public int BasicTierPercentage        { get; set; } = 20;
    public int StandardTierPercentage     { get; set; } = 40;
    public int ProfessionalTierPercentage { get; set; } = 30;
    public int MasterTierPercentage       { get; set; } = 10;

    // --- Course size distribution (used internally by CourseGenerator) ---
    public int SmallCoursePercentage  { get; set; } = 27;
    public int MediumCoursePercentage { get; set; } = 53;
    public int LargeCoursePercentage  { get; set; } = 20;

    // --- Writer options ---
    /// <summary>When <see langword="true"/>, uses <c>IEventStore.AppendAsync</c> instead of <c>DirectEventWriter</c>. Suitable for small datasets.</summary>
    public bool UseEventStoreWriter { get; set; } = false;
    /// <summary>Maximum concurrent event-file write threads. 0 means <see cref="Environment.ProcessorCount"/>.</summary>
    public int WriteParallelism { get; set; } = 0;

    // --- Misc ---
    public bool ResetDatabase       { get; set; } = false;
    public bool RequireConfirmation { get; set; } = true;

    /// <summary>
    /// Estimated total event count using the formula from the design document.
    /// Intended for display — not a guarantee of the exact output.
    /// </summary>
    public long EstimatedEventCount
    {
        get
        {
            if (StudentCount == 0) return 0;

            // Capacity-constrained enrollment estimate (avg_tier_max ≈ 7.9, avg_capacity ≈ 26.6).
            const double avgTierMax  = 7.9;
            const double avgCapacity = 26.6;
            var avgEnrollments = Math.Min(
                avgTierMax * 0.8,
                (double)CourseCount * avgCapacity / StudentCount);

            var eStudents = (long)(StudentCount * (
                1.0
                + TierUpgradePercentage / 100.0
                + avgEnrollments));

            var eCourses = (long)(CourseCount * (
                1.0
                + CapacityChangePercentage / 100.0
                + AnnouncementsPerCourse * (1.0 + AnnouncementRetractionPercentage / 100.0)
                + ExamsPerCourse * TokensPerExam * (1.0 + TokenRedemptionPercentage / 100.0 + TokenRevocationPercentage / 100.0)));

            var eBooks = (long)(CourseBookCount * (
                1.0
                + PriceChangePercentage / 100.0
                + SingleBookPurchasesPerBook))
                + MultiBookOrders;

            return eStudents + eCourses + eBooks + InvoiceCount;
        }
    }
}
