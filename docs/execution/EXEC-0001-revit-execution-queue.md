# EXEC-0001: Revit execution queue and ExternalEvent dispatch

- Status: Accepted
- Date: 2026-09-08

## Purpose

Define the minimum execution-dispatch contract required to run RevitMCP capability requests in a valid Autodesk Revit API context.

This specification supports CAP-0001 `revit_get_context` and refines the execution responsibilities established by ADR-0001. It intentionally does not define write-operation transaction policy, a general scheduler, or advanced multi-client fairness.

## Scope

The initial execution dispatcher must:

- accept capability work from bridge/background threads without allowing those threads to call the Revit API directly;
- serialize Revit API execution for one Revit process;
- dispatch work through one long-lived Revit `ExternalEvent`;
- provide asynchronous completion back to the bridge caller;
- support cancellation/timeout while work is still waiting in the queue;
- avoid unsafe interruption of work that has already started on the Revit thread;
- isolate failures so one failed request does not break the dispatcher or later requests;
- support graceful shutdown.

## Non-goals

EXEC-0001 does not define:

- write locking;
- automatic `Transaction` creation;
- `TransactionGroup` behavior;
- per-client priority or fairness;
- rate limiting;
- persistent jobs;
- automatic retries;
- parallel Revit API reads;
- background-thread Revit API access;
- distributed scheduling;
- remote/cloud execution semantics.

These concerns require separate decisions when concrete capabilities need them.

## Core model

Each running `RevitMCP.Addin` instance owns one execution dispatcher.

Conceptually:

```text
Bridge connection A --\
Bridge connection B ----> RevitExecutionDispatcher
Bridge connection C --/            |
                                  FIFO queue
                                    |
                               ExternalEvent
                                    |
                         Execute(UIApplication)
                                    |
                                Revit API
```

Multiple bridge connections may enqueue requests concurrently, but Revit API execution is serialized.

## One dispatcher per Revit process

The add-in creates one long-lived execution dispatcher for the lifetime of the RevitMCP-enabled Revit process.

The dispatcher owns:

- a thread-safe FIFO queue;
- one long-lived `IExternalEventHandler`;
- one long-lived `ExternalEvent`;
- dispatcher lifecycle state;
- completion of queued work items.

An `ExternalEvent` must not be created per capability request.

## Work item model

A queued operation is conceptually represented as:

```text
RevitWorkItem<T>
- operation
- completion
- cancellation state
- correlation_id
```

The exact implementation type is not prescribed, but the semantics are required.

### Operation

The operation contains the logic that must execute with access to the valid `UIApplication` supplied by `IExternalEventHandler.Execute`.

### Completion

Completion is asynchronous and allows the bridge/background caller to await the operation result without blocking the Revit thread.

### Cancellation state

Cancellation determines whether a queued item should still begin execution.

### Correlation ID

A correlation identifier may be used for logging and diagnostics. It is infrastructure metadata and is not part of the business capability contract unless a later specification explicitly says otherwise.

## FIFO serialization

The initial dispatcher uses FIFO ordering.

For requests A, B, and C accepted in that order:

```text
A -> execute
B -> execute
C -> execute
```

No two work items may execute Revit API code concurrently through this dispatcher.

FIFO is an intentionally simple initial policy. Priority, fairness, and batching are deferred until observed product requirements justify them.

## Scheduling and ExternalEvent raising

The dispatcher maintains a minimal internal lifecycle such as:

```text
Idle
ScheduledOrExecuting
Stopping
```

The exact synchronization primitive and state representation are implementation details.

When the queue transitions from empty to non-empty while the dispatcher is idle, the dispatcher schedules the long-lived `ExternalEvent`.

Additional requests arriving while execution is already scheduled or in progress are queued without independently requiring one `Raise()` call per request.

The implementation must tolerate Revit delaying execution of the raised event until the application is in a valid state to run the handler.

`ExternalEvent.IsPending` or the result of `Raise()` may be used as diagnostic/runtime inputs, but they must not be the sole concurrency-control mechanism for the dispatcher.

## Handler behavior

In the initial implementation, `IExternalEventHandler.Execute(UIApplication)` processes queued work sequentially.

Conceptually:

```text
Execute(UIApplication app)
    while queue has executable work
        dequeue next item
        if cancelled before start
            complete as cancelled/timeout as appropriate
            continue

        execute item using app
        complete success or failure

    transition dispatcher to Idle
```

If new work arrives during the transition around queue-empty/idle state, synchronization must ensure that work is not stranded without a future `Raise()`.

The implementation must be race-safe around this boundary.

## Initial drain policy

For CAP-0001 and the first vertical slice, the handler may drain all currently queued short operations in one `Execute` invocation.

No fixed batch-size or time-slice limit is required yet.

This decision is intentionally narrow because `revit_get_context` is bounded and fast. Before introducing expensive model queries, long-running operations, or writes, the drain policy must be reassessed.

## Async completion rule

The Revit thread must never wait synchronously for the bridge, MCP server, Named Pipe, or remote caller.

Required direction:

```text
Bridge/background thread
    enqueue work
    await completion
          ^
          |
Revit thread
    execute work
    complete result
```

Prohibited patterns include blocking Revit execution on `.Wait()`, `.Result`, pipe responses, MCP responses, or client acknowledgement.

Completion continuations should not be forced to execute synchronously on the Revit thread when that could cause re-entrancy or unnecessary work in the Revit execution context.

## Timeout and cancellation boundaries

Cancellation is cooperative and has different semantics before and after Revit execution begins.

### Before execution begins

If a caller cancels or times out while its work item is still queued, the item must not begin Revit API execution.

The dispatcher may either remove the item from the queue safely or leave a cancelled marker that is skipped when dequeued.

### After execution begins

Once the work item has started inside `Execute(UIApplication)`, caller cancellation or timeout must not forcibly abort the Revit thread or terminate the operation.

The implementation must not use unsafe interruption mechanisms such as thread abort/termination to stop active Revit API execution.

The caller may stop waiting and receive a timeout/cancellation result while the already-started Revit operation is allowed to finish normally.

For CAP-0001 this is safe because the capability is read-only and has no model side effects.

Future write capabilities must explicitly account for this distinction when defining cancellation semantics.

## Timeout ownership

EXEC-0001 does not define one universal hard-coded timeout duration.

Timeout policy is configured at the Server/Bridge boundary and may evolve based on real Revit validation.

For CAP-0001, an execution wait timeout maps to the accepted capability error:

```text
REVIT_EXECUTION_TIMEOUT
```

A timeout must not be interpreted as proof that the Revit process is dead; it means the capability did not complete within the caller's allowed wait period.

## Failure isolation

Each work item is an independent failure boundary.

Unexpected exceptions produced while executing one work item must be caught by the execution infrastructure at an appropriate boundary and converted into failure completion for that item.

A failed item must not:

- escape from the `ExternalEvent` handler in a way that disables normal dispatcher operation;
- discard unrelated queued work;
- terminate the bridge listener;
- poison subsequent capability requests.

Detailed exceptions and stack traces belong in diagnostics/logs rather than normal client-facing capability results.

For CAP-0001, an unexpected Revit execution failure maps to:

```text
REVIT_EXECUTION_FAILED
```

## Transaction ownership

The execution dispatcher does not automatically start a Revit `Transaction` around work items.

Its responsibility is valid Revit API dispatch, not mutation policy.

Conceptually:

```text
Execution dispatcher
    -> provides UIApplication / valid Revit execution context
Capability/application logic
    -> owns required transaction semantics
```

CAP-0001 is a read operation and starts no transaction.

Future write/destructive capabilities must define their own transaction strategy through accepted specifications/ADRs before implementation.

## Bridge relationship

BRIDGE-0001 `bridge.handshake` does not use this queue.

The handshake is answered from cached startup/process metadata.

Capability RPCs that require Revit API access, including CAP-0001, use the queue.

Conceptually:

```text
bridge.handshake
    -> bridge thread only
    -> no ExternalEvent

get_context
    -> execution queue
    -> ExternalEvent
    -> Revit API
```

## CAP-0001 execution

For the first capability:

```text
GetContextRequest {}
    -> enqueue read work item
    -> Execute(UIApplication)
    -> inspect current UIApplication / UIDocument / Document state
    -> create GetContextResult
    -> complete work item
    -> return through bridge
```

The operation must not start a Revit transaction.

No-active-document remains a successful CAP-0001 result state, as defined by the capability specification.

## Startup lifecycle

The execution dispatcher must be initialized before the add-in publishes its discovery registration as ready for bridge use.

Required conceptual startup order:

```text
1. capture cached instance metadata
2. create execution dispatcher and ExternalEvent
3. start Named Pipe bridge and handshake support
4. publish ADR-0003 registration record
```

A successful BRIDGE-0001 handshake should therefore imply that the instance has an initialized execution dispatcher capable of accepting capability work.

## Shutdown lifecycle

On add-in shutdown:

1. stop accepting new capability work;
2. remove or invalidate the discovery registration;
3. fail/cancel queued work that has not started;
4. allow any currently executing Revit work item to leave the Revit API in a valid state;
5. stop bridge connections/listener;
6. dispose execution infrastructure, including the `ExternalEvent`, when safe.

New callers during shutdown should observe instance/bridge unavailability rather than silently enqueueing work that can never execute.

The exact shutdown error mapping may be refined during implementation but must remain deterministic.

## Multi-client implications

Multiple local MCP clients may reach one Revit instance through separate `RevitMCP.Server` processes.

All their capability requests converge on the one in-process execution dispatcher.

EXEC-0001 intentionally provides FIFO serialization only. It does not guarantee per-client fairness under sustained load.

If starvation or noisy-neighbor behavior appears in real use, a later execution specification may introduce fairness or per-client scheduling without changing capability contracts.

## Thread-safety requirements

The implementation must be safe for concurrent enqueue calls from bridge/background threads.

All Autodesk Revit API access performed by queued capability work must occur only inside the valid Revit API execution context supplied by `ExternalEvent.Execute` or another future officially accepted execution mechanism.

Shared queue/state synchronization must not hold locks while executing arbitrary Revit capability code if that could block enqueue/completion paths or create deadlock risk.

## Required observable outcomes

For the first implementation, the dispatcher must demonstrate:

- concurrent enqueue calls do not execute Revit API work concurrently;
- FIFO order is preserved for non-cancelled queued work;
- a cancelled queued item does not execute;
- an exception in one work item does not prevent a later item from executing;
- completion returns success/failure asynchronously to the bridge caller;
- a timed-out caller does not forcibly abort an already-running Revit operation;
- shutdown rejects new work and completes queued-not-started work deterministically.

## Acceptance criteria

EXEC-0001 is correctly implemented for the first vertical slice when:

1. one long-lived dispatcher and `ExternalEvent` are created for a RevitMCP add-in instance;
2. bridge/background threads can enqueue work without calling the Revit API directly;
3. CAP-0001 executes through the dispatcher in `Execute(UIApplication)`;
4. queued capability work is serialized FIFO;
5. queued cancellation prevents execution;
6. caller timeout does not interrupt an operation already running in Revit;
7. unexpected operation failure maps to failure completion and later requests still execute;
8. no automatic Revit transaction is created by the dispatcher;
9. automated tests cover queue behavior independently from a real Revit process where practical;
10. real Revit validation confirms CAP-0001 execution reaches a valid Revit API context without deadlock or UI-thread misuse.

## Deferred decisions

The following remain explicitly deferred:

- queue capacity/backpressure;
- priorities;
- per-client fairness;
- execution time slicing;
- retry policy;
- write-operation locking;
- transaction/transaction-group strategy;
- destructive-operation confirmation;
- progress reporting;
- long-running jobs/tasks;
- observability schema beyond basic correlation/logging;
- distributed/cloud scheduling.

## References

- ADR-0001: Out-of-process MCP server and Revit capability host
- ADR-0002: Named Pipes + JSON-RPC local bridge
- ADR-0003: Revit instance registration, discovery, and addressing
- CAP-0001: `revit_get_context`
- BRIDGE-0001: Handshake and protocol-version negotiation
- Autodesk Revit API External Events documentation: https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Advanced_Topics/External_Events.html
