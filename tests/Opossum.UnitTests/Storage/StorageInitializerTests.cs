using Opossum.Configuration;
using Opossum.Storage.FileSystem;

namespace Opossum.UnitTests.Storage;

public class StorageInitializerTests : IDisposable
{
    private readonly string _testRootPath;

    public StorageInitializerTests()
    {
        // Create a unique test directory for each test run
        _testRootPath = Path.Combine(
            Path.GetTempPath(),
            "OpossumTests",
            "StorageInitializer",
            Guid.NewGuid().ToString());
    }

    public void Dispose()
    {
        // Clean up test directory after each test
        if (Directory.Exists(_testRootPath))
        {
            try
            {
                Directory.Delete(_testRootPath, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }

    [Fact]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentNullException>(() =>
            new StorageInitializer(null!));
        Assert.Equal("options", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithValidOptions_Succeeds()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("TestContext");

        // Act
        var initializer = new StorageInitializer(options);

        // Assert
        Assert.NotNull(initializer);
    }

    [Fact]
    public void Initialize_WithNoContexts_ThrowsInvalidOperationException()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        var initializer = new StorageInitializer(options);

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() => initializer.Initialize());
        Assert.Contains("no contexts configured", exception.Message);
    }

    [Fact]
    public void Initialize_WithSingleContext_CreatesCorrectStructure()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();

        // Assert - Root directory
        Assert.True(Directory.Exists(_testRootPath));

        // Assert - Context directory
        var contextPath = Path.Combine(_testRootPath, "CourseManagement");
        Assert.True(Directory.Exists(contextPath));

        // Assert - Ledger file
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        Assert.True(File.Exists(ledgerPath));

        // Assert - Events directory
        var eventsPath = Path.Combine(contextPath, "Events");
        Assert.True(Directory.Exists(eventsPath));

        // Assert - Indices directory
        var indicesPath = Path.Combine(contextPath, "Indices");
        Assert.True(Directory.Exists(indicesPath));

        // Assert - EventType index directory
        var eventTypeIndexPath = Path.Combine(indicesPath, "EventType");
        Assert.True(Directory.Exists(eventTypeIndexPath));

        // Assert - Tags index directory
        var tagsIndexPath = Path.Combine(indicesPath, "Tags");
        Assert.True(Directory.Exists(tagsIndexPath));
    }

    [Fact]
    public void Initialize_WithMultipleContexts_CreatesAllStructures()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement")
               .AddContext("StudentEnrollment")
               .AddContext("Billing");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();

        // Assert - All context directories exist
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "StudentEnrollment")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "Billing")));

        // Assert - All ledger files exist
        Assert.True(File.Exists(Path.Combine(_testRootPath, "CourseManagement", ".ledger")));
        Assert.True(File.Exists(Path.Combine(_testRootPath, "StudentEnrollment", ".ledger")));
        Assert.True(File.Exists(Path.Combine(_testRootPath, "Billing", ".ledger")));

        // Assert - All Events directories exist
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement", "Events")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "StudentEnrollment", "Events")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "Billing", "Events")));

        // Assert - All index structures exist
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement", "Indices", "EventType")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement", "Indices", "Tags")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "StudentEnrollment", "Indices", "EventType")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "StudentEnrollment", "Indices", "Tags")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "Billing", "Indices", "EventType")));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "Billing", "Indices", "Tags")));
    }

    [Fact]
    public void Initialize_CalledMultipleTimes_DoesNotThrow()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();
        initializer.Initialize(); // Call again

        // Assert - Should not throw, directories should still exist
        Assert.True(Directory.Exists(_testRootPath));
        Assert.True(Directory.Exists(Path.Combine(_testRootPath, "CourseManagement")));
    }

    [Fact]
    public void Initialize_WithExistingDirectories_DoesNotOverwrite()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Pre-create ledger with content
        var contextPath = Path.Combine(_testRootPath, "CourseManagement");
        Directory.CreateDirectory(contextPath);
        var ledgerPath = Path.Combine(contextPath, ".ledger");
        File.WriteAllText(ledgerPath, "existing content");

        // Act
        initializer.Initialize();

        // Assert - Existing content should be preserved
        var ledgerContent = File.ReadAllText(ledgerPath);
        Assert.Equal("existing content", ledgerContent);
    }

    [Fact]
    public void Initialize_CreatesEmptyLedgerFile()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();

        // Assert
        var ledgerPath = Path.Combine(_testRootPath, "CourseManagement", ".ledger");
        var content = File.ReadAllText(ledgerPath);
        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public void GetContextPath_ReturnsCorrectPath()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        var path = initializer.GetContextPath("CourseManagement");

        // Assert
        var expected = Path.Combine(_testRootPath, "CourseManagement");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void GetEventsPath_ReturnsCorrectPath()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        var path = initializer.GetEventsPath("CourseManagement");

        // Assert
        var expected = Path.Combine(_testRootPath, "CourseManagement", "Events");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void GetLedgerPath_ReturnsCorrectPath()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        var path = initializer.GetLedgerPath("CourseManagement");

        // Assert
        var expected = Path.Combine(_testRootPath, "CourseManagement", ".ledger");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void GetEventTypeIndexPath_ReturnsCorrectPath()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        var path = initializer.GetEventTypeIndexPath("CourseManagement");

        // Assert
        var expected = Path.Combine(_testRootPath, "CourseManagement", "Indices", "EventType");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void GetTagsIndexPath_ReturnsCorrectPath()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        var path = initializer.GetTagsIndexPath("CourseManagement");

        // Assert
        var expected = Path.Combine(_testRootPath, "CourseManagement", "Indices", "Tags");
        Assert.Equal(expected, path);
    }

    [Fact]
    public void Initialize_WithRelativePath_CreatesDirectories()
    {
        // Arrange
        var relativePath = Path.Combine(".", "test-data", Guid.NewGuid().ToString());
        var options = new OpossumOptions { RootPath = relativePath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        try
        {
            // Act
            initializer.Initialize();

            // Assert
            Assert.True(Directory.Exists(relativePath));
            Assert.True(Directory.Exists(Path.Combine(relativePath, "CourseManagement")));
        }
        finally
        {
            // Cleanup
            if (Directory.Exists(relativePath))
            {
                Directory.Delete(relativePath, recursive: true);
            }
        }
    }

    [Fact]
    public void Initialize_WithNestedPath_CreatesAllDirectories()
    {
        // Arrange
        var nestedPath = Path.Combine(_testRootPath, "level1", "level2", "level3");
        var options = new OpossumOptions { RootPath = nestedPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();

        // Assert
        Assert.True(Directory.Exists(nestedPath));
        Assert.True(Directory.Exists(Path.Combine(nestedPath, "CourseManagement")));
    }

    [Fact]
    public void Initialize_CreatesExpectedDirectoryCount()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();

        // Assert
        var contextPath = Path.Combine(_testRootPath, "CourseManagement");
        var directories = Directory.GetDirectories(contextPath, "*", SearchOption.AllDirectories);

        // Expected: Events, Indices, Indices/EventType, Indices/Tags = 4 directories
        Assert.Equal(4, directories.Length);
    }

    [Fact]
    public void Initialize_CreatesOnlyLedgerFile()
    {
        // Arrange
        var options = new OpossumOptions { RootPath = _testRootPath };
        options.AddContext("CourseManagement");
        var initializer = new StorageInitializer(options);

        // Act
        initializer.Initialize();

        // Assert
        var contextPath = Path.Combine(_testRootPath, "CourseManagement");
        var files = Directory.GetFiles(contextPath, "*", SearchOption.TopDirectoryOnly);

        // Should only have .ledger file at context level
        Assert.Single(files);
        Assert.Equal(".ledger", Path.GetFileName(files[0]));
    }
}
