# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/).

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
