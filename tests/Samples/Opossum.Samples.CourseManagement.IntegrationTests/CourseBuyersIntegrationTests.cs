using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the <c>GET /courses/{courseId}/book-buyers</c> endpoint.
///
/// Verifies that <c>CourseBuyersProjection</c> correctly folds
/// <c>CourseCreatedEvent</c>, <c>CourseBookDefinedEvent</c>,
/// <c>CourseBookPurchasedEvent</c>, and <c>CourseBooksOrderedEvent</c>
/// into a per-course buyer list keyed by <c>courseId</c>.
/// </summary>
[Collection("Integration Tests")]
public class CourseBuyersIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CourseBuyersIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // -------------------------------------------------------------------------
    // Not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuyers_NoBuyers_ReturnsNotFoundAsync()
    {
        var courseId = Guid.NewGuid();

        var response = await _client.GetAsync($"/courses/{courseId}/book-buyers");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Course with no book assigned
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuyers_CourseWithNoBook_ReturnsEmptyBuyersAsync()
    {
        var courseId = await CreateCourseAndGetIdAsync("Book-less Course", "A course with no textbook", 20);

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/courses/{courseId}/book-buyers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(courseId.ToString(), body.GetProperty("courseId").GetString(), ignoreCase: true);
        Assert.Equal("Book-less Course", body.GetProperty("courseName").GetString());
        Assert.Equal(0, body.GetProperty("buyers").GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Single book purchase
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuyers_AfterSinglePurchase_ReturnsBuyerAsync()
    {
        var courseId = await CreateCourseAndGetIdAsync("Algorithms Course", "Learn algorithms", 30);
        var bookId = await DefineBookAndGetIdAsync("Introduction to Algorithms", "CLRS", $"ISBN-ALG-{Guid.NewGuid():N}", 59.99m, courseId);
        var studentId = Guid.NewGuid();

        var purchaseResponse = await PurchaseBookAsync(bookId, studentId, 59.99m);
        Assert.Equal(HttpStatusCode.Created, purchaseResponse.StatusCode);

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/courses/{courseId}/book-buyers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        Assert.Equal(courseId.ToString(), body.GetProperty("courseId").GetString(), ignoreCase: true);
        Assert.Equal(bookId.ToString(), body.GetProperty("bookId").GetString(), ignoreCase: true);

        var buyers = body.GetProperty("buyers");
        var buyer = Assert.Single(buyers.EnumerateArray());
        Assert.Equal(studentId.ToString(), buyer.GetProperty("studentId").GetString(), ignoreCase: true);
        Assert.Equal(59.99m, buyer.GetProperty("pricePaid").GetDecimal());
    }

    // -------------------------------------------------------------------------
    // Cart order (same course)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuyers_AfterCartOrder_ReturnsBuyerAsync()
    {
        var courseId = await CreateCourseAndGetIdAsync("Data Structures Course", "Learn data structures", 25);
        var bookId1 = await DefineBookAndGetIdAsync("Data Structures", "Knuth", $"ISBN-DS1-{Guid.NewGuid():N}", 45m, courseId);
        var bookId2 = await DefineBookAndGetIdAsync("Algorithms Unlocked", "Cormen", $"ISBN-DS2-{Guid.NewGuid():N}", 30m, courseId);
        var studentId = Guid.NewGuid();

        // Same-course cart order
        var orderResponse = await OrderBooksAsync(studentId, (bookId1, 45m), (bookId2, 30m));
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);

        await RebuildProjectionAsync();

        // The course book is bookId1 (the first book defined for this course)
        var response = await _client.GetAsync($"/courses/{courseId}/book-buyers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var buyers = body.GetProperty("buyers");

        // Both book items belong to this course; projection records entries for matching bookId only
        Assert.True(buyers.GetArrayLength() >= 1);
        var buyerIds = buyers.EnumerateArray()
            .Select(b => b.GetProperty("studentId").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(studentId.ToString(), buyerIds);
    }

    // -------------------------------------------------------------------------
    // Multiple buyers for same course
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuyers_MultipleBuyers_ReturnsAllAsync()
    {
        var courseId = await CreateCourseAndGetIdAsync("OS Course", "Operating systems", 40);
        var bookId = await DefineBookAndGetIdAsync("Operating Systems", "Tanenbaum", $"ISBN-OS-{Guid.NewGuid():N}", 55m, courseId);

        var student1 = Guid.NewGuid();
        var student2 = Guid.NewGuid();

        await PurchaseBookAsync(bookId, student1, 55m);
        await PurchaseBookAsync(bookId, student2, 55m);

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/courses/{courseId}/book-buyers");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var buyers = body.GetProperty("buyers");
        Assert.Equal(2, buyers.GetArrayLength());

        var buyerIds = buyers.EnumerateArray()
            .Select(b => b.GetProperty("studentId").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains(student1.ToString(), buyerIds);
        Assert.Contains(student2.ToString(), buyerIds);
    }

    // -------------------------------------------------------------------------
    // Multi-course order limitation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuyers_MixedCourseOrder_OnlyFirstCourseGetsUpdatedAsync()
    {
        // Create two separate courses, each with their own book
        var courseAId = await CreateCourseAndGetIdAsync("Course Alpha", "First course", 20);
        var courseBId = await CreateCourseAndGetIdAsync("Course Beta", "Second course", 20);

        var bookAId = await DefineBookAndGetIdAsync("Book Alpha", "Author A", $"ISBN-MCA-{Guid.NewGuid():N}", 20m, courseAId);
        var bookBId = await DefineBookAndGetIdAsync("Book Beta", "Author B", $"ISBN-MCB-{Guid.NewGuid():N}", 25m, courseBId);

        var studentId = Guid.NewGuid();

        // Order books from two different courses in one cart (cross-course order)
        // Tag order: courseA tag is added before courseB tag because bookA is listed first
        var orderResponse = await OrderBooksAsync(studentId, (bookAId, 20m), (bookBId, 25m));
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);

        await RebuildProjectionAsync();

        // Course A gets updated (first courseId tag on the order event)
        var responseA = await _client.GetAsync($"/courses/{courseAId}/book-buyers");
        Assert.Equal(HttpStatusCode.OK, responseA.StatusCode);
        var bodyA = await ReadJsonAsync(responseA);
        var buyersA = bodyA.GetProperty("buyers");
        Assert.Equal(1, buyersA.GetArrayLength());
        Assert.Equal(studentId.ToString(), buyersA[0].GetProperty("studentId").GetString(), ignoreCase: true);

        // Course B does NOT get updated — this is the documented multi-course order limitation.
        // The order event carries courseA's id as the first courseId tag, so the projection
        // only routes the event to Course A's state. Course B's buyers list remains empty.
        var responseB = await _client.GetAsync($"/courses/{courseBId}/book-buyers");
        Assert.Equal(HttpStatusCode.OK, responseB.StatusCode);
        var bodyB = await ReadJsonAsync(responseB);
        var buyersB = bodyB.GetProperty("buyers");
        Assert.Equal(0, buyersB.GetArrayLength());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> CreateCourseAndGetIdAsync(string name, string description, int maxStudentCount)
    {
        var response = await _client.PostAsJsonAsync("/courses", new { name, description, maxStudentCount });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private async Task<Guid> DefineBookAndGetIdAsync(string title, string author, string isbn, decimal price, Guid courseId)
    {
        var response = await _client.PostAsJsonAsync("/course-books", new { title, author, isbn, price, courseId });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private async Task<HttpResponseMessage> PurchaseBookAsync(Guid bookId, Guid studentId, decimal displayedPrice) =>
        await _client.PostAsJsonAsync($"/course-books/{bookId}/purchase", new { studentId, displayedPrice });

    private async Task<HttpResponseMessage> OrderBooksAsync(Guid studentId, params (Guid BookId, decimal DisplayedPrice)[] items) =>
        await _client.PostAsJsonAsync("/course-books/order", new
        {
            studentId,
            items = items.Select(i => new { bookId = i.BookId, displayedPrice = i.DisplayedPrice }).ToArray()
        });

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
    }

    private async Task RebuildProjectionAsync()
    {
        var response = await _client.PostAsync("/admin/projections/CourseBuyers/rebuild", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
