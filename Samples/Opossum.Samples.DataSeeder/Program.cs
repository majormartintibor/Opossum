using Opossum;
using Opossum.DependencyInjection;
using Opossum.Samples.DataSeeder;
using Opossum.Samples.DataSeeder.Core;
using Opossum.Samples.DataSeeder.Generators;
using Opossum.Samples.DataSeeder.Writers;

Console.WriteLine("🌱 Opossum Data Seeder");
Console.WriteLine("======================\n");

// Load configuration from CourseManagement sample app (single source of truth for DB path).
var courseManagementPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Opossum.Samples.CourseManagement");
var configuration = new ConfigurationBuilder()
    .SetBasePath(courseManagementPath)
    .AddJsonFile("appsettings.Development.json", optional: false)
    .Build();

var rootPath    = configuration["Opossum:RootPath"]  ?? throw new InvalidOperationException("Opossum:RootPath not configured.");
var storeName   = configuration["Opossum:StoreName"] ?? throw new InvalidOperationException("Opossum:StoreName not configured.");
var contextPath = Path.Combine(rootPath, storeName);

Console.WriteLine($"Database: {contextPath}");
Console.WriteLine();

// ── Argument parsing ──────────────────────────────────────────────────────────
var (config, presetProvided) = ParseArguments(args);

// ── Interactive menu (skipped when --size was provided) ───────────────────────
if (!presetProvided)
{
    config = RunInteractiveMenu();
    Console.WriteLine();
    Console.Write("Reset existing database? (y/N): ");
    var resetResponse = Console.ReadLine()?.Trim().ToLowerInvariant();
    config.ResetDatabase = resetResponse is "y" or "yes";
}

// ── Confirmation summary ──────────────────────────────────────────────────────
var parallelism       = config.WriteParallelism <= 0 ? Environment.ProcessorCount : config.WriteParallelism;
var writerDescription = config.UseEventStoreWriter
    ? "EventStoreWriter"
    : $"DirectEventWriter (parallel, {parallelism} threads)";

Console.WriteLine();
Console.WriteLine("Configuration:");
Console.WriteLine($"  Size:        {config.PresetName}");
Console.WriteLine($"  Students:    {config.StudentCount:N0}");
Console.WriteLine($"  Courses:     {config.CourseCount:N0}");
Console.WriteLine($"  Books:       {config.CourseBookCount:N0}");
Console.WriteLine($"  Invoices:    {config.InvoiceCount:N0}");
Console.WriteLine($"  Est. events: ~{config.EstimatedEventCount:N0}");
Console.WriteLine($"  Reset:       {(config.ResetDatabase ? "YES" : "NO")}");
Console.WriteLine($"  Writer:      {writerDescription}");
Console.WriteLine();

if (config.RequireConfirmation)
{
    Console.Write("Proceed? (y/N): ");
    var proceed = Console.ReadLine()?.Trim().ToLowerInvariant();
    if (proceed is not ("y" or "yes"))
    {
        Console.WriteLine("\n❌ Seeding cancelled.");
        return;
    }
}

// ── Reset ─────────────────────────────────────────────────────────────────────
if (config.ResetDatabase)
{
    if (Directory.Exists(contextPath))
    {
        Console.WriteLine($"\n🗑️  Deleting: {contextPath}");
        Directory.Delete(contextPath, recursive: true);
        Console.WriteLine("✅ Database cleared.");
    }
    else
    {
        Console.WriteLine("ℹ️  No existing database to clear.");
    }
}

// ── Generators (dependency order) ────────────────────────────────────────────
IReadOnlyList<ISeedGenerator> generators =
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

// ── Writer ────────────────────────────────────────────────────────────────────
IEventWriter writer;
if (config.UseEventStoreWriter)
{
    var services = new ServiceCollection();
    services.AddOpossum(options =>
    {
        var sn = configuration["Opossum:StoreName"];
        if (sn is not null) options.UseStore(sn);
        configuration.GetSection("Opossum").Bind(options);
    });
    var sp = services.BuildServiceProvider();
    writer = new EventStoreWriter(sp.GetRequiredService<IEventStore>());
}
else
{
    writer = new DirectEventWriter(config.WriteParallelism);
}

// ── Run ───────────────────────────────────────────────────────────────────────
Console.WriteLine("\n⏳ Generating events...");
var startTime      = DateTime.UtcNow;
var consoleLock    = new object();
var lastPhaseShown = 0;

IProgress<WriterProgress> progressReporter = new SyncProgress<WriterProgress>(p =>
{
    lock (consoleLock)
    {
        if (p.PhaseNumber != lastPhaseShown)
        {
            if (lastPhaseShown != 0) Console.WriteLine(); // finish previous phase line
            lastPhaseShown = p.PhaseNumber;
        }
        RenderProgressLine(p);
    }
});

var totalEvents = await new SeedPlan(generators).RunAsync(config, writer, contextPath, progressReporter);
var elapsed     = DateTime.UtcNow - startTime;

if (lastPhaseShown != 0) Console.WriteLine(); // finish last progress line

Console.WriteLine($"\n✅ Seeding complete in {elapsed.TotalSeconds:F1}s");
Console.WriteLine($"   Total events written: {totalEvents:N0}");
Console.WriteLine($"   Database location:    {contextPath}");
Console.WriteLine($"\n💡 You can now run the sample application to explore the data.");

return;

// ── Helper methods ────────────────────────────────────────────────────────────

static (SeedingConfiguration Config, bool PresetProvided) ParseArguments(string[] args)
{
    SeedingConfiguration? preset = null;
    var reset         = false;
    var noConfirm     = false;
    var useEventStore = false;
    var parallelism   = 0;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--size":
                if (i + 1 < args.Length)
                {
                    preset = args[++i].ToLowerInvariant() switch
                    {
                        "small"  => SeedingPresets.Small(),
                        "medium" => SeedingPresets.Medium(),
                        "large"  => SeedingPresets.Large(),
                        "prod"   => SeedingPresets.Prod(),
                        var s    => throw new ArgumentException($"Unknown size '{s}'. Valid: small, medium, large, prod")
                    };
                }
                break;

            case "--reset":
                reset = true;
                break;

            case "--no-confirm":
                noConfirm = true;
                break;

            case "--use-event-store":
                useEventStore = true;
                break;

            case "--parallelism":
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
                {
                    parallelism = p;
                    i++;
                }
                break;

            case "--help":
            case "-h":
                DisplayHelp();
                Environment.Exit(0);
                break;
        }
    }

    var config = preset ?? new SeedingConfiguration();
    config.ResetDatabase       = reset;
    config.RequireConfirmation = !noConfirm;
    config.UseEventStoreWriter = useEventStore;
    config.WriteParallelism    = parallelism;

    return (config, preset is not null);
}

static SeedingConfiguration RunInteractiveMenu()
{
    Console.WriteLine("Select a dataset size:");
    Console.WriteLine("  [1] Small   ~620 events       — explore the data model");
    Console.WriteLine("  [2] Medium  ~104 000 events   — growing business, a few months of data");
    Console.WriteLine("  [3] Large   ~1 030 000 events — established platform, 1-3 years of data");
    Console.WriteLine("  [4] Prod    ~5 150 000 events — large-scale performance testing");
    Console.WriteLine();

    while (true)
    {
        Console.Write("Your choice (1-4): ");
        var choice = Console.ReadLine()?.Trim();

        if (choice == "1") return SeedingPresets.Small();
        if (choice == "2") return SeedingPresets.Medium();
        if (choice == "3") return SeedingPresets.Large();
        if (choice == "4") return SeedingPresets.Prod();

        Console.WriteLine("Please enter 1, 2, 3, or 4.");
    }
}

static void DisplayHelp()
{
    Console.WriteLine("Opossum Data Seeder");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run -- [flags]");
    Console.WriteLine();
    Console.WriteLine("  --size <small|medium|large|prod>   Select a preset non-interactively");
    Console.WriteLine("  --reset                            Delete existing data before seeding");
    Console.WriteLine("  --no-confirm                       Skip all confirmation prompts");
    Console.WriteLine("  --use-event-store                  Use IEventStore instead of DirectEventWriter");
    Console.WriteLine("  --parallelism <n>                  File write threads (default: cpu count)");
    Console.WriteLine("  --help                             Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run");
    Console.WriteLine("  dotnet run -- --size small --reset --no-confirm");
    Console.WriteLine("  dotnet run -- --size large --parallelism 16");
}

static void RenderProgressLine(WriterProgress p)
{
    const int barWidth = 30;
    var pct    = p.Total > 0 ? (double)p.Current / p.Total : 0.0;
    var filled = (int)(barWidth * pct);
    var bar    = new string('█', filled) + new string('░', barWidth - filled);
    Console.Write(
        $"\r  [{p.PhaseNumber}/{p.TotalPhases}] {p.PhaseName,-22} [{bar}] {pct * 100,4:F0}%  {p.Current,12:N0} / {p.Total:N0}   ");
}

// ── File-scoped types ─────────────────────────────────────────────────────────

file sealed class SyncProgress<T>(Action<T> callback) : IProgress<T>
{
    public void Report(T value) => callback(value);
}
