#if UNITY_EDITOR
using System;
using System.IO;
using System.Text.RegularExpressions;

using UnityEditor;

using UnityEngine;

namespace CDSP.Editor
{
    public static class CDSPFeatureCreator
    {
        const string MenuSplit = "Assets/Create/CDSP/CDSP Feature (4 files, like Calendar)...";
        const string MenuSingle = "Assets/Create/CDSP/CDSP Feature (single file)...";

        [MenuItem(MenuSplit, false, 0)]
        public static void CreateSplit()
        {
            CDSPFeatureNameWindow.Open(GetSelectedFolder(), false);
        }

        [MenuItem(MenuSingle, false, 1)]
        public static void CreateSingle()
        {
            CDSPFeatureNameWindow.Open(GetSelectedFolder(), true);
        }

        internal static string GetSelectedFolder()
        {
            foreach (var obj in Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets))
            {
                var path = AssetDatabase.GetAssetPath(obj);
                if (string.IsNullOrEmpty(path)) continue;
                if (Directory.Exists(path)) return path;
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) return dir.Replace('\\', '/');
            }
            return "Assets";
        }

        internal static string SanitizeFeature(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "";
            var name = raw.Trim();
            foreach (var suffix in new[] { "Context", "Domain", "System", "Presentation" })
            {
                if (name.Length > suffix.Length && name.EndsWith(suffix, StringComparison.Ordinal))
                {
                    name = name.Substring(0, name.Length - suffix.Length);
                    break;
                }
            }
            name = Regex.Replace(name, @"[^A-Za-z0-9_]", "");
            if (name.Length == 0 || char.IsDigit(name[0])) return "";
            return char.ToUpperInvariant(name[0]) + name.Substring(1);
        }

        internal static bool IsValidFeature(string name)
        {
            return !string.IsNullOrEmpty(name) && Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_]*$");
        }

        internal static string SanitizePart(string part)
        {
            if (string.IsNullOrWhiteSpace(part)) return "";
            var cleaned = Regex.Replace(part.Trim(), @"[^A-Za-z0-9_]", "_").Trim('_');
            if (cleaned.Length == 0) return "";
            if (char.IsDigit(cleaned[0])) cleaned = "_" + cleaned;
            return cleaned;
        }

        [Serializable]
        sealed class AsmdefDto
        {
            public string rootNamespace;
        }

        internal static string ResolveNamespace(string outDirAssetPath)
        {
            var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            var absolute = Path.Combine(projectRoot, outDirAssetPath).Replace('\\', '/');
            var dir = new DirectoryInfo(absolute);
            for (int i = 0; i < 30 && dir != null && dir.Exists; i++)
            {
                FileInfo[] asmdefs = null;
                try { asmdefs = dir.GetFiles("*.asmdef"); }
                catch { asmdefs = null; }
                if (asmdefs != null && asmdefs.Length > 0)
                {
                    Array.Sort(asmdefs, (a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
                    foreach (var asm in asmdefs)
                    {
                        try
                        {
                            var dto = JsonUtility.FromJson<AsmdefDto>(File.ReadAllText(asm.FullName));
                            var root = dto != null && dto.rootNamespace != null ? dto.rootNamespace.Trim() : "";
                            if (root.Length > 0)
                            {
                                var rel = GetRelativeParts(dir.FullName.Replace('\\', '/'), absolute);
                                var tail = rel.Length > 0 ? "." + string.Join(".", rel) : "";
                                return root + tail;
                            }
                        }
                        catch { }
                    }
                }
                dir = dir.Parent;
            }

            var parts = outDirAssetPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            int assetsIndex = Array.FindIndex(parts, p => p.Equals("Assets", StringComparison.Ordinal));
            if (assetsIndex >= 0)
            {
                var tail = "";
                for (int i = assetsIndex + 1; i < parts.Length; i++)
                {
                    var s = SanitizePart(parts[i]);
                    if (s.Length > 0) tail += (tail.Length > 0 ? "." : "") + s;
                }
                if (tail.Length > 0) return tail;
            }

            var fallback = SanitizePart(Path.GetFileName(outDirAssetPath.TrimEnd('/')));
            return fallback.Length > 0 ? fallback : "Game";
        }

        static string[] GetRelativeParts(string fromAbsolute, string toAbsolute)
        {
            var from = fromAbsolute.TrimEnd('/').Split('/');
            var to = toAbsolute.TrimEnd('/').Split('/');
            int common = 0;
            while (common < from.Length && common < to.Length && from[common] == to[common]) common++;
            var result = "";
            for (int i = common; i < to.Length; i++)
            {
                var s = SanitizePart(to[i]);
                if (s.Length == 0) continue;
                result += (result.Length > 0 ? "/" : "") + s;
            }
            return result.Length == 0 ? new string[0] : result.Split('/');
        }

        internal static string DomainTemplate(string ns, string name)
        {
            return "using CDSP.Base;\n\nnamespace " + ns + "\n{\n"
                + "    public class " + name + "Domain : Domain\n    {\n    }\n}\n";
        }

        internal static string PresentationTemplate(string ns, string name)
        {
            return "using CDSP.Base;\n\nnamespace " + ns + "\n{\n"
                + "    public class " + name + "Presentation : Presentation<" + name + "System>\n    {\n    }\n}\n";
        }

        internal static string SystemTemplate(string ns, string name)
        {
            return "using CDSP.Base;\n\nnamespace " + ns + "\n{\n"
                + "    public class " + name + "System : System<" + name + "Domain, " + name + "Presentation>\n    {\n    }\n}\n";
        }

        internal static string ContextTemplate(string ns, string name)
        {
            return "using CDSP.Base;\n\nnamespace " + ns + "\n{\n"
                + "    public class " + name + "Context : Context<" + name + "Domain, " + name + "System, " + name + "Presentation>\n    {\n    }\n}\n";
        }

        internal static string SingleFileTemplate(string ns, string name)
        {
            return "using CDSP.Base;\n\nnamespace " + ns + "\n{\n"
                + "    public class " + name + "Domain : Domain\n    {\n    }\n\n"
                + "    public class " + name + "Presentation : Presentation<" + name + "System>\n    {\n    }\n\n"
                + "    public class " + name + "System : System<" + name + "Domain, " + name + "Presentation>\n    {\n    }\n\n"
                + "    public class " + name + "Context : Context<" + name + "Domain, " + name + "System, " + name + "Presentation>\n    {\n    }\n}\n";
        }

        internal static bool CreateFeature(string parentAssetFolder, string feature, bool singleFile)
        {
            if (!IsValidFeature(feature))
            {
                EditorUtility.DisplayDialog("CDSP Feature",
                    "Invalid feature name '" + feature + "'. Use letters, digits and _ (must not start with a digit).", "OK");
                return false;
            }
            if (!parentAssetFolder.StartsWith("Assets", StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("CDSP Feature",
                    "Select a folder inside Assets/ first.", "OK");
                return false;
            }

            string[] paths;
            string[] bodies;
            string selectPath;
            if (singleFile)
            {
                var ns = ResolveNamespace(parentAssetFolder);
                selectPath = parentAssetFolder.TrimEnd('/') + "/" + feature + ".cs";
                paths = new[] { selectPath };
                bodies = new[] { SingleFileTemplate(ns, feature) };
            }
            else
            {
                var folder = parentAssetFolder.TrimEnd('/') + "/" + feature;
                var ns = ResolveNamespace(folder);
                selectPath = folder;
                paths = new[]
                {
                    folder + "/" + feature + "Domain.cs",
                    folder + "/" + feature + "Presentation.cs",
                    folder + "/" + feature + "System.cs",
                    folder + "/" + feature + "Context.cs",
                };
                bodies = new[]
                {
                    DomainTemplate(ns, feature),
                    PresentationTemplate(ns, feature),
                    SystemTemplate(ns, feature),
                    ContextTemplate(ns, feature),
                };
            }

            var projectRoot = Path.GetDirectoryName(Application.dataPath).Replace('\\', '/');
            foreach (var p in paths)
            {
                if (File.Exists(Path.Combine(projectRoot, p)))
                {
                    EditorUtility.DisplayDialog("CDSP Feature",
                        "Already exists:\n" + p + "\nDelete it first or pick another name.", "OK");
                    return false;
                }
            }

            for (int i = 0; i < paths.Length; i++)
            {
                var absolute = Path.Combine(projectRoot, paths[i]);
                Directory.CreateDirectory(Path.GetDirectoryName(absolute));
                File.WriteAllText(absolute, bodies[i]);
            }
            AssetDatabase.Refresh();
            var created = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(selectPath);
            if (created != null)
            {
                Selection.activeObject = created;
                EditorGUIUtility.PingObject(created);
            }
            return true;
        }
    }

    internal sealed class CDSPFeatureNameWindow : EditorWindow
    {
        string _parentFolder = "Assets";
        string _feature = "Inventory";
        bool _singleFile;

        public static void Open(string parentFolder, bool singleFile)
        {
            var w = CreateInstance<CDSPFeatureNameWindow>();
            w._parentFolder = string.IsNullOrEmpty(parentFolder) ? "Assets" : parentFolder;
            w._singleFile = singleFile;
            w.titleContent = new GUIContent(singleFile ? "CDSP Feature (1 file)" : "CDSP Feature (4 files)");
            w.minSize = new Vector2(440, 150);
            w.maxSize = new Vector2(600, 200);
            w.ShowUtility();
            w.Focus();
        }

        void OnGUI()
        {
            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("Parent folder: " + _parentFolder, EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            _feature = EditorGUILayout.TextField("Feature name", _feature ?? "");
            if (EditorGUI.EndChangeCheck())
            {
                _feature = CDSPFeatureCreator.SanitizeFeature(_feature);
            }

            var clean = CDSPFeatureCreator.SanitizeFeature(_feature);
            var outDir = _singleFile ? _parentFolder : _parentFolder.TrimEnd('/') + "/" + (clean.Length > 0 ? clean : "...");
            EditorGUILayout.LabelField("Namespace: " + CDSPFeatureCreator.ResolveNamespace(outDir), EditorStyles.miniLabel);
            EditorGUILayout.LabelField(
                _singleFile ? ("File: " + outDir.TrimEnd('/') + "/" + (clean.Length > 0 ? clean : "...") + ".cs")
                            : ("Folder: " + outDir + "  (Domain / Presentation / System / Context)"),
                EditorStyles.miniLabel);

            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();
            EditorGUI.BeginDisabledGroup(!CDSPFeatureCreator.IsValidFeature(clean));
            if (GUILayout.Button("Create", GUILayout.Width(90)))
            {
                if (CDSPFeatureCreator.CreateFeature(_parentFolder, clean, _singleFile)) Close();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
            {
                if (CDSPFeatureCreator.IsValidFeature(clean)
                    && CDSPFeatureCreator.CreateFeature(_parentFolder, clean, _singleFile)) Close();
                Event.current.Use();
            }
        }
    }
}
#endif
