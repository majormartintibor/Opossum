using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentShortInfo;

public sealed record GetStudentsShortInfoCommand();
public sealed record GetStudentShortInfoCommand(Guid StudentId);

public sealed record StudentShortInfo(Guid StudentId, string FirstName, string LastName, string Email, Tier EnrollmentTier)
{
    public int MaxCoursesAllowed => StudentMaxCourseEnrollment.GetMaxCoursesAllowed(EnrollmentTier);
};

public static class Endpoint
{
    public static void MapGetStudentsShortInfoEndpoint(this IEndpointRouteBuilder app)
    {
        // GET /students - List all students
        app.MapGet("/students", async (
            [FromServices] IMediator mediator) =>
        {
            var command = new GetStudentsShortInfoCommand();
            var commandResult = await mediator.InvokeAsync<CommandResult<List<StudentShortInfo>>>(command);

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
    public async Task<CommandResult<List<StudentShortInfo>>> HandleAsync(
        GetStudentsShortInfoCommand command,
        IEventStore eventStore)
    {
        var studentsShortInfoQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [],
                    EventTypes = [nameof(StudentRegisteredEvent), nameof(StudentSubscriptionUpdatedEvent)]
                });

        var events = await eventStore.ReadAsync(studentsShortInfoQuery, ReadOption.None);

        var studentsList = events.BuildProjections<StudentShortInfo>(
            aggregateIdSelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: (evt, current) => evt switch
            {
                StudentRegisteredEvent registered => new StudentShortInfo(
                    registered.StudentId,
                    registered.FirstName,
                    registered.LastName,
                    registered.Email,
                    Tier.Basic),

                StudentSubscriptionUpdatedEvent updated when current != null => 
                    current with { EnrollmentTier = updated.EnrollmentTier },

                _ => current
            }
        ).ToList();

        return CommandResult<List<StudentShortInfo>>.Ok(studentsList);
    }
}

public sealed class GetStudentShortInfoCommandHandler()
{
    public async Task<CommandResult<StudentShortInfo>> HandleAsync(
        GetStudentShortInfoCommand command,
        IEventStore eventStore)
    {
        var studentQuery = Query.FromItems(
                new QueryItem
                {
                    Tags = [new Tag { Key = "studentId", Value = command.StudentId.ToString() }],
                    EventTypes = [nameof(StudentRegisteredEvent), nameof(StudentSubscriptionUpdatedEvent)]
                });

        var events = await eventStore.ReadAsync(studentQuery, ReadOption.None);

        if (events.Length == 0)
        {
            return CommandResult<StudentShortInfo>.Fail($"Student with ID {command.StudentId} not found.");
        }

        var student = events.BuildProjections<StudentShortInfo>(
            aggregateIdSelector: e => e.Event.Tags.First(t => t.Key == "studentId").Value,
            applyEvent: (evt, current) => evt switch
            {
                StudentRegisteredEvent registered => new StudentShortInfo(
                    registered.StudentId,
                    registered.FirstName,
                    registered.LastName,
                    registered.Email,
                    Tier.Basic),

                StudentSubscriptionUpdatedEvent updated when current != null => 
                    current with { EnrollmentTier = updated.EnrollmentTier },

                _ => current
            }
        ).FirstOrDefault();

        if (student == null)
        {
            return CommandResult<StudentShortInfo>.Fail($"Student with ID {command.StudentId} not found.");
        }

        return CommandResult<StudentShortInfo>.Ok(student);
    }
}
