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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CourseBookSortField
{
    Title,
    Author,
    Price
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvoiceSortField
{
    InvoiceNumber,
    Amount,
    IssuedAt
}
