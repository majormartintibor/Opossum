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
    SortOrder SortOrder = SortOrder.Ascending,
    bool IncludeEnrollmentCounts = false
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
            bool includeEnrollmentCounts,
            IMediator mediator) =>
        {
            var query = new GetStudentsShortInfoQuery(
                PageNumber: pageNumber > 0 ? pageNumber : 1,
                PageSize: pageSize > 0 ? pageSize : 50,
                TierFilter: tierFilter,
                IsMaxedOut: isMaxedOut,
                SortBy: sortBy,
                SortOrder: sortOrder,
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
        var sortedStudents = query.SortBy switch
        {
            StudentSortField.EnrollmentTier => query.SortOrder == SortOrder.Ascending
                ? filteredStudents.OrderBy(s => s.EnrollmentTier)
                : filteredStudents.OrderByDescending(s => s.EnrollmentTier),

            StudentSortField.EnrollmentCount => query.SortOrder == SortOrder.Ascending
                ? filteredStudents.OrderBy(s => s.CurrentEnrollmentCount)
                : filteredStudents.OrderByDescending(s => s.CurrentEnrollmentCount),

            StudentSortField.Name => query.SortOrder == SortOrder.Ascending
                ? filteredStudents.OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
                : filteredStudents.OrderByDescending(s => s.LastName).ThenByDescending(s => s.FirstName),

            _ => filteredStudents.OrderBy(s => s.LastName).ThenBy(s => s.FirstName)
        };

        var studentList = sortedStudents.ToList();

        // Step 4: Apply pagination
        var totalCount = studentList.Count;
        var paginatedStudents = studentList
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
