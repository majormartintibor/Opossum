using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opossum.Samples.CourseManagement.IntegrationTests;

/// <summary>
/// Integration tests for the invoice creation endpoints.
/// These tests validate the DCB consecutive-sequence pattern: every invoice gets a unique,
/// gap-free number even under concurrent load, enforced by <c>ReadLastAsync</c> +
/// <c>AppendCondition</c> with automatic retry.
/// </summary>
[Collection("Integration Tests")]
public class InvoiceIntegrationTests
{
    private readonly HttpClient _client;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public InvoiceIntegrationTests(IntegrationTestFixture fixture)
    {
        _client = fixture.Client;
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<int> CreateInvoiceAsync(Guid? customerId = null, decimal amount = 100m)
    {
        var request = new
        {
            CustomerId = customerId ?? Guid.NewGuid(),
            Amount = amount
        };

        var response = await _client.PostAsJsonAsync("/invoices", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        return result.GetProperty("invoiceNumber").GetInt32();
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateInvoice_ValidRequest_ReturnsCreatedWithInvoiceNumberAsync()
    {
        // Arrange
        var customerId = Guid.NewGuid();
        var request = new { CustomerId = customerId, Amount = 250.00m };

        // Act
        var response = await _client.PostAsJsonAsync("/invoices", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(content, _jsonOptions);
        Assert.True(result.TryGetProperty("invoiceNumber", out var numberProp));
        Assert.True(numberProp.GetInt32() >= 1);
    }

    [Fact]
    public async Task CreateInvoice_TwoConsecutiveCalls_NumbersAreSequentialAsync()
    {
        // Act — two back-to-back invoices
        var first = await CreateInvoiceAsync();
        var second = await CreateInvoiceAsync();

        // Assert — second must be exactly one higher than first
        Assert.Equal(first + 1, second);
    }

    [Fact]
    public async Task GetInvoices_ReturnsListOrderedByNumberAsync()
    {
        // Arrange — create three invoices
        var a = await CreateInvoiceAsync();
        var b = await CreateInvoiceAsync();
        var c = await CreateInvoiceAsync();

        // Wait for the projection daemon to process the new events
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Act
        var response = await _client.GetAsync("/invoices");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var invoices = await response.Content.ReadFromJsonAsync<List<JsonElement>>(_jsonOptions);
        Assert.NotNull(invoices);

        // Assert — list contains all three and is ordered ascending
        var numbers = invoices.Select(i => i.GetProperty("invoiceNumber").GetInt32()).ToList();
        Assert.Contains(a, numbers);
        Assert.Contains(b, numbers);
        Assert.Contains(c, numbers);
        Assert.Equal([.. numbers.OrderBy(n => n)], numbers);
    }

    [Fact]
    public async Task GetInvoiceByNumber_ExistingInvoice_ReturnsCorrectDataAsync()
    {
        // Arrange — create an invoice and capture the generated number
        var customerId = Guid.NewGuid();
        var amount = 499.99m;
        var invoiceNumber = await CreateInvoiceAsync(customerId, amount);

        // Wait for the projection daemon to process the new event
        await Task.Delay(TimeSpan.FromSeconds(6));

        // Act
        var response = await _client.GetAsync($"/invoices/{invoiceNumber}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var invoice = JsonSerializer.Deserialize<JsonElement>(
            await response.Content.ReadAsStringAsync(), _jsonOptions);

        Assert.Equal(invoiceNumber, invoice.GetProperty("invoiceNumber").GetInt32());
        Assert.Equal(customerId.ToString(), invoice.GetProperty("customerId").GetString());
        Assert.Equal(amount, invoice.GetProperty("amount").GetDecimal());
    }

    [Fact]
    public async Task GetInvoiceByNumber_NonExistentNumber_ReturnsNotFoundAsync()
    {
        // Act — use a number that will never exist in the test run
        var response = await _client.GetAsync("/invoices/999999");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvoice_EmptyGuid_ReturnsBadRequestAsync()
    {
        // Arrange
        var request = new { CustomerId = Guid.Empty, Amount = 100m };

        // Act
        var response = await _client.PostAsJsonAsync("/invoices", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvoice_ZeroAmount_ReturnsBadRequestAsync()
    {
        // Arrange
        var request = new { CustomerId = Guid.NewGuid(), Amount = 0m };

        // Act
        var response = await _client.PostAsJsonAsync("/invoices", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateInvoice_NegativeAmount_ReturnsBadRequestAsync()
    {
        // Arrange
        var request = new { CustomerId = Guid.NewGuid(), Amount = -1m };

        // Act
        var response = await _client.PostAsJsonAsync("/invoices", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
