using Opossum.Exceptions;

namespace Opossum.UnitTests.Exceptions;

/// <summary>
/// Unit tests for custom exception classes in Opossum.Exceptions namespace
/// </summary>
public class EventStoreExceptionsTests
{
    #region EventStoreException Tests

    [Fact]
    public void EventStoreException_DefaultConstructor_CreatesException()
    {
        // Act
        var exception = new EventStoreException();

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<EventStoreException>(exception);
        Assert.IsAssignableFrom<Exception>(exception);
    }

    [Fact]
    public void EventStoreException_MessageConstructor_SetsMessage()
    {
        // Arrange
        var message = "Test error message";

        // Act
        var exception = new EventStoreException(message);

        // Assert
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void EventStoreException_MessageAndInnerExceptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "Test error message";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new EventStoreException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    #endregion

    #region AppendConditionFailedException Tests

    [Fact]
    public void AppendConditionFailedException_DefaultConstructor_CreatesException()
    {
        // Act
        var exception = new AppendConditionFailedException();

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<AppendConditionFailedException>(exception);
        Assert.IsAssignableFrom<EventStoreException>(exception);
    }

    [Fact]
    public void AppendConditionFailedException_MessageConstructor_SetsMessage()
    {
        // Arrange
        var message = "Append condition failed: conflicting events found";

        // Act
        var exception = new AppendConditionFailedException(message);

        // Assert
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void AppendConditionFailedException_MessageAndInnerExceptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "Append condition failed";
        var innerException = new IOException("File system error");

        // Act
        var exception = new AppendConditionFailedException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void AppendConditionFailedException_CanBeCaughtAsEventStoreException()
    {
        // Arrange
        var exception = new AppendConditionFailedException("Test");

        // Act & Assert
        Assert.True(exception is EventStoreException);
    }

    #endregion

    #region ContextNotFoundException Tests

    [Fact]
    public void ContextNotFoundException_DefaultConstructor_CreatesException()
    {
        // Act
        var exception = new ContextNotFoundException();

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<ContextNotFoundException>(exception);
        Assert.IsAssignableFrom<EventStoreException>(exception);
        Assert.Null(exception.ContextName);
    }

    [Fact]
    public void ContextNotFoundException_MessageConstructor_SetsMessage()
    {
        // Arrange
        var message = "Context not found";

        // Act
        var exception = new ContextNotFoundException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.ContextName);
    }

    [Fact]
    public void ContextNotFoundException_MessageAndContextNameConstructor_SetsProperties()
    {
        // Arrange
        var message = "Context 'Billing' not found";
        var contextName = "Billing";

        // Act
        var exception = new ContextNotFoundException(message, contextName);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(contextName, exception.ContextName);
    }

    [Fact]
    public void ContextNotFoundException_MessageAndInnerExceptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "Context not found";
        var innerException = new DirectoryNotFoundException("Directory not found");

        // Act
        var exception = new ContextNotFoundException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
        Assert.Null(exception.ContextName);
    }

    [Fact]
    public void ContextNotFoundException_FullConstructor_SetsAllProperties()
    {
        // Arrange
        var message = "Context 'Billing' not found";
        var contextName = "Billing";
        var innerException = new DirectoryNotFoundException("Directory not found");

        // Act
        var exception = new ContextNotFoundException(message, contextName, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(contextName, exception.ContextName);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void ContextNotFoundException_CanBeCaughtAsEventStoreException()
    {
        // Arrange
        var exception = new ContextNotFoundException("Test");

        // Act & Assert
        Assert.True(exception is EventStoreException);
    }

    #endregion

    #region InvalidQueryException Tests

    [Fact]
    public void InvalidQueryException_DefaultConstructor_CreatesException()
    {
        // Act
        var exception = new InvalidQueryException();

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<InvalidQueryException>(exception);
        Assert.IsAssignableFrom<EventStoreException>(exception);
    }

    [Fact]
    public void InvalidQueryException_MessageConstructor_SetsMessage()
    {
        // Arrange
        var message = "Query must contain at least one QueryItem";

        // Act
        var exception = new InvalidQueryException(message);

        // Assert
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void InvalidQueryException_MessageAndInnerExceptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "Invalid query";
        var innerException = new ArgumentException("Invalid argument");

        // Act
        var exception = new InvalidQueryException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void InvalidQueryException_CanBeCaughtAsEventStoreException()
    {
        // Arrange
        var exception = new InvalidQueryException("Test");

        // Act & Assert
        Assert.True(exception is EventStoreException);
    }

    #endregion

    #region ConcurrencyException Tests

    [Fact]
    public void ConcurrencyException_DefaultConstructor_CreatesException()
    {
        // Act
        var exception = new ConcurrencyException();

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<ConcurrencyException>(exception);
        Assert.IsAssignableFrom<EventStoreException>(exception);
        Assert.Null(exception.ExpectedSequence);
        Assert.Null(exception.ActualSequence);
    }

    [Fact]
    public void ConcurrencyException_MessageConstructor_SetsMessage()
    {
        // Arrange
        var message = "Concurrency conflict detected";

        // Act
        var exception = new ConcurrencyException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.ExpectedSequence);
        Assert.Null(exception.ActualSequence);
    }

    [Fact]
    public void ConcurrencyException_MessageAndSequenceConstructor_SetsProperties()
    {
        // Arrange
        var message = "Ledger sequence conflict";
        long expectedSequence = 42;
        long actualSequence = 43;

        // Act
        var exception = new ConcurrencyException(message, expectedSequence, actualSequence);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(expectedSequence, exception.ExpectedSequence);
        Assert.Equal(actualSequence, exception.ActualSequence);
    }

    [Fact]
    public void ConcurrencyException_MessageAndInnerExceptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "Concurrency conflict";
        var innerException = new IOException("File locked");

        // Act
        var exception = new ConcurrencyException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
        Assert.Null(exception.ExpectedSequence);
        Assert.Null(exception.ActualSequence);
    }

    [Fact]
    public void ConcurrencyException_FullConstructor_SetsAllProperties()
    {
        // Arrange
        var message = "Ledger sequence conflict";
        long expectedSequence = 42;
        long actualSequence = 43;
        var innerException = new IOException("File locked");

        // Act
        var exception = new ConcurrencyException(message, expectedSequence, actualSequence, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(expectedSequence, exception.ExpectedSequence);
        Assert.Equal(actualSequence, exception.ActualSequence);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void ConcurrencyException_CanBeCaughtAsEventStoreException()
    {
        // Arrange
        var exception = new ConcurrencyException("Test");

        // Act & Assert
        Assert.True(exception is EventStoreException);
    }

    [Fact]
    public void ConcurrencyException_SequencePropertiesAreNullable()
    {
        // Arrange
        var exception1 = new ConcurrencyException("Test");
        var exception2 = new ConcurrencyException("Test", 10, 20);

        // Assert
        Assert.Null(exception1.ExpectedSequence);
        Assert.Null(exception1.ActualSequence);
        Assert.NotNull(exception2.ExpectedSequence);
        Assert.NotNull(exception2.ActualSequence);
    }

    [Fact]
    public void ConcurrencyException_IsSubclassOf_AppendConditionFailedException()
    {
        var exception = new ConcurrencyException("Conflict");

        Assert.IsAssignableFrom<AppendConditionFailedException>(exception);
    }

    [Fact]
    public void ConcurrencyException_CanBeCaughtAs_AppendConditionFailedException()
    {
        AppendConditionFailedException? caught = null;

        try { throw new ConcurrencyException("Conflict"); }
        catch (AppendConditionFailedException ex) { caught = ex; }

        Assert.NotNull(caught);
        Assert.IsType<ConcurrencyException>(caught);
    }

    #endregion

    #region EventNotFoundException Tests

    [Fact]
    public void EventNotFoundException_DefaultConstructor_CreatesException()
    {
        // Act
        var exception = new EventNotFoundException();

        // Assert
        Assert.NotNull(exception);
        Assert.IsType<EventNotFoundException>(exception);
        Assert.IsAssignableFrom<EventStoreException>(exception);
        Assert.Null(exception.QueryDescription);
    }

    [Fact]
    public void EventNotFoundException_MessageConstructor_SetsMessage()
    {
        // Arrange
        var message = "No events found";

        // Act
        var exception = new EventNotFoundException(message);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Null(exception.QueryDescription);
    }

    [Fact]
    public void EventNotFoundException_MessageAndQueryDescriptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "No events found for aggregate";
        var queryDescription = "CourseId: 123";

        // Act
        var exception = new EventNotFoundException(message, queryDescription);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(queryDescription, exception.QueryDescription);
    }

    [Fact]
    public void EventNotFoundException_MessageAndInnerExceptionConstructor_SetsProperties()
    {
        // Arrange
        var message = "Events not found";
        var innerException = new FileNotFoundException("Event file not found");

        // Act
        var exception = new EventNotFoundException(message, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Same(innerException, exception.InnerException);
        Assert.Null(exception.QueryDescription);
    }

    [Fact]
    public void EventNotFoundException_FullConstructor_SetsAllProperties()
    {
        // Arrange
        var message = "No events found for aggregate";
        var queryDescription = "CourseId: 123";
        var innerException = new FileNotFoundException("Event file not found");

        // Act
        var exception = new EventNotFoundException(message, queryDescription, innerException);

        // Assert
        Assert.Equal(message, exception.Message);
        Assert.Equal(queryDescription, exception.QueryDescription);
        Assert.Same(innerException, exception.InnerException);
    }

    [Fact]
    public void EventNotFoundException_CanBeCaughtAsEventStoreException()
    {
        // Arrange
        var exception = new EventNotFoundException("Test");

        // Act & Assert
        Assert.True(exception is EventStoreException);
    }

    #endregion

    #region Integration Tests - Exception Hierarchy

    [Fact]
    public void AllExceptions_InheritFromEventStoreException()
    {
        // Arrange & Act
        var exceptions = new Exception[]
        {
            new AppendConditionFailedException(),
            new ContextNotFoundException(),
            new InvalidQueryException(),
            new ConcurrencyException(),
            new EventNotFoundException()
        };

        // Assert
        Assert.All(exceptions, ex => Assert.IsAssignableFrom<EventStoreException>(ex));
    }

    [Fact]
    public void ConcurrencyException_InheritsFrom_AppendConditionFailedException_InHierarchy()
    {
        // ConcurrencyException → AppendConditionFailedException → EventStoreException → Exception
        Assert.True(typeof(AppendConditionFailedException).IsAssignableFrom(typeof(ConcurrencyException)));
        Assert.True(typeof(EventStoreException).IsAssignableFrom(typeof(AppendConditionFailedException)));
        Assert.True(typeof(EventStoreException).IsAssignableFrom(typeof(ConcurrencyException)));
    }

    [Fact]
    public void EventStoreException_CanCatchAllSpecializedException()
    {
        // Arrange
        var exceptions = new Exception[]
        {
            new AppendConditionFailedException("Test"),
            new ContextNotFoundException("Test"),
            new InvalidQueryException("Test"),
            new ConcurrencyException("Test"),
            new EventNotFoundException("Test")
        };

        // Act & Assert
        foreach (var ex in exceptions)
        {
            try
            {
                throw ex;
            }
            catch (EventStoreException caught)
            {
                Assert.NotNull(caught);
                Assert.Equal("Test", caught.Message);
            }
        }
    }

    [Fact]
    public void SpecializedExceptions_CanBeCaughtSpecifically()
    {
        // Arrange & Act & Assert
        try
        {
            throw new AppendConditionFailedException("Append failed");
        }
        catch (AppendConditionFailedException ex)
        {
            Assert.Equal("Append failed", ex.Message);
        }

        try
        {
            throw new ContextNotFoundException("Context not found", "Billing");
        }
        catch (ContextNotFoundException ex)
        {
            Assert.Equal("Billing", ex.ContextName);
        }

        try
        {
            throw new ConcurrencyException("Conflict", 10, 20);
        }
        catch (ConcurrencyException ex)
        {
            Assert.Equal(10, ex.ExpectedSequence);
            Assert.Equal(20, ex.ActualSequence);
        }

        try
        {
            throw new EventNotFoundException("Not found", "Query: CourseId=123");
        }
        catch (EventNotFoundException ex)
        {
            Assert.Equal("Query: CourseId=123", ex.QueryDescription);
        }
    }

    #endregion

    #region Usage Scenarios

    [Fact]
    public void ContextNotFoundException_UsageScenario_WithContextName()
    {
        // Arrange
        var contextName = "NonExistentContext";
        var message = $"Context '{contextName}' not found. Add it via options.AddContext(\"{contextName}\")";

        // Act
        var exception = new ContextNotFoundException(message, contextName);

        // Assert
        Assert.Equal(contextName, exception.ContextName);
        Assert.Contains(contextName, exception.Message);
    }

    [Fact]
    public void ConcurrencyException_UsageScenario_SequenceConflict()
    {
        // Arrange
        long expected = 100;
        long actual = 105;
        var message = $"Ledger sequence conflict: expected {expected}, found {actual}";

        // Act
        var exception = new ConcurrencyException(message, expected, actual);

        // Assert
        Assert.Equal(expected, exception.ExpectedSequence);
        Assert.Equal(actual, exception.ActualSequence);
        Assert.Contains(expected.ToString(), exception.Message);
        Assert.Contains(actual.ToString(), exception.Message);
    }

    [Fact]
    public void EventNotFoundException_UsageScenario_WithQueryDescription()
    {
        // Arrange
        var courseId = Guid.NewGuid();
        var queryDescription = $"CourseId: {courseId}";
        var message = $"No events found for aggregate with {queryDescription}";

        // Act
        var exception = new EventNotFoundException(message, queryDescription);

        // Assert
        Assert.Equal(queryDescription, exception.QueryDescription);
        Assert.Contains(courseId.ToString(), exception.Message);
    }

    [Fact]
    public void AppendConditionFailedException_UsageScenario_DCBViolation()
    {
        // Arrange
        var message = "Cannot append events: AppendCondition failed. Found 3 conflicting events matching the condition query.";

        // Act
        var exception = new AppendConditionFailedException(message);

        // Assert
        Assert.Contains("AppendCondition", exception.Message);
        Assert.Contains("conflicting events", exception.Message);
    }

    [Fact]
    public void InvalidQueryException_UsageScenario_EmptyQueryItems()
    {
        // Arrange
        var message = "Query must contain at least one QueryItem";

        // Act
        var exception = new InvalidQueryException(message);

        // Assert
        Assert.Contains("QueryItem", exception.Message);
    }

    #endregion
}
