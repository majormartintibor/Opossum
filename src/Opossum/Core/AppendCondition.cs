namespace Opossum.Core;

/// <summary>
/// Specifies an optimistic concurrency guard evaluated atomically during <see cref="IEventStore.AppendAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Purpose — preventing the lost-update problem</b><br/>
/// In an event-sourced system multiple writers can read the same events, build independent
/// decision models and then try to append new events. Without a guard, the second writer
/// silently overwrites a state that was already changed by the first writer (a classic
/// Time-Of-Check / Time-Of-Use race). <c>AppendCondition</c> closes this gap by letting the
/// writer declare <em>which events would have invalidated its decision</em>; the event store
/// enforces the check atomically and throws <see cref="Exceptions.ConcurrencyException"/> when
/// a conflict is detected.
/// </para>
///
/// <para>
/// <b>The DCB read → decide → append pattern</b><br/>
/// Dynamic Consistency Boundaries (DCB) replace the traditional single-aggregate lock with a
/// query-scoped lock that spans exactly the events relevant to one business decision —
/// nothing more, nothing less.  The pattern has three steps:
/// <list type="number">
///   <item>
///     <description>
///       <b>Read</b> – query the event store for all events relevant to the decision (e.g.
///       all events for a specific course and student). Record the highest
///       <see cref="SequencedEvent.Position"/> seen; that becomes
///       <see cref="AfterSequencePosition"/>.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Decide</b> – fold the events into a decision model, validate business invariants
///       (capacity, duplicate enrollments, etc.) and produce the events to append.
///     </description>
///   </item>
///   <item>
///     <description>
///       <b>Append</b> – call <see cref="IEventStore.AppendAsync"/> with an
///       <c>AppendCondition</c> whose <see cref="FailIfEventsMatch"/> is the <em>same</em>
///       query used in step 1. The event store atomically re-runs the query restricted to
///       positions &gt; <see cref="AfterSequencePosition"/> and aborts with
///       <see cref="Exceptions.ConcurrencyException"/> if any such events exist, because the
///       decision model would have been different had those events been visible.
///     </description>
///   </item>
/// </list>
/// </para>
///
/// <para>
/// <b>Example — enrol a student in a course</b>
/// <code>
/// // Step 1 – Read
/// var query = Query.FromItems(
///     new QueryItem
///     {
///         EventTypes = [nameof(CourseCreatedEvent), nameof(StudentEnrolledToCourseEvent)],
///         Tags       = [new Tag { Key = "courseId", Value = courseId.ToString() }]
///     },
///     new QueryItem
///     {
///         EventTypes = [nameof(StudentRegisteredEvent)],
///         Tags       = [new Tag { Key = "studentId", Value = studentId.ToString() }]
///     });
///
/// var events      = await eventStore.ReadAsync(query, ReadOption.None);
/// var lastKnownPosition = events.Length > 0 ? events.Max(e => e.Position) : (long?)null;
///
/// // Step 2 – Decide (apply business rules, build aggregate, etc.)
///
/// // Step 3 – Append
/// var condition = new AppendCondition
/// {
///     FailIfEventsMatch    = query,            // same query as step 1
///     AfterSequencePosition = lastKnownPosition // watermark from step 1
/// };
/// await eventStore.AppendAsync([enrollmentEvent], condition);
/// // Throws ConcurrencyException if any matching event appeared after lastKnownPosition.
/// </code>
/// </para>
/// </remarks>
public class AppendCondition
{
    /// <summary>
    /// The query that identifies events which would invalidate the current decision model.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This should be the <b>same <see cref="Query"/> that was used to read events when
    /// building the decision model</b>. By reusing it, you guarantee that the decision model
    /// is still valid at the moment of the append: if any event matching this query appeared
    /// since you last read, your decision could be wrong and the append is aborted.
    /// </para>
    /// <para>
    /// <b>The conflict check is the conjunction of both properties</b><br/>
    /// A conflict is only raised when an event satisfies <em>both</em> conditions at once:
    /// <list type="number">
    ///   <item><description>its position is &gt; <see cref="AfterSequencePosition"/>, <em>and</em></description></item>
    ///   <item><description>it matches this query.</description></item>
    /// </list>
    /// This is the key difference between DCB and naive position-based optimistic concurrency
    /// control. A <c>CourseRenamedEvent</c> or a <c>StudentAddressChangedEvent</c> that was
    /// appended after <see cref="AfterSequencePosition"/> will <em>not</em> cause a conflict,
    /// because it does not match the enrollment query. The consistency boundary is defined
    /// entirely by what this query selects — only events that are <em>semantically relevant</em>
    /// to the decision can invalidate it. Unrelated writes are allowed through, keeping the
    /// boundary as narrow as the business decision requires.
    /// </para>
    /// <para>
    /// <b>Scope of the check depends on <see cref="AfterSequencePosition"/>:</b>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       When <see cref="AfterSequencePosition"/> is set, only events stored
    ///       <em>after</em> that position are checked. This is the standard DCB pattern.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       When <see cref="AfterSequencePosition"/> is <c>null</c>, <em>all</em> events in
    ///       the store are checked. This is useful for bootstrapping uniqueness invariants,
    ///       e.g. "this <c>StudentRegistered</c> event must be the very first event for this
    ///       student id — reject if any already exists".
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// </remarks>
    public required Query FailIfEventsMatch { get; set; }

    /// <summary>
    /// The highest sequence position the client was aware of when building the decision model,
    /// or <c>null</c> if no events were observed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The event store uses this value as an exclusive lower bound: events at positions
    /// <em>less than or equal to</em> this number are invisible to the conflict check.
    /// Only events stored <em>after</em> this position are tested against
    /// <see cref="FailIfEventsMatch"/>.
    /// </para>
    /// <para>
    /// <b>Important:</b> this value is the watermark over <em>all</em> events returned by
    /// the read query, not just the last event of a specific type. It may therefore be higher
    /// than the position of the most recent event that directly affected the decision.
    /// </para>
    /// <para>
    /// <b>Two distinct behaviours:</b>
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <b>Populated</b> — the append is rejected only when a <em>new</em> conflicting
    ///       event appeared since the read. Concurrent writes that do not touch the same
    ///       query domain are allowed through, keeping the consistency boundary as narrow as
    ///       possible. This guarantee only holds when <see cref="FailIfEventsMatch"/> carries
    ///       a scoped query — see the warning below.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <b><c>null</c></b> — no events have ever been seen for this query; the append is
    ///       rejected if <em>any</em> matching event exists anywhere in the store. Use this
    ///       when appending the very first event for a new entity to guarantee uniqueness.
    ///     </description>
    ///   </item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Warning — do not combine with <c>Query.All()</c></b><br/>
    /// When <see cref="FailIfEventsMatch"/> has no query items (i.e. <c>Query.All()</c>),
    /// the query itself is bypassed entirely. The check degenerates to a raw ledger-position
    /// comparison: the append fails if <em>any</em> event at all was written by anyone since
    /// the watermark, regardless of type or tags. This creates an implicit global write lock
    /// and defeats the entire purpose of DCB. There is no valid domain use case for this
    /// combination; if you need global write serialization, use an infrastructure-level
    /// primitive (a <see cref="System.Threading.SemaphoreSlim"/>, a file lock, etc.) instead.
    /// </para>
    /// </remarks>
    public long? AfterSequencePosition { get; set; }
}
