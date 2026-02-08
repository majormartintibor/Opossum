using Tier = Opossum.Samples.CourseManagement.EnrollmentTier.EnrollmentTier;

namespace Opossum.Samples.CourseManagement.Events;

public sealed record StudentSubscriptionUpdatedEvent(
    Guid StudentId,
    Tier EnrollmentTier) : IEvent;
