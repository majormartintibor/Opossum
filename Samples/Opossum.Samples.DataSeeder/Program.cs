using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Opossum.DependencyInjection;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseEnrollment; // For command handler discovery
using Opossum.Samples.DataSeeder;

Console.WriteLine("🌱 Opossum Baseline Data Seeder");
Console.WriteLine("================================\n");

// Load configuration from CourseManagement sample app (single source of truth)
var courseManagementPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "Opossum.Samples.CourseManagement");
var configuration = new ConfigurationBuilder()
    .SetBasePath(courseManagementPath)
    .AddJsonFile("appsettings.Development.json", optional: false)
    .Build();

// Parse command line arguments
var config = ParseArguments(args);

DisplayConfiguration(config, configuration);

if (!ConfirmSeeding(config))
{
    Console.WriteLine("\n❌ Seeding cancelled.");
    return;
}

// Initialize Opossum Event Store
var serviceProvider = ConfigureServices(configuration);

// Get Opossum configuration values
var rootPath = configuration["Opossum:RootPath"] ?? throw new InvalidOperationException("Opossum:RootPath not found in configuration");
var contexts = configuration.GetSection("Opossum:Contexts").Get<string[]>() ?? throw new InvalidOperationException("Opossum:Contexts not found in configuration");
var contextName = contexts.FirstOrDefault() ?? throw new InvalidOperationException("No context configured in Opossum:Contexts");

// Run seeding
var seeder = new DataSeeder(serviceProvider, config, rootPath, contextName);
await seeder.SeedAsync();

Console.WriteLine("\n✅ Seeding complete!");
Console.WriteLine($"   Total events created: {seeder.TotalEventsCreated}");
Console.WriteLine($"   Database location: {configuration["Opossum:RootPath"]}");
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

static void DisplayConfiguration(SeedingConfiguration config, IConfiguration configuration)
{
    var rootPath = configuration["Opossum:RootPath"];
    var contexts = configuration.GetSection("Opossum:Contexts").Get<string[]>();
    var contextName = contexts?.FirstOrDefault() ?? "N/A";

    Console.WriteLine("Configuration:");
    Console.WriteLine($"  Database Path: {rootPath}");
    Console.WriteLine($"  Context:       {contextName}");
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

static IServiceProvider ConfigureServices(IConfiguration configuration)
{
    var services = new ServiceCollection();

    services.AddOpossum(options =>
    {
        // Add contexts from configuration
        var contexts = configuration.GetSection("Opossum:Contexts").Get<string[]>();
        if (contexts != null)
        {
            foreach (var context in contexts)
            {
                options.UseStore(context);
            }
        }

        // Bind all properties from configuration (RootPath, FlushEventsImmediately, etc.)
        configuration.GetSection("Opossum").Bind(options);
    });

    // Add mediator for command handling (DataSeeder now uses commands instead of direct event append)
    // Scan CourseManagement assembly for command handlers
    services.AddMediator(options =>
    {
        options.Assemblies.Add(typeof(EnrollStudentToCourseCommand).Assembly);
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
