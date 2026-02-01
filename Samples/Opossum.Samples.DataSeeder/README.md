# Opossum Baseline Data Seeder

## Overview

The **Opossum.Samples.DataSeeder** is a console application that generates realistic baseline data for testing the Opossum event sourcing library at production scale. It simulates a 1-year operational history of a private school course management system.

## Purpose

After completing the sample application development, this seeder enables:
- **Performance Testing**: Evaluate query performance with ~2,295 events
- **Index Validation**: Verify EventType and Tag indices work correctly at scale
- **Ledger Validation**: Ensure sequence position tracking remains consistent
- **Concurrency Testing**: Test DCB (Dynamic Consistency Boundary) pattern with realistic data
- **Baseline Dataset**: Provide reproducible test data for integration tests

## Data Model

### Entities Created

| Entity | Count | Event Types Generated |
|--------|-------|----------------------|
| **Students** | 350 | StudentRegisteredEvent (350) |
| **Courses** | 75 | CourseCreatedEvent (75) |
| **Tier Upgrades** | ~105 (30%) | StudentSubscriptionUpdatedEvent (~105) |
| **Capacity Changes** | ~15 (20%) | CourseStudentLimitModifiedEvent (~15) |
| **Enrollments** | ~1,750 (avg 5/student) | StudentEnrolledToCourseEvent (~1,750) |

**Total Events: ~2,295**

### Temporal Distribution

The seeder creates events with realistic timestamps simulating a 1-year operational period:

```
Timeline (365 days ago → Today)
├─ [365-180 days] Student Registrations (6-12 months ago)
├─ [365-200 days] Course Creation (7-12 months ago)
├─ [180-30 days]  Tier Upgrades (1-6 months ago)
├─ [150-60 days]  Capacity Changes (2-5 months ago)
└─ [120-1 days]   Student Enrollments (recent activity)
```

This creates:
- Older base data (students/courses established months ago)
- Medium-age modifications (tier upgrades, capacity changes)
- Recent activity (enrollments in last 4 months)

### Student Distribution by Tier

| Tier | Percentage | Count | Max Courses |
|------|-----------|-------|-------------|
| Basic | 20% | 70 | 2 |
| Standard | 40% | 140 | 4 |
| Professional | 30% | 105 | 6 |
| Master | 10% | 35 | 10 |

### Course Distribution by Size

| Size Category | Percentage | Count | Capacity Range |
|---------------|-----------|-------|----------------|
| Small | 27% | 20 | 10-15 students |
| Medium | 53% | 40 | 20-30 students |
| Large | 20% | 15 | 40-60 students |

## Usage

### Basic Usage (Default Settings)

```bash
cd Samples/Opossum.Samples.DataSeeder
dotnet run
```

This creates:
- 350 students
- 75 courses
- ~2,295 total events
- Database at: `D:\Database\OpossumSampleApp`

### Custom Configuration

```bash
# Create more data
dotnet run -- --students 500 --courses 100

# Reset database before seeding
dotnet run -- --reset

# Skip confirmation prompt (CI/automation)
dotnet run -- --reset --no-confirm

# Combine options
dotnet run -- --students 1000 --courses 200 --reset --no-confirm
```

### Command Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `--students <count>` | Number of students to create | 350 |
| `--courses <count>` | Number of courses to create | 75 |
| `--reset` | Delete existing database before seeding | false |
| `--no-confirm` | Skip confirmation prompt | false |
| `--help`, `-h` | Display help message | - |

## Seeding Phases

The seeder executes in **5 sequential phases** to create a realistic event history:

### Phase 1: Student Registration
- Creates `StudentCount` students (default: 350)
- Generates realistic names from predefined lists
- Creates email addresses: `{firstname}.{lastname}@school.edu`
- Assigns tier based on distribution percentages
- Timestamps: 6-12 months ago
- Tags: Each event tagged with `studentId`

**Progress:** Reports every 50 students

### Phase 2: Course Creation
- Creates `CourseCount` courses (default: 75)
- Generates course names based on size category
  - Small: Subject-based (e.g., "Mathematics", "Physics")
  - Medium/Large: Subject + Level (e.g., "Chemistry - Level 2")
- Assigns capacity based on size distribution
- Timestamps: 7-12 months ago
- Tags: Each event tagged with `courseId`

**Progress:** Reports every 10 courses

### Phase 3: Tier Upgrades
- Upgrades ~30% of students to next tier
- Students progress through tiers: Basic → Standard → Professional → Master
- Cannot upgrade Master tier students
- Timestamps: 1-6 months ago
- Tags: Each event tagged with `studentId`

**Purpose:** Creates multiple events per student aggregate

### Phase 4: Capacity Changes
- Modifies capacity for ~20% of courses
- Increases/decreases capacity within size category range
- Small: ±2 students
- Medium: ±5 students
- Large: ±10 students
- Timestamps: 2-5 months ago
- Tags: Each event tagged with `courseId`

**Purpose:** Creates multiple events per course aggregate

### Phase 5: Student Enrollments
- Enrolls students in courses (avg ~5 enrollments per student)
- Respects business rules:
  - Student tier limits (Basic: 2, Standard: 4, Professional: 6, Master: 10)
  - Course capacity limits
- Tracks enrollments to prevent:
  - Duplicate enrollments (same student in same course)
  - Capacity violations
  - Tier limit violations
- Timestamps: Recent (0-120 days ago)
- Tags: Each event tagged with both `studentId` AND `courseId`

**Progress:** Reports every 100 enrollments

## Expected Storage Structure

After seeding completes, the database will have:

```
D:\Database\OpossumSampleApp\
├── events\                           [2,295 files]
│   ├── 0000000001.json              (First student)
│   ├── 0000000002.json
│   ├── ...
│   └── 0000002295.json              (Last enrollment)
│
├── Indices\
│   ├── EventType\                    [5 files]
│   │   ├── StudentRegisteredEvent.json
│   │   │   → { "Positions": [1, 2, ..., 350] }
│   │   ├── CourseCreatedEvent.json
│   │   │   → { "Positions": [351, 352, ..., 425] }
│   │   ├── StudentSubscriptionUpdatedEvent.json
│   │   │   → { "Positions": [426, ..., 530] }
│   │   ├── CourseStudentLimitModifiedEvent.json
│   │   │   → { "Positions": [531, ..., 545] }
│   │   └── StudentEnrolledToCourseEvent.json
│   │       → { "Positions": [546, ..., 2295] }
│   │
│   └── Tags\                         [425 files]
│       ├── studentId_<guid1>.json   → Multiple positions
│       ├── studentId_<guid2>.json   → Multiple positions
│       ├── ...                       (350 student tag files)
│       ├── courseId_<guid1>.json    → Multiple positions
│       ├── courseId_<guid2>.json    → Multiple positions
│       └── ...                       (75 course tag files)
│
└── .ledger
    → { "LastSequencePosition": 2295, "EventCount": 2295 }
```

**Total Files Created: 2,726**
- 2,295 event files
- 5 EventType index files
- 425 Tag index files (350 students + 75 courses)
- 1 ledger file

## Deterministic Randomization

The seeder uses a **fixed random seed (42)** to ensure:
- ✅ **Reproducible Results**: Same data every run
- ✅ **Consistent Testing**: Reliable baseline for comparisons
- ✅ **Debugging**: Easier to investigate specific scenarios

You can modify the seed in `DataSeeder.cs` if needed:
```csharp
private readonly Random _random = new Random(42); // Change seed here
```

## Validation

### Verify Seeding Success

1. **Check Event Count:**
   ```bash
   # Count event files
   ls D:\Database\OpossumSampleApp\events | Measure-Object
   ```
   Should show ~2,295 files

2. **Check Ledger:**
   ```json
   // D:\Database\OpossumSampleApp\.ledger
   {
     "LastSequencePosition": 2295,
     "EventCount": 2295
   }
   ```

3. **Check EventType Indices:**
   - `Indices/EventType/StudentRegisteredEvent.json` → 350 positions
   - `Indices/EventType/CourseCreatedEvent.json` → 75 positions
   - `Indices/EventType/StudentEnrolledToCourseEvent.json` → ~1,750 positions

4. **Check Tag Indices:**
   ```bash
   # Count student tag files
   ls D:\Database\OpossumSampleApp\Indices\Tags\studentId_*.json | Measure-Object
   # Should show 350 files

   # Count course tag files
   ls D:\Database\OpossumSampleApp\Indices\Tags\courseId_*.json | Measure-Object
   # Should show 75 files
   ```

### Query Through Sample App

Test the seeded data using the sample application endpoints:

```bash
cd Samples/Opossum.Samples.CourseManagement
dotnet run
```

**Query Endpoints:**
- `GET /students` → Should return 350 students
- `GET /students/{studentId}` → Should return student details
- `GET /courses` → Should return 75 courses
- `GET /courses/{courseId}` → Should return course details

## Implementation Details

### Storage Integrity

The seeder uses **only the Opossum public API** (`IEventStore.AppendAsync`), ensuring:
- ✅ All indices are correctly maintained
- ✅ Ledger stays synchronized
- ✅ Atomic operations are respected
- ✅ No risk of corrupted storage

See [STORAGE_ANALYSIS.md](./STORAGE_ANALYSIS.md) for detailed analysis of Opossum's storage implementation.

### Event Builder Pattern

All events are created using the fluent API:

```csharp
var @event = new StudentRegisteredEvent(studentId, firstName, lastName, email)
    .ToDomainEvent()
    .WithTag("studentId", studentId.ToString())
    .WithTimestamp(GetRandomPastTimestamp(365, 180));

await _eventStore.AppendAsync(@event);
```

This ensures:
- Proper event metadata
- Tag assignment for indexing
- Custom timestamps for temporal distribution

### Business Rule Compliance

The seeder respects all business rules defined in the sample application:

1. **Student Tier Limits:**
   - Basic: Max 2 courses
   - Standard: Max 4 courses
   - Professional: Max 6 courses
   - Master: Max 10 courses

2. **Course Capacity Limits:**
   - Cannot enroll more students than capacity
   - Capacity modified in Phase 4 before enrollments

3. **No Duplicate Enrollments:**
   - Tracks enrollments per student-course pair
   - Prevents same student enrolling in same course twice

## Performance Characteristics

### Execution Time

Expected seeding time (default configuration):
- **Phase 1 (350 students):** ~5-10 seconds
- **Phase 2 (75 courses):** ~1-2 seconds
- **Phase 3 (105 upgrades):** ~2-3 seconds
- **Phase 4 (15 changes):** ~1 second
- **Phase 5 (1,750 enrollments):** ~30-60 seconds

**Total: ~40-80 seconds** (depending on disk I/O)

### Disk Space

Expected storage size:
- **Event files:** ~5-10 MB (JSON, ~2-5 KB per event)
- **Indices:** ~50-100 KB (compact JSON arrays)
- **Total:** ~5-15 MB

## Next Steps After Seeding

1. **Test Query Performance:**
   ```bash
   cd Samples/Opossum.Samples.CourseManagement
   dotnet run
   # Query endpoints and measure response times
   ```

2. **Run Integration Tests:**
   ```bash
   cd tests/Opossum.IntegrationTests
   dotnet test
   ```

3. **Test Concurrency:**
   - Use enrollment endpoint with multiple concurrent requests
   - Verify DCB retry logic handles conflicts correctly

4. **Monitor Performance:**
   - Track query response times with ~2,295 events
   - Identify optimization opportunities if needed

## Troubleshooting

### "Database already exists" Error

**Solution:** Use `--reset` flag:
```bash
dotnet run -- --reset
```

### Enrollment Phase Takes Long Time

**Expected:** Phase 5 creates ~1,750 events, which can take 30-60 seconds.

**Progress:** Watch console output (reports every 100 enrollments)

### Different Event Count Each Run

**Issue:** Random seed not fixed or configuration changed.

**Solution:** Verify `DataSeeder.cs` uses fixed seed (42) and same configuration.

## Configuration

Default configuration in `SeedingConfiguration.cs`:

```csharp
public class SeedingConfiguration
{
    public string RootPath { get; set; } = @"D:\Database";
    public int StudentCount { get; set; } = 350;
    public int CourseCount { get; set; } = 75;
    public bool ResetDatabase { get; set; } = false;
    public bool RequireConfirmation { get; set; } = true;

    // Distribution percentages
    public double BasicTierPercentage { get; set; } = 0.20;     // 20%
    public double StandardTierPercentage { get; set; } = 0.40;  // 40%
    public double ProfessionalTierPercentage { get; set; } = 0.30; // 30%
    public double MasterTierPercentage { get; set; } = 0.10;    // 10%

    public double SmallCoursePercentage { get; set; } = 0.27;   // 27%
    public double MediumCoursePercentage { get; set; } = 0.53;  // 53%
    public double LargeCoursePercentage { get; set; } = 0.20;   // 20%

    public int EstimatedEventCount =>
        StudentCount +           // Phase 1: Student registrations
        CourseCount +            // Phase 2: Course creation
        (int)(StudentCount * 0.3) +  // Phase 3: ~30% tier upgrades
        (int)(CourseCount * 0.2) +   // Phase 4: ~20% capacity changes
        (int)(StudentCount * 5);     // Phase 5: ~5 enrollments/student
}
```

## Related Documentation

- [STORAGE_ANALYSIS.md](./STORAGE_ANALYSIS.md) - Deep analysis of Opossum storage implementation
- [../Opossum.Samples.CourseManagement/API_DESIGN_PATTERNS.md](../Opossum.Samples.CourseManagement/API_DESIGN_PATTERNS.md) - API design patterns
- [../Opossum.Samples.CourseManagement/RETRY_LOGIC_DCB.md](../Opossum.Samples.CourseManagement/RETRY_LOGIC_DCB.md) - Concurrency handling with DCB

## License

Part of the Opossum event sourcing library samples.

---

**Ready to Seed! 🌱**

Run `dotnet run` to create your baseline dataset and start testing Opossum at production scale.
