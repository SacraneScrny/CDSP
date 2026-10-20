# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [0.2.0] - 2026-10-20

### Added

- `Context<TDomain, TSystem, TPresentation>.FindPresentation(scope?, includeChildren)` / `TryFindPresentation(...)`: bind an already existing view without instantiating (e.g. nested inside another presentation). Adopts the found view's `GameObject` as `Instance`, releases a previous own root via the destroy action, awakes late-bound views immediately when the context already runs.
- `Presentation.ChildrenRoot`: children slot picked in the Inspector (Editor `Children Root` button with a hierarchy dropdown, stored per prefab/scene, view root when unset). All child placement — creation, `PutTransformInside`, awake fix-up, hot-swap park/reattach, `FindPresentation` adoption — targets the slot.
- Runtime overlay (`CDSPOverlay`) is stripped from Release builds (Editor and Development Builds unaffected): no windows, labels, input polling or `GameObject`; the public API stays and becomes no-ops (`Instance` is `null`). Define `CDSP_OVERLAY_IN_RELEASE` to force-include it in Release.
- Signals (`CDSP.Entities`): `Signal` + `Signal<T1..T5>` (up to 5 payload args) with `Subscribe` / `Emit`, disposable `SignalHandler`, `ASignal` base with `SubscriberCount`. Subscribing/unsubscribing from inside a handler of the same signal logs an error and is refused.

### Fixed

- Runtime overlay hotkeys (`F1`/`F2`/`F3`) and label mouse-fade no longer throw `InvalidOperationException` with `Active Input Handling = Input System`: dual-backend input (`CDSPOverlayInput`, legacy guarded + new Input System via reflection), zero new dependencies.

### Changed

- Relaxed generic constraints from `where T : Context` to `where T : class`: `GetChild<T>`, `TryGetChild<T>`, `HasChild<T>`, `DestroyChild<T>`, `SpecificChildCount<T>`, `GetChildren<T>`, `Is<T>`, `As<T>`, `IsValid<T>`, `IsSimulationRunning<T>`, `EntryPoint.FindRoot<T>` now accept interfaces implemented by contexts. Creation (`CreateChild<T>`, `ContextRoot<TContext>`) still requires a concrete `Context` with `new()`.
- `Context.Instance` setter widened from `private` to `internal` so triple contexts can adopt a found view's `GameObject`.
- All child placement (`CreatePresentation`, `PutTransformInside`, awake fix-up, hot-swap park/reattach, `FindPresentation` adoption) now parents with `worldPositionStays: false`: a child's local transform (scale included) stays as authored instead of being rescaled under a scaled parent.
- `Context.System`, `SharedContext.System` and `Presentation.System` are now `internal`: cross-context logic goes through `Signal` fields declared on contexts (reached via `Up<T>()`); `Domain` stays readable. `Presentation<TSystem>.System` keeps `protected` typed access to the owning system.

## [0.1.0] - 2026-10-10

### Added

- Initial UPM package layout: `Runtime/` + `Editor/` + `Samples~/`.
- Runtime assembly `CDSP` (`Runtime/CDSP.asmdef`): `Base`, `Components`, `Entry`, `Entities`, `Overlay`, `Utils`.
- Editor assembly `CDSP.Editor` (`Editor/CDSP.Editor.asmdef`, Editor-only): CDSP Explorer window (`CDSP / Explorer`, `Ctrl+Shift+E`).
- `package.json` (`com.cdsp.sackrany`): Unity 2021.3+, zero dependencies.
- Sample `MinimalCounter`: one Domain / System / Presentation / Context triple.

### Changed

- Removed `JetBrains.Annotations` dependency (`[CanBeNull]` dropped, plain `string = null` optionals). Package now builds without Rider/annotations.
- `README`: installation via Unity Package Manager (git URL).

### Removed

- Event bus: `EventBus<TKey>`, `IEventBus<TKey>`, `Subscription<TKey>`, handlers, `ClearEvents` and the `Context<TDomain, TSystem, TPresentation, TEventKey>` flavor. One-to-many notifications are now `Signal` fields on the concrete context (no key enums). Explorer and runtime overlay show signals instead of bus subscriptions.
- Single-folder `CDSP.asmdef` at package root (moved to `Runtime/CDSP.asmdef`).
