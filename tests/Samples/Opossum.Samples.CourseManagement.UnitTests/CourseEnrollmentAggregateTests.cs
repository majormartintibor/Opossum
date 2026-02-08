using Opossum.Samples.CourseManagement.CourseEnrollment;
using Opossum.Samples.CourseManagement.Events;
using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.UnitTests;

public class CourseEnrollmentAggregateTests
{
    private readonly Guid _courseId = Guid.NewGuid();
    private readonly Guid _studentId = Guid.NewGuid();

    [Fact]
    public void Apply_CourseCreatedEvent_SetsCourseMaxCapacity()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);
        var @event = new CourseCreatedEvent(_courseId, "Math 101", "Basic Math", MaxStudentCount: 30);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(30, result.CourseMaxCapacity);
        Assert.Equal(_courseId, result.CourseId);
    }

    [Fact]
    public void Apply_CourseCreatedEvent_ForDifferentCourse_DoesNotUpdateAggregate()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);
        var differentCourseId = Guid.NewGuid();
        var @event = new CourseCreatedEvent(differentCourseId, "Physics 101", "Basic Physics", MaxStudentCount: 25);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(0, result.CourseMaxCapacity); // Should remain default
        Assert.Same(aggregate, result); // Should return unchanged instance
    }

    [Fact]
    public void Apply_CourseStudentLimitModifiedEvent_UpdatesCourseMaxCapacity()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId)
            .Apply(new CourseCreatedEvent(_courseId, "Test", "Test", MaxStudentCount: 30));
        var @event = new CourseStudentLimitModifiedEvent(_courseId, NewMaxStudentCount: 50);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(50, result.CourseMaxCapacity);
    }

    [Fact]
    public void Apply_StudentRegisteredEvent_SetsBasicTier()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);
        var @event = new StudentRegisteredEvent(_studentId, "John", "Doe", "john@example.com");

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(Tier.Basic, result.StudentEnrollmentTier);
        Assert.Equal(2, result.StudentMaxCourseEnrollmentLimit); // Basic tier allows 2 courses
    }

    [Fact]
    public void Apply_StudentSubscriptionUpdatedEvent_UpdatesStudentTier()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId)
            .Apply(new StudentRegisteredEvent(_studentId, "Test", "User", "test@example.com"));
        var @event = new StudentSubscriptionUpdatedEvent(_studentId, Tier.Professional);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(Tier.Professional, result.StudentEnrollmentTier);
        Assert.Equal(10, result.StudentMaxCourseEnrollmentLimit); // Professional tier allows 10 courses
    }

    [Fact]
    public void Apply_StudentEnrolledToCourseEvent_SameStudentAndCourse_SetsIsAlreadyEnrolled()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);
        var @event = new StudentEnrolledToCourseEvent(_courseId, _studentId);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.True(result.IsStudentAlreadyEnrolledInThisCourse);
    }

    [Fact]
    public void Apply_StudentEnrolledToCourseEvent_SameCourse_IncrementsCourseEnrollmentCount()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);
        var differentStudentId = Guid.NewGuid();
        var @event = new StudentEnrolledToCourseEvent(_courseId, differentStudentId);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(1, result.CourseCurrentEnrollmentCount);
        Assert.False(result.IsStudentAlreadyEnrolledInThisCourse); // Different student
    }

    [Fact]
    public void Apply_StudentEnrolledToCourseEvent_SameStudent_IncrementsStudentEnrollmentCount()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);
        var differentCourseId = Guid.NewGuid();
        var @event = new StudentEnrolledToCourseEvent(differentCourseId, _studentId);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Equal(1, result.StudentCurrentCourseEnrollmentCount);
        Assert.False(result.IsStudentAlreadyEnrolledInThisCourse); // Different course
    }

    [Fact]
    public void Apply_MultipleEnrollmentEvents_CorrectlyAccumulatesCounts()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);

        var student2 = Guid.NewGuid();
        var student3 = Guid.NewGuid();
        var course2 = Guid.NewGuid();

        // Act - Enroll 3 students to same course, and enroll our student to another course
        var result = aggregate
            .Apply(new StudentEnrolledToCourseEvent(_courseId, student2))  // Different student, same course
            .Apply(new StudentEnrolledToCourseEvent(_courseId, student3))  // Different student, same course
            .Apply(new StudentEnrolledToCourseEvent(course2, _studentId)); // Same student, different course

        // Assert
        Assert.Equal(2, result.CourseCurrentEnrollmentCount); // 2 other students enrolled in this course
        Assert.Equal(1, result.StudentCurrentCourseEnrollmentCount); // This student enrolled in 1 other course
        Assert.False(result.IsStudentAlreadyEnrolledInThisCourse); // This student not yet enrolled in this course
    }

    [Fact]
    public void Apply_ComplexScenario_AllEventTypes()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId);

        // Act - Apply a realistic sequence of events
        var result = aggregate
            .Apply(new StudentRegisteredEvent(_studentId, "Jane", "Smith", "jane@example.com"))
            .Apply(new CourseCreatedEvent(_courseId, "Chemistry 101", "Intro to Chemistry", MaxStudentCount: 25))
            .Apply(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Standard))
            .Apply(new CourseStudentLimitModifiedEvent(_courseId, NewMaxStudentCount: 30))
            .Apply(new StudentEnrolledToCourseEvent(_courseId, _studentId));

        // Assert
        Assert.Equal(Tier.Standard, result.StudentEnrollmentTier);
        Assert.Equal(5, result.StudentMaxCourseEnrollmentLimit); // Standard tier
        Assert.Equal(30, result.CourseMaxCapacity);
        Assert.Equal(0, result.CourseCurrentEnrollmentCount); // This specific enrollment doesn't count itself
        Assert.Equal(0, result.StudentCurrentCourseEnrollmentCount); // This specific enrollment doesn't count itself
        Assert.True(result.IsStudentAlreadyEnrolledInThisCourse);
    }

    [Fact]
    public void Apply_UnrelatedEvent_ReturnsUnchangedAggregate()
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId)
            .Apply(new CourseCreatedEvent(_courseId, "Test", "Test", MaxStudentCount: 30))
            .Apply(new StudentRegisteredEvent(_studentId, "Test", "User", "test@example.com"))
            .Apply(new StudentSubscriptionUpdatedEvent(_studentId, Tier.Standard));

        var otherCourse = Guid.NewGuid();
        var otherStudent = Guid.NewGuid();
        var @event = new StudentEnrolledToCourseEvent(otherCourse, otherStudent);

        // Act
        var result = aggregate.Apply(@event);

        // Assert
        Assert.Same(aggregate, result); // Should return same instance unchanged
        Assert.Equal(30, result.CourseMaxCapacity);
        Assert.Equal(Tier.Standard, result.StudentEnrollmentTier);
    }

    [Theory]
    [InlineData(Tier.Basic, 2)]
    [InlineData(Tier.Standard, 5)]
    [InlineData(Tier.Professional, 10)]
    [InlineData(Tier.Master, 25)]
    public void StudentMaxCourseEnrollmentLimit_ReturnsCorrectLimitForTier(Tier tier, int expectedLimit)
    {
        // Arrange
        var aggregate = new CourseEnrollmentAggregate(_courseId, _studentId)
            .Apply(new StudentRegisteredEvent(_studentId, "Test", "User", "test@example.com"))
            .Apply(new StudentSubscriptionUpdatedEvent(_studentId, tier));

        // Act
        var limit = aggregate.StudentMaxCourseEnrollmentLimit;

        // Assert
        Assert.Equal(expectedLimit, limit);
    }
}
