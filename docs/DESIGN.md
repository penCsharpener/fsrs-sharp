# FsrsSharp design (port of ts-fsrs v6.0.0)

FsrsSharp is a C# 15 / .NET 11 port of [ts-fsrs](https://github.com/open-spaced-repetition/ts-fsrs)
**v6.0.0** (packages `ts-fsrs` and its internal dependency `@open-spaced-repetition/srs-kit`). The
NuGet package version tracks the ts-fsrs release it ports (6.0.0).

History: the code started as a port of ts-fsrs v5.4.1 (imported from an earlier project), which
now lives on under `FsrsSharp.Legacy` — mirroring upstream, where v6 moved the v5 `FSRS` class to
`src/legacy/`.

## Layout and namespaces

Directory = namespace. Upstream path in parentheses.

| Namespace | Contents |
|---|---|
| `FsrsSharp` | `Rating`, `State`, `ScheduleStatus`, `Grades`, exceptions (`FsrsException`, `FsrsValidationException`, `FsrsOperationException`), `FsrsMath` (help.ts), `DefaultScheduler` + options (scheduler/) |
| `FsrsSharp.Kit` | the srs-kit core: `FieldKey<T>`/`FieldBag`, `IModel`/`IModelCore`, `IChrono`/`IChronoCore`, `Middleware` + contexts, `Card<TState>`, `ReviewLog<TState>`, `SchedulerDefinition`, `Scheduler<TState,TTime>` (srs-kit/src/{schema,model,chrono,middleware,scheduler}) |
| `FsrsSharp.Chrono` | `DateTimeChrono` (srs-kit chrono/presets/date), `NumericChrono` (presets/numeric). The Temporal-instant preset is not ported: `DateTime` in UTC already is an instant. |
| `FsrsSharp.Models` | `FsrsState`, `Fsrs7State` (fsrs/src/kit) |
| `FsrsSharp.Models.Fsrs3` … `Fsrs7` (`Fsrs4Dot5` for 4.5) | `FsrsNModel` (singleton `Instance`), `FsrsNAlgorithm`, `FsrsNConstants` (default weights, bounds), parameter migrate/clip/check (fsrs/src/models/fsrs-N) |
| `FsrsSharp.Middlewares` | `StatsMiddleware` (srs-kit middleware/stats) and every fsrs/src/middlewares/* policy, one sub-folder each |
| `FsrsSharp.Reschedule` | fsrs/src/reschedule |
| `FsrsSharp.Legacy` | the v5-style `Fsrs` class and friends (fsrs/src/legacy) |

## Core concepts

* **Model** – pure memory-state math (`Step`, `NextInterval`, `ForgettingCurve`, `Forward`).
  `IModel<TState>` is a stateless singleton; `Create(config)` returns a configured `IModelCore<TState>`.
  Memory states are immutable **reference** types (records). The scheduler maps a stepped state back
  to its grade by reference identity, exactly like upstream's `Map<object, Grade>`.
* **Chrono** – time representation and arithmetic. `TTime` is a struct (`DateTime`, `double`).
  The chrono owns its card/revlog fields (`dueAt`, `lastReviewAt`, `reviewTime` for `DateTimeChrono`).
* **Middleware** – scheduling policy (learning steps, fuzzing, interval caps, stats, ...).
  Stateless singletons, model/chrono-agnostic thanks to generic virtual handlers. Onion model:
  call `next()` exactly once; overrides are applied on unwind.
* **Fields** – upstream composes card/revlog/config *schemas* from model + chrono + middlewares at
  the type level. C# can't, so the composed parts live in a name-keyed `FieldBag` with typed
  `FieldKey<T>` accessors (name = upstream camelCase field name, so persisted data is
  interchangeable). Each owner also ships C# 14 `extension` properties (`card.Reps`, `card.DueAt`,
  `card.LearningStep`) so day-to-day code stays strongly typed.
* **Card<TState> / ReviewLog<TState>** – the core fields every scheduler owns are real properties
  (`MemoryState`, `State`, `ScheduleStatus`, and `Rating` on the log); all the rest is in `Fields`.
  Upstream flattens the memory state into the card (`card.stability`); we nest it in `MemoryState`.
* **Config** – one flat `FieldBag` (e.g. `weights`, `fractionalDays`, `desiredRetention`,
  `learningSteps`, ...), validated at `SchedulerDefinition.Create` against every declared key.

## Conventions

* Field/config key names, defaults and validation messages follow upstream verbatim
  (e.g. `"Expected non-negative integer reps"`). Validation failures throw `FsrsValidationException`.
* Key objects live as `public static FieldKey<T> Xxx { get; }` on the owning type
  (`StatsMiddleware.Reps`, `DateTimeChrono.DueAt`, `Fsrs6Model.Weights`); extension properties
  live in a separate `static class XxxExtensions` in the same namespace (`StatsCardExtensions`).
  Two owners declaring the same field name (stats and leech both declare `lapses`) share one field.
* Collection-valued fields use `IReadOnlyList<T>` (`weights`: `IReadOnlyList<double>`,
  learning steps: `IReadOnlyList<string>` step units like `"1m"`, `"10m"`, `"1h"`, `"2d"`).
* Numeric parity with JavaScript: use `Helpers.JsRound` for `Math.round` (half rounds toward +∞).
  `DateTimeChrono.Add` mirrors `new Date(ms + days * 86_400_000)`: the millisecond offset is
  truncated toward zero to whole milliseconds. `DateTime`s with `Kind == Local` are converted to
  UTC; `Unspecified` is treated as UTC.
* Upstream's async `DefaultScheduler()` (dynamic imports) is synchronous here.
* Pure type-level upstream tests (`*.type-display.spec.ts`, `expectTypeOf`) are not ported; every
  runtime assertion is.
* Tests: xUnit v3 + AwesomeAssertions, under `tests/FsrsSharp.Tests/<Area>/`, one C# test class per
  upstream spec file, named after it (`learning-steps/middleware.spec.ts` →
  `Middlewares/LearningSteps/LearningStepsMiddlewareTests.cs`).
