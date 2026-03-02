using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the <c>GET /students/{studentId}/purchased-books</c> endpoint.
///
/// Verifies that <c>StudentPurchasedBooksProjection</c> correctly accumulates
/// <c>CourseBookPurchasedEvent</c> and <c>CourseBooksOrderedEvent</c> into a
/// per-student deduplicated book list.
/// </summary>
[Collection("Integration Tests")]
public class StudentPurchasedBooksIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public StudentPurchasedBooksIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // -------------------------------------------------------------------------
    // Not found
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPurchasedBooks_NoPurchases_ReturnsNotFoundAsync()
    {
        var studentId = Guid.NewGuid();

        var response = await _client.GetAsync($"/students/{studentId}/purchased-books");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Single purchase
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPurchasedBooks_AfterSinglePurchase_ReturnsEntryAsync()
    {
        var studentId = Guid.NewGuid();
        var bookId = await DefineBookAndGetIdAsync("Test Book A", "Author A", $"ISBN-SPA-{Guid.NewGuid():N}", 29.99m);

        var purchaseResponse = await PurchaseBookAsync(bookId, studentId, 29.99m);
        Assert.Equal(HttpStatusCode.Created, purchaseResponse.StatusCode);

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/students/{studentId}/purchased-books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var books = body.GetProperty("books");
        Assert.Equal(1, books.GetArrayLength());

        var entry = books[0];
        Assert.Equal(bookId.ToString(), entry.GetProperty("bookId").GetString(), ignoreCase: true);
        Assert.Equal(29.99m, entry.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(1, entry.GetProperty("purchaseCount").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Cart order
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPurchasedBooks_AfterOrderPurchase_ReturnsAllItemsAsync()
    {
        var studentId = Guid.NewGuid();
        var bookId1 = await DefineBookAndGetIdAsync("Cart Book A", "Author A", $"ISBN-CBA-{Guid.NewGuid():N}", 20m);
        var bookId2 = await DefineBookAndGetIdAsync("Cart Book B", "Author B", $"ISBN-CBB-{Guid.NewGuid():N}", 35m);

        var orderResponse = await OrderBooksAsync(studentId, (bookId1, 20m), (bookId2, 35m));
        Assert.Equal(HttpStatusCode.Created, orderResponse.StatusCode);

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/students/{studentId}/purchased-books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var books = body.GetProperty("books");
        Assert.Equal(2, books.GetArrayLength());

        var bookIds = books.EnumerateArray()
            .Select(b => b.GetProperty("bookId").GetString())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(bookId1.ToString(), bookIds);
        Assert.Contains(bookId2.ToString(), bookIds);
    }

    // -------------------------------------------------------------------------
    // Aggregation of repeated purchases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPurchasedBooks_SameBookPurchasedTwice_AggregatesCorrectlyAsync()
    {
        var studentId = Guid.NewGuid();
        var bookId = await DefineBookAndGetIdAsync("Repeat Book", "Author R", $"ISBN-RPT-{Guid.NewGuid():N}", 15m);

        await PurchaseBookAsync(bookId, studentId, 15m);
        await PurchaseBookAsync(bookId, studentId, 15m);

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/students/{studentId}/purchased-books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var books = body.GetProperty("books");
        var entry = Assert.Single(books.EnumerateArray());

        Assert.Equal(30m, entry.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(2, entry.GetProperty("purchaseCount").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Mix of single purchase and cart order for same book
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetPurchasedBooks_MixedPurchaseTypes_DeduplicatesBookAsync()
    {
        var studentId = Guid.NewGuid();
        var bookId = await DefineBookAndGetIdAsync("Mixed Book", "Author M", $"ISBN-MXD-{Guid.NewGuid():N}", 22m);

        // Buy once as single purchase
        await PurchaseBookAsync(bookId, studentId, 22m);
        // Buy again as part of a cart order
        await OrderBooksAsync(studentId, (bookId, 22m));

        await RebuildProjectionAsync();

        var response = await _client.GetAsync($"/students/{studentId}/purchased-books");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadJsonAsync(response);
        var books = body.GetProperty("books");
        var entry = Assert.Single(books.EnumerateArray());

        Assert.Equal(44m, entry.GetProperty("totalPaid").GetDecimal());
        Assert.Equal(2, entry.GetProperty("purchaseCount").GetInt32());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> DefineBookAsync(string title, string author, string isbn, decimal price) =>
        await _client.PostAsJsonAsync("/course-books", new { title, author, isbn, price, courseId = Guid.NewGuid() });

    private async Task<Guid> DefineBookAndGetIdAsync(string title, string author, string isbn, decimal price)
    {
        var response = await DefineBookAsync(title, author, isbn, price);
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

    /// <summary>
    /// Triggers a full rebuild of the StudentPurchasedBooks projection via the admin endpoint.
    /// This is preferred over waiting for the daemon because the daemon's global minCheckpoint
    /// approach can cause event re-application for accumulating projections.
    /// </summary>
    private async Task RebuildProjectionAsync()
    {
        var response = await _client.PostAsync("/admin/projections/StudentPurchasedBooks/rebuild", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
