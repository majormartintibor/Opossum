using Microsoft.Extensions.DependencyInjection;
using Opossum.DependencyInjection;
using Opossum.Samples.DataSeeder;

Console.WriteLine("🌱 Opossum Baseline Data Seeder");
Console.WriteLine("================================\n");

// Parse command line arguments
var config = ParseArguments(args);

DisplayConfiguration(config);

if (!ConfirmSeeding(config))
{
    Console.WriteLine("\n❌ Seeding cancelled.");
    return;
}

// Initialize Opossum Event Store
var serviceProvider = ConfigureServices(config);

// Run seeding
var seeder = new DataSeeder(serviceProvider, config);
await seeder.SeedAsync();

Console.WriteLine("\n✅ Seeding complete!");
Console.WriteLine($"   Total events created: {seeder.TotalEventsCreated}");
Console.WriteLine($"   Database location: {config.RootPath}");
Console.WriteLine($"\n💡 You can now run the sample application or integration tests.");

return;

// ============================================================================
// Helper Methods
// ============================================================================

static SeedingConfiguration ParseArguments(string[] args)
{
    var config = new SeedingConfiguration();

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i].ToLowerInvariant())
        {
            case "--students":
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int students))
                {
                    config.StudentCount = students;
                    i++;
                }
                break;

            case "--courses":
                if (i + 1 < args.Length && int.TryParse(args[i + 1], out int courses))
                {
                    config.CourseCount = courses;
                    i++;
                }
                break;

            case "--reset":
                config.ResetDatabase = true;
                break;

            case "--no-confirm":
                config.RequireConfirmation = false;
                break;

            case "--help":
            case "-h":
                DisplayHelp();
                Environment.Exit(0);
                break;
        }
    }

    return config;
}

static void DisplayConfiguration(SeedingConfiguration config)
{
    Console.WriteLine("Configuration:");
    Console.WriteLine($"  Database Path: {config.RootPath}");
    Console.WriteLine($"  Students:      {config.StudentCount}");
    Console.WriteLine($"  Courses:       {config.CourseCount}");
    Console.WriteLine($"  Est. Events:   ~{config.EstimatedEventCount}");
    Console.WriteLine($"  Reset DB:      {(config.ResetDatabase ? "YES" : "NO")}");
    Console.WriteLine();
}

static bool ConfirmSeeding(SeedingConfiguration config)
{
    if (!config.RequireConfirmation)
    {
        return true;
    }

    if (config.ResetDatabase)
    {
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("⚠️  WARNING: This will DELETE all existing data!");
        Console.ResetColor();
    }

    Console.Write("Proceed with seeding? (y/n): ");
    var response = Console.ReadLine()?.Trim().ToLowerInvariant();
    return response == "y" || response == "yes";
}

static IServiceProvider ConfigureServices(SeedingConfiguration config)
{
    var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();

    services.AddOpossum(options =>
    {
        options.RootPath = config.RootPath;
        options.AddContext("OpossumSampleApp");
    });

    return services.BuildServiceProvider();
}

static void DisplayHelp()
{
    Console.WriteLine("Opossum Baseline Data Seeder");
    Console.WriteLine();
    Console.WriteLine("Usage: dotnet run [options]");
    Console.WriteLine();
    Console.WriteLine("Options:");
    Console.WriteLine("  --students <count>   Number of students to create (default: 350)");
    Console.WriteLine("  --courses <count>    Number of courses to create (default: 75)");
    Console.WriteLine("  --reset              Delete existing database before seeding");
    Console.WriteLine("  --no-confirm         Skip confirmation prompt");
    Console.WriteLine("  --help, -h           Show this help message");
    Console.WriteLine();
    Console.WriteLine("Examples:");
    Console.WriteLine("  dotnet run");
    Console.WriteLine("  dotnet run -- --students 500 --courses 100");
    Console.WriteLine("  dotnet run -- --reset --no-confirm");
}
