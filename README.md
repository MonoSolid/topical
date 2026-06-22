# Topics

A lightweight, allocation-efficient pub/sub library for .NET with first-class support for async handlers, keyed routing,
and predicate filtering. Designed as an alternative to standard C# events where more control over dispatch,
cancellation, and routing is needed.

---

## Topic Types

| Class                      | Dispatch       | Invoke | Handlers     |
|----------------------------|----------------|--------|--------------|
| `KeyedTopic<TKey, T>`      | O(1) by key    | Sync   | Sync         |
| `AsyncKeyedTopic<TKey, T>` | O(1) by key    | Async  | Sync + Async |
| `Topic<T>`                 | O(n) predicate | Sync   | Sync         |
| `AsyncTopic<T>`            | O(n) predicate | Async  | Sync + Async |

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
```

---

## Topics

Use when subscribers need to express arbitrary filter criteria, or when there is no natural routing key. All subscribers
are evaluated on every invoke — O(n).

### `Topic<T>` (sync)

```csharp
public class NetworkTopic : Topic<NetworkState>;

// Subscribe without filter — receives all updates
var sub = topic.Subscribe(state => UpdateUi(state));

// Subscribe with filter — receives only matching updates
var sub2 = topic.Subscribe(
    state => state.Type == NetworkMonitorType.Knx,
    state => HandleKnxNetwork(state));

topic.Invoke(new NetworkState(...));
```

### `AsyncTopic<T>`

```csharp
public class ScenarioTopic : AsyncTopic<ScenarioUpdate>;

// Async handler with filter
var sub = topic.Subscribe(
    update => update.ScenarioId == activeId,
    async (update, ct) => await ApplyScenarioAsync(update, ct));

// Concurrent invocation
await topic.InvokeAsync(scenarioUpdate, cancellationToken);
```

---

## Thread Safety

All topic types are safe for concurrent subscribe, dispose, and invoke operations.

- **Keyed topics** use `ConcurrentDictionary` with `ImmutableArray` values. Subscription changes use atomic
  `AddOrUpdate`.
- **Topics** use an `ImmutableArray` field updated via `ImmutableInterlocked.Update` (lock-free CAS loop).
  Invoke takes a snapshot of the subscription list — concurrent subscribe/dispose during an active invoke affects the
  *next* invocation, not the current one.

---

## Defining a Topic

Extend the appropriate base class and declare a matching non-generic interface. The class itself carries no logic — it
is purely a named, typed channel.

```csharp
// Keyed — sync
public interface IKnxFloatTopic : IKeyedTopic<KnxKey, KnxFloatTopicUpdate>;
public class KnxFloatTopic : KeyedTopic<KnxKey, KnxFloatTopicUpdate>, IKnxFloatTopic;

// Keyed — async
public interface IKnxBoolTopic : IAsyncKeyedTopic<KnxKey, KnxBoolTopicUpdate>;
public class KnxBoolTopic : AsyncKeyedTopic<KnxKey, KnxBoolTopicUpdate>, IKnxBoolTopic;

// Topic — sync
public interface INetworkTopic : ITopic<NetworkState>;
public class NetworkTopic : Topic<NetworkState>, INetworkTopic;

// Topic — async
public interface IScenarioTopic : IAsyncTopic<ScenarioUpdate>;
public class ScenarioTopic : AsyncTopic<ScenarioUpdate>, IScenarioTopic;

// Signal-only (no data)
public interface IReloadTopic : ITopic<EmptyUpdate>;
public class ReloadTopic : Topic<EmptyUpdate>, IReloadTopic;
```

Register as singletons in your DI container so all publishers and subscribers share the same instance.

---

## Dependency Injection

Each topic class implements a matching generic interface that exposes its full public API:

| Interface                      | Implemented by             |
|--------------------------------|----------------------------|
| `IKeyedTopic<TKey, T>`         | `KeyedTopic<TKey, T>`      |
| `IAsyncKeyedTopic<TKey, T>`    | `AsyncKeyedTopic<TKey, T>` |
| `ITopic<T>`                    | `Topic<T>`                 |
| `IAsyncTopic<T>`               | `AsyncTopic<T>`            |

### Recommended: named (non-generic) topic interfaces

The recommended DI pattern is to define a dedicated non-generic interface for each topic. This keeps injection sites
clean and free of type parameters, and gives each topic a distinct identity in the DI container.

**1. Declare the topic interface** by extending the appropriate library interface:

```csharp
public interface IKnxFloatTopic : IAsyncKeyedTopic<KnxKey, float>;
public interface INetworkTopic : ITopic<NetworkState>;
public interface IScenarioTopic : IAsyncTopic<ScenarioUpdate>;
```

**2. Implement it on the topic class** alongside the base class:

```csharp
public class KnxFloatTopic : AsyncKeyedTopic<KnxKey, float>, IKnxFloatTopic;
public class NetworkTopic : Topic<NetworkState>, INetworkTopic;
public class ScenarioTopic : AsyncTopic<ScenarioUpdate>, IScenarioTopic;
```

**3. Register against the named interface:**

```csharp
services.AddSingleton<IKnxFloatTopic, KnxFloatTopic>();
services.AddSingleton<INetworkTopic, NetworkTopic>();
services.AddSingleton<IScenarioTopic, ScenarioTopic>();
```

**4. Inject the named interface** — no generic parameters at the injection site:

```csharp
// Subscriber
public class KnxListener(IKnxFloatTopic topic)
{
    public IDisposable Start() =>
        topic.Subscribe(KnxKey.Temperature, async (value, ct) =>
            await HandleTemperatureAsync(value, ct));
}

// Publisher
public class KnxPublisher(IKnxFloatTopic topic)
{
    public Task PublishAsync(KnxKey key, float value, CancellationToken ct) =>
        topic.InvokeAsync(key, value, ct);
}
```

Because `IKnxFloatTopic` extends `IAsyncKeyedTopic<KnxKey, float>`, it carries the full API with no extra boilerplate.
Multiple consumers can share the same singleton instance via the same named interface.
