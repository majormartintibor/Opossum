namespace Opossum.Samples.CourseManagement.Shared;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SortOrder
{
    Ascending,
    Descending
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum StudentSortField
{
    Name,
    EnrollmentTier,
    EnrollmentCount
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CourseSortField
{
    Name,
    EnrollmentCount,
    Capacity
}
