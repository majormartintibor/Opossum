using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Shared;

namespace Opossum.Samples.CourseManagement.CourseShortInfo;

public sealed record GetCoursesShortInfoQuery(
    int PageNumber = 1,
    int PageSize = 50,
    bool? IsFull = null,
    CourseSortField SortBy = CourseSortField.Name,
    SortOrder SortOrder = SortOrder.Ascending,
    bool IncludeEnrollmentCounts = false
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
            int pageNumber,
            int pageSize,
            bool? isFull,
            CourseSortField sortBy,
            SortOrder sortOrder,
            IMediator mediator) =>
        {
            var query = new GetCoursesShortInfoQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                IsFull: isFull,
                SortBy: sortBy,
                SortOrder: sortOrder
            );

            var commandResult = await mediator.InvokeAsync<CommandResult<PaginatedResponse<CourseShortInfo>>>(query);

            if (!commandResult.Success)
            {
                return Results.BadRequest(commandResult.ErrorMessage);
            }

            return Results.Ok(commandResult.Value);
        })
        .WithName("GetCourses")
        .WithTags("Queries")
        .WithOpenApi(operation =>
        {
            operation.Parameters[0].Description = "Page number (default: 1)";
            operation.Parameters[1].Description = "Page size (default: 50)";
            operation.Parameters[3].Description = "Sort field (default: Name)";
            operation.Parameters[4].Description = "Sort order (default: Ascending)";
            return operation;
        });

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
        var sortedCourses = query.SortBy switch
        {
            CourseSortField.EnrollmentCount => query.SortOrder == SortOrder.Ascending
                ? filteredCourses.OrderBy(c => c.CurrentEnrollmentCount)
                : filteredCourses.OrderByDescending(c => c.CurrentEnrollmentCount),

            CourseSortField.Capacity => query.SortOrder == SortOrder.Ascending
                ? filteredCourses.OrderBy(c => c.MaxStudentCount)
                : filteredCourses.OrderByDescending(c => c.MaxStudentCount),

            CourseSortField.Name => query.SortOrder == SortOrder.Ascending
                ? filteredCourses.OrderBy(c => c.Name)
                : filteredCourses.OrderByDescending(c => c.Name),

            _ => filteredCourses.OrderBy(c => c.Name)
        };

        var courseList = sortedCourses.ToList();

        // Step 4: Apply pagination
        var totalCount = courseList.Count;
        var paginatedCourses = courseList
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
