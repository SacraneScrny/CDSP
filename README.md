# CDSP — Context / Domain / System / Presentation

> **Repo:** <https://github.com/SacraneScrny/CDSP> · **Package:** `com.cdsp.sackrany` · **Unity:** 2021.3+ · **Dependencies:** none · **License:** MIT

## Links and installation

- GitHub: <https://github.com/SacraneScrny/CDSP>
- Issues / bugs / feature requests: <https://github.com/SacraneScrny/CDSP/issues>
- Changelog: [`CHANGELOG.md`](CHANGELOG.md), license: [`LICENSE.md`](LICENSE.md), package manifest: [`package.json`](package.json)

Pick one way to add CDSP to your Unity project:

**1. Package Manager UI — from git URL (recommended):**
1. `Window → Package Manager → + → Install package from git URL…`
2. Paste:
   ```
   https://github.com/SacraneScrny/CDSP.git
   ```
   Specific version / branch (optional):
   ```
   https://github.com/SacraneScrny/CDSP.git#v0.1.0
   https://github.com/SacraneScrny/CDSP.git#main
   ```
3. Update: `Package Manager → Packages — CDSP → Update`, remove: `Remove`.

**2. Via `Packages/manifest.json` (same result, as text):**
```json
{
  "dependencies": {
    "com.cdsp.sackrany": "https://github.com/SacraneScrny/CDSP.git#v0.1.0"
  }
}
```
Use `#main` for latest, `#vX.Y.Z` for a pinned version. Unity will download it on next resolve.

**3. ZIP (no git):**
1. `Code → Download ZIP` on the GitHub page (or `https://github.com/SacraneScrny/CDSP/archive/refs/heads/main.zip`).
2. Unzip, then either:
   - `Package Manager → + → Install package from disk… → package.json`, or
   - copy the folder into your project's `Packages/` as `Packages/com.cdsp.sackrany`.

**4. Local clone (for contributing):**
```bash
git clone https://github.com/SacraneScrny/CDSP.git
```
Then `Install package from disk… → <clone-path>/package.json`.

After install, import the demo: `Package Manager → Packages — CDSP → Samples → Minimal Counter`.

---

CDSP is a lightweight Unity architecture built around a **tree of contexts** with strict separation of responsibilities:

| Part | What it is | What it does |
|---|---|---|
| **Domain** | Plain data. No Unity API, no logic. | Holds state, cleans itself in `OnDestroy`. |
| **System** | Logic. Ticks, input, spawning, rules. | Reads/writes `Domain`, calls `Presentation`, spawns children. |
| **Presentation** | Unity view (`MonoBehaviour`). | Transforms, animators, particles, UI widgets. No game rules, no input polling. |
| **Context** | Owns the triple plus children. | Lifecycle, hierarchy, per-branch `TimeScale`, optional `EventBus`. |

You never write `MonoBehaviour.Awake / Start / Update`. You override `OnAwake / OnStart / OnTick / OnFixedTick / OnLateTick / OnDestroy`, and CDSP calls them top-down through the tree.

```
GameContext
├── PlayerContext
│   └── WeaponContext
├── EnemyContext (x N)
└── HUDContext
```

Each context owns one `GameObject` (`Context.Instance`). Child instances are parented under their parent's children slot (`Presentation.ChildrenRoot`, defaults to the view root) with `worldPositionStays: false`, so a child's local transform (scale included) stays as authored and the Unity hierarchy mirrors the CDSP tree. Destroying a parent destroys the whole subtree.

To redirect children into a nested container (e.g. `RootCanvas/Container` instead of `RootCanvas` itself), select the presentation component in the Inspector and press the `Children Root: …` button — a dropdown lists the whole hierarchy of that view, pick the container. Empty means the view root. The choice is stored in the prefab/scene, survives view hot-swap, and applies to creation, `PutTransformInside`, awake fix-up and `FindPresentation` adoption alike.

---

## 1. Installation and scene setup

Install as a UPM package (zero dependencies, Unity 2021.3+). See “Links and installation” above for all options; short version:

1. `Window → Package Manager → + → Install package from git URL…`
2. Enter `https://github.com/SacraneScrny/CDSP.git` (package name: `com.cdsp.sackrany`).
   Update: `Package Manager → Packages — CDSP → Update`, remove: `Remove`.
3. Import the demo: `Package Manager → Packages — CDSP → Samples → Minimal Counter`.

No settings or extra packages required. Assemblies: runtime `CDSP` (`Runtime/`), Editor-only `CDSP.Editor` (`Editor/`, CDSP Explorer).

Each gameplay scene needs at least one root object:

1. Create an empty `GameObject` at the **scene root**.
2. Add your concrete `ContextRoot<TContext>` component.
3. Assign **Prefab** in the Inspector (optional).

```csharp
public sealed class GameRoot : ContextRoot<GameContext> { }

public sealed class GameRootWithData : ContextRoot<GameContext, GameDomain>
{
    public override void Bootstrap(GameContext context, GameDomain domain)
    {
        context.CreateChild<PlayerContext>(playerPrefab);
        context.CreateChild<HUDContext>(hudPrefab);
    }
}
```

How the root boots (`ContextRoot.Awake` — runs once per lifetime, not on every enable):

1. `CreateContext()` — `new TContext()` (or swaps in the serialized `Domain` for `ContextRoot<TContext, TDomain>`).
2. `EntryPoint.Register(Context)` — adds the tree to the tick loop, so `FindRoot` works from inside `Awake` code.
3. `Bootstrap()` — sealed: creates the root presentation first (`Prefab`, or an empty `{GO}_ROOT` node), then calls **your** `void Bootstrap(...)` override (spawn initial children, configure the root), then `AwakeAndStart()`.

Notes:

- `Prefab` is **optional**. If `null`, the root wakes as an empty `GameObject` named `{GameObjectName}_ROOT`. Use this for pure-logic roots.
- If a prefab is assigned to a `Context<TDomain, TSystem, TPresentation>`, it must contain the matching `TPresentation` component on the root or a child — otherwise creation throws. It is found via `GetComponent` first, then `GetComponentInChildren`.
- The presentation is created **before** your `Bootstrap`, so children spawned there parent under the root `GameObject` right away.
- The root unregisters from the tick loop and destroys the whole tree only in `OnDestroy`. Disabling the `GameObject` does **not** unregister/destroy anything. Never create the context with `new` in `Awake` yourself — the root owns that.

---

## 2. Minimal example

One triple: data, logic, view, wiring.

```csharp
using CDSP.Base;
using UnityEngine;

public sealed class CounterDomain : Domain
{
    public float Timer;
    public int Seconds;
}

public sealed class CounterPresentation : Presentation<CounterSystem>
{
    public void ShowSeconds(int seconds) { /* update label */ }
}

public sealed class CounterSystem : System<CounterDomain, CounterPresentation>
{
    protected override void OnTick(float deltaTime)
    {
        Domain.Timer += deltaTime;
        if (Domain.Timer < 1f) return;
        Domain.Timer -= 1f;
        Domain.Seconds++;
        Presentation.ShowSeconds(Domain.Seconds);
    }
}

public sealed class CounterContext
    : Context<CounterDomain, CounterSystem, CounterPresentation> { }
```

Spawn it from any running system (systems reach their context via `Context`):

```csharp
Context.CreateChild<CounterContext>(counterPrefab);
```

Notes:

- `System` never touches UI components; `Presentation` never decides anything.
- `Presentation` is resolved from the prefab and is guaranteed to exist — access it directly, never null-check it. A `null` here means a contract violation (unawaken context, prefab without the component, or externally destroyed view) and must fail loudly, not be silently tolerated.
- Input is logic: poll it in `System.OnTick` (e.g. `Input.GetAxisRaw`), then push results to `Presentation` via methods like `ApplyMove()`. `Presentation` never reads `Input`.

---

## 3. Context flavors

Pick the generic arity that matches what the node needs:

```csharp
// Pure node: hierarchy and lifecycle only
public sealed class FolderContext : Context { }

// Data + hierarchy (Domain created once in Initialize)
public sealed class WaveContext : Context<WaveDomain> { }

// Data + logic (System.Tick drives the node)
public sealed class SpawnerContext : Context<SpawnerDomain, SpawnerSystem> { }

// Full triple: data + logic + Unity view
public sealed class EnemyContext
    : Context<EnemyDomain, EnemySystem, EnemyPresentation> { }

// Full triple + per-instance event bus (key is usually an enum)
public sealed class GameContext
    : Context<GameDomain, GameSystem, GamePresentation, GameEvents> { }
```

- `Context<TDomain>` exposes `Domain` and `UpdateDomain(...)` (used by `ContextRoot<TContext, TDomain>` to inject the Inspector-assigned domain).
- `Context<TDomain, TSystem>` seals `OnAwake / OnStart / OnTick / OnFixedTick / OnLateTick` into `System` calls — gameplay code lives only in `System.OnX`.
- `Context<TDomain, TSystem, TPresentation>` exposes `Presentation` and keeps one shared `PresentationContainer` between context and system, so both see the same view.
- `Context<TDomain, TSystem, TPresentation, TEventKey>` adds `Subscribe / Emit / ClearEvents` proxies over its own `EventBus`.

`Context` exposes `OnCreate()` / `OnDestroy()` overrides for **emergency cases only** (e.g. wiring that cannot live in `System`/`Domain`). Normal gameplay code belongs in `System.OnAwake / OnStart / OnTick / OnDestroy` and `Domain.OnDestroy`, and view cleanup belongs in `Presentation.OnDisposing()`. If you are reaching for `Context.OnCreate`, reconsider first.

---

## 4. Lifecycle

Order is parent-first for creation and ticks, children-first for destruction:

```
Initialize (Domain/System creation, runs once from the base constructor — do not override, use OnCreate) →
OnCreate → EnsurePresentation → OnAwake (System → Presentation) → children Awake
  → OnStart (System → Presentation) → children Start
  → Tick / FixedTick / LateTick → Destroy (children reversed → self)
```

Creation details:

- `Awake` and `Start` run exactly once each. `Start` requires `Awake` first and refuses to run otherwise (error, no auto-awake).
- Flow per node: `Context → System → Presentation`, then children recursively. Inside `System.Tick`, `System.OnTick` runs **before** `Presentation.OnTick`.
- A context created without a `GameObject` gets an empty node on `Awake` via `CreateEmptyPresentation` (the `Presentation` component is auto-added in that case; roots are named `{GO}_ROOT`, others default to the context type name). The shared empty template object is hidden (`HideAndDontSave`) and never lands in the saved scene.

Destruction details:

- `Destroy()` kills children in **reverse** order, then runs `Destroying()` (`System.OnDestroy` + disposes tracked subscriptions, `Domain.OnDestroy`, `ClearEvents()`), then `Context.OnDestroy()`, then detaches from the parent and destroys the `GameObject` via the configured destroy action.
- Detachment is two-phase: the child is marked dead immediately (`IsDead`), but removed from the parent's list at the end of the next tick pass. Always check `IsDead` on cached references.

---

## 5. Ticks and time

CDSP injects itself into the Unity PlayerLoop (after `ScriptRunBehaviourUpdate`, `ScriptRunBehaviourFixedUpdate`, `ScriptRunBehaviourLateUpdate`) and ticks every registered root: `Update → Tick(clamped Time.deltaTime)`, `FixedUpdate → FixedTick(clamped Time.fixedDeltaTime)`, `LateUpdate → LateTick(clamped Time.deltaTime)`. Re-initialization is idempotent — re-entering PlayMode does not duplicate the loop systems.

The `0.1s` clamp is applied once at the loop entry. Each pass, per node:

```
if (!IsReady || !IsUpdating || !IsUpdateTick || deltaTime <= 0) return;
deltaTime *= TimeScale;
OnTick → children (recursively, with the already-scaled deltaTime) → sweep dead → wake deferred children
```

Time control is layered:

- **Global** — `EntryPointUpdate.SetActive(bool)` pauses everything; `SetUpdateTick / SetUpdateFixedTick / SetUpdateLateTick` toggle each pass independently.
- **Branch** — `Context.SetUpdating(false)` pauses a subtree (state is kept, `Destroy()` is permanent — don't confuse them).
- **Per-node rate** — `Context.TimeScale` (default `1`, clamped to stay above zero, so it can never pause). Each tree level multiplies its own scale onto the already-scaled `deltaTime` it received, so nested slow-motion composes.
- **Per-pass opt-out** — `IsUpdateTick / IsUpdateFixedTick / IsUpdateLateTick` exist on `Context`, `System` and `Presentation`. `Presentation` ticks carry the already-scaled `deltaTime`; they apply no extra scaling.

`TimeScale` is for slow/fast motion; `SetUpdating(false)` is for pausing.

---

## 6. Spawning children

Two creation modes, one lookup family, three destruction flavors:

```csharp
// Deferred (default, safe to call inside OnTick):
// the child wakes at the end of the current tick pass and joins ticking after that.
var enemy = Context.CreateChild<EnemyContext>(enemyPrefab);
var delayed = Context.CreateChild<EnemyContext>(enemyPrefab, isUpdating: false);
var configured = Context.CreateChild<EnemyContext>(enemyPrefab, init: c => c.Setup(level));

// Immediate: Awake/Start run synchronously (if the parent already runs).
// Prefer the deferred version inside tick callbacks.
var urgent = Context.CreateChildImmediate<EnemyContext>(enemyPrefab);

// Wake several deferred children at once, after configuring all of them:
Context.CreateChild<EnemyContext>(prefabA);
Context.CreateChild<EnemyContext>(prefabB);
Context.StartAllChildren();
```

- `prefab` may be `null` → an empty `GameObject` node (presentation auto-added on wake).
- `init` runs after parenting but before `Awake`. If it throws, the child is rolled back (removed and destroyed) and the exception rethrows.
- A child created while the parent is dead returns `null` with an error.
- `SetInstantiateAction / SetDestroyAction` (fluent, per context) override how the `GameObject` is born and dies — the hook for pooling or addressables:

```csharp
myContext
    .SetInstantiateAction(prefab => MyPool.Get(prefab))
    .SetDestroyAction(go => MyPool.Release(go));
```

Destroying:

```csharp
Context.Destroy();                    // self + subtree + GameObject (the only way to remove self)
Context.DestroyChild(child);          // by reference → bool, no-op (false) if not a live child of this parent
Context.DestroyChild<EnemyContext>(); // all live children of type T → bool
Context.DestroyChild(index);          // by raw list index → bool
```

Never call `GameObject.Destroy` on a context instance yourself, and never hand-wire `Domain`/`System` — the context owns both.

Reading the tree (all skip dead children):

```csharp
Context.GetChild<EnemyContext>();      // first live child of type T
Context.GetChild<EnemyContext>(1);     // 2nd live child of type T
Context.GetChild(0);                   // by raw list index (same indexing as the internal list, null if dead/out of range)
Context.TryGetChild<EnemyContext>(out var e);
Context.HasChild<EnemyContext>();      // any live child of type T
Context.HasChild(someRef);             // identity + liveness
Context.ChildCount;                    // live children
Context.SpecificChildCount<EnemyContext>();
Context.GetChildren<EnemyContext>(reusableList); // fills, clears first
```

Note: `GetChild(int)` / `DestroyChild(int)` use the raw internal-list index (which may briefly contain not-yet-swept dead entries until the end of the tick pass), while `ChildCount` counts only live children. Take the index and the lookup from the same raw order — don't mix a `0..ChildCount` range with the raw-index lookup.

The generic lookups accept concrete context types **or interfaces** implemented by contexts: `GetChild<IDamageable>()`, `HasChild<IDamageable>()`, `DestroyChild<IDamageable>()`, `GetChildren<IDamageable>(list)`, `SpecificChildCount<IDamageable>()`. Same for `Is<T>() / As<T>() / IsValid<T>()`, `Up<T>() / TryUp<T>()`, `IsSimulationRunning<T>()` and `EntryPoint.FindRoot<T>()`. Creation stays concrete (`CreateChild<T>`, `ContextRoot<TContext>` need `new T()`).

Reparenting a whole `GameObject` under another context's `GameObject` (visual only — the logical parent, tick ownership and `Up<T>` chain are unchanged):

```csharp
child.PutTransformInside(newParent);
```

---

## 7. Talking up the tree: `Up`, `Is`, `As`

From any `Context` or `System` (systems delegate to their context), walk the parent chain instead of storing references:

```csharp
var game = Context.Up<GameContext>();                   // includes self
var game = Context.Up<GameContext>(includeSelf: false); // parents only
if (Context.TryUp<GameContext>(out var g)) { /* silent, no error log */ }
```

- `Up<T>()` returns the nearest **live** ancestor of that type and logs an error when none is found (noting `(Found was dead)` if a dead match was skipped). `TryUp<T>()` is the silent `bool` variant.
- Prefer `Up<T>()` over holding a direct `Context` reference to the parent. A stored reference hardcodes the exact parent type and distance; `Up<T>()` keeps working when you later insert a layer (e.g. `Game → Wave → Enemy` instead of `Game → Enemy`).
- `Is<T>()` / `As<T>()` / `IsValid<T>()`: type check, checked cast (error + `null` on mismatch), and null-if-dead cast.
- `IsSimulationRunning()` (or `IsSimulationRunning(from)`) is `true` only while the whole chain from the given node (default: `this`) up to the root is `IsUpdating`. `IsSimulationRunning<T>()` finds the nearest live ancestor of type `T` (including self) and checks the chain from **it** up to the root — the caller's own flag and any nodes below `T` are not checked. In other words it answers "is the `T`-scope simulation running up there", not "am I paused". Use it to skip work when the enclosing scope is paused.

For truly global access to a scene root from anywhere:

```csharp
using CDSP.Entry;
var game = EntryPoint.FindRoot<GameContext>(); // nearest live root of type T, null if none/dead
```

`Up<T>()` is the default; `FindRoot<T>()` is for things with no natural ancestor (e.g. UI finding the session). Do not cache results across scene unloads without checking `IsDead`.

---

## 8. Presentation handling

- The view is resolved once per `GameObject`: `GetComponent<TPresentation>()`, then `GetComponentInChildren<TPresentation>()`. Every resolved view is passed through `ResetPresentation()` first, so a pooled/reused component always starts with a clean lifecycle (`IsAwaken/IsStarted == false`, tick flags on, `System == null`); override `OnResetPresentation()` for custom view-state reset.
- Calling `Context.CreatePresentation(prefab)` again on a live context **hot-swaps** the view: children `GameObject`s are detached, the old view gets `OnDisposing()` and its object released via the context's destroy action (pooling-safe), children are re-attached to the new object, and — if the old view was awake — the new one is awoken/started immediately with `System.OnPresentationUpdated(...)` fired. Plain (non-triple) contexts drop their previous `Instance` through the same destroy action instead of leaking it.
- React to view swaps in `System.OnPresentationUpdated(view)` / `OnPresentationDestroy(view)`; release view resources in `Presentation.OnDisposing()`. `OnDisposing` fires exactly once per view lifetime (guarded internally, so the deferred Unity `OnDestroy` after a hot-swap does not re-dispose).
- When the view already exists in the hierarchy (e.g. nested inside another presentation), bind it instead of instantiating:

```csharp
// In init (before awake): adopt the nested view found under the parent instance.
var enemy = Context.CreateChild<EnemyContext>(init: c => c.FindPresentation(Context.Instance));

// ...or search own instance later (null when missing, state untouched).
if (enemy.TryFindPresentation(out var view)) { /* use view */ }
```

`FindPresentation(scope?)` never instantiates: it searches `scope` (default: own `Instance`, self then children including inactive — so disabled/pooled views are reusable; pass `includeChildren: false` for self-only) and binds the found view (reset via `ResetPresentation()`, so reuse starts clean). Miss returns `null` and touches nothing. A view found outside the current instance is adopted — its `GameObject` becomes `Instance`, live logical children parked under the previous root are re-attached to it (hot-swap parity), the previous root is released via the destroy action (the searched scope itself is never destroyed), and the adopted root is parented under the logical parent when ready (pass `alignTransform: false` to keep the found placement as is — breaks hierarchy mirroring). Ownership transfers: destroying the context later releases the adopted root like a created one (customize via `SetDestroyAction` for pooling). A failed release aborts loudly and keeps the previous root. If the context already runs, the late-bound view is awoken/started immediately with `System.OnPresentationUpdated(...)` fired. Binding a view already owned by another system logs an error.
- If the view's `GameObject` is destroyed from outside CDSP (a contract violation — see rule 4), Unity's `Presentation.OnDestroy` still notifies the context and system through the same `OnPresentationDestroy` path so subscriptions and pooled state don't leak. User code must keep assuming non-null: the resulting `NullReferenceException` on next access is the intended loud signal, not something to guard against.

---

## 9. Events

Add a fourth generic argument — usually an enum — to get a per-instance bus:

```csharp
public enum GameEvents { ScoreChanged, Damaged, PlayerDied }

public sealed class GameContext
    : Context<GameDomain, GameSystem, GamePresentation, GameEvents> { }
```

```csharp
// Subscribe (keep the subscription, Dispose to unsubscribe)
_subscription = game.Subscribe(GameEvents.ScoreChanged, Refresh);
_damageSub = game.Subscribe<int>(GameEvents.Damaged, OnDamaged);

// Publish
game.Emit(GameEvents.ScoreChanged);
game.Emit(GameEvents.Damaged, 25);

// Unsubscribe manually, or clear wholesale
subscription.Dispose();
game.Unsubscribe(GameEvents.ScoreChanged, Refresh);
game.ClearEvents(GameEvents.Damaged);
game.ClearEvents();
```

Semantics:

- `Emit<T>(key, payload)` fires typed `Handler<T>` handlers **and** plain handlers on the same key; plain `Emit(key)` fires only plain handlers.
- The bus is **per context instance** — publisher and subscriber must use the same instance (usually the nearest common ancestor).
- The context clears all subscriptions automatically on destroy.
- `Subscription` is a disposable struct — track it in the system so it dies with the node:

```csharp
protected override void OnAwake()
{
    Track(Context.Up<GameContext>().Subscribe<int>(GameEvents.Damaged, OnDamaged));
}
```

Use events for one-to-many notifications (death → HUD + audio + analytics); use direct calls for parent ↔ known child. Do not subscribe/unsubscribe from inside a handler for the same key — the bus does not guard against mutation during `Emit`.

---

## 10. `EntryPoint`: roots and the tick loop

```csharp
using CDSP.Entry;

EntryPoint.Register(context);     // add a root tree to the tick loop (called by ContextRoot)
EntryPoint.Unregister(context);   // remove from the loop; does NOT destroy
EntryPoint.FindRoot<GameContext>(); // nearest live root of type T, null if none/dead
EntryPoint.MainContextsList;      // read-only list of roots

EntryPointUpdate.SetActive(false);       // pause everything
EntryPointUpdate.SetUpdateTick(false);   // pause one pass globally
```

---

## 11. Debugging

**Editor:** `CDSP / Explorer` (`Ctrl+Shift+E`) — live tree with state badges, `GameObject` path, tick flags, `Domain` / `System` / `Presentation` member dumps, `EventBus` subscriptions, children list, parent chain. Follow-selection, ping, dead-grace highlighting, play-mode lifecycle buttons (with confirmations). Editor-only, zero build cost.

**Runtime overlay** (build-safe IMGUI, auto-spawns as `DontDestroyOnLoad`, near-zero cost when fully off):

| Key | Action |
|---|---|
| `F1` | Explorer window: tree + details + tick-loop switches + `Time.timeScale` slider + click-to-select in scene |
| `F2` | Label settings window |
| `F3` | World-space labels on/off (draw even with windows closed) |

```csharp
using CDSP.Overlay;
CDSPOverlay.EnsureExists();
CDSPOverlay.Show(); CDSPOverlay.Hide(); CDSPOverlay.Toggle();
CDSPOverlay.ShowLabels(); CDSPOverlay.HideLabels(); CDSPOverlay.ToggleLabels();
CDSPOverlay.ShowLabelsWindow();
```

The explorer mirrors the Editor window: search, expand/fold, private/hidden-standard-member filters, dead-grace view, per-node pause (`SetUpdating`), `TimeScale` slider, per-pass tick flags, lifecycle buttons, member dumps, event-bus view with clearing, children with per-node destroy, and parent-chain breadcrumbs. Labels render configurable world-space cards (`Box / Name / Domain / System / Presentation` fields) with distance/mouse fade, parent links, and overlap avoidance.

---

## 12. Rules

1. Override `OnAwake / OnStart / OnTick / OnFixedTick / OnLateTick / OnDestroy`. Never Unity `Awake / Start / Update / OnDestroy` in gameplay code; in `Presentation` use `OnAwake / OnStart / OnTick / OnDisposing` (plus `OnResetPresentation` for pooled view reuse). Never override `Context.Initialize()` — it runs once from the base constructor and creates `Domain`/`System`; use `OnCreate` instead.
2. State in `Domain`, decisions (including input) in `System`, Unity in `Presentation`.
3. `Presentation` always exists — access it directly, never null-check it. Null-checking hides contract violations (see §2); let it throw.
4. Spawn via `CreateChild` (`Immediate` only outside ticks), remove via `Destroy / DestroyChild`. Never `GameObject.Destroy` a context instance yourself, never hand-wire `Domain`/`System`.
5. The root context is created, presented and registered by `ContextRoot` in `Awake`; do gameplay wiring in `void Bootstrap(...)`. `Prefab` is optional (null → `{GO}_ROOT` empty node).
6. Reach parents via `Up<T>()` / `TryUp<T>()`, not stored references, so layers can be inserted later.
7. `Context.OnCreate / OnDestroy` are an escape hatch, not a pattern.
8. Check `IsDead` on cached child references; sweeping is deferred to end of tick.

## 13. Cheat sheet

```csharp
// Spawn
Context.CreateChild<T>(prefab?, init?, isActive: true);
Context.CreateChildImmediate<T>(prefab?, init?, isActive: true);
Context.StartAllChildren();

// Remove
Context.Destroy(); / DestroyChild(ref|T|index);

// Read the tree (T = concrete context or implemented interface)
Context.GetChild<T>(); / GetChild<T>(n); / GetChild(i);
Context.TryGetChild<T>(out var c); / HasChild<T>(); / HasChild(ref);
Context.ChildCount; / SpecificChildCount<T>(); / GetChildren<T>(list);

// Bind an existing view instead of instantiating (null when missing)
Context.FindPresentation(scope?); / TryFindPresentation(out var v);

// State & placement
Context.SetUpdating(bool);  Context.TimeScale; // default 1, min Epsilon
Context.PutTransformInside(parent); // transform-only, logical parent unchanged
Context.SetInstantiateAction(fn); / SetDestroyAction(fn); // destroy action is used for hot-swap too

// Identity & navigation
Context.Instance; Context.Parent; Context.IsRoot;
Context.IsAwaken; Context.IsStarted; Context.IsReady; Context.IsDead;
Context.Is<T>(); Context.As<T>(); Context.IsValid<T>();
Context.Up<T>(includeSelf: true); Context.TryUp<T>(out var x, includeSelf: true);
Context.IsSimulationRunning(); / IsSimulationRunning<T>(); / IsSimulationRunning(from);
EntryPoint.FindRoot<T>(); EntryPoint.MainContextsList; // FindRoot skips dead roots

// Events (4-arg contexts)
game.Subscribe(key, h); game.Subscribe<T>(key, h); game.Emit(key); game.Emit(key, payload);
sub.Dispose(); game.Unsubscribe(key, h); game.ClearEvents(); game.ClearEvents(key);

// System helpers
Track(subscription); // auto-dispose on System.Destroy
Up<T>(includeSelf: true); // from inside a System
```
