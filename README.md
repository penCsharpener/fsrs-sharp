# FsrsSharp

A C# 15 / .NET 11 port of [ts-fsrs](https://github.com/open-spaced-repetition/ts-fsrs) **v6.0.0**,
the Free Spaced Repetition Scheduler. It supports FSRS-3, 4, 4.5, 5, 6 and 7, a composable
scheduler built from models, chronos and middlewares, and the v5-style legacy API.

The NuGet version tracks the ts-fsrs release it ports, so `FsrsSharp 6.0.0` is ts-fsrs `6.0.0`.

**Upstream base:** ts-fsrs tag [`v6.0.0`](https://github.com/open-spaced-repetition/ts-fsrs/tree/v6.0.0),
commit [`b69d6ca2dca55579198c913fd55a43d6bf703b34`](https://github.com/open-spaced-repetition/ts-fsrs/commit/b69d6ca2dca55579198c913fd55a43d6bf703b34)
(2026-10-27). The port started from an earlier C# port of tag `v5.4.1`
(commit [`bfc0a1960dfde4b4627ae4f4c8757b9211314963`](https://github.com/open-spaced-repetition/ts-fsrs/commit/bfc0a1960dfde4b4627ae4f4c8757b9211314963)),
which was then upgraded to v6.0.0. When syncing with a newer ts-fsrs release, diff upstream from
the base commit above and update this section.

FsrsSharp is MIT-licensed and a derivative of ts-fsrs. See [LICENSE](LICENSE) and
[LICENSE-ts-fsrs](LICENSE-ts-fsrs).

## Install

```
dotnet add package FsrsSharp
```

## Quickstart

```csharp
using FsrsSharp;
using FsrsSharp.Kit;
using FsrsSharp.Chrono;
using FsrsSharp.Middlewares;

var scheduler = DefaultScheduler.Create();               // FSRS-7, Scheduler<Fsrs7State, DateTime>
var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
var card = scheduler.NewCard(now);

foreach (var item in scheduler.Preview(card, now))
    Console.WriteLine($"{item.Grade}: due {item.Card.DueAt}");

var result = scheduler.Review(card, Rating.Good, now);  // result.Card, result.Revlog: store both
```

`Preview` returns all four grades without changing the card. `Review` applies one grade and returns
the next card and the review log entry.

### Options

```csharp
var scheduler = DefaultScheduler.Create(new DefaultSchedulerOptions
{
    DesiredRetention = 0.9,
    EnableShortTerm = true,
    LearningSteps = ["1m", "10m"],
    RelearningSteps = ["10m"],
    EnableFuzz = false,
    MaximumInterval = 36_500,
});

// Other models: returns Scheduler<FsrsState, DateTime>
var fsrs6 = DefaultScheduler.Create(FsrsVersion.Fsrs6, options);
```

## Retrievability and next interval

```csharp
// Probability of recall at `checkedAt` for a card last reviewed at `reviewedAt`.
double elapsedDays = scheduler.Chrono.Difference(reviewedAt, checkedAt);
double retrievability = scheduler.Model.ForgettingCurve(card.MemoryState, elapsedDays);

// Days until retention drops to the target, from the model directly.
double days = scheduler.Model.NextInterval(card.MemoryState, 0.9);

// Same question through the scheduler, with its middlewares applied (learning steps, caps, fuzz).
double scheduled = scheduler.NextInterval(card.MemoryState, 0.9,
    new NextIntervalQuery<Fsrs7State>(card, Rating.Good, 0));
```

## Rollback, forget and forward

```csharp
// Undo the last review: the card as it was before `result.Revlog` was applied.
Card<Fsrs7State> previous = scheduler.Rollback(result.Card, result.Revlog);

// Reset the card to New. With StatsMiddleware installed, stats are cleared unless
// StatsMiddleware.ClearStatsOnForget is set to false.
Card<Fsrs7State> forgotten = scheduler.Forget(result.Card, now);

// Replay a history from scratch (or from initialCard) and get every intermediate result.
var history = new List<SchedulerReview<DateTime>>
{
    new(Rating.Good, now),
    new(Rating.Good, now.AddDays(3)),
    new(Rating.Again, now.AddDays(10)),
};
IReadOnlyList<ScheduleResult<Fsrs7State>> steps = scheduler.Forward(history);
```

## Custom schedulers

`DefaultScheduler` is a preset. The underlying pieces are public, so you can build your own:

```csharp
using FsrsSharp.Chrono;
using FsrsSharp.Kit;
using FsrsSharp.Middlewares;
using FsrsSharp.Models;
using FsrsSharp.Models.Fsrs7;

var definition = SchedulerDefinition
    .Define(Fsrs7Model.Instance, DateTimeChrono.Instance)   // model + chrono
    .Use(DesiredRetentionMiddleware.Instance);              // middlewares, applied in order

Scheduler<Fsrs7State, DateTime> scheduler = definition.Create(new FieldBag.Builder
{
    { Fsrs7Model.Weights, Fsrs7Constants.DefaultWeights },
    { KitSchemas.FractionalDays, true },
    { DesiredRetentionMiddleware.DesiredRetention, 0.9 },
});
```

- **Models** (`IModel<TState>`) hold the memory-state math: `Step`, `NextInterval`, `ForgettingCurve`,
  `Forward`. `FsrsSharp.Models.Fsrs3Model` through `Fsrs7Model` are stateless singletons, each with an
  `Instance`. Memory states are immutable records: `FsrsState(Stability, Difficulty)` for FSRS-3 to 6,
  and `Fsrs7State(Stability, StabilityFast, Difficulty)` for FSRS-7.
- **Chronos** (`IChrono<TTime>`) own time arithmetic and the time fields on cards and logs.
  `DateTimeChrono.Instance` uses `DateTime` (UTC) and owns `DueAt`, `LastReviewAt` and `ReviewTime`.
  `NumericChrono.Instance` uses `double` days.
- **Middlewares** (`Middleware`) add policy: `LearningStepsMiddleware`, `FuzzingMiddleware`,
  `MaximumIntervalMiddleware`, `MonotonicIntervalMiddleware`, `ScheduledDaysMiddleware`,
  `DesiredRetentionMiddleware`, `StatsMiddleware`, `LeechMiddleware`, `CostAdrMiddleware`.
  Order matters: register `LearningStepsMiddleware` before caps such as `MaximumIntervalMiddleware`.
- **FieldBag** is the name-keyed config and field store. Keys are `FieldKey<T>` values with upstream
  camelCase names (`weights`, `fractionalDays`, `maximumInterval`). `Create` validates every
  supplied key against the declared ones.
- **Extension properties** give strongly typed access: `card.DueAt`, `card.LastReviewAt`
  (`FsrsSharp.Chrono`), `card.Reps`, `card.Lapses` (`FsrsSharp.Middlewares`), `card.LearningStep`.

### Writing a middleware

A middleware overrides one or more of `NextInterval`, `Review`, `Rollback` or `WriteCardDefaults`
and calls `next()` exactly once. Code before `next()` runs on the way in; code after it runs on the
way out, when the inner layers have set their results.

```csharp
using FsrsSharp.Kit;

public sealed class ShortIntervalCapMiddleware : Middleware
{
    public static ShortIntervalCapMiddleware Instance { get; } = new();
    private ShortIntervalCapMiddleware() { }

    public override string Name => "myapp.short-interval-cap";

    public override void NextInterval<TState, TTime>(NextIntervalContext<TState, TTime> context, Action next)
    {
        next();   // let the model and inner middlewares run first
        if (context.ScheduledDays is { } days)
            context.ScheduledDays = Math.Min(days, 365);
    }
}
```

Use `Review` to change the `ResultCard` and `ResultRevlog` drafts, and `Rollback` for the reverse
direction. Do not repeat the constraints (`where TState : class`) on overrides; they are inherited.

## Persisting cards

A `Card<TState>` has four parts: `MemoryState` (the model's state record), `State`
(`New`, `Learning`, `Review`, `Relearning`), `ScheduleStatus` (a string-backed value) and `Fields`
(a `FieldBag` holding the chrono, middleware and config-derived fields by name).

Store these in your own database, for example:

- the memory-state values (`Stability`, `StabilityFast`, `Difficulty`),
- `State` and `ScheduleStatus.Value`,
- each `Fields` entry as a name and value pair (`dueAt`, `lastReviewAt`, `reps`, `lapses`, ...).

To rebuild, convert each stored value back to its key's CLR type (`DateTime`, `int`,
`IReadOnlyList<double>`, ...). `FieldBag.From` does not coerce values:

```csharp
var fields = FieldBag.From(storedPairs);              // IEnumerable<KeyValuePair<string, object?>>
var card = new Card<Fsrs7State>(
    new Fsrs7State(stability, stabilityFast, difficulty),
    state,
    new ScheduleStatus(scheduleStatusValue),
    fields);

DateTime due = card.DueAt;
```

## Legacy v5-style API

`FsrsSharp.Legacy.Fsrs` mirrors the v5 class shape (`Repeat`, `Next`, `Retrievability`, `Rollback`,
`Forget`, `Reschedule`) on top of the FSRS-6 algorithm. It mirrors ts-fsrs's `src/legacy`. Use it
when you are migrating code written against the v5 port; new code should use `DefaultScheduler` or
the Kit.

```csharp
using FsrsSharp;
using FsrsSharp.Legacy;

var f = new Fsrs(new FsrsParametersInput { RequestRetention = 0.9 });
var card = Defaults.CreateEmptyCard(now);

RecordLog preview = f.Repeat(card, now);             // all four grades
RecordLogItem next = f.Next(card, now, Rating.Good); // next.Card, next.Log

double r = f.Retrievability(next.Card, DateTime.UtcNow);
Card previous = f.Rollback(next.Card, next.Log);
```

## Differences from ts-fsrs

- **Sync construction.** ts-fsrs's `DefaultScheduler()` is async because it loads presets with
  dynamic imports. The C# `DefaultScheduler.Create` is synchronous.
- **Dates are `DateTime`.** Use UTC. `Kind == Local` is converted to UTC, and `Unspecified` is treated as
  UTC. There is no `DateInput`/`TypeConvert` layer.
- **Temporal chrono is not ported.** A UTC `DateTime` is already an instant.
- **Rounding matches JavaScript.** `Helpers.JsRound` implements `Math.round` (halves round toward +∞),
  not .NET's banker's rounding. This keeps intervals identical to ts-fsrs. `DateTimeChrono.Add` also
  truncates the millisecond offset the way `new Date(ms + days * 86_400_000)` does.
- **Schema composition is `FieldBag`, not types.** ts-fsrs composes card, revlog and config schemas at
  the type level. C# can't, so these live in a name-keyed `FieldBag` with typed `FieldKey<T>` accessors.
- **Memory state is nested.** ts-fsrs flattens it (`card.stability`). Here it is `card.MemoryState`.
- **Preview is lazy and cached per grade.** Each grade is computed on first access.
- **Legacy changes.** `get_retrievability` is `Retrievability`. The generic `afterHandler` result
  mapper is dropped; map `RecordLog` and `RecordLogItem` yourself. Assign individual parameters through
  `fsrs.UpdateParameters(new FsrsParametersInput { ... })`.
- **Upstream type-level tests are not ported** (`expectTypeOf` and `*.type-display.spec.ts`). Every
  runtime assertion is.

Rounding and fuzzing are verified against upstream golden vectors. The fuzzer uses an `Alea` PRNG port,
so with `EnableFuzz = true` and the same seed you get the same intervals as ts-fsrs.

## Building, testing and packing

```
dotnet test tests/FsrsSharp.Tests/FsrsSharp.Tests.csproj
dotnet pack src/FsrsSharp -c Release
```

The tests are an xUnit v3 port of ts-fsrs's `__tests__` suite, file by file, with AwesomeAssertions.
Test classes live under `tests/FsrsSharp.Tests/<Area>/` and are named after the upstream spec.

Project layout:

```
src/FsrsSharp/            the library (zero dependencies)
tests/FsrsSharp.Tests/    the test suite
docs/DESIGN.md            namespaces, conventions and the design of the port
```

### Versioning and releases

- `TsFsrsVersion` in `Directory.Build.props` is the single source of the package version
  (`VersionPrefix = TsFsrsVersion`). Set it to the ts-fsrs release you are porting.
- Port-only fixes between upstream releases can use a fourth segment (`6.0.0.1`) or a prerelease
  suffix (`VersionSuffix`, for example `6.0.1-beta.1`) in a local build.
- Pushing a tag `vX.Y.Z` runs `.github/workflows/release.yml`. It runs build and test, packs the
  library, checks that the tag matches the packed version, and pushes to NuGet. This needs a
  `NUGET_API_KEY` repository secret.

## Credits

FsrsSharp is a port of [ts-fsrs](https://github.com/open-spaced-repetition/ts-fsrs), and the FSRS
algorithm and its reference implementation come from the
[Open Spaced Repetition](https://github.com/open-spaced-repetition) organization and its contributors.
The ts-fsrs authors deserve the credit for the algorithm and the test vectors this port checks against.
