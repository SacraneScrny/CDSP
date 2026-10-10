#if UNITY_EDITOR
using System;
using System.Collections.Generic;

using CDSP.Base;
using CDSP.Components;
using CDSP.Entry;

using UnityEditor;

using UnityEngine;

namespace CDSP.Editor
{
    /// <summary>Live tree explorer: state badges, GameObject path, tick flags, Domain/System/Presentation dumps, EventBus subscriptions, children and parent chain. Editor-only, zero build cost.</summary>
    public sealed class CDSPExplorerWindow : EditorWindow
    {
        /// <summary>Opens the explorer (<c>CDSP / Explorer</c>, Ctrl+Shift+E).</summary>
        [MenuItem("CDSP/Explorer %#e")]
        public static void Open()
        {
            var w = GetWindow<CDSPExplorerWindow>("CDSP Explorer");
            w.minSize = new Vector2(700, 400);
            w.Show();
        }

        string _search = "";
        bool _autoRefresh = true;
        float _refreshInterval = 0.25f;
        bool _showPrivate = false;
        bool _hideStd = true;
        bool _showDeadChildren = false;
        bool _expandNew = true;
        bool _followSelection = true;
        bool _showIds = false;
        int _nextContextId = 1;
        readonly Dictionary<Context, int> _contextIds = new Dictionary<Context, int>();
        float _leftWidth = 340f;

        Vector2 _treeScroll;
        Vector2 _detailsScroll;
        Context _selected;

        readonly HashSet<Context> _expanded = new HashSet<Context>();
        readonly HashSet<Context> _known = new HashSet<Context>();
        double _nextRepaint;

        GUIStyle _badgeStyle;
        GUIStyle _titleStyle;
        GUIStyle _idStyle;
        GUIStyle _sectionEvenStyle;
        GUIStyle _sectionOddStyle;
        Texture2D _sectionOddTex;
        bool _sectionDarkSkin;
        int _sectionIndex;

        const float DeadGraceSeconds = 3f;
        const float FlashSeconds = 0.5f;
        readonly Dictionary<Context, double> _deadSince = new Dictionary<Context, double>();
        readonly Dictionary<Context, Context> _deadParent = new Dictionary<Context, Context>();
        readonly Dictionary<Context, List<Context>> _prevChildren = new Dictionary<Context, List<Context>>();
        readonly Dictionary<Context, double> _bornAt = new Dictionary<Context, double>();
        readonly HashSet<Context> _stampedDead = new HashSet<Context>();
        bool _firstTrack = true;

        void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Selection.selectionChanged += OnUnitySelectionChanged;
            RefreshExpandForNew();
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Selection.selectionChanged -= OnUnitySelectionChanged;
        }

        void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
                ResetSessionState();
            Repaint();
        }

        void ResetSessionState()
        {
            _nextContextId = 1;
            _contextIds.Clear();
            _deadSince.Clear();
            _deadParent.Clear();
            _prevChildren.Clear();
            _bornAt.Clear();
            _stampedDead.Clear();
            _known.Clear();
            _expanded.Clear();
            _selected = null;
        }

        void OnUnitySelectionChanged()
        {
            if (!_followSelection) return;
            var go = Selection.activeGameObject;
            if (go == null) return;
            var found = FindByGameObject(go);
            if (found != null && found != _selected)
            {
                _selected = found;
                Repaint();
            }
        }

        void OnEditorUpdate()
        {
            if (!_autoRefresh) return;
            if (EditorApplication.timeSinceStartup >= _nextRepaint)
            {
                float interval = Mathf.Max(0.05f, _refreshInterval);
                if (HasDying()) interval = Mathf.Min(interval, 0.1f);
                if (HasFlash()) interval = 0.05f;
                _nextRepaint = EditorApplication.timeSinceStartup + interval;
                Repaint();
            }
        }

        bool HasDying()
        {
            double now = EditorApplication.timeSinceStartup;
            foreach (var kv in _deadSince)
                if (now - kv.Value < DeadGraceSeconds) return true;
            return false;
        }

        bool HasFlash()
        {
            double now = EditorApplication.timeSinceStartup;
            foreach (var kv in _deadSince)
                if (now - kv.Value < FlashSeconds) return true;
            foreach (var kv in _bornAt)
                if (now - kv.Value < FlashSeconds) return true;
            return false;
        }

        float FlashRemaining(Context ctx, double now, out Color color)
        {
            if (ctx.IsDead && _deadSince.TryGetValue(ctx, out var dsince))
            {
                float r = 1f - (float)((now - dsince) / FlashSeconds);
                if (r > 0f) { color = new Color(1f, 0.3f, 0.25f, 0.55f); return Mathf.Clamp01(r); }
            }
            if (!ctx.IsDead && _bornAt.TryGetValue(ctx, out var bsince))
            {
                float r = 1f - (float)((now - bsince) / FlashSeconds);
                if (r > 0f) { color = new Color(0.35f, 0.9f, 0.45f, 0.5f); return Mathf.Clamp01(r); }
            }
            color = Color.clear;
            return 0f;
        }

        void DrawFlashBar(Rect rowRect, float remaining, Color color)
        {
            if (rowRect.width <= 0f || rowRect.height <= 0f || remaining <= 0f) return;
            color.a *= remaining;
            EditorGUI.DrawRect(rowRect, color);
        }

        void InitStyles()
        {
            if (_badgeStyle == null)
            {
                _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(8, 8, 2, 2)
                };
                _badgeStyle.normal.textColor = Color.white;
            }
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            }
            if (_idStyle == null)
            {
                _idStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            }
        }

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

        void CollectAll(Context ctx, List<Context> outList, HashSet<Context> guard)
        {
            if (ctx == null || guard.Contains(ctx)) return;
            guard.Add(ctx);
            outList.Add(ctx);
            var kids = CDSPExplorerReflection.GetChildren(ctx);
            if (kids == null) return;
            foreach (var k in kids)
                CollectAll(k, outList, guard);
        }

        List<Context> AllContexts()
        {
            var all = new List<Context>();
            var guard = new HashSet<Context>();
            foreach (var r in Roots) CollectAll(r, all, guard);
            return all;
        }

        void RefreshExpandForNew()
        {
            if (!_expandNew) return;
            foreach (var c in AllContexts())
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
            var d = CDSPExplorerReflection.GetDomain(c);
            var sys = CDSPExplorerReflection.GetSystem(c);
            var p = CDSPExplorerReflection.GetPresentation(c);
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
            var kids = CDSPExplorerReflection.GetChildren(c);
            if (kids != null)
                foreach (var k in kids)
                    if (SubtreeMatches(k, new HashSet<Context>(guard))) return true;
            return false;
        }

        void OnGUI()
        {
            InitStyles();
            RefreshExpandForNew();
            TrackDeaths();
            DrawToolbar();
            DrawRootsBar();
            DrawPlayerLoopBar();
            DrawMain();
            DrawFooter();
        }

        void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
                Repaint();
            _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(50));
            GUILayout.Label("every", GUILayout.Width(34));
            _refreshInterval = EditorGUILayout.FloatField(_refreshInterval, GUILayout.Width(45));
            _refreshInterval = Mathf.Clamp(_refreshInterval, 0.05f, 5f);

            GUILayout.Space(8);
            _search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSeachTextField") ?? EditorStyles.toolbarTextField, GUILayout.MinWidth(120));
            if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22))) _search = "";

            GUILayout.Space(8);
            if (GUILayout.Button("Expand", EditorStyles.toolbarButton, GUILayout.Width(60))) SetAllExpanded(true);
            if (GUILayout.Button("Fold", EditorStyles.toolbarButton, GUILayout.Width(44))) SetAllExpanded(false);

            GUILayout.FlexibleSpace();
            _showPrivate = GUILayout.Toggle(_showPrivate, "Private", EditorStyles.toolbarButton, GUILayout.Width(58));
            _hideStd = GUILayout.Toggle(_hideStd, "HideStd", EditorStyles.toolbarButton, GUILayout.Width(62));
            _showDeadChildren = GUILayout.Toggle(_showDeadChildren, "Dead", EditorStyles.toolbarButton, GUILayout.Width(48));
            _expandNew = GUILayout.Toggle(_expandNew, "Auto-expand", EditorStyles.toolbarButton, GUILayout.Width(88));
            _followSelection = GUILayout.Toggle(_followSelection, "Follow", EditorStyles.toolbarButton, GUILayout.Width(58));
            _showIds = GUILayout.Toggle(_showIds, "ID", EditorStyles.toolbarButton, GUILayout.Width(40));
            EditorGUILayout.EndHorizontal();
        }

        void DrawPlayerLoopBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(Application.isPlaying ? "● PLAY" : "○ EDIT", GUILayout.Width(60));
            try
            {
                bool a = EntryPointUpdate.IsActive;
                bool t = EntryPointUpdate.IsUpdateTick;
                bool f = EntryPointUpdate.IsUpdateFixedTick;
                bool l = EntryPointUpdate.IsUpdateLateTick;
                EditorGUI.BeginDisabledGroup(!Application.isPlaying);
                bool na = GUILayout.Toggle(a, "Loop", GUILayout.Width(55));
                bool nt = GUILayout.Toggle(t, "Tick", GUILayout.Width(50));
                bool nf = GUILayout.Toggle(f, "Fixed", GUILayout.Width(55));
                bool nl = GUILayout.Toggle(l, "Late", GUILayout.Width(50));
                if (Application.isPlaying)
                {
                    if (na != a) EntryPointUpdate.SetActive(na);
                    if (nt != t) EntryPointUpdate.SetUpdateTick(nt);
                    if (nf != f) EntryPointUpdate.SetUpdateFixedTick(nf);
                    if (nl != l) EntryPointUpdate.SetUpdateLateTick(nl);
                }
                EditorGUI.EndDisabledGroup();
            }
            catch { GUILayout.Label("EntryPointUpdate n/a"); }
            GUILayout.FlexibleSpace();
            var all = AllContexts();
            int alive = 0;
            foreach (var c in all) if (!c.IsDead) alive++;
            GUILayout.Label($"Roots: {Roots.Count}   Contexts: {all.Count} (alive {alive})");
            EditorGUILayout.EndHorizontal();
        }

        void DrawMain()
        {
            EditorGUILayout.BeginHorizontal();
            DrawTreePanel();
            DrawSplitter();
            DrawDetailsPanel();
            EditorGUILayout.EndHorizontal();
        }

        void DrawSplitter()
        {
            var r = GUILayoutUtility.GetRect(9f, 0f, GUILayout.ExpandHeight(true));
            int id = GUIUtility.GetControlID("CDSP_Splitter".GetHashCode(), FocusType.Passive);
            EditorGUIUtility.AddCursorRect(r, MouseCursor.ResizeHorizontal);
            var e = Event.current;
            switch (e.type)
            {
                case EventType.MouseDown:
                    if (e.button == 0 && r.Contains(e.mousePosition))
                    {
                        GUIUtility.hotControl = id;
                        e.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (GUIUtility.hotControl == id)
                    {
                        GUIUtility.hotControl = 0;
                        e.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == id)
                    {
                        _leftWidth = Mathf.Clamp(_leftWidth + e.delta.x, 180f, 700f);
                        e.Use();
                        Repaint();
                    }
                    break;
            }
            var c = GUIUtility.hotControl == id
                ? new Color(0.3f, 0.6f, 1f, 0.8f)
                : new Color(0.5f, 0.5f, 0.5f, 0.35f);
            EditorGUI.DrawRect(new Rect(r.x + r.width * 0.5f - 0.5f, r.y, 1, r.height), c);
        }

        void DrawTreePanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(_leftWidth), GUILayout.ExpandHeight(true));
            _treeScroll = EditorGUILayout.BeginScrollView(_treeScroll);
            var roots = Roots;
            if (roots.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No contexts found.\n\n• In PlayMode roots come from EntryPoint.MainContextsList.\n• In EditMode — from ContextRoot in the scene.\n• Make sure the scene has a ContextRoot with a prefab.",
                    MessageType.Info);
            }
            var guard = new HashSet<Context>();
            bool filtering = !string.IsNullOrEmpty(_search);
            foreach (var r in roots)
            {
                if (filtering && !SubtreeMatches(r, new HashSet<Context>())) continue;
                DrawNode(r, 0, guard, filtering);
            }
            EditorGUILayout.EndScrollView();
            if (GUILayout.Button("Find by Selection", GUILayout.ExpandWidth(true)))
            {
                var go = Selection.activeGameObject;
                if (go != null)
                {
                    var found = FindByGameObject(go);
                    if (found != null) _selected = found;
                    else ShowNotification(new GUIContent("GO does not belong to CDSP"));
                }
            }
            EditorGUILayout.EndVertical();
        }

        void DrawNode(Context ctx, int depth, HashSet<Context> guard, bool filtering)
        {
            if (ctx == null || guard.Contains(ctx)) return;
            guard.Add(ctx);
            if (filtering && !SubtreeMatches(ctx, new HashSet<Context>())) return;

            double now = EditorApplication.timeSinceStartup;
            if (ctx.IsDead && !IsInGrace(ctx, now) && !_showDeadChildren) return;

            var kids = GetDisplayChildren(ctx, now);
            bool hasKids = kids.Count > 0;
            bool expanded = filtering || _expanded.Contains(ctx);

            bool grayed = ctx.IsDead;
            if (grayed) GUI.enabled = false;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 14f);
            if (_showIds)
                GUILayout.Label(GetContextId(ctx).ToString(), _idStyle, GUILayout.Width(36));

            bool newExpanded = expanded;
            if (hasKids && !filtering)
            {
                var foldRect = GUILayoutUtility.GetRect(14f, EditorGUIUtility.singleLineHeight, GUILayout.Width(14f));
                newExpanded = EditorGUI.Foldout(foldRect, expanded, GUIContent.none);
                if (newExpanded != expanded)
                {
                    if (newExpanded) _expanded.Add(ctx);
                    else _expanded.Remove(ctx);
                }
            }
            else GUILayout.Space(16);

            var dot = ctx.IsDead ? "✕" : (!ctx.IsUpdating ? "◌" : (!ctx.IsStarted ? "△" : "●"));
            var dotColor = ctx.IsDead ? Color.red : (!ctx.IsUpdating ? Color.gray : (!ctx.IsStarted ? Color.yellow : Color.green));
            var oldC = GUI.contentColor;
            GUI.contentColor = dotColor;
            GUILayout.Label(dot, GUILayout.Width(16));
            GUI.contentColor = oldC;

            string goName = ctx.Instance != null ? ctx.Instance.name : "<no GO>";
            string label = $"{ctx.GetType().Name}  ({goName})";
            if (GUILayout.Button(label, EditorStyles.label, GUILayout.ExpandWidth(true)))
            {
                _selected = ctx;
            }
            DrawPauseButton(ctx);
            DrawSelectButton(ctx);
            EditorGUILayout.EndHorizontal();

            if (grayed) GUI.enabled = true;

            Rect rowRect = GUILayoutUtility.GetLastRect();

            if (_selected == ctx)
                EditorGUI.DrawRect(rowRect, new Color(0.2f, 0.5f, 0.95f, 0.35f));

            if (ctx.IsDead && IsInGrace(ctx, now))
                DrawDeathBar(rowRect, DeathRemaining(ctx, now));

            float flashRem = FlashRemaining(ctx, now, out Color flashColor);
            if (flashRem > 0f)
                DrawFlashBar(rowRect, flashRem, flashColor);

            if (hasKids && (expanded || filtering))
                foreach (var k in kids)
                    DrawNode(k, depth + 1, guard, filtering);
        }

        void SetAllExpanded(bool value)
        {
            foreach (var c in AllContexts())
            {
                if (value) _expanded.Add(c);
                else _expanded.Remove(c);
            }
        }

        void DrawDetailsPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            _detailsScroll = EditorGUILayout.BeginScrollView(_detailsScroll);
            if (_selected == null)
            {
                EditorGUILayout.HelpBox("Select a context on the left.\n○ button — select + ping GameObject.", MessageType.Info);
                DrawSceneRootsSection();
            }
            else
            {
                DrawContextDetails(_selected);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        void DrawSceneRootsSection()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Scene ContextRoots", EditorStyles.boldLabel);
            try
            {
#if UNITY_2023_1_OR_NEWER
                var sceneRoots = UnityEngine.Object.FindObjectsByType<ContextRoot>(FindObjectsSortMode.None);
#else
                var sceneRoots = UnityEngine.Object.FindObjectsOfType<ContextRoot>();
#endif
                if (sceneRoots.Length == 0)
                    EditorGUILayout.LabelField("— none —", EditorStyles.miniLabel);
                foreach (var r in sceneRoots)
                {
                    if (r == null) continue;
                    var c = r.Context;
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(r, typeof(ContextRoot), true, GUILayout.ExpandWidth(true));
                    string info = c == null ? "Context: null" : $"Context: {c.GetType().Name}";
                    GUILayout.Label(info, GUILayout.Width(220));
                    EditorGUI.BeginDisabledGroup(c == null);
                    if (GUILayout.Button("→", GUILayout.Width(28)))
                        _selected = c;
                    EditorGUI.EndDisabledGroup();
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.ObjectField("Prefab", r.Prefab, typeof(GameObject), true);
                    EditorGUILayout.EndVertical();
                }
            }
            catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Warning); }
        }

        void DrawContextDetails(Context ctx)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label(ctx.GetType().FullName, _titleStyle);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Ping GO", GUILayout.Width(70)) && ctx.Instance != null)
                EditorGUIUtility.PingObject(ctx.Instance);
            if (GUILayout.Button("Select GO", GUILayout.Width(76)) && ctx.Instance != null)
                Selection.activeGameObject = ctx.Instance;
            if (GUILayout.Button("✕", GUILayout.Width(26)))
                _selected = null;
            EditorGUILayout.EndHorizontal();
            DrawSeparator();
            _sectionIndex = 0;

            Section(DrawBadges, ctx);
            Section(DrawGoSection, ctx);
            Section(DrawFlagsSection, ctx);
            Section(DrawLifecycleButtons, ctx);
            Section(DrawTripleSection, ctx);
            if (CDSPExplorerReflection.GetEventBusInfo(ctx).hasBus) Section(DrawEventsSection, ctx);
            Section(DrawChildrenSection, ctx);
            Section(DrawBreadcrumb, ctx);
        }

        void Section(Action<Context> draw, Context ctx)
        {
            InitSectionStyles();
            var style = (_sectionIndex++ % 2 == 0) ? _sectionEvenStyle : _sectionOddStyle;
            EditorGUILayout.BeginVertical(style);
            draw(ctx);
            EditorGUILayout.EndVertical();
        }

        void InitSectionStyles()
        {
            bool pro = EditorGUIUtility.isProSkin;
            if (_sectionEvenStyle != null && _sectionOddStyle != null && _sectionDarkSkin == pro) return;
            _sectionDarkSkin = pro;
            if (_sectionOddTex != null) UnityEngine.Object.DestroyImmediate(_sectionOddTex);
            _sectionEvenStyle = new GUIStyle(EditorStyles.helpBox);
            _sectionOddStyle = new GUIStyle(EditorStyles.helpBox);
            _sectionOddTex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _sectionOddTex.SetPixel(0, 0, pro ? new Color(0.20f, 0.20f, 0.20f, 1f) : new Color(0.74f, 0.74f, 0.74f, 1f));
            _sectionOddTex.Apply();
            _sectionOddTex.hideFlags = HideFlags.HideAndDontSave;
            _sectionOddStyle.normal.background = _sectionOddTex;
        }

        void DrawSeparator()
        {
            var r = EditorGUILayout.GetControlRect(GUILayout.Height(1));
            EditorGUI.DrawRect(r, new Color(0.5f, 0.5f, 0.5f, 0.3f));
        }

        void DrawBadges(Context ctx)
        {
            EditorGUILayout.BeginHorizontal();
            Badge(ctx.IsDead ? "DEAD" : "ALIVE", ctx.IsDead ? new Color(0.6f, 0.15f, 0.15f) : new Color(0.2f, 0.5f, 0.25f));
            Badge(ctx.IsUpdating ? "Active" : "Inactive", ctx.IsUpdating ? new Color(0.2f, 0.45f, 0.7f) : Color.gray);
            Badge(ctx.IsAwaken ? "Awake" : "¬Awake", ctx.IsAwaken ? new Color(0.25f, 0.5f, 0.25f) : new Color(0.5f, 0.45f, 0.15f));
            Badge(ctx.IsStarted ? "Started" : "¬Started", ctx.IsStarted ? new Color(0.25f, 0.5f, 0.25f) : new Color(0.5f, 0.45f, 0.15f));
            Badge(ctx.IsReady ? "Ready" : "¬Ready", ctx.IsReady ? new Color(0.3f, 0.6f, 0.3f) : new Color(0.45f, 0.45f, 0.45f));
            Badge(ctx.IsRoot ? "Root" : "Child", ctx.IsRoot ? new Color(0.45f, 0.3f, 0.6f) : new Color(0.35f, 0.35f, 0.4f));
            EditorGUILayout.EndHorizontal();

            var dbg = CDSPExplorerReflection.GetDebugFlags(ctx);
            EditorGUILayout.LabelField($"hold-awake: {(dbg.notAwaken ? "WAIT" : "ok")}   hold-start: {(dbg.notStarted ? "WAIT" : "ok")}   _hasDeadChildren: {dbg.hasDeadChildren}",
                EditorStyles.miniLabel);
        }

        void Badge(string text, Color bg)
        {
            var old = GUI.backgroundColor;
            GUI.backgroundColor = bg;
            GUILayout.Label(text, _badgeStyle, GUILayout.Height(18));
            GUI.backgroundColor = old;
        }

        void DrawGoSection(Context ctx)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Presentation GameObject", EditorStyles.boldLabel);
            EditorGUILayout.ObjectField("Presentation", ctx.Instance, typeof(GameObject), true);
            if (ctx.Instance == null)
            {
                EditorGUILayout.HelpBox("Presentation == null: CreatePresentation has not been called yet (or the object was destroyed).", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"active: {ctx.Instance.activeInHierarchy} (self {ctx.Instance.activeSelf})",
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                EditorGUILayout.LabelField($"children: {ctx.Instance.transform.childCount}",
                    EditorStyles.miniLabel, GUILayout.Width(90));
                EditorGUILayout.EndHorizontal();
            }
        }

        void DrawFlagsSection(Context ctx)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Ticks & Activity", EditorStyles.boldLabel);
            DrawBoolToggleRow("Context.IsUpdating (SetUpdating)", ctx.IsUpdating, v =>
            {
                if (Application.isPlaying) ctx.SetUpdating(v);
                else CDSPExplorerReflection.TrySetBoolProp(ctx, "IsUpdating", v);
            });
            float ts = ctx.TimeScale;
            float nts = EditorGUILayout.Slider("Context.TimeScale", ts, 0f, 4f);
            if (!Mathf.Approximately(nts, ts)) ctx.TimeScale = nts;
            if (ts <= 0f)
                EditorGUILayout.HelpBox("TimeScale <= 0: all ticks receive deltaTime = 0.", MessageType.Warning);
            DrawBoolToggleRow("Context.IsUpdateTick", ctx.IsUpdateTick, v =>
                CDSPExplorerReflection.TrySetBoolProp(ctx, "IsUpdateTick", v));
            DrawBoolToggleRow("Context.IsUpdateFixedTick", ctx.IsUpdateFixedTick, v =>
                CDSPExplorerReflection.TrySetBoolProp(ctx, "IsUpdateFixedTick", v));
            DrawBoolToggleRow("Context.IsUpdateLateTick", ctx.IsUpdateLateTick, v =>
                CDSPExplorerReflection.TrySetBoolProp(ctx, "IsUpdateLateTick", v));

            var sys = CDSPExplorerReflection.GetSystem(ctx);
            if (sys != null)
            {
                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField($"System: {sys.GetType().Name}", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField(
                    $"ready {sys.IsReady} · awake {sys.IsAwaken} · started {sys.IsStarted} · active {sys.IsUpdating}",
                    EditorStyles.miniLabel);
                DrawBoolToggleRow("System.IsUpdating", sys.IsUpdating, v =>
                    CDSPExplorerReflection.TrySetBoolProp(sys, "IsUpdating", v));
                DrawBoolToggleRow("System.IsUpdateTick", sys.IsUpdateTick, v =>
                    CDSPExplorerReflection.TrySetBoolProp(sys, "IsUpdateTick", v));
                DrawBoolToggleRow("System.IsUpdateFixedTick", sys.IsUpdateFixedTick, v =>
                    CDSPExplorerReflection.TrySetBoolProp(sys, "IsUpdateFixedTick", v));
                DrawBoolToggleRow("System.IsUpdateLateTick", sys.IsUpdateLateTick, v =>
                    CDSPExplorerReflection.TrySetBoolProp(sys, "IsUpdateLateTick", v));
            }

            var pres = CDSPExplorerReflection.GetPresentation(ctx);
            if (pres != null)
            {
                EditorGUILayout.LabelField(
                    $"Presentation: {pres.GetType().Name} · ready {pres.IsReady} · awake {pres.IsAwaken} · started {pres.IsStarted} · active {pres.IsUpdating}",
                    EditorStyles.miniLabel);
            }
        }

        void DrawBoolToggleRow(string label, bool value, Action<bool> set)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.ExpandWidth(true));
            bool nv = GUILayout.Toggle(value, value ? "ON" : "OFF", GUI.skin.button, GUILayout.Width(52));
            if (nv != value) set(nv);
            EditorGUILayout.EndHorizontal();
        }

        void DrawLifecycleButtons(Context ctx)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Lifecycle", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            if (GUILayout.Button("Awake+Start") && EditorUtility.DisplayDialog("CDSP", $"Call AwakeAndStart on {ctx.GetType().Name}?", "Yes", "No"))
            {
                try { ctx.AwakeAndStart(); } catch (Exception e) { Debug.LogException(e); }
            }
            if (GUILayout.Button("Start") && EditorUtility.DisplayDialog("CDSP", $"Call Start on {ctx.GetType().Name}?", "Yes", "No"))
            {
                try { ctx.Start(); } catch (Exception e) { Debug.LogException(e); }
            }
            if (GUILayout.Button("StartAllChildren"))
            {
                try { ctx.StartAllChildren(); } catch (Exception e) { Debug.LogException(e); }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(!Application.isPlaying || ctx.IsDead);
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("Destroy context"))
            {
                if (EditorUtility.DisplayDialog("CDSP",
                        $"Destroy context {ctx.GetType().Name}?\n{(ctx.IsRoot ? "This is a ROOT — all children will die." : "The context will be removed from its parent.")}",
                        "Destroy", "Cancel"))
                {
                    try
                    {
                        ctx.Destroy();
                        if (ctx.IsRoot) _selected = null;
                        else if (ctx.Parent != null) _selected = ctx.Parent;
                    }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            GUI.backgroundColor = Color.white;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Lifecycle mutations are only available in PlayMode.", MessageType.None);
        }

        void DrawTripleSection(Context ctx)
        {
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Domain / System / Presentation", EditorStyles.boldLabel);
            var d = CDSPExplorerReflection.GetDomain(ctx);
            var sys = CDSPExplorerReflection.GetSystem(ctx);
            var p = CDSPExplorerReflection.GetPresentation(ctx);

            DrawTripleRow("Domain", d, d != null ? d.GetType().FullName : "null (non-generic Context?)");
            DrawTripleRow("System", null, sys != null ? sys.GetType().FullName : "null (non-generic Context?)");
            if (p != null) DrawTripleRow("Presentation", p, p.GetType().FullName);
            else DrawTripleRow("Presentation", null, "null (prefab without component or Presentation==null)");

            if (d != null) DrawDumpFoldout($"Domain fields: {d.GetType().Name}", d);
            if (sys != null) DrawDumpFoldout($"System fields: {sys.GetType().Name}", sys);
            if (p != null) DrawDumpFoldout($"Presentation fields: {p.GetType().Name}", p);
        }

        void DrawTripleRow(string kind, object obj, string typeName)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            GUILayout.Label(kind, EditorStyles.miniBoldLabel, GUILayout.Width(90));
            GUILayout.Label(typeName, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
            if (obj is UnityEngine.Object uo && uo != null)
            {
                if (GUILayout.Button("Ping", GUILayout.Width(46))) EditorGUIUtility.PingObject(uo);
                if (GUILayout.Button("Sel", GUILayout.Width(36))) Selection.activeObject = uo;
            }
            EditorGUILayout.EndHorizontal();
        }

        readonly Dictionary<object, bool> _dumpFoldouts = new Dictionary<object, bool>();

        void DrawDumpFoldout(string title, object obj)
        {
            if (obj == null) return;
            if (!_dumpFoldouts.TryGetValue(obj, out var open)) open = false;
            bool no = EditorGUILayout.Foldout(open, title);
            _dumpFoldouts[obj] = no;
            if (!no) return;
            EditorGUI.indentLevel++;
            var rows = CDSPExplorerReflection.DumpMembers(obj, 60, _showPrivate, _hideStd);
            if (rows.Count == 0) EditorGUILayout.LabelField("— no public fields —", EditorStyles.miniLabel);
            foreach (var r in rows)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"{r.name} : {r.typeName}", EditorStyles.miniLabel, GUILayout.Width(300));
                EditorGUILayout.LabelField(r.valueText, EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUI.indentLevel--;
        }

        void DrawEventsSection(Context ctx)
        {
            var bus = CDSPExplorerReflection.GetEventBusInfo(ctx);
            if (!bus.hasBus) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"EventBus<{bus.keyTypeName}>  (keys: {bus.keyCount})", EditorStyles.boldLabel);
            if (bus.entries.Count == 0)
            {
                EditorGUILayout.LabelField("— no subscriptions —", EditorStyles.miniLabel);
                return;
            }
            foreach (var e in bus.entries)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label(e.keyText, EditorStyles.miniBoldLabel, GUILayout.Width(180));
                GUILayout.Label($"plain x{e.plainCount}", EditorStyles.miniLabel, GUILayout.Width(80));
                GUILayout.Label(e.typedCounts.Count == 0 ? "—" : string.Join(" · ", e.typedCounts),
                    EditorStyles.miniLabel, GUILayout.ExpandWidth(true));
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUI.BeginDisabledGroup(!Application.isPlaying);
            if (GUILayout.Button("ClearEvents", GUILayout.Width(110)))
            {
                if (EditorUtility.DisplayDialog("CDSP", "Clear all subscriptions of this context?", "Yes", "No"))
                {
                    try
                    {
                        var m = ctx.GetType().GetMethod("ClearEvents", Type.EmptyTypes);
                        m?.Invoke(ctx, null);
                    }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        void DrawChildrenSection(Context ctx)
        {
            double now = EditorApplication.timeSinceStartup;
            var kids = GetDisplayChildren(ctx, now);
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField($"Children ({kids.Count})", EditorStyles.boldLabel);
            if (kids.Count == 0)
            {
                EditorGUILayout.LabelField("— no children —", EditorStyles.miniLabel);
                return;
            }
            for (int i = 0; i < kids.Count; i++)
            {
                var k = kids[i];
                if (k == null) continue;
                if (k.IsDead && !IsInGrace(k, now) && !_showDeadChildren) continue;
                bool grayed = k.IsDead;
                if (grayed) GUI.enabled = false;
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                GUILayout.Label($"[{i}]", GUILayout.Width(32));
                GUILayout.Label($"{k.GetType().Name}", EditorStyles.miniBoldLabel, GUILayout.Width(200));
                GUILayout.Label(k.IsDead ? "DEAD" : (k.IsUpdating ? "active" : "off"), EditorStyles.miniLabel, GUILayout.Width(60));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("→", GUILayout.Width(28))) _selected = k;
                DrawPauseButton(k);
                DrawSelectButton(k);
                EditorGUI.BeginDisabledGroup(!Application.isPlaying || k.IsDead);
                if (GUILayout.Button("✕", GUILayout.Width(28)))
                {
                    if (EditorUtility.DisplayDialog("CDSP", $"Destroy child [{i}] {k.GetType().Name}?", "Yes", "No"))
                        try { ctx.DestroyChild(k); } catch (Exception e) { Debug.LogException(e); }
                }
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
                if (grayed) GUI.enabled = true;
                if (k.IsDead && IsInGrace(k, now))
                    DrawDeathBar(GUILayoutUtility.GetLastRect(), DeathRemaining(k, now));
            }
        }

        void DrawBreadcrumb(Context ctx)
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Parent chain", EditorStyles.boldLabel);
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
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < chain.Count; i++)
            {
                if (i > 0) GUILayout.Label("›", GUILayout.Width(14));
                var c = chain[i];
                if (GUILayout.Button(c.GetType().Name, EditorStyles.miniButton, GUILayout.MaxWidth(160)))
                    _selected = c;
            }
            EditorGUILayout.EndHorizontal();
        }

        Context FindByGameObject(GameObject go)
        {
            if (go == null) return null;
            var all = AllContexts();
            foreach (var c in all)
                if (c.Instance == go) return c;
            var t = go.transform.parent;
            while (t != null)
            {
                foreach (var c in all)
                    if (c.Instance == t.gameObject) return c;
                t = t.parent;
            }
            foreach (var c in all)
            {
                var inst = c.Instance;
                if (inst == null) continue;
                var p = inst.transform.parent;
                while (p != null)
                {
                    if (p.gameObject == go) return c;
                    p = p.parent;
                }
            }
            return null;
        }

        void ToggleActive(Context ctx)
        {
            if (ctx == null || ctx.IsDead) return;
            if (Application.isPlaying) ctx.SetUpdating(!ctx.IsUpdating);
            else CDSPExplorerReflection.TrySetBoolProp(ctx, "IsUpdating", !ctx.IsUpdating);
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

        void DrawPauseButton(Context ctx)
        {
            var content = new GUIContent(
                ctx.IsDead ? "✕" : (ctx.IsUpdating ? "II" : "▶"),
                ctx.IsDead ? "Context is dead" : (ctx.IsUpdating ? "Pause context (SetUpdating(false))" : "Resume context (SetUpdating(true))"));
            if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(30)))
                ToggleActive(ctx);
        }

        void DrawSelectButton(Context ctx)
        {
            var content = new GUIContent("○", "Select + ping GameObject in Hierarchy");
            EditorGUI.BeginDisabledGroup(ctx == null || ctx.Instance == null);
            if (GUILayout.Button(content, EditorStyles.miniButton, GUILayout.Width(26)))
                PingSelect(ctx);
            EditorGUI.EndDisabledGroup();
        }

        void PingSelect(Context ctx)
        {
            if (ctx == null || ctx.Instance == null) return;
            Selection.activeGameObject = ctx.Instance;
            EditorGUIUtility.PingObject(ctx.Instance);
        }

        void DrawRootsBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Roots:", EditorStyles.miniLabel, GUILayout.Width(44));
            var roots = Roots;
            if (roots.Count == 0)
            {
                GUILayout.Label("— none —", EditorStyles.miniLabel);
            }
            foreach (var r in roots)
            {
                if (r == null) continue;
                EditorGUI.BeginDisabledGroup(r.IsDead);
                var content = new GUIContent(
                    $"{(r.IsUpdating ? "II" : "▶")} {r.GetType().Name}",
                    r.IsUpdating ? "Pause root context (SetUpdating(false))" : "Resume root context (SetUpdating(true))");
                if (GUILayout.Button(content, EditorStyles.toolbarButton, GUILayout.MaxWidth(240)))
                    ToggleActive(r);
                EditorGUI.EndDisabledGroup();
            }
            EditorGUILayout.EndHorizontal();
        }

        void TrackDeaths()
        {
            double now = EditorApplication.timeSinceStartup;
            var seen = new HashSet<Context>();
            foreach (var r in Roots)
                TrackRecursive(r, null, now, seen);

            if (_prevChildren.Count > 2000) _prevChildren.Clear();
            if (_deadSince.Count > 2000)
            {
                foreach (var c in new List<Context>(_deadSince.Keys))
                {
                    if (_deadSince.Count <= 1500) break;
                    if (now - _deadSince[c] >= DeadGraceSeconds) { _deadSince.Remove(c); _deadParent.Remove(c); }
                }
            }
            if (!_showDeadChildren)
            {
                var expired = new List<Context>();
                foreach (var kv in _deadSince)
                    if (now - kv.Value >= DeadGraceSeconds) expired.Add(kv.Key);
                foreach (var c in expired) { _deadSince.Remove(c); _deadParent.Remove(c); }
            }
            var staleSnaps = new List<Context>();
            foreach (var k in _prevChildren.Keys)
                if (!seen.Contains(k)) staleSnaps.Add(k);
            foreach (var k in staleSnaps) _prevChildren.Remove(k);
            var goneBorn = new List<Context>();
            foreach (var c in _bornAt.Keys)
                if (!seen.Contains(c)) goneBorn.Add(c);
            foreach (var c in goneBorn) _bornAt.Remove(c);
            var goneStamped = new List<Context>();
            foreach (var c in _stampedDead)
                if (!seen.Contains(c)) goneStamped.Add(c);
            foreach (var c in goneStamped) _stampedDead.Remove(c);
            var goneIds = new List<Context>();
            foreach (var c in _contextIds.Keys)
                if (!seen.Contains(c) && !_deadSince.ContainsKey(c)) goneIds.Add(c);
            foreach (var c in goneIds) _contextIds.Remove(c);
            _firstTrack = false;
        }

        void TrackRecursive(Context ctx, Context parent, double now, HashSet<Context> seen)
        {
            if (ctx == null || seen.Contains(ctx)) return;
            seen.Add(ctx);
            if (ctx.IsDead && !_stampedDead.Contains(ctx))
            {
                _stampedDead.Add(ctx);
                _deadSince[ctx] = now;
                _deadParent[ctx] = parent;
            }
            if (_firstTrack) _bornAt[ctx] = now - FlashSeconds;
            else if (!_bornAt.ContainsKey(ctx)) _bornAt[ctx] = now;
            if (!_contextIds.ContainsKey(ctx)) _contextIds[ctx] = _nextContextId++;
            var live = CDSPExplorerReflection.GetChildren(ctx);
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
            foreach (var k in live)
                TrackRecursive(k, ctx, now, seen);
        }

        List<Context> GetDisplayChildren(Context ctx, double now)
        {
            var list = new List<Context>();
            var live = CDSPExplorerReflection.GetChildren(ctx);
            if (live != null) list.AddRange(live);
            foreach (var kv in _deadSince)
            {
                var dead = kv.Key;
                if (dead == null || list.Contains(dead)) continue;
                Context p = _deadParent.TryGetValue(dead, out var dp) ? dp : dead.Parent;
                if (p != ctx) continue;
                if (!_showDeadChildren && now - kv.Value >= DeadGraceSeconds) continue;
                list.Add(dead);
            }
            return list;
        }

        bool IsInGrace(Context ctx, double now)
        {
            if (!ctx.IsDead) return false;
            if (!_deadSince.TryGetValue(ctx, out var since)) return true;
            return now - since < DeadGraceSeconds;
        }

        float DeathRemaining(Context ctx, double now)
        {
            if (_deadSince.TryGetValue(ctx, out var since))
                return Mathf.Clamp01(1f - (float)((now - since) / DeadGraceSeconds));
            return 1f;
        }

        void DrawDeathBar(Rect rowRect, float remaining)
        {
            if (rowRect.width <= 0f || rowRect.height <= 0f) return;
            EditorGUI.DrawRect(new Rect(rowRect.x, rowRect.y, rowRect.width * remaining, rowRect.height), new Color(0.75f, 0.75f, 0.75f, 0.15f));
        }

        void DrawFooter()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("CDSP Explorer · ○ = select + ping · tick edits via private-setter reflection",
                EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Docs: Context.cs", EditorStyles.toolbarButton, GUILayout.Width(130)))
                SelectAssetByName("Context.cs");
            EditorGUILayout.EndHorizontal();
        }

        static void SelectAssetByName(string fileName)
        {
            var guids = AssetDatabase.FindAssets(System.IO.Path.GetFileNameWithoutExtension(fileName));
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (path.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    EditorGUIUtility.PingObject(Selection.activeObject);
                    return;
                }
            }
            ShowNotificationStatic("File not found: " + fileName);
        }

        static void ShowNotificationStatic(string text)
        {
            var w = GetWindow<CDSPExplorerWindow>();
            if (w != null) w.ShowNotification(new GUIContent(text));
        }
    }
}
#endif
