namespace Opossum.Samples.DataSeeder;

public class SeedingConfiguration
{
    public int StudentCount { get; set; } = 350;
    public int CourseCount { get; set; } = 75;

    public bool ResetDatabase { get; set; } = false;
    public bool RequireConfirmation { get; set; } = true;

    // Tier distribution (percentages)
    public int BasicTierPercentage { get; set; } = 20;
    public int StandardTierPercentage { get; set; } = 40;
    public int ProfessionalTierPercentage { get; set; } = 30;
    public int MasterTierPercentage { get; set; } = 10;

    // Course size distribution (percentages)
    public int SmallCoursePercentage { get; set; } = 27;
    public int MediumCoursePercentage { get; set; } = 53;
    public int LargeCoursePercentage { get; set; } = 20;

    // Estimated event count for display
    public int EstimatedEventCount =>
        StudentCount +  // StudentRegisteredEvent
        CourseCount +   // CourseCreatedEvent
        (int)(StudentCount * 0.3) +  // ~30% tier upgrades
        (int)(CourseCount * 0.2) +   // ~20% capacity changes
        StudentCount * 5;     // ~5 enrollments per student avg
}
