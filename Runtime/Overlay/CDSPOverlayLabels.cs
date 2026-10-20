using System.Collections.Generic;
using System.Text;

using CDSP.Base;

using UnityEngine;

namespace CDSP.Overlay
{
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CDSP_OVERLAY_IN_RELEASE
    public sealed partial class CDSPOverlay
    {
        public KeyCode LabelsKey = KeyCode.F2;
        public KeyCode LabelsQuickKey = KeyCode.F3;

        const int LabelsWindowId = 19872;
        const float LabelCacheTTL = 0.5f;

        bool _labelsWindowVisible;
        Rect _labelsRect = new Rect(940, 10, 420, 560);
        Vector2 _labelsScroll;

        bool LabelsEnabled { get; set; }

        bool _showBox = true;
        bool _showName = true;
        bool _showContextName;
        bool _showState;
        bool _showTimeScale;
        bool _showSignals;
        bool _showDomainFields = true;
        bool _showSystemFields;
        bool _showPresentationFields;

        bool _showPrivateLabels;
        bool _labelsShowDead;
        bool _labelsShowInactive;
        bool _labelsOnlyWithPresentation = true;
        bool _labelsOnlySelected;
        bool _highlightSelected = true;
        string _labelsSearch = "";

        bool _showParentLines = true;
        bool _fadeByMouse;
        float _depthContrast;
        int _maxDistance;

        int _maxLabels = 64;
        int _maxFieldsPerSource = 8;
        int _labelFontSize = 11;
        float _labelCardWidth = 140f;
        float _labelYOffset;

        GUIStyle _cardStyle;

        sealed class LabelCacheEntry
        {
            public double time;
            public string text = "";
            public int lineCount = 1;
            public List<CDSPOverlayReflection.FieldRow> dRows = new();
            public List<CDSPOverlayReflection.FieldRow> sRows = new();
            public List<CDSPOverlayReflection.FieldRow> pRows = new();
        }

        readonly Dictionary<Context, LabelCacheEntry> _labelCache = new();
        static readonly StringBuilder _sb = new StringBuilder(512);

        public static void ShowLabels() => EnsureExists().LabelsEnabled = true;
        public static void HideLabels() => EnsureExists().LabelsEnabled = false;
        public static void ToggleLabels() => EnsureExists().LabelsEnabled = !EnsureExists().LabelsEnabled;
        public static void ShowLabelsWindow() => EnsureExists()._labelsWindowVisible = true;

        void ClampLabelsWindow()
        {
            _labelsRect.width = Mathf.Clamp(_labelsRect.width, 300f, Screen.width - 10f);
            _labelsRect.height = Mathf.Clamp(_labelsRect.height, 220f, Screen.height - 10f);
            _labelsRect.x = Mathf.Clamp(_labelsRect.x, 0f, Screen.width - 60f);
            _labelsRect.y = Mathf.Clamp(_labelsRect.y, 0f, Screen.height - 40f);
        }

        void InitLabelStyles()
        {
            if (_cardStyle == null)
            {
                _cardStyle = new GUIStyle(GUI.skin.box)
                {
                    alignment = TextAnchor.UpperLeft,
                    padding = new RectOffset(7, 7, 5, 5),
                    wordWrap = true,
                    richText = true,
                    normal = { textColor = Color.white }
                };
            }
            _cardStyle.fontSize = Mathf.Clamp(_labelFontSize, 10, 16);
        }

        void PruneLabelCache()
        {
            if (_labelCache.Count == 0) return;
            if (_labelCache.Count > 400) { _labelCache.Clear(); return; }
            var alive = new HashSet<Context>(_cachedAll);
            var dead = new List<Context>();
            foreach (var k in _labelCache.Keys)
                if (k == null || !alive.Contains(k)) dead.Add(k);
            foreach (var k in dead) _labelCache.Remove(k);
        }

        static bool ToggleWide(bool v, string text)
        {
            return GUILayout.Toggle(v, text, GUILayout.Width(170), GUILayout.Height(20));
        }

        #region LABELS SETTINGS WINDOW

        void DrawLabelsWindow(int id)
        {
            Header($"Labels  ·  {(LabelsEnabled ? "ON" : "OFF")}");
            _labelsScroll = GUILayout.BeginScrollView(_labelsScroll, GUILayout.ExpandHeight(true));
            _sectionIndex = 200;

            GUILayout.BeginHorizontal(GUILayout.Height(26));
            bool en = GUILayout.Toggle(LabelsEnabled, LabelsEnabled ? "ENABLED (F3)" : "disabled (F3)", GUILayout.Height(22), GUILayout.ExpandWidth(true));
            if (en != LabelsEnabled) LabelsEnabled = en;
            if (GUILayout.Button("X", GUILayout.Width(30), GUILayout.Height(22))) _labelsWindowVisible = false;
            GUILayout.EndHorizontal();

            Section("Card content", () =>
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _showBox = ToggleWide(_showBox, "Box");
                _showName = ToggleWide(_showName, "Name");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _showContextName = ToggleWide(_showContextName, "Context name");
                _showState = ToggleWide(_showState, "State");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _showTimeScale = ToggleWide(_showTimeScale, "TimeScale");
                _showSignals = ToggleWide(_showSignals, "Signals");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _showDomainFields = ToggleWide(_showDomainFields, "Domain fields");
                _showSystemFields = ToggleWide(_showSystemFields, "System fields");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _showPresentationFields = ToggleWide(_showPresentationFields, "Presentation fields");
                _showPrivateLabels = ToggleWide(_showPrivateLabels, "Private fields");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _highlightSelected = ToggleWide(_highlightSelected, "Highlight selected");
                GUILayout.EndHorizontal();
            });

            Section("Filter", () =>
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _labelsShowDead = ToggleWide(_labelsShowDead, "Show dead");
                _labelsShowInactive = ToggleWide(_labelsShowInactive, "Show inactive");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _labelsOnlyWithPresentation = ToggleWide(_labelsOnlyWithPresentation, "Has presentation");
                _labelsOnlySelected = ToggleWide(_labelsOnlySelected, "Selected only");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal(GUILayout.Height(24));
                GUILayout.Label("Search", _dimStyle, GUILayout.Width(52));
                _labelsSearch = GUILayout.TextField(_labelsSearch, GUILayout.Height(22), GUILayout.ExpandWidth(true));
                if (GUILayout.Button("X", GUILayout.Width(28), GUILayout.Height(22))) _labelsSearch = "";
                GUILayout.EndHorizontal();
            });

            Section("Links & fade", () =>
            {
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                _showParentLines = ToggleWide(_showParentLines, "Parent-child lines");
                _fadeByMouse = ToggleWide(_fadeByMouse, "Fade by mouse");
                GUILayout.EndHorizontal();
                SliderRowF("Depth fade", ref _depthContrast, 0f, 2f, true);
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label("Max distance", _bodyStyle, GUILayout.Width(120));
                GUILayout.Label(_maxDistance <= 0 ? "off" : $"{_maxDistance}m", _dimStyle, GUILayout.Width(36));
                int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(_maxDistance, 0, 200, GUILayout.ExpandWidth(true)));
                if (nv != _maxDistance) _maxDistance = nv;
                GUILayout.EndHorizontal();
                GUILayout.Label("Depth fade is relative to the nearest label (it stays opaque). 0 = off.", _dimStyle);
            });

            Section("Size", () =>
            {
                SliderRow("Max labels", ref _maxLabels, 5, 100);
                SliderRow("Fields per source", ref _maxFieldsPerSource, 0, 8);
                SliderRow("Font", ref _labelFontSize, 10, 16);
                GUILayout.BeginHorizontal(GUILayout.Height(22));
                GUILayout.Label("Card width", _bodyStyle, GUILayout.Width(120));
                GUILayout.Label($"{_labelCardWidth:0}", _dimStyle, GUILayout.Width(36));
                _labelCardWidth = GUILayout.HorizontalSlider(_labelCardWidth, 100f, 340f, GUILayout.ExpandWidth(true));
                GUILayout.EndHorizontal();
                SliderRowF("Card Y offset", ref _labelYOffset, -120f, 120f);
            });

            Section("Info", () =>
            {
                KV("visible", $"{CountVisibleLabels()} / {_cachedAll.Count} contexts");
                KV("cache", $"{_labelCache.Count} cards");
                GUILayout.BeginHorizontal(GUILayout.Height(24));
                if (GUILayout.Button("Select none", GUILayout.Height(22))) _selected = null;
                if (GUILayout.Button("Open explorer (F1)", GUILayout.Height(22))) _explorerVisible = true;
                GUILayout.EndHorizontal();
                GUILayout.Label("Card text is above the box.\nClick a card or an object = select.\nF3 toggles labels with windows closed.", _dimStyle);
            });

            GUILayout.EndScrollView();
            GUI.DragWindow(new Rect(0, 0, _labelsRect.width, 26f));
        }

        void SliderRow(string label, ref int value, int min, int max)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label(label, _bodyStyle, GUILayout.Width(120));
            GUILayout.Label(value.ToString(), _dimStyle, GUILayout.Width(36));
            int nv = Mathf.RoundToInt(GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true)));
            if (nv != value) value = nv;
            GUILayout.EndHorizontal();
        }

        void SliderRowF(string label, ref float value, float min, float max, bool showOff = false)
        {
            GUILayout.BeginHorizontal(GUILayout.Height(22));
            GUILayout.Label(label, _bodyStyle, GUILayout.Width(120));
            GUILayout.Label(showOff && value <= 0.001f ? "off" : value.ToString("0.00"), _dimStyle, GUILayout.Width(36));
            float nv = GUILayout.HorizontalSlider(value, min, max, GUILayout.ExpandWidth(true));
            if (!Mathf.Approximately(nv, value)) value = nv;
            GUILayout.EndHorizontal();
        }

        #endregion

        #region SCREEN LABELS PASS

        struct LabelItem
        {
            public Context ctx;
            public Rect box;
            public Rect card;
            public float depth;
            public string text;
            public int lines;
            public float alpha;
        }

        bool LabelMatches(Context ctx)
        {
            if (string.IsNullOrEmpty(_labelsSearch)) return true;
            var s = _labelsSearch.Trim().ToLowerInvariant();
            if (ctx.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            if (ctx.Instance != null && ctx.Instance.name.ToLowerInvariant().Contains(s)) return true;
            var d = CDSPOverlayReflection.GetDomain(ctx);
            var sys = CDSPOverlayReflection.GetSystem(ctx);
            var p = CDSPOverlayReflection.GetPresentation(ctx);
            if (d != null && d.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            if (sys != null && sys.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            if (p != null && p.GetType().Name.ToLowerInvariant().Contains(s)) return true;
            return false;
        }

        int CountVisibleLabels()
        {
            int n = 0;
            foreach (var c in _cachedAll)
            {
                if (c == null || c.Instance == null) continue;
                if (c.IsDead && !_labelsShowDead) continue;
                if (!c.IsUpdating && !_labelsShowInactive) continue;
                if (_labelsOnlySelected && c != _selected) continue;
                n++;
            }
            return n;
        }

        int CompareNearFirst(LabelItem a, LabelItem b)
        {
            int c = a.depth.CompareTo(b.depth);
            if (c != 0) return c;
            c = TreeDepth(a.ctx).CompareTo(TreeDepth(b.ctx));
            if (c != 0) return c;
            return GetContextId(a.ctx).CompareTo(GetContextId(b.ctx));
        }

        int CompareFarFirst(LabelItem a, LabelItem b)
        {
            int c = b.depth.CompareTo(a.depth);
            if (c != 0) return c;
            c = TreeDepth(a.ctx).CompareTo(TreeDepth(b.ctx));
            if (c != 0) return c;
            return GetContextId(a.ctx).CompareTo(GetContextId(b.ctx));
        }

        void DrawScreenLabels()
        {
            var cam = ActiveCamera;
            if (cam == null || _cachedAll.Count == 0) return;

            var items = new List<LabelItem>(_maxLabels + 8);
            foreach (var ctx in _cachedAll)
            {
                if (ctx == null || ctx.Instance == null) continue;
                if (ctx.IsDead && !_labelsShowDead) continue;
                if (!ctx.IsUpdating && !_labelsShowInactive) continue;
                if (_labelsOnlySelected && ctx != _selected) continue;
                var pres = CDSPOverlayReflection.GetPresentation(ctx);
                if (_labelsOnlyWithPresentation && pres == null) continue;
                if (!LabelMatches(ctx)) continue;
                if (!TryGetScreenRect(ctx, cam, out var box, out float depth)) continue;
                if (IsCoveredByWindows(new Vector2(box.center.x, box.center.y))) continue;
                if (_maxDistance > 0 && depth > _maxDistance) continue;
                GetCardText(ctx, out var text, out var lines);
                items.Add(new LabelItem { ctx = ctx, box = box, depth = depth, text = text, lines = lines, alpha = 1f });
                if (items.Count >= _maxLabels * 2) break;
            }

            items.Sort(CompareNearFirst);
            if (items.Count > _maxLabels) items.RemoveRange(_maxLabels, items.Count - _maxLabels);
            if (items.Count == 0) return;

            float minD = items[0].depth, maxD = items[0].depth;
            foreach (var it in items)
            {
                if (it.depth < minD) minD = it.depth;
                if (it.depth > maxD) maxD = it.depth;
            }
            float depthRange = Mathf.Max(maxD - minD, 0.001f);
            Vector2 mouse = CDSPOverlayInput.MousePositionGUI();
            float mouseNorm = Mathf.Max(Mathf.Max(Screen.width, Screen.height) * 0.5f, 1f);
            float fadeBand = _maxDistance > 0 ? Mathf.Min(10f, _maxDistance * 0.25f) : 0f;

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                float a = 1f;
                if (_depthContrast > 0.001f)
                    a *= Mathf.Clamp01(1f - _depthContrast * (it.depth - minD) / depthRange);
                if (_fadeByMouse)
                {
                    float md = Vector2.Distance(it.box.center, mouse) / mouseNorm;
                    a *= Mathf.Clamp01(1f - md);
                }
                if (fadeBand > 0.001f && it.depth > _maxDistance - fadeBand)
                {
                    float t = Mathf.Clamp01((_maxDistance - it.depth) / fadeBand);
                    a *= t * t * (3f - 2f * t);
                }
                it.alpha = a;
                items[i] = it;
            }
            items.RemoveAll(it => it.alpha < 0.03f);
            if (items.Count == 0) return;
            items.Sort(CompareFarFirst);

            float lineH = _cardStyle.fontSize + 6f;
            float cardW = Mathf.Clamp(_labelCardWidth, 100f, 340f);
            var placed = new List<Rect>(items.Count);
            for (int k = items.Count - 1; k >= 0; k--)
            {
                var it = items[k];
                float cardH = it.lines * lineH + 10f;
                it.card = PlaceCard(AnchorCard(it.box, cardW, cardH), it.box, cardW, cardH, placed);
                items[k] = it;
                placed.Add(it.card);
            }

            foreach (var it in items)
            {
                var ctx = it.ctx;
                bool isSel = _selected == ctx;
                bool isFlash = _flashHighlight != null && _flashHighlight == ctx && Time.realtimeSinceStartup < _flashHighlightUntil;
                var state = StateColor(ctx);
                var card = it.card;
                bool cardAbove = card.center.y < it.box.center.y;

                if (_showParentLines)
                    DrawParentLine(ctx, it.box, cam, state, it.alpha * 0.85f);

                if (_showBox)
                    DrawOutline(it.box, WithAlpha(state, it.alpha), (isSel || isFlash) ? 3f : 1f);
                else if (isSel && _highlightSelected)
                    DrawOutline(it.box, WithAlpha(new Color(0.3f, 0.6f, 1f), it.alpha), 2f);

                if (_showBox)
                {
                    Vector2 cardPt = new Vector2(
                        Mathf.Clamp(it.box.center.x, card.xMin + 3f, card.xMax - 3f),
                        cardAbove ? card.yMax : card.yMin);
                    Vector2 boxPt = new Vector2(it.box.center.x, cardAbove ? it.box.yMin : it.box.yMax);
                    DrawElbow(cardPt, boxPt, WithAlpha(state, it.alpha));
                }

                var oldColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, it.alpha);
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = isSel
                    ? new Color(0.20f, 0.40f, 0.80f, 0.94f)
                    : ctx.IsDead
                        ? new Color(0.45f, 0.15f, 0.15f, 0.90f)
                        : new Color(0.05f, 0.06f, 0.085f, 0.88f);
                if (GUI.Button(card, it.text, _cardStyle))
                    _selected = ctx;
                GUI.backgroundColor = oldBg;
                GUI.color = oldColor;
                DrawOutline(card, WithAlpha(isSel ? new Color(0.45f, 0.70f, 1f) : state, it.alpha), 1f);
            }

            if (_highlightSelected && _selected != null && _selected.Instance != null)
            {
                bool drawn = false;
                foreach (var it in items) if (it.ctx == _selected) { drawn = true; break; }
                if (!drawn && TryGetScreenRect(_selected, cam, out var selBox, out _)
                    && !IsCoveredByWindows(selBox.center))
                    DrawOutline(selBox, new Color(0.3f, 0.6f, 1f), 3f);
            }
            if (_flashHighlight != null && _flashHighlight != _selected
                && Time.realtimeSinceStartup < _flashHighlightUntil
                && _flashHighlight.Instance != null
                && TryGetScreenRect(_flashHighlight, cam, out var flBox, out _)
                && !IsCoveredByWindows(flBox.center))
                DrawOutline(flBox, new Color(1f, 0.8f, 0.2f), 3f);
        }

        Rect AnchorCard(Rect box, float w, float h)
        {
            float maxX = Mathf.Max(4f, Screen.width - w - 4f);
            float maxY = Mathf.Max(4f, Screen.height - h - 4f);
            float cx = Mathf.Clamp(box.center.x - w * 0.5f, 4f, maxX);
            float cy = box.yMin - h - 6f;
            if (cy < 4f) cy = box.yMax + 6f;
            cy = Mathf.Clamp(cy + _labelYOffset, 4f, maxY);
            return new Rect(cx, cy, w, h);
        }

        static Rect ClampCard(float x, float y, float w, float h)
        {
            float maxX = Mathf.Max(4f, Screen.width - w - 4f);
            float maxY = Mathf.Max(4f, Screen.height - h - 4f);
            return new Rect(Mathf.Clamp(x, 4f, maxX), Mathf.Clamp(y, 4f, maxY), w, h);
        }

        static Rect PlaceCard(Rect anchor, Rect box, float w, float h, List<Rect> placed)
        {
            Rect best = anchor;
            float bestScore = ScoreCard(anchor, anchor, placed);
            Consider(anchor.x, anchor.y);
            for (int k = 1; k <= 6; k++)
            {
                Consider(anchor.x + 28f * k, anchor.y);
                Consider(anchor.x - 28f * k, anchor.y);
            }
            float belowY = box.yMax + 6f;
            float aboveY = box.yMin - h - 6f;
            for (int k = 0; k <= 3; k++)
            {
                Consider(anchor.x + 28f * k, belowY);
                Consider(anchor.x - 28f * k, belowY);
                Consider(anchor.x + 28f * k, aboveY);
                Consider(anchor.x - 28f * k, aboveY);
            }
            return best;

            void Consider(float x, float y)
            {
                Rect c = ClampCard(x, y, w, h);
                float s = ScoreCard(c, anchor, placed);
                if (s < bestScore) { bestScore = s; best = c; }
            }
        }

        static float ScoreCard(Rect c, Rect anchor, List<Rect> placed)
        {
            float overlap = 0f;
            foreach (var p in placed)
                overlap += OverlapArea(c, p);
            float dx = Mathf.Abs(c.center.x - anchor.center.x);
            float dy = Mathf.Abs(c.center.y - anchor.center.y) * 2f;
            return overlap * 0.1f + dx + dy;
        }

        static float OverlapArea(Rect a, Rect b)
        {
            float x0 = Mathf.Max(a.xMin, b.xMin), x1 = Mathf.Min(a.xMax, b.xMax);
            if (x1 <= x0) return 0f;
            float y0 = Mathf.Max(a.yMin, b.yMin), y1 = Mathf.Min(a.yMax, b.yMax);
            if (y1 <= y0) return 0f;
            return (x1 - x0) * (y1 - y0);
        }

        static Color WithAlpha(Color c, float a) => new Color(c.r, c.g, c.b, c.a * a);

        void DrawParentLine(Context ctx, Rect childBox, Camera cam, Color state, float alpha)
        {
            var parent = ctx.Parent;
            if (parent == null || parent.Instance == null) return;
            if (CDSPOverlayReflection.GetPresentation(parent) == null) return;
            Vector3 psp = cam.WorldToScreenPoint(parent.Instance.transform.position);
            if (psp.z <= 0 || !float.IsFinite(psp.x + psp.y + psp.z)) return;
            Vector2 pp = new Vector2(psp.x, Screen.height - psp.y);
            if (pp.x < -50f || pp.x > Screen.width + 50f || pp.y < -50f || pp.y > Screen.height + 50f) return;
            DrawLine(childBox.center, pp, WithAlpha(state, alpha), 1.5f);
        }

        void DrawLine(Vector2 a, Vector2 b, Color c, float width)
        {
            if (_texWhite == null) return;
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 1f) return;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            var oldM = GUI.matrix;
            var oldC = GUI.color;
            GUI.color = c;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _texWhite);
            GUI.matrix = oldM;
            GUI.color = oldC;
        }

        void DrawElbow(Vector2 cardPt, Vector2 boxPt, Color c)
        {
            if (_texWhite == null) return;
            if (Mathf.Abs(cardPt.x - boxPt.x) < 2f)
            {
                VSeg(cardPt.x, cardPt.y, boxPt.y, c);
                return;
            }
            float midY = (cardPt.y + boxPt.y) * 0.5f;
            VSeg(cardPt.x, cardPt.y, midY, c);
            HSeg(midY, cardPt.x, boxPt.x, c);
            VSeg(boxPt.x, midY, boxPt.y, c);
        }

        void VSeg(float x, float y0, float y1, Color c)
        {
            if (y1 < y0) { float t = y0; y0 = y1; y1 = t; }
            if (y1 - y0 < 0.5f) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x - 1f, y0, 2f, y1 - y0), _texWhite);
            GUI.color = old;
        }

        void HSeg(float y, float x0, float x1, Color c)
        {
            if (x1 < x0) { float t = x0; x0 = x1; x1 = t; }
            if (x1 - x0 < 0.5f) return;
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(new Rect(x0, y - 1f, x1 - x0, 2f), _texWhite);
            GUI.color = old;
        }

        void GetCardText(Context ctx, out string text, out int lines)
        {
            if (!_labelCache.TryGetValue(ctx, out var e) || e == null)
            {
                e = new LabelCacheEntry();
                _labelCache[ctx] = e;
            }
            if (Now - e.time < LabelCacheTTL && !string.IsNullOrEmpty(e.text))
            {
                text = e.text;
                lines = e.lineCount;
                return;
            }
            e.time = Now;

            var d = CDSPOverlayReflection.GetDomain(ctx);
            var sys = CDSPOverlayReflection.GetSystem(ctx);
            var p = CDSPOverlayReflection.GetPresentation(ctx);
            if (_showDomainFields && d != null)
                e.dRows = CDSPOverlayReflection.DumpMembers(d, _maxFieldsPerSource, _showPrivateLabels, true);
            else e.dRows.Clear();
            if (_showSystemFields && sys != null)
                e.sRows = CDSPOverlayReflection.DumpMembers(sys, _maxFieldsPerSource, _showPrivateLabels, true);
            else e.sRows.Clear();
            if (_showPresentationFields && p != null)
                e.pRows = CDSPOverlayReflection.DumpMembers(p, _maxFieldsPerSource, _showPrivateLabels, true, typeof(Presentation));
            else e.pRows.Clear();

            int sources = (_showDomainFields ? 1 : 0) + (_showSystemFields ? 1 : 0) + (_showPresentationFields ? 1 : 0);
            bool single = sources == 1;

            _sb.Length = 0;
            int n = 0;
            if (_showName || _showContextName)
            {
                if (_showContextName)
                {
                    _sb.Append("<b>").Append(ctx.GetType().Name).Append("</b>");
                    n++;
                    if (_showName)
                    {
                        _sb.Append('\n').Append(ctx.Instance != null ? Trunc(ctx.Instance.name, 26) : "<no GO>");
                        n++;
                    }
                }
                else
                {
                    _sb.Append("<b>").Append(ctx.Instance != null ? Trunc(ctx.Instance.name, 26) : "<no GO>").Append("</b>");
                    n++;
                }
            }
            if (_showState)
            {
                if (n > 0) _sb.Append('\n');
                string dot = ctx.IsDead ? "<color=#ff5a4e>■</color>"
                    : !ctx.IsUpdating ? "<color=#9aa0aa>○</color>"
                    : !ctx.IsStarted ? "<color=#ffd94d>◐</color>"
                    : "<color=#4ade80>●</color>";
                _sb.Append(dot).Append(' ')
                   .Append(ctx.IsDead ? "DEAD" : !ctx.IsUpdating ? "OFF" : !ctx.IsStarted ? "!started" : "active")
                   .Append("  T").Append(ctx.IsUpdateTick ? "1" : "0")
                   .Append(" F").Append(ctx.IsUpdateFixedTick ? "1" : "0")
                   .Append(" L").Append(ctx.IsUpdateLateTick ? "1" : "0");
                n++;
            }
            if (_showTimeScale)
            {
                if (n > 0) _sb.Append('\n');
                _sb.Append("ts ×").Append(ctx.TimeScale.ToString("0.00"));
                n++;
            }
            if (_showSignals)
            {
                if (n > 0) _sb.Append('\n');
                var signals = CDSPOverlayReflection.GetSignalInfo(ctx);
                _sb.Append(signals.hasSignals ? $"sig {signals.signalCount}" : "sig —");
                n++;
            }
            n += AppendRows(single ? "" : "D.", e.dRows, n);
            n += AppendRows(single ? "" : "S.", e.sRows, n);
            n += AppendRows(single ? "" : "P.", e.pRows, n);

            if (n == 0)
            {
                _sb.Append("<b>").Append(ctx.GetType().Name).Append("</b>");
                n = 1;
            }
            e.text = _sb.ToString();
            e.lineCount = n;
            text = e.text;
            lines = n;
        }

        static int AppendRows(string prefix, List<CDSPOverlayReflection.FieldRow> rows, int n)
        {
            int added = 0;
            foreach (var r in rows)
            {
                if (n + added > 0) _sb.Append('\n');
                _sb.Append(prefix).Append(r.name).Append(" = ").Append(Trunc(r.valueText, 30));
                added++;
            }
            return added;
        }

        static string Trunc(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s ?? "";
            return s.Substring(0, max) + "…";
        }

        #endregion
    }

#else

    public sealed partial class CDSPOverlay
    {
    }

#endif
}
