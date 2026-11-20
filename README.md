# FsrsSharp

A C# port of [ts-fsrs](https://github.com/open-spaced-repetition/ts-fsrs) v5.4.1 (FSRS **6.0**
algorithm), targeting **.NET 8**. There is no official C# implementation in
[open-spaced-repetition/awesome-fsrs](https://github.com/open-spaced-repetition/awesome-fsrs) —
this fills that gap by porting the TypeScript reference implementation line-for-line where the
languages allow it, rather than re-deriving the algorithm from the papers.

## What's ported

Everything needed to schedule cards day to day:

- `FsrsAlgorithm` — the DSR math (`algorithm.ts`): difficulty/stability formulas, the
  forgetting curve, fuzzing.
- `Fsrs` — the high-level API (`fsrs.ts`): `Repeat`, `Next`, `GetRetrievability`,
  `Rollback`, `Forget`, `Reschedule`.
- `BasicScheduler` / `LongTermScheduler` (`impl/*.ts`) — selected automatically based on
  `EnableShortTerm`, exactly like upstream.
- The default **learning-steps** and **seed** strategies (`strategies/*.ts`), pluggable via
  `Fsrs.UseLearningStepsStrategy` / `Fsrs.UseSeedStrategy`.
- `Defaults.GeneratorParameters` / `ClipParameters` / `MigrateParameters` (`default.ts`) — so
  FSRS-4.5 (17 weights) and FSRS-5 (19 weights) parameter sets you already have (e.g. from an
  existing Anki collection) are auto-upgraded to FSRS-6 (21 weights), same as upstream.
- `Reschedule` (`reschedule.ts`) — replay a full review history through the current
  parameters, e.g. after re-optimizing weights.
- The `Alea` PRNG, used for fuzzing, ported bit-for-bit so fuzzed intervals are
  deterministic for a given seed.

**Not ported** (rarely used, and easy to add yourself if you need them): the pluggable
"Scheduler" strategy slot (`useStrategy(StrategyMode.SCHEDULER, ...)`, for swapping in an
entirely custom scheduler class) and the `Date.prototype` convenience extensions
(`help.ts`'s `scheduler`/`diff`/`format`/`dueFormat`, all marked deprecated upstream anyway).

### Deliberate API differences from ts-fsrs

- Dates are plain `DateTime` rather than `Date | number | string` — no `DateInput`/`TypeConvert`
  layer, since C#'s type system already gives you that.
- ts-fsrs mutates `fsrs.parameters` through a JS `Proxy` (assigning `f.parameters.w = [...]`
  recomputes derived state as a side effect). C# has no equivalent, so call
  `fsrs.UpdateParameters(new FsrsParametersInput { ... })` instead — the `Parameters` getter
  returns a read-only snapshot.
- The generic `afterHandler` result-mapping parameter (`f.repeat(card, now, myMapper)`) is
  dropped; map `RecordLog`/`RecordLogItem` yourself, C# doesn't need the indirection.
- Rounding matches JavaScript's `Math.round` (half rounds toward +∞), not .NET's default
  banker's rounding — see `Helpers.JsRound`. This matters for exact interval parity.

## Usage

```csharp
using FsrsSharp;

var f = new Fsrs(); // FSRS-6 defaults, request_retention = 0.9
var card = Defaults.CreateEmptyCard();
var now = DateTime.UtcNow;

// Preview all four grades:
var preview = f.Repeat(card, now);
Console.WriteLine(preview[Rating.Good].Card.Due);

// Or apply one grade directly:
var (nextCard, log) = f.Next(card, now, Rating.Good);

// Later, check recall probability before it's due:
double r = f.GetRetrievability(nextCard, DateTime.UtcNow);

// Undo the last review:
Card previous = f.Rollback(nextCard, log);
```

Custom parameters (equivalent to ts-fsrs's `generatorParameters`/`fsrs({...})`):

```csharp
var f = new Fsrs(new FsrsParametersInput
{
    RequestRetention = 0.85,
    MaximumInterval = 3650,
    W = myOptimizedWeights,     // 17, 19 or 21 numbers — auto-migrated to FSRS-6
    EnableFuzz = true,
    LearningSteps = new[] { "1m", "10m" },
    RelearningSteps = new[] { "10m" },
});
```

## Project layout

```
src/FsrsSharp/            the library (class library, zero dependencies)
tests/FsrsSharp.Tests/    an xUnit v3 port of ts-fsrs's own __tests__ suite (see below)
```

## Verifying correctness

`tests/FsrsSharp.Tests` is a port of ts-fsrs's own `__tests__` suite, file by file — the same
scenarios, the same golden vectors, the same tolerances. Run it with:

```
dotnet test tests/FsrsSharp.Tests/FsrsSharp.Tests.csproj
```

186 tests, covering:

| Upstream file | Ported to | What it pins |
|---|---|---|
| `FSRS-6.test.ts` | `Fsrs6Tests` | the 13-review `ivl_history` vector `[0, 2, 11, 46, 163, 498, 0, 0, 2, 4, 7, 12, 21]`, the short/long-term memory states to 4 dp, and the first-review outcome for all four grades |
| `FSRS-5.test.ts` | `Fsrs5Tests` | the same, seeded with a 19-weight FSRS-5 set — so the migration to 21 weights is pinned along with the scheduler |
| `impl/long-term_scheduler.test.ts` | `LongTermSchedulerTests` | five full review sequences through the long-term scheduler, asserting interval, stability *and* difficulty after every review, plus switching schedulers mid-history |
| `impl/basic_scheduler.test.ts`, `impl/abstract_scheduler.test.ts` | `BasicSchedulerTests` | previewing all four grades agrees with applying each one, from New, Learning and Review |
| `algorithm.test.ts` | `AlgorithmTests` | the DSR math on its own — decay/factor, the forgetting curve, initial and next difficulty/stability, the interval modifier, fuzz ranges, and every `next_state` validation branch |
| `default.test.ts` | `DefaultsTests` | FSRS-4.5 → 6 and FSRS-5 → 6 weight migration, parameter validation, and the w17/w18 ceiling |
| `reschedule.test.ts` | `RescheduleTests` | replay-vs-live parity over **every** 4-grade sequence (256) and every 5-entry sequence including manual entries (3125), plus the manual-entry and alignment-item golden values |
| `strategies/learning-steps.test.ts` | `LearningStepsStrategyTests` | every learning/relearning step table, and custom strategies wired through `Repeat` |
| `fixed/prevent-skipping.test.ts` | `PreventSkippingStepsTests` | six learning steps advance one at a time (upstream's #311) |
| `fixed/calc-elapsed-days.test.ts` | `SimulatorTests` | two real review logs replayed end to end |
| `fixed/same-seed.test.ts` | `SameSeedTests` | fuzzing is deterministic per seed (upstream's #113) |
| `alea.test.ts` | `AleaTests` | the PRNG's known-value vectors, so fuzzed intervals match ts-fsrs bit for bit |
| `rollback.test.ts`, `forget.test.ts`, `elapsed_days.test.ts` | `RollbackTests`, `ForgetTests`, `ElapsedDaysTests` | rollback inverts next for every grade and state; forget with and without resetting counts; elapsed-day arithmetic |
| `models.test.ts`, `constant.test.ts`, `help.test.ts`, `error.test.ts` | `ModelsTests`, `ConstantsTests`, `HelpersTests`, `ErrorsTests` | wire-format enum values, defaults and clamping, date/rounding helpers, the exception hierarchy |

### What is deliberately not ported

Each of these tests a feature this port doesn't have, not a gap in the testing:

- **`show_diff_message.test.ts`** and `help.test.ts`'s `formatDate` group — the `Date.prototype`
  convenience extensions, which aren't ported (they're deprecated upstream too).
- **`handler.test.ts`** and `help.test.ts`'s `TypeConvert` group — the `afterHandler` result mapper
  and the `DateInput` string/number union, neither of which exists here: dates are `DateTime`.
- **`alea.test.ts`'s `state()` / `importState()` / `int32()` / `double()` cases** — the C# `Alea`
  exposes only the plain generator call, which is all the fuzzer needs.
- **`strategies/seed.test.ts`'s private-field assertions** — `SeedStrategyTests` checks the
  behaviour those assertions protect (fuzzing varies by seed, and is stable for a given one)
  rather than which string was installed.
- **`version.test.ts`** — there is no `package.json` version to pair `FSRSVersion` against.

Fuzzing (`EnableFuzz`) is off by default and is exercised only where upstream exercises it. If you
turn it on in production, the `AleaTests` vectors are the reason to expect the same intervals
ts-fsrs would have produced.

## License

ts-fsrs is MIT-licensed; this port follows the same terms. Attribution: algorithm and
original TypeScript implementation by the [Open Spaced Repetition](https://github.com/open-spaced-repetition)
organization.
