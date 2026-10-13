# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added

- `Context<TDomain, TSystem, TPresentation>.FindPresentation(scope?, includeChildren)` / `TryFindPresentation(...)`: bind an already existing view without instantiating (e.g. nested inside another presentation). Adopts the found view's `GameObject` as `Instance`, releases a previous own root via the destroy action, awakes late-bound views immediately when the context already runs.
- `Presentation.ChildrenRoot`: children slot picked in the Inspector (Editor `Children Root` button with a hierarchy dropdown, stored per prefab/scene, view root when unset). All child placement — creation, `PutTransformInside`, awake fix-up, hot-swap park/reattach, `FindPresentation` adoption — targets the slot.

### Changed

- Relaxed generic constraints from `where T : Context` to `where T : class`: `GetChild<T>`, `TryGetChild<T>`, `HasChild<T>`, `DestroyChild<T>`, `SpecificChildCount<T>`, `GetChildren<T>`, `Is<T>`, `As<T>`, `IsValid<T>`, `IsSimulationRunning<T>`, `EntryPoint.FindRoot<T>` now accept interfaces implemented by contexts. Creation (`CreateChild<T>`, `ContextRoot<TContext>`) still requires a concrete `Context` with `new()`.
- `Context.Instance` setter widened from `private` to `internal` so triple contexts can adopt a found view's `GameObject`.

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

- Single-folder `CDSP.asmdef` at package root (moved to `Runtime/CDSP.asmdef`).
