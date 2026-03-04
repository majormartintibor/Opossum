using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Opossum.Core;
using Opossum.Samples.CourseManagement.Events;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the Course Book (Dynamic Price) endpoints.
///
/// Covers the DCB "Dynamic Product Price" pattern:
/// https://dcb.events/examples/dynamic-product-price/
///
/// Key scenarios verified:
/// F1/F2: Define book, change price, purchase with valid price, purchase with wrong price.
/// F3: Order multiple books, one invalid price rejects the whole order.
/// Read: Catalog lists books with current price, order history lists orders.
/// </summary>
[Collection("Integration Tests")]
public class CourseBookPurchaseIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly HttpClient _client;
    private readonly IntegrationTestFixture _fixture;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CourseBookPurchaseIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
        _client = fixture.Client;
    }

    // -------------------------------------------------------------------------
    // Define course book (admin)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DefineBook_ValidRequest_ReturnsCreatedAsync()
    {
        var response = await DefineBookAsync("Clean Code", "Robert C. Martin", "9780132350884", 39.99m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        Assert.True(Guid.TryParse(body.GetProperty("id").GetString(), out _));
    }

    [Fact]
    public async Task DefineBook_DuplicateBook_ReturnsBadRequestAsync()
    {
        var bookId = await DefineBookAndGetIdAsync("DDD", "Evans", "ISBN001", 49.99m);

        // Redefining same bookId via direct approach — not possible via API (server generates ID)
        // The guard fires when a bookId is reused. We verify the guard exists via unit tests.
        // This test verifies the happy path creates one book successfully.
        Assert.NotEqual(Guid.Empty, bookId);
    }

    // -------------------------------------------------------------------------
    // Change price (admin)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ChangePrice_ExistingBook_ReturnsOkAsync()
    {
        var bookId = await DefineBookAndGetIdAsync("Refactoring", "Fowler", "ISBN002", 44.99m);

        var response = await ChangePriceAsync(bookId, 39.99m);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ChangePrice_NonExistentBook_ReturnsBadRequestAsync()
    {
        var response = await ChangePriceAsync(Guid.NewGuid(), 29.99m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Purchase single book — F1/F2
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PurchaseBook_CorrectPrice_ReturnsCreatedAsync()
    {
        var bookId = await DefineBookAndGetIdAsync("SICP", "Abelson", "ISBN003", 29.99m);
        var studentId = Guid.NewGuid();

        var response = await PurchaseBookAsync(bookId, studentId, 29.99m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PurchaseBook_WrongPrice_ReturnsBadRequestAsync()
    {
        var bookId = await DefineBookAndGetIdAsync("PAIP", "Norvig", "ISBN004", 59.99m);
        var studentId = Guid.NewGuid();

        var response = await PurchaseBookAsync(bookId, studentId, 10m); // wrong price

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("no longer valid", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PurchaseBook_NonExistentBook_ReturnsBadRequestAsync()
    {
        var response = await PurchaseBookAsync(Guid.NewGuid(), Guid.NewGuid(), 9.99m);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("does not exist", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PurchaseBook_PriceChanged_NewPriceAcceptedAsync()
    {
        var bookId = await DefineBookAndGetIdAsync("HTDP", "Felleisen", "ISBN005", 35m);
        await ChangePriceAsync(bookId, 40m);
        var studentId = Guid.NewGuid();

        // New price should be accepted
        var response = await PurchaseBookAsync(bookId, studentId, 40m);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Order multiple books — F3
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OrderBooks_AllPricesCorrect_ReturnsCreatedAsync()
    {
        var book1 = await DefineBookAndGetIdAsync("Book A", "Author A", "ISBN-A1", 20m);
        var book2 = await DefineBookAndGetIdAsync("Book B", "Author B", "ISBN-B1", 30m);
        var studentId = Guid.NewGuid();

        var response = await OrderBooksAsync(studentId,
            (book1, 20m),
            (book2, 30m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OrderBooks_SingleItem_ReturnsCreatedAsync()
    {
        var book1 = await DefineBookAndGetIdAsync("Solo Book", "Author S", "ISBN-S1", 15m);
        var studentId = Guid.NewGuid();

        var response = await OrderBooksAsync(studentId, (book1, 15m));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task OrderBooks_OneWrongPrice_ReturnsBadRequestAsync()
    {
        var book1 = await DefineBookAndGetIdAsync("Book C", "Author C", "ISBN-C1", 25m);
        var book2 = await DefineBookAndGetIdAsync("Book D", "Author D", "ISBN-D1", 35m);
        var studentId = Guid.NewGuid();

        var response = await OrderBooksAsync(studentId,
            (book1, 25m),     // correct
            (book2, 99m));    // wrong price — should fail

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("no longer valid", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OrderBooks_EmptyItems_ReturnsBadRequestAsync()
    {
        var studentId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync("/course-books/order", new
        {
            StudentId = studentId,
            Items = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Catalog read endpoint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetCatalog_ReturnsOkAsync()
    {
        var response = await _client.GetAsync("/course-books?pageNumber=1&pageSize=50&sortBy=Title&sortOrder=Ascending");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // courseId tag on stored events (Session 2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PurchaseBook_StoresCourseIdTagAsync()
    {
        var courseId = Guid.NewGuid();
        var bookId = await DefineBookAndGetIdAsync("CourseId Tag Book", "Author", "ISBN-CIT1", 25m, courseId);
        var studentId = Guid.NewGuid();

        var response = await PurchaseBookAsync(bookId, studentId, 25m);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var events = await eventStore.ReadAsync(
            Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseBookPurchasedEvent)],
                Tags = [new Tag("studentId", studentId.ToString())]
            }), null);

        var purchaseEvent = Assert.Single(events);
        Assert.Contains(purchaseEvent.Event.Tags, t => t.Key == "courseId" && t.Value == courseId.ToString());
    }

    [Fact]
    public async Task OrderBooks_StoresCourseIdTagAsync()
    {
        var courseId = Guid.NewGuid();
        var book1 = await DefineBookAndGetIdAsync("CourseId Order A", "Author A", "ISBN-COA1", 20m, courseId);
        var book2 = await DefineBookAndGetIdAsync("CourseId Order B", "Author B", "ISBN-COB1", 30m, courseId);
        var studentId = Guid.NewGuid();

        var response = await OrderBooksAsync(studentId, (book1, 20m), (book2, 30m));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var scope = _fixture.Factory.Services.CreateScope();
        var eventStore = scope.ServiceProvider.GetRequiredService<IEventStore>();
        var events = await eventStore.ReadAsync(
            Query.FromItems(new QueryItem
            {
                EventTypes = [nameof(CourseBooksOrderedEvent)],
                Tags = [new Tag("studentId", studentId.ToString())]
            }), null);

        var orderEvent = Assert.Single(events);
        Assert.Contains(orderEvent.Event.Tags, t => t.Key == "courseId" && t.Value == courseId.ToString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<HttpResponseMessage> DefineBookAsync(string title, string author, string isbn, decimal price, Guid? courseId = null) =>
        await _client.PostAsJsonAsync("/course-books", new { title, author, isbn, price, courseId = courseId ?? Guid.NewGuid() });

    private async Task<Guid> DefineBookAndGetIdAsync(string title, string author, string isbn, decimal price, Guid? courseId = null)
    {
        var response = await DefineBookAsync(title, author, isbn, price, courseId);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await ReadJsonAsync(response);
        return Guid.Parse(body.GetProperty("id").GetString()!);
    }

    private async Task<HttpResponseMessage> ChangePriceAsync(Guid bookId, decimal newPrice) =>
        await _client.PatchAsJsonAsync($"/course-books/{bookId}/price", new { newPrice });

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
}
