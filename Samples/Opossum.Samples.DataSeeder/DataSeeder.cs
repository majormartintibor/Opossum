using Microsoft.Extensions.DependencyInjection;
using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.DataSeeder;

public class DataSeeder
{
    private readonly IEventStore _eventStore;
    private readonly IMediator _mediator;
    private readonly SeedingConfiguration _config;
    private readonly string _rootPath;
    private readonly string _contextName;
    private readonly Random _random = new(42); // Fixed seed for reproducibility

    private readonly List<(Guid StudentId, Tier Tier, int MaxCourses)> _students = [];
    private readonly List<(Guid CourseId, string Name, int MaxCapacity)> _courses = [];

    public int TotalEventsCreated { get; private set; }

    public DataSeeder(IServiceProvider serviceProvider, SeedingConfiguration config, string rootPath, string contextName)
    {
        _eventStore = serviceProvider.GetRequiredService<IEventStore>();
        _mediator = serviceProvider.GetRequiredService<IMediator>();
        _config = config;
        _rootPath = rootPath;
        _contextName = contextName;
    }

    public async Task SeedAsync()
    {
        if (_config.ResetDatabase)
        {
            ResetDatabase();
        }

        Console.WriteLine("\n📝 Phase 1: Registering students...");
        await SeedStudentsAsync();

        Console.WriteLine("\n📚 Phase 2: Creating courses...");
        await SeedCoursesAsync();

        Console.WriteLine("\n⬆️  Phase 3: Upgrading student tiers...");
        await SeedTierUpgradesAsync();

        Console.WriteLine("\n📏 Phase 4: Modifying course capacities...");
        await SeedCapacityChangesAsync();

        Console.WriteLine("\n🎓 Phase 5: Enrolling students in courses...");
        await SeedEnrollmentsAsync();
    }

    private void ResetDatabase()
    {
        var dbPath = Path.Combine(_rootPath, _contextName);

        if (Directory.Exists(dbPath))
        {
            Console.WriteLine($"🗑️  Deleting: {dbPath}");
            Directory.Delete(dbPath, recursive: true);
            Console.WriteLine("✅ Database cleared.");
        }
        else
        {
            Console.WriteLine("ℹ️  No existing database to clear.");
        }
    }

    private async Task SeedStudentsAsync()
    {
        var tierDistribution = CalculateTierDistribution();
        int studentIndex = 1;

        foreach (var (tier, count) in tierDistribution)
        {
            for (int i = 0; i < count; i++)
            {
                var studentId = Guid.NewGuid();
                var firstName = GetFirstName();
                var lastName = GetLastName();
                var email = $"{firstName.ToLower()}.{lastName.ToLower()}@privateschool.edu";

                var @event = new StudentRegisteredEvent(studentId, firstName, lastName, email)
                    .ToDomainEvent()
                    .WithTag("studentEmail", email)
                    .WithTag("studentId", studentId.ToString())
                    .WithTimestamp(GetRandomPastTimestamp(365, 180)); // 6-12 months ago

                await _eventStore.AppendAsync(@event);
                TotalEventsCreated++;
                // OPTIMIZATION REMOVED: Task.Delay(1) - let optimized file I/O handle concurrency

                _students.Add((studentId, tier, GetMaxCoursesForTier(tier)));

                if (studentIndex % 50 == 0)
                {
                    Console.Write($"   Registered {studentIndex}/{_config.StudentCount} students...\r");
                }

                studentIndex++;
            }
        }

        Console.WriteLine($"   ✅ Registered {_config.StudentCount} students.       ");
    }

    private async Task SeedCoursesAsync()
    {
        var sizeDistribution = CalculateCourseSizeDistribution();
        int courseIndex = 1;

        foreach (var (sizeCategory, minCapacity, maxCapacity, count) in sizeDistribution)
        {
            for (int i = 0; i < count; i++)
            {
                var courseId = Guid.NewGuid();
                var courseName = GetCourseName(sizeCategory);
                var capacity = _random.Next(minCapacity, maxCapacity + 1);
                var description = $"{sizeCategory} course with capacity for {capacity} students.";

                var @event = new CourseCreatedEvent(courseId, courseName, description, capacity)
                    .ToDomainEvent()
                    .WithTag("courseId", courseId.ToString())
                    .WithTimestamp(GetRandomPastTimestamp(365, 200)); // 7-12 months ago

                await _eventStore.AppendAsync(@event);
                TotalEventsCreated++;
                // OPTIMIZATION REMOVED: Task.Delay(1) - let optimized file I/O handle concurrency

                _courses.Add((courseId, courseName, capacity));

                if (courseIndex % 10 == 0)
                {
                    Console.Write($"   Created {courseIndex}/{_config.CourseCount} courses...\r");
                }

                courseIndex++;
            }
        }

        Console.WriteLine($"   ✅ Created {_config.CourseCount} courses.       ");
    }

    private async Task SeedTierUpgradesAsync()
    {
        // Upgrade ~30% of students to higher tiers
        var upgradeCount = (int)(_students.Count * 0.3);
        var studentsToUpgrade = _students
            .Where(s => s.Tier != Tier.Master) // Can't upgrade Master tier
            .OrderBy(_ => _random.Next())
            .Take(upgradeCount)
            .ToList();

        int upgraded = 0;
        foreach (var (studentId, currentTier, _) in studentsToUpgrade)
        {
            var newTier = GetNextTier(currentTier);

            var @event = new StudentSubscriptionUpdatedEvent(studentId, newTier)
                .ToDomainEvent()
                .WithTag("studentId", studentId.ToString())
                .WithTimestamp(GetRandomPastTimestamp(180, 30)); // 1-6 months ago

            await _eventStore.AppendAsync(@event);
            TotalEventsCreated++;
            // OPTIMIZATION REMOVED: Task.Delay(1) - let optimized file I/O handle concurrency

            // Update in-memory list
            var index = _students.FindIndex(s => s.StudentId == studentId);
            if (index >= 0)
            {
                _students[index] = (studentId, newTier, GetMaxCoursesForTier(newTier));
            }

            upgraded++;
        }

        Console.WriteLine($"   ✅ Upgraded {upgraded} student subscriptions.");
    }

    private async Task SeedCapacityChangesAsync()
    {
        // Modify ~20% of course capacities
        var modifyCount = (int)(_courses.Count * 0.2);
        var coursesToModify = _courses
            .OrderBy(_ => _random.Next())
            .Take(modifyCount)
            .ToList();

        int modified = 0;
        foreach (var (courseId, _, currentCapacity) in coursesToModify)
        {
            // Increase or decrease capacity by 5-10 students
            var change = _random.Next(-10, 11);
            var newCapacity = Math.Max(10, currentCapacity + change); // Min capacity of 10

            var @event = new CourseStudentLimitModifiedEvent(courseId, newCapacity)
                .ToDomainEvent()
                .WithTag("courseId", courseId.ToString())
                .WithTimestamp(GetRandomPastTimestamp(150, 60)); // 2-5 months ago

            await _eventStore.AppendAsync(@event);
            TotalEventsCreated++;
            // OPTIMIZATION REMOVED: Task.Delay(1) - let optimized file I/O handle concurrency

            // Update in-memory list
            var index = _courses.FindIndex(c => c.CourseId == courseId);
            if (index >= 0)
            {
                var (id, name, _) = _courses[index];
                _courses[index] = (id, name, newCapacity);
            }

            modified++;
        }

        Console.WriteLine($"   ✅ Modified {modified} course capacities.");
    }

    private async Task SeedEnrollmentsAsync()
    {
        int totalEnrollments = 0;
        int attempts = 0;
        int skippedDuplicates = 0;
        int skippedCapacity = 0;
        int skippedStudentLimit = 0;

        // Track enrollments per course and per student
        var courseEnrollments = _courses.ToDictionary(c => c.CourseId, _ => 0);
        var studentEnrollments = _students.ToDictionary(s => s.StudentId, _ => 0);

        // Track unique student-course pairs to prevent duplicates
        var enrolledPairs = new HashSet<(Guid StudentId, Guid CourseId)>();

        // Aim for ~5 enrollments per student on average
        var targetEnrollments = _config.StudentCount * 5;

        Console.WriteLine($"   🎯 Target: {targetEnrollments} enrollments");

        // OPTIMIZATION: Use smart selection instead of random picking
        // Build lists of available students and courses, removing them as they fill up
        var availableStudents = _students.ToList();
        var availableCourses = _courses.ToList();

        while (totalEnrollments < targetEnrollments && availableStudents.Count > 0 && availableCourses.Count > 0)
        {
            attempts++;

            // OPTIMIZATION: Pick student with fewest enrollments first (better distribution)
            var student = availableStudents
                .OrderBy(s => studentEnrollments[s.StudentId])
                .ThenBy(_ => _random.Next()) // Randomize within same enrollment count
                .First();

            // OPTIMIZATION: Pick course with most available capacity first (better utilization)
            var course = availableCourses
                .OrderByDescending(c => c.MaxCapacity - courseEnrollments[c.CourseId])
                .ThenBy(_ => _random.Next()) // Randomize within same availability
                .First();

            // Check if this pair is already enrolled (DUPLICATE CHECK)
            if (enrolledPairs.Contains((student.StudentId, course.CourseId)))
            {
                skippedDuplicates++;
                // Remove this course from consideration for this student
                availableCourses = [.. availableCourses.Where(c => c.CourseId != course.CourseId)];
                if (availableCourses.Count == 0)
                {
                    availableCourses = [.. _courses]; // Reset
                    availableStudents.Remove(student); // This student is done
                }
                continue; // Try again
            }

            // Check if student can enroll
            if (studentEnrollments[student.StudentId] >= student.MaxCourses)
            {
                skippedStudentLimit++;
                availableStudents.Remove(student); // Student at limit, remove from pool
                continue;
            }

            // Check if course has capacity
            if (courseEnrollments[course.CourseId] >= course.MaxCapacity)
            {
                skippedCapacity++;
                availableCourses.Remove(course); // Course full, remove from pool
                continue;
            }

            // Enroll student using COMMAND HANDLER (enforces business rules!)
            var command = new EnrollStudentToCourseCommand(course.CourseId, student.StudentId);
            var result = await _mediator.InvokeAsync<CommandResult>(command);

            if (!result.Success)
            {
                // Command rejected - this should rarely happen with our smart selection
                // but can occur due to race conditions or if our tracking is slightly off
                if (result.ErrorMessage?.Contains("already enrolled") == true)
                {
                    skippedDuplicates++;
                }
                else if (result.ErrorMessage?.Contains("capacity") == true)
                {
                    skippedCapacity++;
                    availableCourses.Remove(course);
                }
                else if (result.ErrorMessage?.Contains("enrollment limit") == true)
                {
                    skippedStudentLimit++;
                    availableStudents.Remove(student);
                }
                continue; // Command failed, try next
            }

            TotalEventsCreated++;

            // OPTIMIZATION REMOVED: No more Task.Delay(1) - let parallel file I/O handle it
            // The optimized EventFileManager with parallel reads can handle concurrent writes better

            // Update tracking
            courseEnrollments[course.CourseId]++;
            studentEnrollments[student.StudentId]++;
            enrolledPairs.Add((student.StudentId, course.CourseId));
            totalEnrollments++;

            if (totalEnrollments % 100 == 0)
            {
                Console.Write($"   Enrolled {totalEnrollments}/{targetEnrollments} (attempts: {attempts}, skipped: {skippedDuplicates + skippedCapacity + skippedStudentLimit})...\r");
            }
        }

        Console.WriteLine($"   ✅ Created {totalEnrollments} enrollments in {attempts} attempts.                    ");
        Console.WriteLine($"      Skipped - Duplicates: {skippedDuplicates}, Capacity: {skippedCapacity}, Student Limit: {skippedStudentLimit}");
        Console.WriteLine($"      💡 Efficiency: {(double)totalEnrollments / attempts * 100:F1}% successful enrollments");
    }

    // ============================================================================
    // Helper Methods - Tier Distribution
    // ============================================================================

    private List<(Tier Tier, int Count)> CalculateTierDistribution()
    {
        return
        [
            (Tier.Basic, _config.StudentCount * _config.BasicTierPercentage / 100),
            (Tier.Standard, _config.StudentCount * _config.StandardTierPercentage / 100),
            (Tier.Professional, _config.StudentCount * _config.ProfessionalTierPercentage / 100),
            (Tier.Master, _config.StudentCount * _config.MasterTierPercentage / 100)
        ];
    }

    private List<(string Category, int MinCap, int MaxCap, int Count)> CalculateCourseSizeDistribution()
    {
        return
        [
            ("Small", 10, 15, _config.CourseCount * _config.SmallCoursePercentage / 100),
            ("Medium", 20, 30, _config.CourseCount * _config.MediumCoursePercentage / 100),
            ("Large", 40, 60, _config.CourseCount * _config.LargeCoursePercentage / 100)
        ];
    }

    private static int GetMaxCoursesForTier(Tier tier) => tier switch
    {
        Tier.Basic => 2,
        Tier.Standard => 5,
        Tier.Professional => 10,
        Tier.Master => 25,
        _ => 2
    };

    private static Tier GetNextTier(Tier currentTier) => currentTier switch
    {
        Tier.Basic => Tier.Standard,
        Tier.Standard => Tier.Professional,
        Tier.Professional => Tier.Master,
        Tier.Master => Tier.Master,
        _ => Tier.Basic
    };

    // ============================================================================
    // Helper Methods - Data Generation
    // ============================================================================

    private string GetFirstName()
    {
        string[] names = ["Emma", "Liam", "Olivia", "Noah", "Ava", "Ethan", "Sophia", "Mason",
            "Isabella", "William", "Mia", "James", "Charlotte", "Benjamin", "Amelia", "Lucas",
            "Harper", "Henry", "Evelyn", "Alexander", "Abigail", "Michael", "Emily", "Daniel",
            "Elizabeth", "Matthew", "Sofia", "Jackson", "Avery", "Sebastian", "Ella", "David",
            "Scarlett", "Joseph", "Grace", "Carter", "Chloe", "Owen", "Victoria", "Wyatt",
            "Riley", "John", "Aria", "Jack", "Lily", "Luke", "Aubrey", "Jayden", "Zoey"];
        return names[_random.Next(names.Length)];
    }

    private string GetLastName()
    {
        string[] names = ["Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller",
            "Davis", "Rodriguez", "Martinez", "Hernandez", "Lopez", "Gonzalez", "Wilson",
            "Anderson", "Thomas", "Taylor", "Moore", "Jackson", "Martin", "Lee", "Perez",
            "Thompson", "White", "Harris", "Sanchez", "Clark", "Ramirez", "Lewis", "Robinson"];
        return names[_random.Next(names.Length)];
    }

    private string GetCourseName(string sizeCategory)
    {
        var subjects = sizeCategory switch
        {
            "Small" => new[] { "Advanced Poetry", "Latin Prose", "Music Theory", "Studio Art", "Philosophy Seminar",
                "Creative Writing", "Ethics Debate", "Ancient Greek", "Jazz Ensemble", "Shakespeare Studies" },
            "Medium" => [ "World History", "Chemistry", "Algebra II", "English Literature", "Biology",
                "Spanish I", "Physics", "U.S. History", "Geometry", "French II", "Psychology", "Economics" ],
            "Large" => [ "Introduction to Computer Science", "Physical Education", "Health & Wellness",
                "Public Speaking", "College Prep Math", "SAT Preparation" ],
            _ => ["General Studies"]
        };

        var subject = subjects[_random.Next(subjects.Length)];
        var level = _random.Next(1, 4); // Levels 1-3

        return sizeCategory == "Large" ? subject : $"{subject} - Level {level}";
    }

    private DateTimeOffset GetRandomPastTimestamp(int daysAgo, int daysAgoMin)
    {
        var days = _random.Next(daysAgoMin, daysAgo + 1);
        return DateTimeOffset.UtcNow.AddDays(-days);
    }
}
