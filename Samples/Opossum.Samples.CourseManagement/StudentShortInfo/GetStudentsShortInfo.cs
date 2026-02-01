using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.Shared;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentShortInfo;

public sealed record GetStudentsShortInfoQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Tier? TierFilter = null,
    bool? IsMaxedOut = null,
    string? SortBy = null,  // "tier", "enrollmentCount", "name"
    bool IncludeEnrollmentCounts = false  // NEW: Opt-in for enrollment counts
) : PaginationQuery
{
    public new int PageNumber { get; init; } = PageNumber;
    public new int PageSize { get; init; } = PageSize;
}

public sealed record GetStudentShortInfoCommand(Guid StudentId);

public sealed record StudentShortInfo(
    Guid StudentId, 
    string FirstName, 
    string LastName, 
    string Email, 
    Tier EnrollmentTier,
    int CurrentEnrollmentCount,
    int MaxEnrollmentCount)
{
    public bool IsMaxedOut => CurrentEnrollmentCount >= MaxEnrollmentCount;
};

public static class Endpoint
{
    public static void MapGetStudentsShortInfoEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /students - List all students with pagination and filtering
        app.MapGet("/students", async (
            [FromQuery] int pageNumber,
            [FromQuery] int pageSize,
            [FromQuery] Tier? tierFilter,
            [FromQuery] bool? isMaxedOut,
            [FromQuery] string? sortBy,
            [FromQuery] bool includeEnrollmentCounts,
            [FromServices] IMediator mediator) =>
        {
            // Apply defaults if not provided
            var query = new GetStudentsShortInfoQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                TierFilter: tierFilter,
                IsMaxedOut: isMaxedOut,
                SortBy: sortBy,
                IncludeEnrollmentCounts: includeEnrollmentCounts
            );

            var commandResult = await mediator.InvokeAsync<CommandResult<PaginatedResponse<StudentShortInfo>>>(query);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetStudents")
        .WithTags("Queries");

        // GET /students/{studentId} - Get single student
        app.MapGet("/students/{studentId:guid}", async (
            Guid studentId,
            [FromServices] IMediator mediator) =>
        {
            var command = new GetStudentShortInfoCommand(studentId);
            var commandResult = await mediator.InvokeAsync<CommandResult<StudentShortInfo>>(command);

            if (!commandResult.Success)
            {
                return Results.NotFound(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetStudentById")
        .WithTags("Queries");
    }
}

public sealed class GetStudentsShortInfoCommandHandler()
{
    public async Task<CommandResult<PaginatedResponse<StudentShortInfo>>> HandleAsync(
        GetStudentsShortInfoQuery query,
        IProjectionStore<StudentShortInfo> projectionStore)
    {
        // Step 1: Load all students from projection store
        var allStudents = await projectionStore.GetAllAsync();

        // Step 2: Apply filters
        var filteredStudents = allStudents.AsEnumerable();

        if (query.TierFilter.HasValue)
        {
            filteredStudents = filteredStudents.Where(s => s.EnrollmentTier == query.TierFilter.Value);
        }

        if (query.IsMaxedOut.HasValue)
        {
            filteredStudents = filteredStudents.Where(s => s.IsMaxedOut == query.IsMaxedOut.Value);
        }

        // Step 3: Apply sorting
        filteredStudents = query.SortBy?.ToLowerInvariant() switch
        {
            "tier" => filteredStudents.OrderBy(s => s.EnrollmentTier),
            "enrollmentcount" => filteredStudents.OrderByDescending(s => s.CurrentEnrollmentCount),
            "name" => filteredStudents.OrderBy(s => s.LastName).ThenBy(s => s.FirstName),
            _ => filteredStudents.OrderBy(s => s.LastName).ThenBy(s => s.FirstName) // Default sort by name
        };

        var sortedStudents = filteredStudents.ToList();

        // Step 4: Apply pagination
        var totalCount = sortedStudents.Count;
        var paginatedStudents = sortedStudents
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        // Step 5: Return paginated response
        var response = new PaginatedResponse<StudentShortInfo>
        {
            Items = paginatedStudents,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };

        return CommandResult<PaginatedResponse<StudentShortInfo>>.Ok(response);
    }
}

public sealed class GetStudentShortInfoCommandHandler()
{
    public async Task<CommandResult<StudentShortInfo>> HandleAsync(
        GetStudentShortInfoCommand command,
        IProjectionStore<StudentShortInfo> projectionStore)
    {
        var student = await projectionStore.GetAsync(command.StudentId.ToString());

        if (student == null)
        {
            return CommandResult<StudentShortInfo>.Fail($"Student with ID {command.StudentId} not found.");
        }

        return CommandResult<StudentShortInfo>.Ok(student);
    }
}
