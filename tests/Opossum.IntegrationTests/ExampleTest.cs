using Opossum.Core;
using Opossum.Mediator;
using System.Windows.Input;

namespace Opossum.IntegrationTests;

public class ExampleTest(OpossumFixture fixture) : IClassFixture<OpossumFixture>
{
    private IMediator _mediator = fixture.Mediator;
    private IEventStore _eventStore = fixture.EventStore;

    [Fact]
    public async Task Example()
    {
        //TODO: figure out how to create Query objects
        Query enlistStudentToCourseQuery = new();

        var studentId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        var command = new EnlistStudentToCourseCommand(courseId, studentId);

        var result = await _mediator.InvokeAsync<CommandResult>(command);
        Assert.True(result.Success);

        var events = await _eventStore.ReadAsync(enlistStudentToCourseQuery, []);
        Assert.NotEmpty(events);
        Assert.Single(events);
        //Assert event type and event data

        //TODO: assert building CourseEnlistmentAggregate
    }
}

public record EnlistStudentToCourseCommand(Guid CourseId, Guid StudentId);
public record CommandResult(bool Success);