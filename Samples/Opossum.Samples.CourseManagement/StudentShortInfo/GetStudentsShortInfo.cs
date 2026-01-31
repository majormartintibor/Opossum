using Microsoft.AspNetCore.Mvc;
using Opossum.Core;
using Opossum.Extensions;
using Opossum.Mediator;
using Opossum.Samples.CourseManagement.EnrollmentTier;
using Opossum.Samples.CourseManagement.StudentRegistration;
using Opossum.Samples.CourseManagement.StudentSubscription;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.StudentShortInfo;

public sealed record GetStudentsShortInfoRequest();

public sealed record GetStudentsShortInfoCommand();

public sealed record StudentShortInfo(Guid StudentId, string FirstName, string LastName, string Email, Tier EnrollmentTier)
{
    public int MaxCoursesAllowed => StudentMaxCourseEnrollment.GetMaxCoursesAllowed(EnrollmentTier);
};

public static class Endpoint
{
    public static void MapGetStudentsShortInfoEndpoint(this IEndpointRouteBuilder app)
    {
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