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
public class StockPriceTopic : KeyedTopic<string, decimal>;

// Subscribe — only called when "AAPL" is published
var sub = topic.Subscribe("AAPL", price => Console.WriteLine(price));

// Invoke — only handlers registered for "AAPL" are called
topic.Invoke("AAPL", 189.45m);

// Unsubscribe
sub.Dispose();
```

### `AsyncKeyedTopic<TKey, T>`

Supports both sync and async handlers. Accepts a `CancellationToken` on invoke that is linked with each subscription's
own token.

```csharp
public class OrderTopic : AsyncKeyedTopic<Guid, OrderEvent>;

// Async handler
var sub = topic.Subscribe(orderId, async (e, ct) =>
{
    await SendConfirmationEmailAsync(e, ct);
});

// Sync handler (adapted automatically)
var sub2 = topic.Subscribe(orderId, e => UpdateOrderCache(e));

// Subscribe multiple keys from a dictionary
var subs = topic.Subscribe(dictionary);

// Concurrent — all handlers for the key run in parallel
await topic.InvokeAsync(orderId, orderEvent, cancellationToken);
```

---

## Topics

Use when subscribers need to express arbitrary filter criteria, or when there is no natural routing key. All subscribers
are evaluated on every invoke — O(n).

### `Topic<T>` (sync)

```csharp
public class LogTopic : Topic<LogEntry>;

// Subscribe without filter — receives all log entries
var sub = topic.Subscribe(entry => Console.WriteLine(entry.Message));

// Subscribe with filter — receives only errors
var sub2 = topic.Subscribe(
    entry => entry.Level == LogLevel.Error,
    entry => AlertOpsTeam(entry));

topic.Invoke(new LogEntry(LogLevel.Error, "Disk full"));
```

### `AsyncTopic<T>`

```csharp
public class PaymentTopic : AsyncTopic<PaymentEvent>;

// Async handler with filter — only failed payments
var sub = topic.Subscribe(
    e => e.Status == PaymentStatus.Failed,
    async (e, ct) => await NotifyCustomerAsync(e, ct));

// Concurrent invocation
await topic.InvokeAsync(paymentEvent, cancellationToken);
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
public interface IStockPriceTopic : IKeyedTopic<string, decimal>;
public class StockPriceTopic : KeyedTopic<string, decimal>, IStockPriceTopic;

// Keyed — async
public interface IOrderTopic : IAsyncKeyedTopic<Guid, OrderEvent>;
public class OrderTopic : AsyncKeyedTopic<Guid, OrderEvent>, IOrderTopic;

// Topic — sync
public interface ILogTopic : ITopic<LogEntry>;
public class LogTopic : Topic<LogEntry>, ILogTopic;

// Topic — async
public interface IPaymentTopic : IAsyncTopic<PaymentEvent>;
public class PaymentTopic : AsyncTopic<PaymentEvent>, IPaymentTopic;

// Signal-only (no data)
public interface IShutdownTopic : ITopic<Unit>;
public class ShutdownTopic : Topic<Unit>, IShutdownTopic;
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
public interface IOrderTopic : IAsyncKeyedTopic<Guid, OrderEvent>;
public interface ILogTopic : ITopic<LogEntry>;
public interface IPaymentTopic : IAsyncTopic<PaymentEvent>;
```

**2. Implement it on the topic class** alongside the base class:

```csharp
public class OrderTopic : AsyncKeyedTopic<Guid, OrderEvent>, IOrderTopic;
public class LogTopic : Topic<LogEntry>, ILogTopic;
public class PaymentTopic : AsyncTopic<PaymentEvent>, IPaymentTopic;
```

**3. Register against the named interface:**

```csharp
services.AddSingleton<IOrderTopic, OrderTopic>();
services.AddSingleton<ILogTopic, LogTopic>();
services.AddSingleton<IPaymentTopic, PaymentTopic>();
```

**4. Inject the named interface** — no generic parameters at the injection site:

```csharp
// Subscriber
public class OrderConfirmationService(IOrderTopic topic)
{
    public IDisposable Start() =>
        topic.Subscribe(orderId, async (e, ct) =>
            await SendConfirmationEmailAsync(e, ct));
}

// Publisher
public class OrderService(IOrderTopic topic)
{
    public Task PlaceOrderAsync(Order order, CancellationToken ct) =>
        topic.InvokeAsync(order.Id, new OrderEvent(order), ct);
}
```

Because `IOrderTopic` extends `IAsyncKeyedTopic<Guid, OrderEvent>`, it carries the full API with no extra boilerplate.
Multiple consumers can share the same singleton instance via the same named interface.
