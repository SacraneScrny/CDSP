# Minimal Counter sample

One Domain / System / Presentation / Context triple.

Setup:

1. Import the sample via Package Manager → Packages — CDSP → Samples → Minimal Counter
   (or copy `Samples~/MinimalCounter` into your project).
2. Create an empty `GameObject` at the scene root.
3. Add the `CounterRoot` component (`CDSP.Samples`).
4. Enter Play Mode — `Seconds` increments once per second (see Console).

Spawning the same node from any running system:

```csharp
Context.CreateChild<CounterContext>(counterPrefab);
```
