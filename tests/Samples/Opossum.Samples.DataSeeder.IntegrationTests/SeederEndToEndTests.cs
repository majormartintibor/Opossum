using Opossum.Core;
using Opossum.DependencyInjection;
using Opossum.Samples.CourseManagement.Events;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;
using Opossum.Samples.DataSeeder.Writers;

namespace Opossum.Samples.DataSeeder.IntegrationTests;

/// <summary>
/// End-to-end integration tests for the redesigned seeder pipeline:
/// <see cref="SeedingPresets"/> → generators → <see cref="SeedPlan"/> →
/// <see cref="DirectEventWriter"/> → <see cref="IEventStore"/> round-trip.
/// </summary>
public sealed class SeederEndToEndTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly string _contextPath;
    private const string StoreName = "TestStore";

    public SeederEndToEndTests()
    {
        _tempRoot    = Path.Combine(Path.GetTempPath(), "OpossumSeederE2E", Guid.NewGuid().ToString());
        _contextPath = Path.Combine(_tempRoot, StoreName);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    private static IReadOnlyList<ISeedGenerator> BuildGenerators() =>
    [
        new StudentGenerator(),
        new TierUpgradeGenerator(),
        new CourseGenerator(),
        new CapacityChangeGenerator(),
        new EnrollmentGenerator(),
        new InvoiceGenerator(),
        new AnnouncementGenerator(),
        new ExamTokenGenerator(),
        new CourseBookGenerator()
    ];

    // ── Small preset — full pipeline ─────────────────────────────────────────

    [Fact]
    public async Task SeedPlan_SmallPreset_ProducesReadableDatabaseAsync()
    {
        var config = SeedingPresets.Small();
        var plan   = new SeedPlan(BuildGenerators());
        var writer = new DirectEventWriter();

        var totalEvents = await plan.RunAsync(config, writer, _contextPath);

        Assert.True(totalEvents > 0, "Expected at least one event to be written.");

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOpossum(options =>
        {
            options.RootPath = _tempRoot;
            options.UseStore(StoreName);
        });
        await using var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var allEvents = await eventStore.ReadAsync(Query.All(), null);

        Assert.Equal(totalEvents, allEvents.Length);
    }

    [Fact]
    public async Task SeedPlan_SmallPreset_ProducesCorrectStudentCountAsync()
    {
        var config = SeedingPresets.Small();
        await new SeedPlan(BuildGenerators()).RunAsync(config, new DirectEventWriter(), _contextPath);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOpossum(options =>
        {
            options.RootPath = _tempRoot;
            options.UseStore(StoreName);
        });
        await using var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var studentEvents = await eventStore.ReadAsync(
            Query.FromEventTypes(nameof(StudentRegisteredEvent)), null);

        Assert.Equal(config.StudentCount, studentEvents.Length);
    }

    [Fact]
    public async Task SeedPlan_SmallPreset_ProducesCorrectCourseCountAsync()
    {
        var config = SeedingPresets.Small();
        await new SeedPlan(BuildGenerators()).RunAsync(config, new DirectEventWriter(), _contextPath);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOpossum(options =>
        {
            options.RootPath = _tempRoot;
            options.UseStore(StoreName);
        });
        await using var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var courseEvents = await eventStore.ReadAsync(
            Query.FromEventTypes(nameof(CourseCreatedEvent)), null);

        // CourseGenerator uses integer division for size-category distribution,
        // so the actual count may be 1-2 less than requested for small values.
        Assert.InRange(courseEvents.Length, config.CourseCount - 2, config.CourseCount);
    }

    [Fact]
    public async Task SeedPlan_SmallPreset_EventsAreInAscendingPositionOrderAsync()
    {
        var config = SeedingPresets.Small();
        await new SeedPlan(BuildGenerators()).RunAsync(config, new DirectEventWriter(), _contextPath);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOpossum(options =>
        {
            options.RootPath = _tempRoot;
            options.UseStore(StoreName);
        });
        await using var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var allEvents = await eventStore.ReadAsync(Query.All(), null);

        for (var i = 0; i < allEvents.Length - 1; i++)
            Assert.True(allEvents[i].Position < allEvents[i + 1].Position,
                $"Events not in ascending position order at index {i}");
    }

    [Fact]
    public async Task SeedPlan_SmallPreset_ContainsAllExpectedEventTypesAsync()
    {
        var config = SeedingPresets.Small();
        await new SeedPlan(BuildGenerators()).RunAsync(config, new DirectEventWriter(), _contextPath);

        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
        services.AddOpossum(options =>
        {
            options.RootPath = _tempRoot;
            options.UseStore(StoreName);
        });
        await using var sp = services.BuildServiceProvider();
        var eventStore = sp.GetRequiredService<IEventStore>();

        var allEvents = await eventStore.ReadAsync(Query.All(), null);
        var eventTypes = allEvents.Select(e => e.Event.EventType).ToHashSet();

        Assert.Contains(nameof(StudentRegisteredEvent),          eventTypes);
        Assert.Contains(nameof(CourseCreatedEvent),              eventTypes);
        Assert.Contains(nameof(StudentEnrolledToCourseEvent),    eventTypes);
        Assert.Contains(nameof(InvoiceCreatedEvent),             eventTypes);
        Assert.Contains(nameof(CourseAnnouncementPostedEvent),   eventTypes);
        Assert.Contains(nameof(ExamRegistrationTokenIssuedEvent), eventTypes);
        Assert.Contains(nameof(CourseBookDefinedEvent),          eventTypes);
    }

    // ── SeedingPresets factory ────────────────────────────────────────────────

    [Fact]
    public void SeedingPresets_Small_HasExpectedEntityCounts()
    {
        var config = SeedingPresets.Small();

        Assert.Equal("Small",  config.PresetName);
        Assert.Equal(40,        config.StudentCount);
        Assert.Equal(25,        config.CourseCount);
        Assert.Equal(25,        config.CourseBookCount);
        Assert.Equal(30,        config.InvoiceCount);
        Assert.Equal(8,         config.MultiBookOrders);
    }

    [Fact]
    public void SeedingPresets_Medium_HasExpectedEntityCounts()
    {
        var config = SeedingPresets.Medium();

        Assert.Equal("Medium",  config.PresetName);
        Assert.Equal(7_000,     config.StudentCount);
        Assert.Equal(4_000,     config.CourseCount);
        Assert.Equal(4_000,     config.CourseBookCount);
        Assert.Equal(2_500,     config.InvoiceCount);
        Assert.Equal(600,       config.MultiBookOrders);
    }

    [Fact]
    public void SeedingPresets_Large_HasExpectedEntityCounts()
    {
        var config = SeedingPresets.Large();

        Assert.Equal("Large",   config.PresetName);
        Assert.Equal(70_000,    config.StudentCount);
        Assert.Equal(40_000,    config.CourseCount);
        Assert.Equal(40_000,    config.CourseBookCount);
        Assert.Equal(15_000,    config.InvoiceCount);
        Assert.Equal(7_000,     config.MultiBookOrders);
    }

    [Fact]
    public void SeedingPresets_Prod_HasExpectedEntityCounts()
    {
        var config = SeedingPresets.Prod();

        Assert.Equal("Prod",    config.PresetName);
        Assert.Equal(350_000,   config.StudentCount);
        Assert.Equal(200_000,   config.CourseCount);
        Assert.Equal(200_000,   config.CourseBookCount);
        Assert.Equal(75_000,    config.InvoiceCount);
        Assert.Equal(35_000,    config.MultiBookOrders);
    }

    // ── SeedingConfiguration.EstimatedEventCount ─────────────────────────────

    [Fact]
    public void SeedingConfiguration_EstimatedEventCount_IsPositiveForSmallPreset()
    {
        var config = SeedingPresets.Small();

        Assert.True(config.EstimatedEventCount > 0);
    }

    [Fact]
    public void SeedingConfiguration_EstimatedEventCount_ScalesWithPresetSize()
    {
        var small  = SeedingPresets.Small().EstimatedEventCount;
        var medium = SeedingPresets.Medium().EstimatedEventCount;
        var large  = SeedingPresets.Large().EstimatedEventCount;
        var prod   = SeedingPresets.Prod().EstimatedEventCount;

        Assert.True(small  < medium, "Small should estimate fewer events than Medium");
        Assert.True(medium < large,  "Medium should estimate fewer events than Large");
        Assert.True(large  < prod,   "Large should estimate fewer events than Prod");
    }

    [Fact]
    public void SeedingConfiguration_EstimatedEventCount_IsZeroWhenNoStudents()
    {
        var config = new SeedingConfiguration { StudentCount = 0 };

        Assert.Equal(0, config.EstimatedEventCount);
    }
}
