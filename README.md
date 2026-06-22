# Topics

A lightweight, allocation-efficient pub/sub library for .NET with first-class support for async handlers, keyed routing,
and predicate filtering. Designed as an alternative to standard C# events where more control over dispatch,
cancellation, and routing is needed.

---

## Topic Types

| Class                      | Dispatch       | Invoke                           | Handlers     |
|----------------------------|----------------|----------------------------------|--------------|
| `KeyedTopic<TKey, T>`      | O(1) by key    | Sync                             | Sync         |
| `AsyncKeyedTopic<TKey, T>` | O(1) by key    | Async (concurrent or sequential) | Sync + Async |
| `FilterableTopic<T>`       | O(n) predicate | Sync                             | Sync         |
| `AsyncFilterableTopic<T>`  | O(n) predicate | Async (concurrent or sequential) | Sync + Async |

---

## Core Concepts

### Subscription lifetime via `IDisposable`

All `Subscribe` overloads return an `IDisposable`. Disposing it unsubscribes the handler and, for async topics, cancels
any in-flight invocation of that handler.

```csharp
var subscription = topic.Subscribe(key, handler);

// Later — unsubscribes and signals cancellation to the handler
subscription.Dispose();
```

### Exception behavior

Exceptions propagate to the invoke caller, matching standard C# event semantics. The invoker is responsible for handling
them — topics do not swallow or log exceptions internally.

## Keyed Topics

Use when each update naturally has a routing key. Subscribers register for a specific key; only matching handlers are
invoked. Dispatch is O(1) via `ConcurrentDictionary`.

### `KeyedTopic<TKey, T>` (sync)

```csharp
public class KnxFloatTopic : KeyedTopic<KnxKey, float>;

// Subscribe
var sub = topic.Subscribe(knxKey, value => Console.WriteLine(value));

// Invoke — only handlers registered for knxKey are called
topic.Invoke(knxKey, 0.75f);

// Unsubscribe
sub.Dispose();
```

### `AsyncKeyedTopic<TKey, T>`

Supports both sync and async handlers. Accepts a `CancellationToken` on invoke that is linked with each subscription's
own token.

```csharp
public class KnxBoolTopic : AsyncKeyedTopic<KnxKey, bool>;

// Async handler
var sub = topic.Subscribe(knxKey, async (value, ct) =>
{
    await ProcessAsync(value, ct);
});

// Sync handler (adapted automatically)
var sub2 = topic.Subscribe(knxKey, value => Process(value));

// Subscribe multiple keys from a dictionary
var subs = topic.Subscribe(dictionary);

// Concurrent — all handlers for the key run in parallel
await topic.InvokeAsync(knxKey, true, cancellationToken);

// Sequential — handlers run one at a time; an exception stops the rest
await topic.InvokeSequentialAsync(knxKey, true, cancellationToken);
```

---

## Filterable Topics

Use when subscribers need to express arbitrary filter criteria, or when there is no natural routing key. All subscribers
are evaluated on every invoke — O(n).

### `FilterableTopic<T>` (sync)

```csharp
public class NetworkTopic : FilterableTopic<NetworkState>;

// Subscribe without filter — receives all updates
var sub = topic.Subscribe(state => UpdateUi(state));

// Subscribe with filter — receives only matching updates
var sub2 = topic.Subscribe(
    state => state.Type == NetworkMonitorType.Knx,
    state => HandleKnxNetwork(state));

topic.Invoke(new NetworkState(...));
```

### `AsyncFilterableTopic<T>`

```csharp
public class ScenarioTopic : AsyncFilterableTopic<ScenarioUpdate>;

// Async handler with filter
var sub = topic.Subscribe(
    update => update.ScenarioId == activeId,
    async (update, ct) => await ApplyScenarioAsync(update, ct));

// Concurrent invocation
await topic.InvokeAsync(scenarioUpdate, cancellationToken);

// Sequential invocation
await topic.InvokeSequentialAsync(scenarioUpdate, cancellationToken);
```

---

## Thread Safety

All topic types are safe for concurrent subscribe, dispose, and invoke operations.

- **Keyed topics** use `ConcurrentDictionary` with `ImmutableArray` values. Subscription changes use atomic
  `AddOrUpdate`.
- **Filterable topics** use an `ImmutableArray` field updated via `ImmutableInterlocked.Update` (lock-free CAS loop).
  Invoke takes a snapshot of the subscription list — concurrent subscribe/dispose during an active invoke affects the
  *next* invocation, not the current one.

---

## Defining a Topic

Extend the appropriate base class. The class itself carries no logic — it is purely a named, typed channel.

```csharp
// Keyed — sync
public class KnxFloatTopic : KeyedTopic<KnxKey, KnxFloatTopicUpdate>;

// Keyed — async
public class KnxBoolTopic : AsyncKeyedTopic<KnxKey, KnxBoolTopicUpdate>;

// Filterable — sync
public class NetworkTopic : FilterableTopic<NetworkState>;

// Filterable — async
public class ScenarioTopic : AsyncFilterableTopic<ScenarioUpdate>;

// Signal-only (no data)
public class ReloadTopic : FilterableTopic<EmptyUpdate>;
```

Register as singletons in your DI container so all publishers and subscribers share the same instance.

---

## Choosing Between Concurrent and Sequential Invoke

|                   | `InvokeAsync`                                  | `InvokeSequentialAsync`                  |
|-------------------|------------------------------------------------|------------------------------------------|
| Handler execution | All start immediately in parallel              | One at a time, in subscription order     |
| Total time        | ≈ max(handler durations)                       | ≈ sum(handler durations)                 |
| On exception      | All handlers still run; all exceptions surface | First exception stops remaining handlers |

Use `InvokeAsync` by default. Use `InvokeSequentialAsync` when handlers must not overlap or when ordering matters (e.g.
write-then-notify patterns).
