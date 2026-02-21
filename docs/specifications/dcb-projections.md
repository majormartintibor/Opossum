# DCB Projections Specification

> Extracted from [dcb.events/topics/projections/](https://dcb.events/topics/projections/)

In software architecture, how we view and handle data often comes down to two fundamental perspectives: **event-based** and **state-based**. The state-based view focuses on the current snapshot of data. It's straightforward and efficient for querying, reporting, and displaying data to users. In contrast, the event-based view captures every change over time, providing a complete history of how the state evolved.

Depending on your use case, one view may serve better than the other — or you might need both. That's where projections come in: they translate an event-based history into a state-based model tailored to specific needs.

The result is commonly used for persistent **Read Models** *(representation of data tailored for specific read operations, often denormalized for performance)*. In Event Sourcing, however, projections are also used to build the **Decision Model** *(representation of the system's current state, used to enforce integrity constraints before moving the system to a new state)* needed to enforce consistency constraints.

This specification focuses on this latter kind of projection since DCB primarily focuses on ensuring the consistency of the Event Store during write operations.

---

## What is a Projection

In 2013 Greg Young posted the following minimal definition of a projection:

> left fold of previous facts

In TypeScript the equivalent type definition would be:

```typescript
type Projection<S, E> = (state: S, event: E) => S
```

A projection is a pure function that takes the current state and an event, and returns a new state. Applied repeatedly via a fold/reduce over a sequence of events, it builds up a derived state from history.

---

## Implementation

A typical DCB projection looks like this:

```js
{
  initialState: null,
  handlers: {
    CourseDefined: (state, event) => event.data.title,
    CourseRenamed: (state, event) => event.data.newTitle,
  },
  tags: [`course:${courseId}`],
}
```

### Basic Functionality

A projection is defined by:
- **`initialState`** – the starting value before any events are applied
- **`handlers`** – a map of event type names to reducer functions `(state, event) => state`

JavaScript's `Array.reduce` (or equivalent in any language) aggregates all events into a single state, starting from `initialState`:

```js
const projection = (state, event) => {
  switch (event) {
    case 'CourseDefined':
      return state + 1;
    case 'CourseArchived':
      return state - 1;
    default:
      return state;
  }
}
const initialState = 0
const numberOfActiveCourses = events.reduce(projection, initialState)
```

> **Note:** A projection function must be **pure** (free of side effects) to produce deterministic results. When replaying a projection for the same events, the resulting state must be identical every time.

---

## Query Only Relevant Events

In a real application events are not stored in memory and there can be many of them. They should be filtered **before** being read from the Event Store.

In the context of DCB, projections are typically used to reconstruct the minimal model required to validate constraints — usually in response to a command. It is therefore paramount to minimize the time needed to rebuild the Decision Model by loading only the events that are relevant to validating the received command.

### Filter Events by Type

The event type is the primary filter criterion. By defining handlers declaratively (as a map keyed by event type name), the handled types can be derived automatically from the projection definition:

```js
const projection = {
  initialState: 0,
  handlers: {
    CourseDefined: (state, event) => state + 1,
    CourseArchived: (state, event) => state - 1,
  }
}

// Only load events whose type is a key in projection.handlers
const relevantEvents = events.filter((event) => event in projection.handlers)
```

### Filter Events by Tags

Decision Models are usually only concerned with single entities. For example, to determine whether a course with a specific id exists, only `CourseDefined` events tagged for **that course** should be loaded — not all `CourseDefined` events globally.

In DCB there is no concept of multiple streams. Events are stored in a **single global sequence**. Instead, events can be associated with entities using **Tags**, and a compliant DCB Event Store allows filtering events by their tags in addition to their type.

A projection extended with a `tagFilter`:

```js
const projection = {
  initialState: false,
  handlers: {
    CourseDefined:  (state, event) => true,
    CourseArchived: (state, event) => false,
  },
  tagFilter: [`course:c1`]
}

// Filter: event type must be handled AND event must carry all required tags
const relevantEvents = events.filter((event) =>
    event.type in projection.handlers &&
    projection.tagFilter.every((tag) => event.tags.includes(tag))
)
```

Because the `tagFilter` is the most dynamic part of a projection (it depends on the specific entity instance), it is best expressed via a **factory function** that accepts the relevant dynamic parameters:

```js
const CourseExistsProjection = (courseId) => ({
  initialState: false,
  handlers: {
    CourseDefined:  (state, event) => true,
    CourseArchived: (state, event) => false,
  },
  tagFilter: [`course:${courseId}`]
})
```

### Projection Object Shape (Library)

The `createProjection` helper function wraps the raw projection definition and returns a richer object:

```typescript
type Projection<S> = {
  get initialState(): S
  apply(state: S, event: SequencedEvent): S
  get query(): Query
}
```

- **`initialState`** – the starting state value
- **`apply(state, event)`** – applies a single event to the current state
- **`query`** – a `Query` value that can be used to filter events (manually or translated to an Event Store query)

Usage:

```js
const CourseExistsProjection = (courseId) =>
  createProjection({
    initialState: false,
    handlers: {
      CourseDefined:  (state, event) => true,
      CourseArchived: (state, event) => false,
    },
    tagFilter: [`course:${courseId}`],
  })

const projection = CourseExistsProjection("c1")

const state = events
  .filter((event) => projection.query.matchesEvent(event))
  .reduce(
    (state, event) => projection.apply(state, event),
    projection.initialState
  )
```

---

## Composing Projections

Usually there are **multiple** hard constraints to enforce simultaneously. For example, to change a course's capacity, the system must ensure that:

- the course exists
- the specified new capacity differs from the current capacity

It is tempting to write a single "fat" projection that answers both questions. However, this has drawbacks:

- It increases complexity and makes the projection harder to reason about.
- It makes the projection more "greedy" — if only part of the state is needed for a given decision, it still loads more events than necessary, widening the consistency boundary needlessly.

Instead, **compose multiple small projections** into one using `composeProjections`:

```js
const compositeProjection = composeProjections({
  courseExists: CourseExistsProjection("c1"),
  courseTitle:  CourseTitleProjection("c1"),
})

const state = events
  .filter((event) => compositeProjection.query.matchesEvent(event))
  .reduce(compositeProjection.apply, compositeProjection.initialState)

// state => { courseExists: true, courseTitle: "Course 1 renamed" }
```

The state of the composite projection is an object keyed by each sub-projection name. The resulting query matches only events that are relevant to **at least one** of the composed projections — no more, no less.

---

## How to Use This with DCB

In the context of DCB, composite projections are particularly useful for building Decision Models that require strong consistency.

A lightweight translation layer extracts a query that efficiently loads only the events relevant to the composed projections. The `buildDecisionModel` function handles this:

```js
const { state, appendCondition } = buildDecisionModel(eventStore, {
  courseExists: CourseExistsProjection("c1"),
  courseTitle:  CourseTitleProjection("c1"),
})
```

- **`state`** – contains the composed state of all projections
- **`appendCondition`** – can be passed to the Event Store's `append()` method to enforce consistency (see [DCB Specification](../../Specification/DCB-Specification.md) for details)

The `appendCondition` ties the decision model to a specific position in the event stream. If any relevant event is appended by a concurrent operation between reading and writing, the append will be rejected — enforcing the **Dynamic Consistency Boundary**.

---

## Summary

| Concept | Description |
|---|---|
| **Projection** | Pure function `(state, event) => state` folded over an event sequence |
| **`initialState`** | Starting value before any events are applied |
| **`handlers`** | Map of event type → reducer; also defines which event types to load |
| **`tagFilter`** | Limits events to those tagged with specific entity identifiers |
| **Factory function** | Wraps a projection definition to inject dynamic parameters (e.g. entity id) |
| **`composeProjections`** | Combines multiple small projections; resulting query is the union of all sub-queries |
| **`buildDecisionModel`** | Reads the event store using the composed query and returns `state` + `appendCondition` |

---

## Conclusion

Projections play a fundamental role in DCB and Event Sourcing as a whole. The ability to combine multiple simple projections into more complex ones — tailored to specific use cases — unlocks a range of possibilities that can influence application design. Keeping projections small and single-purpose is key: it keeps the consistency boundary as narrow as possible and the code easy to reason about.
