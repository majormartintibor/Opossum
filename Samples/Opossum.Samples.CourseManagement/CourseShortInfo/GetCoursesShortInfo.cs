using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Shared;

namespace Opossum.Samples.CourseManagement.CourseShortInfo;

public sealed record GetCoursesShortInfoQuery(
    int PageNumber = 1,
    int PageSize = 50,
    bool? IsFull = null,
    string? SortBy = null,  // "name", "enrollmentCount", "capacity"
    bool IncludeEnrollmentCounts = false  // Opt-in for enrollment counts
) : PaginationQuery
{
    public new int PageNumber { get; init; } = PageNumber;
    public new int PageSize { get; init; } = PageSize;
}

public sealed record GetCourseShortInfoCommand(Guid CourseId);

public sealed record CourseShortInfo(
    Guid CourseId,
    string Name,
    int MaxStudentCount,
    int CurrentEnrollmentCount)
{
    public bool IsFull => CurrentEnrollmentCount >= MaxStudentCount;
};

public static class Endpoint
{
    public static void MapGetCoursesShortInfoEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /courses - List all courses with pagination and filtering
        app.MapGet("/courses", async (
            [FromQuery] int pageNumber,
            [FromQuery] int pageSize,
            [FromQuery] bool? isFull,
            [FromQuery] string? sortBy,
            [FromServices] IMediator mediator) =>
        {
            // Apply defaults if not provided
            var query = new GetCoursesShortInfoQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                IsFull: isFull,
                SortBy: sortBy
            );

            var commandResult = await mediator.InvokeAsync<CommandResult<PaginatedResponse<CourseShortInfo>>>(query);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetCourses")
        .WithTags("Queries");

        // GET /courses/{courseId} - Get single course
        app.MapGet("/courses/{courseId:guid}", async (
            Guid courseId,
            [FromServices] IMediator mediator) =>
        {
            var command = new GetCourseShortInfoCommand(courseId);
            var commandResult = await mediator.InvokeAsync<CommandResult<CourseShortInfo>>(command);

            if (!commandResult.Success)
            {
                return Results.NotFound(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetCourseById")
        .WithTags("Queries");
    }
}

public sealed class GetCoursesShortInfoCommandHandler()
{
    public async Task<CommandResult<PaginatedResponse<CourseShortInfo>>> HandleAsync(
        GetCoursesShortInfoQuery query,
        IProjectionStore<CourseShortInfo> projectionStore)
    {
        // Step 1: Load all courses from projection store
        var allCourses = await projectionStore.GetAllAsync();

        // Step 2: Apply filters
        var filteredCourses = allCourses.AsEnumerable();

        if (query.IsFull.HasValue)
        {
            filteredCourses = filteredCourses.Where(c => c.IsFull == query.IsFull.Value);
        }

        // Step 3: Apply sorting
        filteredCourses = query.SortBy?.ToLowerInvariant() switch
        {
            "enrollmentcount" => filteredCourses.OrderByDescending(c => c.CurrentEnrollmentCount),
            "capacity" => filteredCourses.OrderByDescending(c => c.MaxStudentCount),
            "name" => filteredCourses.OrderBy(c => c.Name),
            _ => filteredCourses.OrderBy(c => c.Name) // Default sort by name
        };

        var sortedCourses = filteredCourses.ToList();

        // Step 4: Apply pagination
        var totalCount = sortedCourses.Count;
        var paginatedCourses = sortedCourses
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        // Step 5: Return paginated response
        var response = new PaginatedResponse<CourseShortInfo>
        {
            Items = paginatedCourses,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };

        return CommandResult<PaginatedResponse<CourseShortInfo>>.Ok(response);
    }
}

public sealed class GetCourseShortInfoCommandHandler()
{
    public async Task<CommandResult<CourseShortInfo>> HandleAsync(
        GetCourseShortInfoCommand command,
        IProjectionStore<CourseShortInfo> projectionStore)
    {
        var course = await projectionStore.GetAsync(command.CourseId.ToString());

        if (course == null)
        {
            return CommandResult<CourseShortInfo>.Fail($"Course with ID {command.CourseId} not found.");
        }

        return CommandResult<CourseShortInfo>.Ok(course);
    }
}
