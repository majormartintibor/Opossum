namespace Opossum.Exceptions;

/// <summary>
/// Base exception class for all Opossum Event Store exceptions
/// </summary>
public class EventStoreException : Exception
{
    /// <summary>
    /// Initializes a new instance of the EventStoreException class
    /// </summary>
    public EventStoreException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the EventStoreException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public EventStoreException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the EventStoreException class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public EventStoreException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when an append condition fails during event appending.
/// This is the single exception type that represents an optimistic concurrency conflict
/// as defined by the DCB specification.
/// <para>
/// <see cref="ConcurrencyException"/> is a subclass of this exception and is thrown by
/// the file-system layer for internal ledger-level races. Callers should always catch
/// <see cref="AppendConditionFailedException"/> — this covers both cases.
/// </para>
/// </summary>
public class AppendConditionFailedException : EventStoreException
{
    /// <summary>
    /// Initializes a new instance of the AppendConditionFailedException class
    /// </summary>
    public AppendConditionFailedException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the AppendConditionFailedException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public AppendConditionFailedException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the AppendConditionFailedException class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public AppendConditionFailedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when attempting to access a bounded context that doesn't exist or hasn't been configured
/// </summary>
public class ContextNotFoundException : EventStoreException
{
    /// <summary>
    /// Gets the name of the context that was not found
    /// </summary>
    public string? ContextName { get; }

    /// <summary>
    /// Initializes a new instance of the ContextNotFoundException class
    /// </summary>
    public ContextNotFoundException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the ContextNotFoundException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public ContextNotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the ContextNotFoundException class with a specified error message and context name
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="contextName">The name of the context that was not found</param>
    public ContextNotFoundException(string message, string contextName) : base(message)
    {
        ContextName = contextName;
    }

    /// <summary>
    /// Initializes a new instance of the ContextNotFoundException class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public ContextNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the ContextNotFoundException class with a specified error message, context name, and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="contextName">The name of the context that was not found</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public ContextNotFoundException(string message, string contextName, Exception innerException) : base(message, innerException)
    {
        ContextName = contextName;
    }
}

/// <summary>
/// Exception thrown when a query is invalid or malformed
/// </summary>
public class InvalidQueryException : EventStoreException
{
    /// <summary>
    /// Initializes a new instance of the InvalidQueryException class
    /// </summary>
    public InvalidQueryException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the InvalidQueryException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public InvalidQueryException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the InvalidQueryException class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public InvalidQueryException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown by the file-system event store for internal ledger-level concurrency
/// conflicts (e.g. a stale <see cref="Opossum.Core.AppendCondition.AfterSequencePosition"/> check).
/// <para>
/// This is a subclass of <see cref="AppendConditionFailedException"/>. Callers should
/// catch <see cref="AppendConditionFailedException"/> rather than this type directly, 
/// unless they need to inspect the <see cref="ExpectedSequence"/> / <see cref="ActualSequence"/>
/// properties for diagnostic purposes.
/// </para>
/// </summary>
public class ConcurrencyException : AppendConditionFailedException
{
    /// <summary>
    /// Gets the expected sequence position
    /// </summary>
    public long? ExpectedSequence { get; }

    /// <summary>
    /// Gets the actual sequence position found
    /// </summary>
    public long? ActualSequence { get; }

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class
    /// </summary>
    public ConcurrencyException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public ConcurrencyException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class with a specified error message and sequence information
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="expectedSequence">The expected sequence position</param>
    /// <param name="actualSequence">The actual sequence position found</param>
    public ConcurrencyException(string message, long expectedSequence, long actualSequence) : base(message)
    {
        ExpectedSequence = expectedSequence;
        ActualSequence = actualSequence;
    }

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public ConcurrencyException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the ConcurrencyException class with a specified error message, sequence information, and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="expectedSequence">The expected sequence position</param>
    /// <param name="actualSequence">The actual sequence position found</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public ConcurrencyException(string message, long expectedSequence, long actualSequence, Exception innerException)
        : base(message, innerException)
    {
        ExpectedSequence = expectedSequence;
        ActualSequence = actualSequence;
    }
}

/// <summary>
/// Exception thrown when expected events cannot be found in the event store
/// </summary>
public class EventNotFoundException : EventStoreException
{
    /// <summary>
    /// Gets the query that was used to search for events
    /// </summary>
    public string? QueryDescription { get; }

    /// <summary>
    /// Initializes a new instance of the EventNotFoundException class
    /// </summary>
    public EventNotFoundException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the EventNotFoundException class with a specified error message
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    public EventNotFoundException(string message) : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the EventNotFoundException class with a specified error message and query description
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="queryDescription">Description of the query that was used</param>
    public EventNotFoundException(string message, string queryDescription) : base(message)
    {
        QueryDescription = queryDescription;
    }

    /// <summary>
    /// Initializes a new instance of the EventNotFoundException class with a specified error message and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public EventNotFoundException(string message, Exception innerException) : base(message, innerException)
    {
    }

    /// <summary>
    /// Initializes a new instance of the EventNotFoundException class with a specified error message, query description, and inner exception
    /// </summary>
    /// <param name="message">The message that describes the error</param>
    /// <param name="queryDescription">Description of the query that was used</param>
    /// <param name="innerException">The exception that is the cause of the current exception</param>
    public EventNotFoundException(string message, string queryDescription, Exception innerException)
        : base(message, innerException)
    {
        QueryDescription = queryDescription;
    }
}
