using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.CourseCreation;
using Opossum.Samples.CourseManagement.CourseStudentLimitModification;

namespace Opossum.Samples.CourseManagement.CourseShortInfo;

public sealed record GetCoursesShortInfoCommand();
public sealed record GetCourseShortInfoCommand(Guid CourseId);

public sealed record CourseShortInfo(
    Guid CourseId, 
    string Name,
    int MaxStudentCount);

public static class Endpoint
{
    public static void MapGetCoursesShortInfoEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /courses - List all courses
        app.MapGet("/courses", async (
            [FromServices] IMediator mediator) =>
        {
            var command = new GetCoursesShortInfoCommand();
            var commandResult = await mediator.InvokeAsync<CommandResult<List<CourseShortInfo>>>(command);

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
    public async Task<CommandResult<List<CourseShortInfo>>> HandleAsync(
        GetCoursesShortInfoCommand command,
        IEventStore eventStore)
    {
        var coursesShortInfoQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [],
                    EventTypes = [nameof(CourseCreatedEvent), nameof(CourseStudentLimitModifiedEvent)]
                });

        var events = await eventStore.ReadAsync(coursesShortInfoQuery, ReadOption.None);

        var coursesList = events.BuildProjections<CourseShortInfo>(
            aggregateIdSelector: e => e.Event.Tags.First(t => t.Key == "courseId").Value,
            applyEvent: (evt, current) => evt switch
            {
                CourseCreatedEvent created => new CourseShortInfo(
                    created.CourseId,
                    created.Name,                    
                    created.MaxStudentCount),

                CourseStudentLimitModifiedEvent limitModified when current != null => 
                    current with { MaxStudentCount = limitModified.NewMaxStudentCount },

                _ => current
            }
        ).ToList();

        return CommandResult<List<CourseShortInfo>>.Ok(coursesList);
    }
}

public sealed class GetCourseShortInfoCommandHandler()
{
    public async Task<CommandResult<CourseShortInfo>> HandleAsync(
        GetCourseShortInfoCommand command,
        IEventStore eventStore)
    {
        var courseQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [new Tag { Key = "courseId", Value = command.CourseId.ToString() }],
                    EventTypes = [nameof(CourseCreatedEvent), nameof(CourseStudentLimitModifiedEvent)]
                });

        var events = await eventStore.ReadAsync(courseQuery, ReadOption.None);

        if (events.Length == 0)
        {
            return CommandResult<CourseShortInfo>.Fail($"Course with ID {command.CourseId} not found.");
        }

        var course = events.BuildProjections<CourseShortInfo>(
            aggregateIdSelector: e => e.Event.Tags.First(t => t.Key == "courseId").Value,
            applyEvent: (evt, current) => evt switch
            {
                CourseCreatedEvent created => new CourseShortInfo(
                    created.CourseId,
                    created.Name,                    
                    created.MaxStudentCount),

                CourseStudentLimitModifiedEvent limitModified when current != null => 
                    current with { MaxStudentCount = limitModified.NewMaxStudentCount },

                _ => current
            }
        ).FirstOrDefault();

        if (course == null)
        {
            return CommandResult<CourseShortInfo>.Fail($"Course with ID {command.CourseId} not found.");
        }

        return CommandResult<CourseShortInfo>.Ok(course);
    }
}
