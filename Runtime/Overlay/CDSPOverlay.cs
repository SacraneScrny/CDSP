using System;
using System.Collections.Generic;

using CDSP.Base;
using CDSP.Components;
using CDSP.Entry;

using UnityEngine;

namespace CDSP.Overlay
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CDSP_OVERLAY_IN_RELEASE
    public sealed partial class CDSPOverlay : MonoBehaviour
    {
        public static CDSPOverlay Instance { get; private set; }

        [Header("Overlay")]
        public KeyCode ToggleKey = KeyCode.F1;
        public bool StartVisible;

        const int ExplorerWindowId = 19871;
        const float DeadGraceSeconds = 3f;
        const float CacheSeconds = 0.25f;

        bool _explorerVisible;
        Rect _explorerRect = new Rect(10, 10, 960, 620);
        Vector2 _treeScroll;
        Vector2 _detailsScroll;

        string _search = "";
        bool _showPrivate;
        bool _hideStd = true;
        bool _showDead;
        bool _expandNew = true;
        bool _showIds;
        bool _onlyWithPresentation;
        float _leftWidth = 350f;

        Context _selected;
        readonly HashSet<Context> _expanded = new HashSet<Context>();
        readonly HashSet<Context> _known = new HashSet<Context>();
        readonly Dictionary<Context, int> _contextIds = new Dictionary<Context, int>();
        int _nextContextId = 1;

        readonly Dictionary<Context, double> _deadSince = new Dictionary<Context, double>();
        readonly Dictionary<Context, Context> _deadParent = new Dictionary<Context, Context>();
        readonly Dictionary<Context, List<Context>> _prevChildren = new Dictionary<Context, List<Context>>();
        readonly HashSet<Context> _stampedDead = new HashSet<Context>();

        List<Context> _cachedAll = new List<Context>();
        List<Context> _cachedRoots = new List<Context>();
        float _nextCacheTime;

        Context _flashHighlight;
        float _flashHighlightUntil;

        string _pendingConfirm;
        double _pendingConfirmTime;

        string _notice;
        double _noticeUntil;

        int _sectionIndex;
        int _rowIndex;

        Texture2D _texWhite;
        Texture2D _texBg;
        Texture2D _texPanel;
        Texture2D _texHeader;
        Texture2D _texTitle;
        Texture2D _texRowA;
        Texture2D _texRowB;

        GUIStyle _windowStyle;
        GUIStyle _headerStyle;
        GUIStyle _sectionTitleStyle;
        GUIStyle _panelStyle;
        GUIStyle _rowAStyle;
        GUIStyle _rowBStyle;
        GUIStyle _boldStyle;
        GUIStyle _bodyStyle;
        GUIStyle _smallStyle;
        GUIStyle _dimStyle;
        GUIStyle _rowBtnStyle;
        GUIStyle _badgeStyle;

        float _fps = 60f;

        #region BOOTSTRAP

        [RuntimeInitializeOnLoadMethod]
        static void AutoInit()
        {
            EnsureExists();
        }

        public static CDSPOverlay EnsureExists()
        {
            if (Instance != null) return Instance;
#if UNITY_2023_1_OR_NEWER
            var existing = UnityEngine.Object.FindFirstObjectByType<CDSPOverlay>();
#else
            var existing = UnityEngine.Object.FindObjectOfType<CDSPOverlay>();
#endif
            if (existing != null)
            {
                Instance = existing;
                return Instance;
            }

            var go = new GameObject("CDSPOverlay");
            DontDestroyOnLoad(go);
            Instance = go.AddComponent<CDSPOverlay>();
            Instance._explorerVisible = Instance.StartVisible;
            return Instance;
        }

        public static void Show() => EnsureExists().SetVisible(true);
        public static void Hide() => EnsureExists().SetVisible(false);
        public static void Toggle() => EnsureExists().SetVisible(!EnsureExists()._explorerVisible);

        public void SetVisible(bool v) => _explorerVisible = v;
        public bool IsVisible => _explorerVisible;
        public bool IsFullyOff => !_explorerVisible && !_labelsWindowVisible && !LabelsEnabled;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            _explorerVisible = StartVisible;
            _explorerRect.width = Mathf.Min(960f, Screen.width - 20f);
            _explorerRect.height = Mathf.Min(620f, Screen.height - 20f);
            _labelsRect.width = Mathf.Min(420f, Screen.width - 20f);
            _labelsRect.height = Mathf.Min(560f, Screen.height - 20f);
            if (_labelsRect.x + _labelsRect.width > Screen.width - 10f)
                _labelsRect.x = Screen.width - _labelsRect.width - 10f;
        }

        void Update()
        {
            if (CDSPOverlayInput.WasPressed(ToggleKey))
                _explorerVisible = !_explorerVisible;
            if (CDSPOverlayInput.WasPressed(LabelsKey))
                _labelsWindowVisible = !_labelsWindowVisible;
            if (CDSPOverlayInput.WasPressed(LabelsQuickKey))
                LabelsEnabled = !LabelsEnabled;
            if (IsFullyOff) return;

            float dt = Time.unscaledDeltaTime;
            if (dt > 0f)
                _fps = Mathf.Lerp(_fps, 1f / dt, 0.05f);
        }

        #endregion

        #region DATA

        internal Camera ActiveCamera
        {
            get
            {
                var c = Camera.main;
                if (c != null) return c;
                if (Camera.allCamerasCount > 0) return Camera.allCameras[0];
                return null;
            }
        }

        static double Now => Time.realtimeSinceStartup;

        List<Context> Roots
        {
            get
            {
                var roots = new List<Context>();
                try
                {
                    var live = EntryPoint.MainContextsList;
                    if (live != null)
                        foreach (var c in live)
                            if (c != null && !roots.Contains(c)) roots.Add(c);
                }
                catch { }

                try
                {
#if UNITY_2023_1_OR_NEWER
                    var sceneRoots = UnityEngine.Object.FindObjectsByType<ContextRoot>(FindObjectsSortMode.None);
#else
                    var sceneRoots = UnityEngine.Object.FindObjectsOfType<ContextRoot>();
#endif
                    foreach (var r in sceneRoots)
                    {
                        if (r == null || r.Context == null) continue;
                        if (!roots.Contains(r.Context)) roots.Add(r.Context);
                    }
                }
                catch { }
                return roots;
            }
        }

        void RefreshCache(bool force = false)
        {
            if (!force && Time.realtimeSinceStartup < _nextCacheTime) return;
            _nextCacheTime = Time.realtimeSinceStartup + CacheSeconds;
            _cachedRoots = Roots;
            var all = new List<Context>();
            var guard = new HashSet<Context>();
            foreach (var r in _cachedRoots) CollectAll(r, all, guard);
            _cachedAll = all;
            RefreshExpandForNew();
            TrackDeaths();
            RebuildOwnedInstances();
            PruneBoxCache();
            PruneLabelCache();
        }

        void CollectAll(Context ctx, List<Context> outList, HashSet<Context> guard)
        {
            if (ctx == null || guard.Contains(ctx)) return;
            guard.Add(ctx);
            outList.Add(ctx);
            var kids = CDSPOverlayReflection.GetChildren(ctx);
            if (kids == null) return;
            foreach (var k in kids) CollectAll(k, outList, guard);
        }

        void RefreshExpandForNew()
        {
            if (!_expandNew) return;
            foreach (var c in _cachedAll)
            {
                if (!_known.Contains(c))
                {
                    _known.Add(c);
                    if (c.Parent == null) _expanded.Add(c);
                    else if (_expanded.Contains(c.Parent)) _expanded.Add(c);
                }
            }
        }

        bool Matches(Context c)
        {
            if (string.IsNullOrEmpty(_search)) return true;
            var s = _search.Trim().ToLowerInvariant();
            if (c.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            if (c.GetType().FullName != null && c.GetType().FullName.ToLowerInvariant().Contains(s)) return true;
            if (c.Instance != null && c.Instance.name.ToLowerInvariant().Contains(s)) return true;
            var d = CDSPOverlayReflection.GetDomain(c);
            var sys = CDSPOverlayReflection.GetSystem(c);
            var p = CDSPOverlayReflection.GetPresentation(c);
            if (d != null && d.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            if (sys != null && sys.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            if (p != null && p.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            return false;
        }

        bool SubtreeMatches(Context c, HashSet<Context> guard)
        {
            if (c == null || guard.Contains(c)) return false;
            guard.Add(c);
            if (Matches(c)) return true;
            var kids = CDSPOverlayReflection.GetChildren(c);
            if (kids != null)
                foreach (var k in kids)
                    if (SubtreeMatches(k, new HashSet<Context>(guard))) return true;
            return false;
        }

        void TrackDeaths()
        {
            double now = Now;
            var seen = new HashSet<Context>();
            foreach (var r in _cachedRoots)
                TrackRecursive(r, null, seen);

            if (!_showDead && !_labelsShowDead)
            {
                var expired = new List<Context>();
                foreach (var kv in _deadSince)
                    if (now - kv.Value >= DeadGraceSeconds) expired.Add(kv.Key);
                foreach (var c in expired) { _deadSince.Remove(c); _deadParent.Remove(c); }
            }
        }

        void TrackRecursive(Context ctx, Context parent, HashSet<Context> seen)
        {
            if (ctx == null || seen.Contains(ctx)) return;
            seen.Add(ctx);
            double now = Now;
            if (ctx.IsDead && !_stampedDead.Contains(ctx))
            {
                _stampedDead.Add(ctx);
                _deadSince[ctx] = now;
                _deadParent[ctx] = parent;
            }
            if (!_contextIds.ContainsKey(ctx)) _contextIds[ctx] = _nextContextId++;
            var live = CDSPOverlayReflection.GetChildren(ctx);
            if (_prevChildren.TryGetValue(ctx, out var prev) && prev != null)
            {
                foreach (var p in prev)
                {
                    if (p == null || _stampedDead.Contains(p)) continue;
                    if ((live == null || !live.Contains(p)) && p.IsDead)
                    {
                        _stampedDead.Add(p);
                        _deadSince[p] = now;
                        _deadParent[p] = ctx;
                        if (!_contextIds.ContainsKey(p)) _contextIds[p] = _nextContextId++;
                    }
                }
            }
            _prevChildren[ctx] = live != null ? new List<Context>(live) : new List<Context>();
            if (live == null) return;
            foreach (var k in live) TrackRecursive(k, ctx, seen);
        }

        List<Context> GetDisplayChildren(Context ctx)
        {
            double now = Now;
            var list = new List<Context>();
            var live = CDSPOverlayReflection.GetChildren(ctx);
            if (live != null) list.AddRange(live);
            foreach (var kv in _deadSince)
            {
                var dead = kv.Key;
                if (dead == null || list.Contains(dead)) continue;
                Context p = _deadParent.TryGetValue(dead, out var dp) ? dp : dead.Parent;
                if (p != ctx) continue;
                if (!_showDead && now - kv.Value >= DeadGraceSeconds) continue;
                list.Add(dead);
            }
            return list;
        }

        bool IsInGrace(Context ctx)
        {
            if (!ctx.IsDead) return false;
            if (!_deadSince.TryGetValue(ctx, out var since)) return true;
            return Now - since < DeadGraceSeconds;
        }

        int GetContextId(Context ctx)
        {
            if (ctx == null) return 0;
            if (!_contextIds.TryGetValue(ctx, out var id))
            {
                id = _nextContextId++;
                _contextIds[ctx] = id;
            }
            return id;
        }

        Context FindByGameObject(GameObject go)
        {
            if (go == null) return null;
            foreach (var c in _cachedAll)
                if (c.Instance == go) return c;
            var t = go.transform.parent;
            while (t != null)
            {
                foreach (var c in _cachedAll)
                    if (c.Instance == t.gameObject) return c;
                t = t.parent;
            }
            return null;
        }

        Context FindByPresentation(Presentation p)
        {
            if (p == null) return null;
            foreach (var c in _cachedAll)
                if (CDSPOverlayReflection.GetPresentation(c) == p) return c;
            return null;
        }

        bool ConfirmButton(string id, string armLabel, string confirmLabel, float width)
        {
            bool armed = _pendingConfirm == id && (Now - _pendingConfirmTime) < 3.0;
            var opt = width > 0 ? GUILayout.Width(width) : GUILayout.ExpandWidth(true);
            if (armed)
            {
                var old = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.85f, 0.30f, 0.28f);
                bool click = GUILayout.Button(confirmLabel, opt, GUILayout.Height(22));
                GUI.backgroundColor = old;
                if (click)
                {
                    _pendingConfirm = null;
                    return true;
                }
            }
            else
            {
                if (GUILayout.Button(armLabel, opt, GUILayout.Height(22)))
                {
                    _pendingConfirm = id;
                    _pendingConfirmTime = Now;
                    Notify("Press again to confirm");
                }
            }
            return false;
        }

        internal void Notify(string msg)
        {
            _notice = msg;
            _noticeUntil = Now + 3.0;
        }

        internal bool IsCoveredByWindows(Vector2 guiPoint)
        {
            if (_explorerVisible && _explorerRect.Contains(guiPoint)) return true;
            if (_labelsWindowVisible && _labelsRect.Contains(guiPoint)) return true;
            return false;
        }

        #endregion

        #region STYLES

        static Texture2D MakeTex(Color c)
        {
            var t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            t.SetPixel(0, 0, c);
            t.Apply();
            t.hideFlags = HideFlags.HideAndDontSave;
            return t;
        }

        void InitStyles()
        {
            if (_windowStyle != null) return;

            _texWhite = MakeTex(Color.white);
            _texBg = MakeTex(new Color(0.085f, 0.095f, 0.125f, 0.97f));
            _texPanel = MakeTex(new Color(0.135f, 0.150f, 0.185f, 0.97f));
            _texHeader = MakeTex(new Color(0.165f, 0.42f, 0.69f, 1f));
            _texTitle = MakeTex(new Color(0.175f, 0.20f, 0.25f, 1f));
            _texRowA = MakeTex(new Color(1f, 1f, 1f, 0.028f));
            _texRowB = MakeTex(new Color(1f, 1f, 1f, 0.065f));

            _windowStyle = new GUIStyle(GUI.skin.window)
            {
                fontSize = 12,
                padding = new RectOffset(8, 8, 6, 8)
            };
            _windowStyle.normal.background = _texBg;
            _windowStyle.onNormal.background = _texBg;

            _headerStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 3, 3),
                normal = { textColor = Color.white, background = _texHeader }
            };

            _sectionTitleStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(8, 8, 2, 2),
                normal = { textColor = new Color(0.57f, 0.81f, 0.96f), background = _texTitle }
            };

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(6, 6, 5, 5),
                normal = { background = _texPanel }
            };

            _rowAStyle = new GUIStyle
            {
                padding = new RectOffset(2, 2, 1, 1),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { background = _texRowA }
            };
            _rowBStyle = new GUIStyle
            {
                padding = new RectOffset(2, 2, 1, 1),
                margin = new RectOffset(0, 0, 0, 0),
                normal = { background = _texRowB }
            };

            _boldStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12, normal = { textColor = Color.white } };
            _bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = new Color(0.92f, 0.93f, 0.95f) } };
            _smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.82f, 0.84f, 0.88f) } };
            _dimStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, normal = { textColor = new Color(0.63f, 0.68f, 0.75f) } };

            _rowBtnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 6, 2, 2)
            };

            _badgeStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(6, 6, 2, 2),
                normal = { textColor = Color.white }
            };
        }

        GUIStyle RowStyle => (_rowIndex++ % 2 == 0) ? _rowAStyle : _rowBStyle;

        void Header(string text)
        {
            GUILayout.Box(text, _headerStyle, GUILayout.Height(24), GUILayout.ExpandWidth(true));
        }

        void Section(string title, Action draw)
        {
            var style = (_sectionIndex++ % 2 == 0) ? _panelStyle : _panelStyle;
            GUILayout.BeginVertical(style);
            GUILayout.Box(title, _sectionTitleStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true));
            GUILayout.Space(2);
            draw();
            GUILayout.EndVertical();
            GUILayout.Space(4);
        }

        void KV(string label, string value)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(19));
            GUILayout.Label(label, _dimStyle, GUILayout.Width(172));
            GUILayout.Label(value, _bodyStyle, GUILayout.ExpandWidth(true));
            GUILayout.EndHorizontal();
        }

        void Badge(string text, Color bg)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = bg;
            GUILayout.Box(text, _badgeStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true));
            GUI.backgroundColor = old;
        }

        static Color StateColor(Context ctx)
        {
            if (ctx.IsDead) return new Color(1f, 0.36f, 0.32f, 0.9f);
            if (!ctx.IsUpdating) return new Color(0.55f, 0.59f, 0.65f, 0.9f);
            if (!ctx.IsStarted) return new Color(1f, 0.84f, 0.32f, 0.92f);
            return new Color(0.28f, 0.78f, 0.42f, 0.92f);
        }

        static string StateGlyph(Context ctx)
        {
            if (ctx.IsDead) return "■";
            if (!ctx.IsUpdating) return "○";
            if (!ctx.IsStarted) return "◐";
            return "●";
        }

        internal void DrawOutline(Rect r, Color c, float thickness)
        {
            if (_texWhite == null) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, thickness), _texWhite);
            GUI.DrawTexture(new Rect(r.x, r.yMax - thickness, r.width, thickness), _texWhite);
            GUI.DrawTexture(new Rect(r.x, r.y, thickness, r.height), _texWhite);
            GUI.DrawTexture(new Rect(r.xMax - thickness, r.y, thickness, r.height), _texWhite);
            GUI.color = old;
        }

        #endregion

        #region ONGUI / EXPLORER WINDOW

        void OnGUI()
        {
            if (IsFullyOff) return;

            InitStyles();
            InitLabelStyles();
            RefreshCache();

            if (LabelsEnabled) DrawScreenLabels();

            if (_explorerVisible)
            {
                ClampExplorer();
                _explorerRect = GUILayout.Window(ExplorerWindowId, _explorerRect, DrawExplorerWindow, "", _windowStyle);
            }

            if (_labelsWindowVisible)
            {
                ClampLabelsWindow();
                _labelsRect = GUILayout.Window(LabelsWindowId, _labelsRect, DrawLabelsWindow, "", _windowStyle);
            }

            var e = Event.current;
            if ((_explorerVisible || _labelsWindowVisible || LabelsEnabled)
                && e != null && e.type == EventType.MouseDown && e.button == 0
                && !IsCoveredByWindows(e.mousePosition))
            {
                PickAt(e.mousePosition);
                e.Use();
            }
        }

        void ClampExplorer()
        {
            _explorerRect.width = Mathf.Clamp(_explorerRect.width, 480f, Screen.width - 10f);
            _explorerRect.height = Mathf.Clamp(_explorerRect.height, 240f, Screen.height - 10f);
            _explorerRect.x = Mathf.Clamp(_explorerRect.x, 0f, Screen.width - 60f);
            _explorerRect.y = Mathf.Clamp(_explorerRect.y, 0f, Screen.height - 40f);
        }

        void DrawExplorerWindow(int id)
        {
            int alive = 0;
            foreach (var c in _cachedAll) if (!c.IsDead) alive++;
            Header($"CDSP  ·  {_cachedRoots.Count} roots  ·  {alive}/{_cachedAll.Count} alive  ·  {_fps:0} fps");
            DrawToolbar();
            DrawLoopBar();
            DrawMain();
            DrawFooter();
            GUI.DragWindow(new Rect(0, 0, _explorerRect.width, 26f));
        }

        void DrawToolbar()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(24));
            if (GUILayout.Button("X", GUILayout.Width(30), GUILayout.Height(22))) _explorerVisible = false;
            GUILayout.Label("Search", _dimStyle, GUILayout.Width(52));
            _search = GUILayout.TextField(_search, GUILayout.MinWidth(80), GUILayout.Height(22));
            if (GUILayout.Button("Clear", GUILayout.Width(52), GUILayout.Height(22))) _search = "";
            if (GUILayout.Button("Expand", GUILayout.Width(66), GUILayout.Height(22))) SetAllExpanded(true);
            if (GUILayout.Button("Fold", GUILayout.Width(52), GUILayout.Height(22))) SetAllExpanded(false);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(22));
            _showPrivate = ToggleCell(_showPrivate, "Private fields", 104);
            _hideStd = ToggleCell(_hideStd, "Hide standard", 108);
            _showDead = ToggleCell(_showDead, "Show dead", 96);
            _showIds = ToggleCell(_showIds, "ID", 44);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(22));
            _onlyWithPresentation = ToggleCell(_onlyWithPresentation, "Has presentation", 128);
            _expandNew = ToggleCell(_expandNew, "Auto expand", 104);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal(GUILayout.Height(24));
            GUILayout.Label($"Split {_leftWidth:0}", _dimStyle, GUILayout.Width(64));
            _leftWidth = GUILayout.HorizontalSlider(_leftWidth, 220f, 620f, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Focus", GUILayout.Width(62), GUILayout.Height(22))) FlashSelected();
            if (GUILayout.Button(LabelsEnabled ? "Labels: ON" : "Labels: OFF", GUILayout.Width(96), GUILayout.Height(22)))
                LabelsEnabled = !LabelsEnabled;
            if (GUILayout.Button("Labels...", GUILayout.Width(76), GUILayout.Height(22))) _labelsWindowVisible = true;
            GUILayout.EndHorizontal();
        }

        static bool ToggleCell(bool v, string text, float w = 76)
        {
            return GUILayout.Toggle(v, text, GUILayout.Width(w), GUILayout.Height(20));
        }

        void SetAllExpanded(bool v)
        {
            foreach (var c in _cachedAll)
            {
                if (v) _expanded.Add(c);
                else _expanded.Remove(c);
            }
        }

        void DrawLoopBar()
        {
            _sectionIndex = 100;
            Section("Loop  ·  Time", () =>
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                try
                {
                    bool a = EntryPointUpdate.IsActive;
                    bool t = EntryPointUpdate.IsUpdateTick;
                    bool f = EntryPointUpdate.IsUpdateFixedTick;
                    bool l = EntryPointUpdate.IsUpdateLateTick;
                    bool na = GUILayout.Toggle(a, "Loop", GUILayout.Width(58));
                    bool nt = GUILayout.Toggle(t, "Tick", GUILayout.Width(54));
                    bool nf = GUILayout.Toggle(f, "Fixed", GUILayout.Width(60));
                    bool nl = GUILayout.Toggle(l, "Late", GUILayout.Width(54));
                    if (na != a) EntryPointUpdate.SetActive(na);
                    if (nt != t) EntryPointUpdate.SetUpdateTick(nt);
                    if (nf != f) EntryPointUpdate.SetUpdateFixedTick(nf);
                    if (nl != l) EntryPointUpdate.SetUpdateLateTick(nl);
                }
                catch { GUILayout.Label("loop n/a", _dimStyle); }
                GUILayout.Label($"{(1f / Mathf.Max(_fps, 1f)) * 1000f:0.0} ms", _dimStyle, GUILayout.Width(70));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label($"Global {Time.timeScale:0.00}", _dimStyle, GUILayout.Width(84));
                float nts = GUILayout.HorizontalSlider(Time.timeScale, 0f, 2f, GUILayout.ExpandWidth(true));
                if (!Mathf.Approximately(nts, Time.timeScale)) Time.timeScale = nts;
                if (GUILayout.Button("1x", GUILayout.Width(38), GUILayout.Height(20))) Time.timeScale = 1f;
                GUILayout.EndHorizontal();
            });
        }

        void DrawMain()
        {
            GUILayout.BeginHorizontal();
            DrawTreePanel();
            DrawDetailsPanel();
            GUILayout.EndHorizontal();
        }

        void DrawTreePanel()
        {
            GUILayout.BeginVertical(GUILayout.Width(_leftWidth), GUILayout.ExpandHeight(true));
            GUILayout.Box("Tree", _sectionTitleStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true));
            _treeScroll = GUILayout.BeginScrollView(_treeScroll, GUILayout.ExpandHeight(true));
            _rowIndex = 0;
            if (_cachedRoots.Count == 0)
            {
                GUILayout.Box("No contexts.\nNeed ContextRoot in scene (Prefab optional).", GUILayout.ExpandWidth(true));
            }
            var guard = new HashSet<Context>();
            bool filtering = !string.IsNullOrEmpty(_search);
            foreach (var r in _cachedRoots)
            {
                if (filtering && !SubtreeMatches(r, new HashSet<Context>())) continue;
                if (_onlyWithPresentation && !HasPresentationInSubtree(r)) continue;
                DrawNode(r, 0, guard, filtering);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        bool HasPresentationInSubtree(Context ctx)
        {
            if (CDSPOverlayReflection.GetPresentation(ctx) != null) return true;
            var kids = CDSPOverlayReflection.GetChildren(ctx);
            if (kids == null) return false;
            foreach (var k in kids)
                if (HasPresentationInSubtree(k)) return true;
            return false;
        }

        void DrawNode(Context ctx, int depth, HashSet<Context> guard, bool filtering)
        {
            if (ctx == null || guard.Contains(ctx)) return;
            guard.Add(ctx);
            if (filtering && !SubtreeMatches(ctx, new HashSet<Context>())) return;
            if (_onlyWithPresentation && !HasPresentationInSubtree(ctx)) return;
            if (ctx.IsDead && !IsInGrace(ctx) && !_showDead) return;

            var kids = GetDisplayChildren(ctx);
            bool hasKids = kids.Count > 0;
            bool expanded = filtering || _expanded.Contains(ctx);

            bool wasDead = ctx.IsDead;
            if (wasDead) GUI.enabled = false;

            GUILayout.BeginHorizontal(RowStyle, GUILayout.Height(22));
            GUILayout.Space(depth * 14f);
            if (_showIds)
                GUILayout.Label(GetContextId(ctx).ToString(), _dimStyle, GUILayout.Width(28));
            if (hasKids && !filtering)
            {
                if (GUILayout.Button(expanded ? "–" : "+", GUILayout.Width(22), GUILayout.Height(20)))
                {
                    if (expanded) _expanded.Remove(ctx);
                    else _expanded.Add(ctx);
                }
            }
            else GUILayout.Space(24);

            var oldC = GUI.contentColor;
            GUI.contentColor = StateColor(ctx);
            GUILayout.Label(StateGlyph(ctx), GUILayout.Width(14));
            GUI.contentColor = oldC;

            string goName = ctx.Instance != null ? ctx.Instance.name : "<no GO>";
            string label = $"{ctx.GetType().Name}  ·  {goName}";
            var oldBg = GUI.backgroundColor;
            if (_selected == ctx) GUI.backgroundColor = new Color(0.22f, 0.45f, 0.85f);
            else if (wasDead) GUI.backgroundColor = new Color(0.5f, 0.28f, 0.28f, 0.6f);
            if (GUILayout.Button(label, _rowBtnStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true)))
                _selected = ctx;
            GUI.backgroundColor = oldBg;

            if (GUILayout.Button(ctx.IsUpdating ? "❚❚" : "▶", GUILayout.Width(32), GUILayout.Height(20)))
                ToggleActive(ctx);

            GUILayout.EndHorizontal();
            if (wasDead) GUI.enabled = true;

            if (hasKids && (expanded || filtering))
                foreach (var k in kids)
                    DrawNode(k, depth + 1, guard, filtering);
        }

        void DrawDetailsPanel()
        {
            GUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            GUILayout.Box(_selected == null ? "Details" : $"Details  ·  {_selected.GetType().Name}", _sectionTitleStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true));
            _detailsScroll = GUILayout.BeginScrollView(_detailsScroll, GUILayout.ExpandHeight(true));
            _sectionIndex = 0;
            _rowIndex = 1000;
            if (_selected == null)
            {
                GUILayout.Box("Select a context on the left.\nScreen labels are clickable (F2).\nClick any 3D object on screen = select.", GUILayout.ExpandWidth(true));
            }
            else
            {
                DrawContextDetails(_selected);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }

        void DrawContextDetails(Context ctx)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label(ctx.GetType().FullName, _boldStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(20))) _selected = null;
            GUILayout.EndHorizontal();

            Section("State", () =>
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                Badge(ctx.IsDead ? "DEAD" : "ALIVE", ctx.IsDead ? new Color(0.55f, 0.16f, 0.16f) : new Color(0.16f, 0.44f, 0.24f));
                Badge(ctx.IsUpdating ? "Active" : "Off", ctx.IsUpdating ? new Color(0.17f, 0.42f, 0.68f) : new Color(0.32f, 0.34f, 0.38f));
                Badge(ctx.IsAwaken ? "Awake" : "!Awake", ctx.IsAwaken ? new Color(0.20f, 0.44f, 0.22f) : new Color(0.45f, 0.38f, 0.13f));
                Badge(ctx.IsStarted ? "Started" : "!Start", ctx.IsStarted ? new Color(0.20f, 0.44f, 0.22f) : new Color(0.45f, 0.38f, 0.13f));
                Badge(ctx.IsRoot ? "Root" : "Child", ctx.IsRoot ? new Color(0.40f, 0.26f, 0.55f) : new Color(0.30f, 0.31f, 0.35f));
                GUILayout.EndHorizontal();

                var dbg = CDSPOverlayReflection.GetDebugFlags(ctx);
                KV("internal", $"hold-awake:{(dbg.notAwaken ? "WAIT" : "ok")}  hold-start:{(dbg.notStarted ? "WAIT" : "ok")}  deadKids:{dbg.hasDeadChildren}  id:{GetContextId(ctx)}");
            });

            Section("GameObject", () =>
            {
                if (ctx.Instance == null)
                {
                    GUILayout.Label("Presentation == null", _bodyStyle);
                }
                else
                {
                    KV("name", ctx.Instance.name);
                    KV("active", $"{ctx.Instance.activeInHierarchy}  (self {ctx.Instance.activeSelf})  ·  kids {ctx.Instance.transform.childCount}");
                    KV("path", FullPath(ctx.Instance));
                    GUILayout.BeginHorizontal(GUILayout.Height(24));
                    if (GUILayout.Button(ctx.IsUpdating ? "SetUpdating(false)" : "SetUpdating(true)", GUILayout.Height(22)))
                        ToggleActive(ctx);
                    if (GUILayout.Button("Highlight", GUILayout.Width(84), GUILayout.Height(22)))
                    {
                        _flashHighlight = ctx;
                        _flashHighlightUntil = Time.realtimeSinceStartup + 3f;
                        LabelsEnabled = true;
                    }
                    if (GUILayout.Button("Copy", GUILayout.Width(58), GUILayout.Height(22)))
                    {
                        GUIUtility.systemCopyBuffer = $"{ctx.GetType().FullName}\nGO: {FullPath(ctx.Instance)}\nDomain: {CDSPOverlayReflection.GetDomain(ctx)?.GetType().FullName}\nSystem: {CDSPOverlayReflection.GetSystem(ctx)?.GetType().FullName}\nPresentation: {CDSPOverlayReflection.GetPresentation(ctx)?.GetType().FullName}";
                        Notify("Copied to clipboard");
                    }
                    GUILayout.EndHorizontal();
                }
            });

            Section("Ticks & Activity", () =>
            {
                DrawBoolRow("Context.IsUpdating", ctx.IsUpdating, v => { ctx.SetUpdating(v); });
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label("Context.TimeScale", _bodyStyle, GUILayout.Width(172));
                GUILayout.Label($"{ctx.TimeScale:0.00}", _dimStyle, GUILayout.Width(40));
                float newTs = GUILayout.HorizontalSlider(ctx.TimeScale, 0f, 4f, GUILayout.ExpandWidth(true));
                if (!Mathf.Approximately(newTs, ctx.TimeScale)) ctx.TimeScale = newTs;
                GUILayout.EndHorizontal();
                DrawBoolPropRow(ctx, "IsUpdateTick");
                DrawBoolPropRow(ctx, "IsUpdateFixedTick");
                DrawBoolPropRow(ctx, "IsUpdateLateTick");

                var sys = CDSPOverlayReflection.GetSystem(ctx);
                if (sys != null)
                {
                    GUILayout.Space(2);
                    GUILayout.Label($"System {sys.GetType().Name}  ·  ready:{sys.IsReady} awake:{sys.IsAwaken} started:{sys.IsStarted} active:{sys.IsUpdating}", _smallStyle);
                    DrawBoolPropRow(sys, "IsUpdating");
                    DrawBoolPropRow(sys, "IsUpdateTick");
                    DrawBoolPropRow(sys, "IsUpdateFixedTick");
                    DrawBoolPropRow(sys, "IsUpdateLateTick");
                }
                var pres = CDSPOverlayReflection.GetPresentation(ctx);
                if (pres != null)
                    GUILayout.Label($"Presentation {pres.GetType().Name}  ·  ready:{pres.IsReady} awake:{pres.IsAwaken} started:{pres.IsStarted} active:{pres.IsUpdating}", _smallStyle);
            });

            Section("Lifecycle", () =>
            {
                GUILayout.BeginHorizontal(GUILayout.Height(24));
                if (GUILayout.Button("Awake+Start", GUILayout.Height(22))) Try(() => ctx.AwakeAndStart());
                if (GUILayout.Button("Start", GUILayout.Height(22))) Try(() => ctx.Start());
                if (GUILayout.Button("StartAllKids", GUILayout.Height(22))) Try(() => ctx.StartAllChildren());
                GUILayout.EndHorizontal();
                if (ConfirmButton($"destroy-{ctx.GetHashCode()}", "Destroy", "Sure? Destroy!", 0))
                {
                    Try(() => ctx.Destroy());
                    _selected = ctx.IsRoot ? null : ctx.Parent;
                }
            });

            Section("Domain / System / Presentation", () =>
            {
                var d = CDSPOverlayReflection.GetDomain(ctx);
                var sys = CDSPOverlayReflection.GetSystem(ctx);
                var pres = CDSPOverlayReflection.GetPresentation(ctx);
                KV("Domain", d != null ? d.GetType().FullName : "null");
                KV("System", sys != null ? sys.GetType().FullName : "null");
                KV("Presentation", pres != null ? pres.GetType().FullName : "null (no component / no Presentation)");
            if (d != null) DrawDump($"Domain  ·  {d.GetType().Name}", d);
            if (sys != null) DrawDump($"System  ·  {sys.GetType().Name}", sys);
            if (pres != null) DrawDump($"Presentation  ·  {pres.GetType().Name}", pres, typeof(Presentation));
            });

            var signals = CDSPOverlayReflection.GetSignalInfo(ctx);
            if (signals.hasSignals)
            {
                Section($"Signals  ·  {signals.signalCount}", () =>
                {
                    foreach (var e2 in signals.entries)
                        KV($"{e2.fieldName}  ({e2.signalType})", $"x{e2.subscriberCount}");
                });
            }

            Section($"Children ({GetDisplayChildren(ctx).Count})", () =>
            {
                var kids = GetDisplayChildren(ctx);
                if (kids.Count == 0) GUILayout.Label("— no children —", _dimStyle);
                for (int i = 0; i < kids.Count; i++)
                {
                    var k = kids[i];
                    if (k == null) continue;
                    GUILayout.BeginHorizontal(RowStyle, GUILayout.Height(22));
                    GUILayout.Label($"[{i}]", _dimStyle, GUILayout.Width(32));
                    GUILayout.Label(k.GetType().Name, _bodyStyle, GUILayout.Width(180));
                    GUILayout.Label(k.IsDead ? "DEAD" : (k.IsUpdating ? "on" : "off"), _smallStyle, GUILayout.Width(48));
                    GUILayout.Label(k.Instance != null ? k.Instance.name : "—", _dimStyle, GUILayout.ExpandWidth(true));
                    if (GUILayout.Button(">", GUILayout.Width(28), GUILayout.Height(20))) _selected = k;
                    if (GUILayout.Button(k.IsUpdating ? "❚❚" : "▶", GUILayout.Width(32), GUILayout.Height(20))) ToggleActive(k);
                    if (ConfirmButton($"dk-{ctx.GetHashCode()}-{i}", "X", "?", 30)) Try(() => { ctx.DestroyChild(k); });
                    GUILayout.EndHorizontal();
                }
            });

            Section("Parent chain", () =>
            {
                var chain = new List<Context>();
                var cur = ctx;
                var guard = new HashSet<Context>();
                while (cur != null && !guard.Contains(cur))
                {
                    guard.Add(cur);
                    chain.Add(cur);
                    cur = cur.Parent;
                }
                chain.Reverse();
                GUILayout.BeginHorizontal();
                for (int i = 0; i < chain.Count; i++)
                {
                    if (i > 0) GUILayout.Label("›", _dimStyle, GUILayout.Width(14));
                    if (GUILayout.Button(chain[i].GetType().Name, GUILayout.MaxWidth(150), GUILayout.Height(20)))
                        _selected = chain[i];
                }
                GUILayout.EndHorizontal();
            });
        }

        readonly Dictionary<object, bool> _dumpFoldouts = new Dictionary<object, bool>();

        void DrawDump(string title, object obj, Type stopBefore = null)
        {
            if (obj == null) return;
            if (!_dumpFoldouts.TryGetValue(obj, out var open)) open = false;
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            bool no = GUILayout.Toggle(open, "", GUILayout.Width(20));
            if (GUILayout.Button(title, _rowBtnStyle, GUILayout.Height(20), GUILayout.ExpandWidth(true))) no = !no;
            _dumpFoldouts[obj] = no;
            GUILayout.EndHorizontal();
            if (!no) return;
            var rows = CDSPOverlayReflection.DumpMembers(obj, 60, _showPrivate, _hideStd, stopBefore);
            if (rows.Count == 0) GUILayout.Label("— no public fields —", _dimStyle);
            foreach (var r in rows)
                KV($"{r.name} : {r.typeName}", r.valueText);
        }

        void DrawBoolRow(string label, bool value, Action<bool> set)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label(label, _bodyStyle, GUILayout.Width(172));
            GUILayout.FlexibleSpace();
            bool nv = GUILayout.Toggle(value, value ? "ON" : "OFF", GUILayout.Width(58));
            if (nv != value) set(nv);
            GUILayout.EndHorizontal();
        }

        void DrawBoolPropRow(object obj, string prop)
        {
            bool cur = false;
            try
            {
                var p = obj.GetType().GetProperty(prop);
                if (p != null) cur = (bool)p.GetValue(obj);
            }
            catch { return; }
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label($"{obj.GetType().Name}.{prop}", _bodyStyle, GUILayout.Width(172));
            GUILayout.FlexibleSpace();
            bool nv = GUILayout.Toggle(cur, cur ? "ON" : "OFF", GUILayout.Width(58));
            if (nv != cur) CDSPOverlayReflection.TrySetBoolProp(obj, prop, nv);
            GUILayout.EndHorizontal();
        }

        void ToggleActive(Context ctx)
        {
            if (ctx == null || ctx.IsDead) return;
            ctx.SetUpdating(!ctx.IsUpdating);
        }

        void Try(Action a)
        {
            try { a(); }
            catch (Exception e) { Notify(e.GetType().Name + ": " + e.Message); Debug.LogException(e); }
        }

        internal static string FullPath(GameObject go)
        {
            if (go == null) return "<null>";
            var parts = new List<string>();
            var t = go.transform;
            while (t != null) { parts.Add(t.name); t = t.parent; }
            parts.Reverse();
            return "/" + string.Join("/", parts);
        }

        void FlashSelected()
        {
            if (_selected == null) return;
            _flashHighlight = _selected;
            _flashHighlightUntil = Time.realtimeSinceStartup + 3f;
            LabelsEnabled = true;
        }

        void DrawFooter()
        {
            GUILayout.BeginHorizontal(GUILayout.Height(20));
            if (!string.IsNullOrEmpty(_notice) && Now < _noticeUntil)
                GUILayout.Label(_notice, _smallStyle, GUILayout.ExpandWidth(true));
            else
                GUILayout.Label("F1 explorer  ·  F2 labels  ·  F3 labels on/off  ·  click object/card = select", _dimStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Copy", GUILayout.Width(62), GUILayout.Height(20)) && _selected != null)
            {
                GUIUtility.systemCopyBuffer = $"{_selected.GetType().FullName}\n{FullPath(_selected.Instance)}";
                Notify("Copied");
            }
            GUILayout.EndHorizontal();
        }

        #endregion

        #region CLICK SELECT / GEOMETRY (shared with labels part)

        void PickAt(Vector2 guiMousePos)
        {
            var cam = ActiveCamera;
            if (cam == null) { Notify("No camera"); return; }
            Vector2 screenPt = new Vector2(guiMousePos.x, Screen.height - guiMousePos.y);
            var ray = cam.ScreenPointToRay(new Vector3(screenPt.x, screenPt.y, 0f));

            Presentation found = null;
            if (Physics.Raycast(ray, out var hit, 1000f))
                found = hit.collider.GetComponentInParent<Presentation>();
            if (found == null)
            {
                var hit2d = Physics2D.GetRayIntersection(ray, 1000f);
                if (hit2d.collider != null)
                    found = hit2d.collider.GetComponentInParent<Presentation>();
            }
            if (found != null)
            {
                var ctx = FindByPresentation(found);
                if (ctx == null) ctx = FindByGameObject(found.gameObject);
                if (ctx != null)
                {
                    _selected = ctx;
                    _flashHighlight = ctx;
                    _flashHighlightUntil = Time.realtimeSinceStartup + 3f;
                    Notify($"Picked {ctx.GetType().Name}");
                }
                else Notify($"Presentation {found.GetType().Name} has no context");
            }
        }

        internal bool TryGetScreenRect(Context ctx, Camera cam, out Rect rect, out float depth)
        {
            rect = default;
            depth = float.MaxValue;
            if (ctx == null || ctx.Instance == null || cam == null) return false;
            var go = ctx.Instance;

            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                return ProjectPoints(corners, cam, out rect, out depth, 40f, 18f);
            }

            ResolveBoxSource(ctx, go, out var rend, out var col);
            if (rend != null)
            {
                var b = rend.bounds;
                var c = b.center;
                var e2 = b.extents;
                var pts = new Vector3[8];
                pts[0] = c + new Vector3(e2.x, e2.y, e2.z);
                pts[1] = c + new Vector3(e2.x, e2.y, -e2.z);
                pts[2] = c + new Vector3(e2.x, -e2.y, e2.z);
                pts[3] = c + new Vector3(e2.x, -e2.y, -e2.z);
                pts[4] = c + new Vector3(-e2.x, e2.y, e2.z);
                pts[5] = c + new Vector3(-e2.x, e2.y, -e2.z);
                pts[6] = c + new Vector3(-e2.x, -e2.y, e2.z);
                pts[7] = c + new Vector3(-e2.x, -e2.y, -e2.z);
                return ProjectPoints(pts, cam, out rect, out depth, 40f, 18f);
            }

            if (col != null)
            {
                var b = col.bounds;
                var pts = new Vector3[2] { b.min, b.max };
                return ProjectPoints(pts, cam, out rect, out depth, 56f, 18f);
            }

            Vector3 sp = cam.WorldToScreenPoint(go.transform.position);
            if (sp.z < 0 || !float.IsFinite(sp.x + sp.y + sp.z)) return false;
            float x = sp.x, y = Screen.height - sp.y;
            if (x < -100 || x > Screen.width + 100 || y < -50 || y > Screen.height + 50) return false;
            rect = new Rect(x - 60f, y - 10f, 120f, 20f);
            depth = sp.z;
            return true;
        }

        internal bool ProjectPoints(Vector3[] worldPts, Camera cam, out Rect rect, out float depth, float minW, float minH)
        {
            rect = default;
            depth = float.MaxValue;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            float zSum = 0f;
            int zN = 0;
            foreach (var w in worldPts)
            {
                Vector3 sp = cam.WorldToScreenPoint(w);
                if (sp.z <= 0 || !float.IsFinite(sp.x + sp.y + sp.z)) continue;
                zSum += sp.z;
                zN++;
                float x = sp.x, y = Screen.height - sp.y;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
            if (zN == 0) return false;
            if (maxX - minX < minW) { float cx = (minX + maxX) * 0.5f; minX = cx - minW * 0.5f; maxX = cx + minW * 0.5f; }
            if (maxY - minY < minH) { float cy = (minY + maxY) * 0.5f; minY = cy - minH * 0.5f; maxY = cy + minH * 0.5f; }
            if (maxX < 0 || minX > Screen.width || maxY < 0 || minY > Screen.height) return false;
            rect = new Rect(minX, minY, maxX - minX, maxY - minY);
            depth = zSum / zN;
            return true;
        }

        static int TreeDepth(Context c)
        {
            int n = 0;
            var cur = c;
            var guard = new HashSet<Context>();
            while (cur != null && n < 64 && guard.Add(cur))
            {
                n++;
                cur = cur.Parent;
            }
            return n;
        }

        #region BOX SOURCE (own visuals only)


        const float BoxCacheTTL = 1f;

        struct BoxSource
        {
            public double time;
            public Renderer rend;
            public Collider col;
            public bool hasRend;
            public bool hasCol;
        }

        readonly Dictionary<Context, BoxSource> _boxCache = new Dictionary<Context, BoxSource>();
        readonly HashSet<Transform> _ownedInstances = new HashSet<Transform>();

        void RebuildOwnedInstances()
        {
            _ownedInstances.Clear();
            foreach (var c in _cachedAll)
            {
                if (c == null || c.Instance == null) continue;
                _ownedInstances.Add(c.Instance.transform);
            }
        }

        void PruneBoxCache()
        {
            if (_boxCache.Count == 0) return;
            if (_boxCache.Count > 256) { _boxCache.Clear(); return; }
            var alive = new HashSet<Context>(_cachedAll);
            var dead = new List<Context>();
            foreach (var k in _boxCache.Keys)
                if (k == null || !alive.Contains(k)) dead.Add(k);
            foreach (var k in dead) _boxCache.Remove(k);
        }

        void ResolveBoxSource(Context ctx, GameObject go, out Renderer rend, out Collider col)
        {
            rend = null;
            col = null;
            if (_boxCache.TryGetValue(ctx, out var src) && Now - src.time < BoxCacheTTL)
            {
                if ((src.hasRend && src.rend == null) || (src.hasCol && src.col == null))
                {
                }
                else
                {
                    rend = src.rend;
                    col = src.col;
                    return;
                }
            }
            var t = go.transform;
            var r = FindOwn<Renderer>(t, 0);
            Collider c = null;
            if (r == null) c = FindOwn<Collider>(t, 0);
            _boxCache[ctx] = new BoxSource
            {
                time = Now,
                rend = r,
                col = c,
                hasRend = r != null,
                hasCol = c != null
            };
            rend = r;
            col = c;
        }

        T FindOwn<T>(Transform t, int depth) where T : Component
        {
            if (t == null || depth > 32) return null;
            var own = t.GetComponent<T>();
            if (own != null) return own;
            foreach (Transform child in t)
            {
                if (child != null && _ownedInstances.Contains(child)) continue;
                var found = FindOwn<T>(child, depth + 1);
                if (found != null) return found;
            }
            return null;
        }

        #endregion

        #endregion
    }

#else

    public sealed partial class CDSPOverlay : MonoBehaviour
    {
        public static CDSPOverlay Instance => null;

        [Header("Overlay")]
        public KeyCode ToggleKey = KeyCode.F1;
        public bool StartVisible;

        public KeyCode LabelsKey = KeyCode.F2;
        public KeyCode LabelsQuickKey = KeyCode.F3;

        public static CDSPOverlay EnsureExists() => null;
        public static void Show() { }
        public static void Hide() { }
        public static void Toggle() { }
        public void SetVisible(bool v) { }
        public bool IsVisible => false;
        public bool IsFullyOff => true;

        public static void ShowLabels() { }
        public static void HideLabels() { }
        public static void ToggleLabels() { }
        public static void ShowLabelsWindow() { }
    }

#endif
}
