using Opossum.Core;
using Opossum.Mediator;
using Opossum.Projections;
using Opossum.Samples.CourseManagement.Shared;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentShortInfo;

public sealed record GetStudentsShortInfoQuery(
    int PageNumber = 1,
    int PageSize = 50,
    Tier? TierFilter = null,
    bool? IsMaxedOut = null,
    StudentSortField SortBy = StudentSortField.Name,
    SortOrder SortOrder = SortOrder.Ascending
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
            int pageNumber,
            int pageSize,
            Tier? tierFilter,
            bool? isMaxedOut,
            StudentSortField sortBy,
            SortOrder sortOrder,
            IMediator mediator) =>
        {
            var query = new GetStudentsShortInfoQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                TierFilter: tierFilter,
                IsMaxedOut: isMaxedOut,
                SortBy: sortBy,
                SortOrder: sortOrder
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
        // Step 1: Query students using tag indices (much more efficient than GetAllAsync)
        IReadOnlyList<StudentShortInfo> students;

        // Build tag list based on filters
        var tags = new List<Tag>();

        if (query.TierFilter.HasValue)
        {
            tags.Add(new Tag("EnrollmentTier", query.TierFilter.Value.ToString()));
        }

        if (query.IsMaxedOut.HasValue)
        {
            tags.Add(new Tag("IsMaxedOut", query.IsMaxedOut.Value.ToString()));
        }

        // Use tag indices if filters are specified, otherwise get all
        if (tags.Count > 0)
        {
            // Query by tags (AND logic) - only loads matching students from indices
            students = await projectionStore.QueryByTagsAsync(tags);
        }
        else
        {
            // No filters - load all students
            students = await projectionStore.GetAllAsync();
        }

        // Step 2: Apply sorting (in-memory on filtered subset)
        var sortedStudents = query.SortBy switch
        {
            StudentSortField.EnrollmentTier => query.SortOrder == SortOrder.Ascending
                ? students.OrderBy(s => s.EnrollmentTier)
                : students.OrderByDescending(s => s.EnrollmentTier),

            StudentSortField.EnrollmentCount => query.SortOrder == SortOrder.Ascending
                ? students.OrderBy(s => s.CurrentEnrollmentCount)
                : students.OrderByDescending(s => s.CurrentEnrollmentCount),

            StudentSortField.Name => query.SortOrder == SortOrder.Ascending
                ? students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                : students.OrderByDescending(s => s.LastName).ThenByDescending(s => s.FirstName),

            _ => students.OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
        };

        var studentList = sortedStudents.ToList();

        // Step 3: Apply pagination
        var totalCount = studentList.Count;
        var paginatedStudents = studentList
            .Skip((query.PageNumber - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToList();

        // Step 4: Return paginated response
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
