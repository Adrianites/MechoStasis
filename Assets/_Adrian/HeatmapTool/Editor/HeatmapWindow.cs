using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace HeatmapTool
{
    public sealed class HeatmapWindow : EditorWindow
    {
        const float RebuildDelay = 0.15f;
        const double LiveRefreshInterval = 2.0;
        const string LastFolderKey = "HeatmapTool.LastFolder";

        static readonly GUIContent[] SourceLabels =
        {
            new GUIContent("Time Spent", "Where players spent their time."),
            new GUIContent("Events", "Where jumps, falls, landings and your own events happened."),
        };

        [Serializable]
        sealed class SessionEntry
        {
            public string path;
            public int indexInFile;
            public bool enabled = true;
        }

        [Serializable]
        sealed class Settings
        {
            public HeatmapQuery query = new HeatmapQuery();
            public HeatmapAppearance look = new HeatmapAppearance();
            public float opacity = 0.8f;
            public bool seeThroughWalls;
            public bool showHeat = true;
            public bool showPaths;
            public HeatmapPathColors pathColors = HeatmapPathColors.ByMovementState;
            public float pathBreakDistance = 8f;
            public bool showMarkers;
            public float markerSize = 0.25f;
            public bool currentSceneOnly = true;
            public bool watchDefaultFolder = true;
            public bool liveUpdate = true;
            public List<string> watchedFolders = new List<string>();
            public List<string> ignoredSessions = new List<string>();
            public List<SessionEntry> sessions = new List<SessionEntry>();
            public bool foldSessions = true, foldRecording = true, foldData = true, foldDisplay = true, foldLayers = true;
        }

        sealed class LoadedSession
        {
            public SessionEntry entry;
            public HeatmapSession data;
        }

        Settings settings;
        HeatmapSettings projectSettings;
        readonly List<LoadedSession> loaded = new List<LoadedSession>();
        readonly List<LoadedSession> shown = new List<LoadedSession>();
        readonly List<HeatmapSession> shownData = new List<HeatmapSession>();
        readonly Dictionary<string, DateTime> fileStamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        readonly HeatmapGrid grid = new HeatmapGrid();
        HeatmapView view;
        Texture2D legendTexture;
        string[] eventChoices = { "All events" };
        float longestSession;
        float colorScaleMax;
        int drawnCells;
        double buildMilliseconds;
        bool rebuildQueued;
        double rebuildAt;
        double nextLiveRefresh;
        Vector2 scroll, sessionScroll;
        string status = "";

        static string PrefsKey => "HeatmapTool.Viewer." + PlayerSettings.productGUID;

        static string LastFolder
        {
            get => EditorPrefs.GetString(LastFolderKey, HeatmapIO.DefaultFolder);
            set => EditorPrefs.SetString(LastFolderKey, value);
        }

        #region Menu items

        [MenuItem("Tools/Heatmap/Heatmap Viewer", priority = 0)]
        public static void Open()
        {
            GetWindow<HeatmapWindow>("Heatmap").Show();
        }

        [MenuItem("Tools/Heatmap/Open Data Folder", priority = 20)]
        public static void OpenDataFolder()
        {
            RevealFolder(HeatmapIO.DefaultFolder);
        }

        [MenuItem("Tools/Heatmap/Add Tracker to Selected", priority = 40)]
        static void AddTrackerToSelection()
        {
            foreach (GameObject go in Selection.gameObjects)
                if (go.GetComponent<HeatmapTracker>() == null) Undo.AddComponent<HeatmapTracker>(go);
        }

        [MenuItem("Tools/Heatmap/Add Tracker to Selected", true)]
        static bool CanAddTrackerToSelection()
        {
            return Selection.gameObjects.Length > 0;
        }

        #endregion

        #region Lifecycle

        void OnEnable()
        {
            titleContent = new GUIContent("Heatmap");
            minSize = new Vector2(340f, 320f);

            LoadSettings();
            projectSettings = FindProjectSettings();
            loaded.Clear();
            foreach (SessionEntry entry in settings.sessions) loaded.Add(new LoadedSession { entry = entry });
            fileStamps.Clear();
            Refresh(scanFolders: true, force: true);

            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            SceneManager.activeSceneChanged += OnSceneChanged;
            QueueRebuild(0f);
        }

        void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            SceneManager.activeSceneChanged -= OnSceneChanged;

            SaveSettings();
            DisposeView();
            if (legendTexture != null) DestroyImmediate(legendTexture);
        }

        void OnEditorUpdate()
        {
            double now = EditorApplication.timeSinceStartup;
            if (settings.liveUpdate && EditorApplication.isPlaying && !EditorApplication.isPaused && now >= nextLiveRefresh)
            {
                nextLiveRefresh = now + LiveRefreshInterval;
                if (RefreshLiveSessions()) QueueRebuild(0f);
            }
            if (rebuildQueued && now >= rebuildAt) Rebuild();
        }

        void OnPlayModeChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                case PlayModeStateChange.ExitingPlayMode:
                    DisposeView();
                    break;
                case PlayModeStateChange.EnteredPlayMode:
                    QueueRebuild(0f);
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    Refresh(scanFolders: true, force: false);
                    QueueRebuild(0f);
                    break;
            }
        }

        void OnSceneChanged(Scene previous, Scene next)
        {
            QueueRebuild(0f);
        }

        void OnProjectChange()
        {
            if (projectSettings == null) projectSettings = FindProjectSettings();
        }

        void DisposeView()
        {
            view?.Dispose();
            view = null;
        }

        void LoadSettings()
        {
            settings = null;
            string json = EditorPrefs.GetString(PrefsKey, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { settings = JsonUtility.FromJson<Settings>(json); }
                catch (Exception) { }
            }

            settings ??= new Settings();
            settings.query ??= new HeatmapQuery();
            settings.look ??= new HeatmapAppearance();
            settings.look.gradient ??= HeatmapAppearance.DefaultGradient();
            settings.watchedFolders ??= new List<string>();
            settings.ignoredSessions ??= new List<string>();
            settings.sessions ??= new List<SessionEntry>();
        }

        void SaveSettings()
        {
            if (settings != null) EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(settings));
        }

        #endregion

        #region Loading

        IEnumerable<string> WatchedFolders()
        {
            if (settings.watchDefaultFolder) yield return HeatmapIO.DefaultFolder;
            if (projectSettings != null && projectSettings.saveBuildsNextToExecutable)
            {
                string buildFolder = LastBuildHeatmapFolder();
                if (buildFolder != null) yield return buildFolder;
            }
            foreach (string folder in settings.watchedFolders) yield return folder;
        }

        bool Refresh(bool scanFolders, bool force)
        {
            bool changed = false;
            var readThisPass = new Dictionary<string, List<HeatmapSession>>(StringComparer.OrdinalIgnoreCase);

            for (int i = loaded.Count - 1; i >= 0; i--)
            {
                LoadedSession item = loaded[i];
                string path = item.entry.path;
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    RemoveAt(i);
                    changed = true;
                    continue;
                }

                if (!readThisPass.TryGetValue(path, out List<HeatmapSession> sessions))
                {
                    sessions = ReadIfChanged(path, force || item.data == null);
                    readThisPass[path] = sessions;
                }

                if (sessions == null)
                {
                    if (item.data == null)
                    {
                        RemoveAt(i);
                        changed = true;
                    }
                    continue;
                }

                if (item.entry.indexInFile < sessions.Count) item.data = sessions[item.entry.indexInFile];
                else RemoveAt(i);
                changed = true;
            }

            if (scanFolders)
            {
                foreach (string folder in WatchedFolders())
                foreach (string file in HeatmapIO.FindFiles(folder))
                {
                    if (fileStamps.ContainsKey(file)) continue;
                    List<HeatmapSession> sessions = ReadIfChanged(file, true);
                    if (sessions != null && AddSessions(file, sessions, respectIgnored: true) > 0) changed = true;
                }
            }

            if (changed) SaveSettings();
            return changed;
        }

        bool RefreshLiveSessions()
        {
            bool changed = false;
            foreach (HeatmapTracker tracker in HeatmapTracker.Active)
            {
                if (!tracker.IsRecording) continue;
                string path = Path.GetFullPath(tracker.CurrentFilePath);
                List<HeatmapSession> sessions = ReadIfChanged(path, true);
                if (sessions == null || sessions.Count == 0) continue;

                LoadedSession existing = loaded.FirstOrDefault(l => SamePath(l.entry.path, path));
                if (existing != null) existing.data = sessions[0];
                else AddSessions(path, sessions, respectIgnored: true);
                changed = true;
            }
            return changed;
        }

        List<HeatmapSession> ReadIfChanged(string path, bool force)
        {
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            if (!force && fileStamps.TryGetValue(path, out DateTime known) && known == stamp) return null;
            fileStamps[path] = stamp;

            try
            {
                return HeatmapIO.Load(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Heatmap] Couldn't read {Path.GetFileName(path)}: {e.Message}");
                return null;
            }
        }

        int AddSessions(string path, List<HeatmapSession> sessions, bool respectIgnored)
        {
            int added = 0;
            for (int i = 0; i < sessions.Count; i++)
            {
                HeatmapSession session = sessions[i];
                if (respectIgnored && settings.ignoredSessions.Contains(session.sessionId)) continue;
                settings.ignoredSessions.Remove(session.sessionId);
                if (loaded.Any(l => l.data != null && l.data.sessionId == session.sessionId)) continue;

                var entry = new SessionEntry { path = path, indexInFile = i };
                settings.sessions.Add(entry);
                loaded.Add(new LoadedSession { entry = entry, data = session });
                added++;
            }
            return added;
        }

        void ImportPaths(IEnumerable<string> paths)
        {
            int added = 0, files = 0;
            foreach (string raw in paths)
            {
                string path = Path.GetFullPath(raw);
                if (Directory.Exists(path))
                {
                    if (!settings.watchedFolders.Any(f => SamePath(f, path))) settings.watchedFolders.Add(path);
                    foreach (string file in HeatmapIO.FindFiles(path))
                    {
                        added += ImportFile(file);
                        files++;
                    }
                }
                else if (HeatmapIO.IsSupportedFile(path))
                {
                    added += ImportFile(path);
                    files++;
                }
            }

            status = $"Imported {added} new session{(added == 1 ? "" : "s")} from {files} file{(files == 1 ? "" : "s")}.";
            SaveSettings();
            QueueRebuild(0f);
        }

        int ImportFile(string path)
        {
            List<HeatmapSession> sessions = ReadIfChanged(path, true);
            return sessions == null ? 0 : AddSessions(path, sessions, respectIgnored: false);
        }

        void RemoveAt(int index)
        {
            settings.sessions.Remove(loaded[index].entry);
            loaded.RemoveAt(index);
        }

        void RemoveSession(LoadedSession item, bool deleteFile)
        {
            string path = item.entry.path;
            if (deleteFile)
            {
                int inFile = loaded.Count(l => SamePath(l.entry.path, path));
                string message = inFile > 1
                    ? $"Delete {Path.GetFileName(path)}?\n\nIt holds {inFile} sessions, which will all be removed. This can't be undone."
                    : $"Delete {Path.GetFileName(path)}?\n\nThis can't be undone.";
                if (!EditorUtility.DisplayDialog("Delete heatmap file", message, "Delete", "Cancel")) return;

                try
                {
                    File.Delete(path);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Heatmap] Couldn't delete {path}: {e.Message}");
                    return;
                }

                for (int i = loaded.Count - 1; i >= 0; i--)
                    if (SamePath(loaded[i].entry.path, path)) RemoveAt(i);
                fileStamps.Remove(path);
            }
            else
            {
                settings.ignoredSessions.Add(item.data.sessionId);
                int index = loaded.IndexOf(item);
                if (index >= 0) RemoveAt(index);
            }

            SaveSettings();
            QueueRebuild(0f);
        }

        #endregion

        #region Building

        void QueueRebuild(float delay = RebuildDelay)
        {
            rebuildQueued = true;
            rebuildAt = EditorApplication.timeSinceStartup + delay;
        }

        void Rebuild()
        {
            rebuildQueued = false;
            var timer = Stopwatch.StartNew();

            CollectShown();
            grid.Build(shownData, settings.query);

            view ??= new HeatmapView();
            drawnCells = HeatmapMeshBuilder.BuildHeat(view.HeatMesh, grid, settings.look);
            colorScaleMax = grid.GetPercentileValue(settings.look.clampPercentile / 100f);

            if (settings.showMarkers) HeatmapMeshBuilder.BuildMarkers(view.MarkerMesh, shownData, settings.query, settings.markerSize);
            else view.MarkerMesh.Clear();

            if (settings.showPaths)
                HeatmapMeshBuilder.BuildPaths(view.PathMesh, shownData, settings.query, settings.pathColors, settings.pathBreakDistance);
            else view.PathMesh.Clear();

            view.SetOpacity(settings.opacity);
            view.SetSeeThrough(settings.seeThroughWalls);
            view.SetLayersVisible(settings.showHeat, settings.showMarkers, settings.showPaths);

            buildMilliseconds = timer.Elapsed.TotalMilliseconds;
            UpdateLegendTexture();
            InternalEditorUtility.RepaintAllViews();
        }

        void CollectShown()
        {
            shown.Clear();
            shownData.Clear();
            longestSession = 0f;
            var names = new SortedSet<string>(StringComparer.Ordinal);

            foreach (LoadedSession item in loaded)
            {
                if (item.data == null || !item.entry.enabled || !PassesSceneFilter(item.data)) continue;
                shown.Add(item);
                shownData.Add(item.data);
                longestSession = Mathf.Max(longestSession, item.data.Duration);
                foreach (string name in item.data.eventNames)
                    if (!string.IsNullOrEmpty(name)) names.Add(name);
            }

            eventChoices = new[] { "All events" }.Concat(names).ToArray();
        }

        bool PassesSceneFilter(HeatmapSession session)
        {
            if (!settings.currentSceneOnly) return true;
            Scene scene = SceneManager.GetActiveScene();
            if (!string.IsNullOrEmpty(scene.path) && !string.IsNullOrEmpty(session.scenePath))
                return string.Equals(scene.path, session.scenePath, StringComparison.OrdinalIgnoreCase);
            return session.sceneName == scene.name;
        }

        void UpdateLegendTexture()
        {
            if (legendTexture == null)
            {
                legendTexture = new Texture2D(128, 1, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    wrapMode = TextureWrapMode.Clamp,
                };
            }

            var pixels = new Color[legendTexture.width];
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = settings.look.gradient.Evaluate(i / (pixels.Length - 1f));
                c.a = 1f;
                pixels[i] = c;
            }
            legendTexture.SetPixels(pixels);
            legendTexture.Apply();
        }

        #endregion

        #region GUI

        void OnGUI()
        {
            HandleDragAndDrop();
            DrawToolbar();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUI.BeginChangeCheck();
            DrawSessionsSection();
            DrawRecordingSection();
            DrawDataSection();
            DrawDisplaySection();
            DrawLayersSection();
            if (EditorGUI.EndChangeCheck()) QueueRebuild();
            DrawSummary();
            EditorGUILayout.EndScrollView();

            if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.miniLabel);
        }

        void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (EditorGUILayout.DropdownButton(new GUIContent("Import"), FocusType.Passive, EditorStyles.toolbarDropDown))
                    ShowImportMenu(GUILayoutUtility.GetLastRect());

                if (GUILayout.Button(new GUIContent("Refresh", "Reload sessions from disk and look for new files in watched folders."),
                        EditorStyles.toolbarButton))
                {
                    Refresh(scanFolders: true, force: true);
                    QueueRebuild(0f);
                }

                if (EditorGUILayout.DropdownButton(new GUIContent("Export"), FocusType.Passive, EditorStyles.toolbarDropDown))
                    ShowExportMenu(GUILayoutUtility.GetLastRect());

                GUILayout.FlexibleSpace();

                if (GUILayout.Button(new GUIContent("Data Folder", HeatmapIO.DefaultFolder), EditorStyles.toolbarButton))
                    RevealFolder(HeatmapIO.DefaultFolder);
            }
        }

        void DrawSessionsSection()
        {
            settings.foldSessions = EditorGUILayout.BeginFoldoutHeaderGroup(settings.foldSessions, $"Sessions ({shown.Count} shown)");
            if (settings.foldSessions)
            {
                if (loaded.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        "No recordings yet.\n\n" +
                        "1. Select your player and use Tools > Heatmap > Add Tracker to Selected.\n" +
                        "2. Press Play and move around for a bit.\n" +
                        "3. Stop. The session appears here.\n\n" +
                        "From a build: drag its .heatmap files (or a whole folder) onto this window, or use Import.",
                        MessageType.Info);
                }
                else
                {
                    DrawSessionList();
                }
                DrawWatchedFolders();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawSessionList()
        {
            var rows = new List<LoadedSession>();
            foreach (LoadedSession item in loaded)
                if (item.data != null && PassesSceneFilter(item.data)) rows.Add(item);
            int hidden = loaded.Count - rows.Count;

            using (new EditorGUILayout.HorizontalScope())
            {
                settings.currentSceneOnly = EditorGUILayout.ToggleLeft(
                    new GUIContent("Current scene only", "Only list sessions recorded in the scene that is open now."),
                    settings.currentSceneOnly, GUILayout.Width(140f));
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("All", EditorStyles.miniButtonLeft, GUILayout.Width(36f))) SetEnabled(rows, true);
                if (GUILayout.Button("None", EditorStyles.miniButtonRight, GUILayout.Width(40f))) SetEnabled(rows, false);
            }

            if (rows.Count == 0)
            {
                EditorGUILayout.HelpBox("None of the loaded sessions were recorded in this scene. " +
                                        "Untick 'Current scene only' to see them all.", MessageType.None);
                return;
            }

            float rowHeight = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            float height = Mathf.Min(rows.Count, 8) * rowHeight + 8f;
            sessionScroll = EditorGUILayout.BeginScrollView(sessionScroll, EditorStyles.helpBox, GUILayout.Height(height));
            foreach (LoadedSession item in rows) DrawSessionRow(item);
            EditorGUILayout.EndScrollView();

            if (hidden > 0)
                EditorGUILayout.LabelField($"{hidden} more from other scenes hidden.", EditorStyles.miniLabel);
        }

        void DrawSessionRow(LoadedSession item)
        {
            HeatmapSession s = item.data;
            using (new EditorGUILayout.HorizontalScope())
            {
                item.entry.enabled = EditorGUILayout.Toggle(item.entry.enabled, GUILayout.Width(16f));

                string label = $"{s.StartTimeUtc.ToLocalTime():dd MMM HH:mm}   {FormatDuration(s.Duration)}   " +
                               $"{(s.fromEditor ? "Editor" : "Build")}";
                if (!settings.currentSceneOnly) label += $"   {s.sceneName}";
                if (!string.IsNullOrEmpty(s.playerLabel) && s.playerLabel != "Player") label += $"   {s.playerLabel}";
                EditorGUILayout.LabelField(new GUIContent(label, SessionTooltip(item)), GUILayout.MinWidth(60f));

                if (GUILayout.Button(new GUIContent("...", "Session options"), EditorStyles.miniButton, GUILayout.Width(28f)))
                    ShowSessionMenu(item);
            }
        }

        void DrawWatchedFolders()
        {
            if (settings.watchedFolders.Count == 0) return;

            EditorGUILayout.LabelField(new GUIContent("Watched folders", "New files in these folders are picked up on Refresh."),
                EditorStyles.miniBoldLabel);
            int remove = -1;
            for (int i = 0; i < settings.watchedFolders.Count; i++)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    string folder = settings.watchedFolders[i];
                    EditorGUILayout.LabelField(new GUIContent(folder, folder), EditorStyles.miniLabel, GUILayout.MinWidth(60f));
                    if (GUILayout.Button(new GUIContent("x", "Stop watching this folder"), EditorStyles.miniButton, GUILayout.Width(22f)))
                        remove = i;
                }
            }

            if (remove >= 0)
            {
                settings.watchedFolders.RemoveAt(remove);
                SaveSettings();
            }
        }

        void DrawRecordingSection()
        {
            settings.foldRecording = EditorGUILayout.BeginFoldoutHeaderGroup(settings.foldRecording, "Recording");
            if (settings.foldRecording)
            {
                bool nextToExe = projectSettings != null && projectSettings.saveBuildsNextToExecutable;
                bool wanted = EditorGUILayout.Toggle(new GUIContent("Save Builds Next To .exe",
                    "Builds save sessions to a Heatmaps folder beside the game's .exe (beside the .app on macOS) instead " +
                    "of persistentDataPath, so testers can find and send them easily. If that folder can't be written " +
                    "to (e.g. inside Program Files) they fall back to persistentDataPath. Editor recordings are unaffected."),
                    nextToExe);
                if (wanted != nextToExe) SetSaveBuildsNextToExecutable(wanted);

                if (!wanted)
                {
                    EditorGUILayout.LabelField("Editor and builds save to the Data Folder (persistentDataPath/Heatmaps).",
                        EditorStyles.wordWrappedMiniLabel);
                }
                else
                {
                    BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
                    if (!IsDesktopTarget(target))
                    {
                        EditorGUILayout.HelpBox($"{target} builds have no .exe folder, so they keep saving to persistentDataPath.",
                            MessageType.Warning);
                    }
                    if (projectSettings != null && !IsLoadableAtRuntime(projectSettings))
                    {
                        EditorGUILayout.HelpBox($"Builds can't find {AssetDatabase.GetAssetPath(projectSettings)}. It must be " +
                                                $"named {HeatmapSettings.ResourceName} and sit directly in a Resources folder.",
                            MessageType.Error);
                    }

                    EditorGUILayout.LabelField("Builds save to a Heatmaps folder next to the .exe. Editor sessions still go " +
                                               "to the Data Folder.", EditorStyles.wordWrappedMiniLabel);

                    string buildFolder = LastBuildHeatmapFolder();
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(buildFolder != null
                                ? new GUIContent($"Last build: {buildFolder}", "Watched automatically: sessions from your latest build appear on Refresh.")
                                : new GUIContent("No build yet. Your next build's folder will be watched automatically."),
                            EditorStyles.wordWrappedMiniLabel, GUILayout.MinWidth(60f));
                        if (buildFolder != null && Directory.Exists(buildFolder)
                            && GUILayout.Button("Open", EditorStyles.miniButton, GUILayout.Width(44f)))
                            RevealFolder(buildFolder);
                    }
                }
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void SetSaveBuildsNextToExecutable(bool value)
        {
            if (projectSettings == null) projectSettings = CreateProjectSettings();
            Undo.RecordObject(projectSettings, "Save Builds Next To .exe");
            projectSettings.saveBuildsNextToExecutable = value;
            EditorUtility.SetDirty(projectSettings);
            AssetDatabase.SaveAssetIfDirty(projectSettings);

            Refresh(scanFolders: true, force: false);
            QueueRebuild(0f);
        }

        void DrawDataSection()
        {
            settings.foldData = EditorGUILayout.BeginFoldoutHeaderGroup(settings.foldData, "What to Show");
            if (settings.foldData)
            {
                HeatmapQuery q = settings.query;
                q.source = (HeatmapSource)GUILayout.Toolbar((int)q.source, SourceLabels);
                if (q.source == HeatmapSource.TimeSpent) DrawStateToggles(q);
                else DrawEventPicker(q);
                DrawTimeWindow(q);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        static void DrawStateToggles(HeatmapQuery q)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Movement", "Which movement states count. The button colours are also the path colours."));
                for (int i = 0; i < 4; i++)
                {
                    var state = (HeatmapMoveState)i;
                    var flag = (HeatmapStateMask)(1 << i);
                    bool on = (q.states & flag) != 0;
                    GUIStyle style = i == 0 ? EditorStyles.miniButtonLeft : i == 3 ? EditorStyles.miniButtonRight : EditorStyles.miniButtonMid;

                    Color oldColor = GUI.backgroundColor;
                    if (on) GUI.backgroundColor = HeatmapMeshBuilder.StateColor(state);
                    bool now = GUILayout.Toggle(on, state.ToString(), style);
                    GUI.backgroundColor = oldColor;

                    if (now != on) q.states = now ? q.states | flag : q.states & ~flag;
                }
            }

            if (q.states == 0) EditorGUILayout.HelpBox("Pick at least one movement state.", MessageType.Warning);
        }

        void DrawEventPicker(HeatmapQuery q)
        {
            string[] choices = eventChoices;
            int index = string.IsNullOrEmpty(q.eventName) ? 0 : Array.IndexOf(choices, q.eventName);
            if (index < 0)
            {
                choices = choices.Append(q.eventName).ToArray();
                index = choices.Length - 1;
            }

            index = EditorGUILayout.Popup("Event", index, choices);
            q.eventName = index <= 0 ? "" : choices[index];

            if (eventChoices.Length == 1)
            {
                EditorGUILayout.HelpBox("The shown sessions have no events. Jump, Fall and Land are recorded automatically; " +
                                        "log your own with Heatmap.LogEvent(\"Death\", position).", MessageType.Info);
            }
        }

        void DrawTimeWindow(HeatmapQuery q)
        {
            q.useTimeRange = EditorGUILayout.Toggle(
                new GUIContent("Limit Time", "Only use part of each session, e.g. the first two minutes of play."), q.useTimeRange);
            if (!q.useTimeRange) return;

            float longest = Mathf.Max(1f, Mathf.Ceil(longestSession));
            float min = Mathf.Clamp(q.timeMin, 0f, longest);
            float max = Mathf.Clamp(q.timeMax, min, longest);
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.MinMaxSlider(new GUIContent($"{FormatClock(min)} to {FormatClock(max)}"), ref min, ref max, 0f, longest);
            }
            q.timeMin = Mathf.Round(min);
            q.timeMax = Mathf.Round(max);
        }

        void DrawDisplaySection()
        {
            settings.foldDisplay = EditorGUILayout.BeginFoldoutHeaderGroup(settings.foldDisplay, "Display");
            if (settings.foldDisplay)
            {
                HeatmapQuery q = settings.query;
                HeatmapAppearance look = settings.look;

                look.style = (HeatmapStyle)EditorGUILayout.EnumPopup(new GUIContent("Style",
                    "Tiles: flat squares on the floor.\nCubes: one block per cell.\nColumns: bar height shows the value."), look.style);
                q.cellSize = EditorGUILayout.Slider(new GUIContent("Cell Size (m)",
                    "Size of each cell in metres. Smaller shows more detail but needs more data."), q.cellSize, 0.25f, 10f);
                q.flatten = EditorGUILayout.Toggle(new GUIContent("Flatten Heights",
                    "Merge all heights into one layer. Good for single-floor maps; leave off for multi-storey levels."), q.flatten);
                q.smoothing = EditorGUILayout.IntSlider(new GUIContent("Smoothing",
                    "Blurs heat into neighbouring cells along the floor. Helps when there isn't much data."), q.smoothing, 0, 5);

                EditorGUILayout.Space(2f);
                look.gradient = EditorGUILayout.GradientField(new GUIContent("Colours", "Cold to hot. Alpha sets transparency."), look.gradient);
                look.scale = (HeatmapScale)EditorGUILayout.EnumPopup(new GUIContent("Scale",
                    "Logarithmic brings out quiet areas when a few spots are extremely hot."), look.scale);
                look.clampPercentile = EditorGUILayout.Slider(new GUIContent("Hottest At Percentile",
                    "Cells at or above this percentile get the hottest colour. Lower it if one spot (like the spawn) " +
                    "makes everything else look cold."), look.clampPercentile, 50f, 100f);
                look.hideBelow = EditorGUILayout.Slider(new GUIContent("Hide Below",
                    "Hide cells colder than this fraction of the colour scale."), look.hideBelow, 0f, 1f);
                settings.opacity = EditorGUILayout.Slider("Opacity", settings.opacity, 0.05f, 1f);
                look.cellFill = EditorGUILayout.Slider(new GUIContent("Cell Fill", "Below 1 leaves a gap between cells."), look.cellFill, 0.1f, 1f);
                if (look.style == HeatmapStyle.Columns)
                {
                    look.columnHeight = EditorGUILayout.Slider(new GUIContent("Column Height",
                        "Height of the hottest column, in cells."), look.columnHeight, 0.5f, 20f);
                }
                settings.seeThroughWalls = EditorGUILayout.Toggle(new GUIContent("See Through Walls",
                    "Draw on top of level geometry so heat behind walls and under floors stays visible."), settings.seeThroughWalls);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawLayersSection()
        {
            settings.foldLayers = EditorGUILayout.BeginFoldoutHeaderGroup(settings.foldLayers, "Layers");
            if (settings.foldLayers)
            {
                settings.showHeat = EditorGUILayout.Toggle("Heatmap", settings.showHeat);

                settings.showPaths = EditorGUILayout.Toggle(new GUIContent("Player Paths",
                    "A line along each player's route, coloured by movement state or by session."), settings.showPaths);
                if (settings.showPaths)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        settings.pathColors = (HeatmapPathColors)EditorGUILayout.EnumPopup("Colour By", settings.pathColors);
                        settings.pathBreakDistance = Mathf.Max(0.5f, EditorGUILayout.FloatField(new GUIContent("Break Gaps Over (m)",
                            "Don't join samples further apart than this, e.g. across a respawn."), settings.pathBreakDistance));
                    }
                }

                settings.showMarkers = EditorGUILayout.Toggle(new GUIContent("Event Markers",
                    "A diamond at every event, coloured by event type."), settings.showMarkers);
                if (settings.showMarkers)
                {
                    using (new EditorGUI.IndentLevelScope())
                    {
                        settings.markerSize = EditorGUILayout.Slider("Marker Size", settings.markerSize, 0.05f, 2f);
                        for (int i = 1; i < eventChoices.Length; i++) DrawColorLabel(HeatmapMeshBuilder.EventColor(eventChoices[i]), eventChoices[i]);
                    }
                }

                settings.liveUpdate = EditorGUILayout.Toggle(new GUIContent("Live Update In Play Mode",
                    "Re-read the session being recorded while playing. New data appears each time the tracker " +
                    "flushes (its Flush Interval)."), settings.liveUpdate);
            }
            EditorGUILayout.EndFoldoutHeaderGroup();
        }

        void DrawSummary()
        {
            EditorGUILayout.Space();
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool time = grid.Source == HeatmapSource.TimeSpent;

                Rect bar = GUILayoutUtility.GetRect(0f, 12f, GUILayout.ExpandWidth(true));
                if (legendTexture != null) GUI.DrawTexture(bar, legendTexture, ScaleMode.StretchToFill);
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("0", EditorStyles.miniLabel, GUILayout.Width(30f));
                    GUILayout.FlexibleSpace();
                    string top = time ? FormatDuration(colorScaleMax) : colorScaleMax.ToString("N0");
                    EditorGUILayout.LabelField($"{top}+", Styles.RightMiniLabel, GUILayout.Width(90f));
                }

                EditorGUILayout.LabelField(time
                    ? $"Total time {FormatDuration(grid.TotalValue)}   ·   hottest cell {FormatDuration(grid.MaxValue)}"
                    : $"{grid.TotalValue:N0} events   ·   busiest cell {grid.MaxValue:N0}", EditorStyles.miniLabel);
                EditorGUILayout.LabelField(
                    $"{drawnCells:N0} of {grid.Cells.Count:N0} cells drawn   ·   {shownData.Count} session{(shownData.Count == 1 ? "" : "s")}" +
                    $"   ·   built in {buildMilliseconds:0.0} ms", EditorStyles.miniLabel);

                using (new EditorGUI.DisabledScope(grid.Cells.Count == 0))
                {
                    if (GUILayout.Button("Frame in Scene View")) Frame(grid.Bounds);
                }
            }
        }

        static void DrawColorLabel(Color color, string label)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(EditorGUI.indentLevel * 15f);
                Rect swatch = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10f), GUILayout.Height(10f));
                swatch.y += 3f;
                EditorGUI.DrawRect(swatch, color);
                GUILayout.Label(label, EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
            }
        }

        void HandleDragAndDrop()
        {
            Event e = Event.current;
            if (e.type != EventType.DragUpdated && e.type != EventType.DragPerform) return;

            string[] paths = DragAndDrop.paths;
            if (paths == null || !paths.Any(p => Directory.Exists(p) || HeatmapIO.IsSupportedFile(p))) return;

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            if (e.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                ImportPaths(paths);
            }
            e.Use();
        }

        #endregion

        #region Menus

        void ShowImportMenu(Rect rect)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("File..."), false, () =>
            {
                string path = EditorUtility.OpenFilePanelWithFilters("Import heatmap file", LastFolder,
                    new[] { "Heatmap data", "heatmap,json", "All files", "*" });
                if (string.IsNullOrEmpty(path)) return;
                LastFolder = Path.GetDirectoryName(path);
                ImportPaths(new[] { path });
            });
            menu.AddItem(new GUIContent("Folder (and watch it for new files)..."), false, () =>
            {
                string folder = EditorUtility.OpenFolderPanel("Import heatmap folder", LastFolder, "");
                if (string.IsNullOrEmpty(folder)) return;
                LastFolder = folder;
                ImportPaths(new[] { folder });
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Watch Default Data Folder"), settings.watchDefaultFolder, () =>
            {
                settings.watchDefaultFolder = !settings.watchDefaultFolder;
                Refresh(scanFolders: true, force: false);
                QueueRebuild(0f);
            });
            if (settings.ignoredSessions.Count > 0)
            {
                menu.AddItem(new GUIContent($"Restore {settings.ignoredSessions.Count} Removed Sessions"), false, () =>
                {
                    settings.ignoredSessions.Clear();
                    fileStamps.Clear();
                    Refresh(scanFolders: true, force: false);
                    QueueRebuild(0f);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Restore Removed Sessions"));
            }
            menu.DropDown(rect);
        }

        void ShowExportMenu(Rect rect)
        {
            var menu = new GenericMenu();
            int count = shown.Count;
            if (count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No sessions shown"));
            }
            else
            {
                string what = count == 1 ? "1 shown session" : $"{count} shown sessions";
                menu.AddItem(new GUIContent($"{what} as .heatmap files..."), false, ExportBinary);
                menu.AddItem(new GUIContent($"{what} as one .json file..."), false, ExportJson);
                menu.AddItem(new GUIContent($"{what} as .csv (raw samples and events)..."), false, ExportCsv);
                menu.AddSeparator("");
                menu.AddItem(new GUIContent("Current heatmap cells as .csv..."), false, ExportCells);
            }
            menu.DropDown(rect);
        }

        void ShowSessionMenu(LoadedSession item)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Show Only This"), false, () =>
            {
                foreach (LoadedSession l in loaded) l.entry.enabled = l == item;
                QueueRebuild(0f);
            });
            menu.AddItem(new GUIContent("Frame in Scene View"), false, () => Frame(SessionBounds(item.data)));
            menu.AddItem(new GUIContent("Show File"), false, () => EditorUtility.RevealInFinder(item.entry.path));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove from List"), false, () => RemoveSession(item, deleteFile: false));
            menu.AddItem(new GUIContent("Delete File..."), false, () => RemoveSession(item, deleteFile: true));
            menu.ShowAsContext();
        }

        void ExportBinary()
        {
            string folder = EditorUtility.SaveFolderPanel("Export sessions as .heatmap files", LastFolder, "");
            if (string.IsNullOrEmpty(folder)) return;
            LastFolder = folder;

            int written = 0;
            foreach (LoadedSession item in shown)
            {
                bool isBinary = string.Equals(Path.GetExtension(item.entry.path), HeatmapIO.BinaryExtension, StringComparison.OrdinalIgnoreCase);
                string name = isBinary ? Path.GetFileName(item.entry.path) : item.data.sessionId + HeatmapIO.BinaryExtension;
                string target = Path.Combine(folder, name);
                if (SamePath(target, item.entry.path)) continue;

                try
                {
                    HeatmapIO.SaveBinary(item.data, target);
                    written++;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Heatmap] Couldn't export {name}: {e.Message}");
                }
            }

            status = $"Exported {written} session{(written == 1 ? "" : "s")} to {folder}";
            RevealFolder(folder);
        }

        void ExportJson()
        {
            string path = EditorUtility.SaveFilePanel("Export sessions as JSON", LastFolder, "heatmap_sessions", "json");
            if (string.IsNullOrEmpty(path)) return;
            LastFolder = Path.GetDirectoryName(path);
            RunExport(path, () => HeatmapIO.SaveJson(shownData, path));
        }

        void ExportCsv()
        {
            string path = EditorUtility.SaveFilePanel("Export sessions as CSV", LastFolder, "heatmap_samples", "csv");
            if (string.IsNullOrEmpty(path)) return;
            LastFolder = Path.GetDirectoryName(path);
            RunExport(path, () => HeatmapIO.SaveCsv(shownData, path));
        }

        void ExportCells()
        {
            string path = EditorUtility.SaveFilePanel("Export heatmap cells as CSV", LastFolder, "heatmap_cells", "csv");
            if (string.IsNullOrEmpty(path)) return;
            LastFolder = Path.GetDirectoryName(path);
            RunExport(path, () => HeatmapIO.SaveCellsCsv(grid, path));
        }

        void RunExport(string path, Action export)
        {
            try
            {
                export();
                status = $"Exported {Path.GetFileName(path)}";
                EditorUtility.RevealInFinder(path);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Heatmap] Export failed: {e.Message}");
            }
        }

        #endregion

        #region Helpers

        void SetEnabled(List<LoadedSession> rows, bool enabled)
        {
            foreach (LoadedSession item in rows) item.entry.enabled = enabled;
            QueueRebuild(0f);
        }

        static string SessionTooltip(LoadedSession item)
        {
            HeatmapSession s = item.data;
            return $"Scene: {s.scenePath}\n" +
                   $"Player: {s.playerLabel}\n" +
                   $"Recorded: {s.StartTimeUtc.ToLocalTime():f} in {(s.fromEditor ? "the Editor" : "a build")} ({s.platform})\n" +
                   $"Game version: {s.appVersion}   Unity {s.unityVersion}\n" +
                   $"{s.samples.Length:N0} samples every {s.sampleInterval:0.##}s, {s.events.Length:N0} events\n" +
                   $"File: {item.entry.path}";
        }

        static Bounds SessionBounds(HeatmapSession session)
        {
            if (session.samples.Length == 0) return default;
            var bounds = new Bounds(session.samples[0].position, Vector3.one);
            foreach (HeatmapSample sample in session.samples) bounds.Encapsulate(sample.position);
            return bounds;
        }

        static void Frame(Bounds bounds)
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || bounds.size == Vector3.zero) return;
            sceneView.Frame(bounds, false);
            sceneView.Focus();
        }

        static HeatmapSettings FindProjectSettings()
        {
            HeatmapSettings fallback = null;
            foreach (string guid in AssetDatabase.FindAssets("t:" + nameof(HeatmapSettings)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<HeatmapSettings>(AssetDatabase.GUIDToAssetPath(guid));
                if (asset == null) continue;
                if (IsLoadableAtRuntime(asset)) return asset;
                if (fallback == null) fallback = asset;
            }
            return fallback;
        }

        static HeatmapSettings CreateProjectSettings()
        {
            var asset = CreateInstance<HeatmapSettings>();
            string scriptPath = AssetDatabase.GetAssetPath(MonoScript.FromScriptableObject(asset));
            string parent = string.IsNullOrEmpty(scriptPath) ? "Assets" : Path.GetDirectoryName(scriptPath).Replace('\\', '/');
            string resources = parent + "/Resources";
            if (!AssetDatabase.IsValidFolder(resources)) AssetDatabase.CreateFolder(parent, "Resources");
            AssetDatabase.CreateAsset(asset, $"{resources}/{HeatmapSettings.ResourceName}.asset");
            return asset;
        }

        static bool IsLoadableAtRuntime(HeatmapSettings asset)
        {
            string path = AssetDatabase.GetAssetPath(asset);
            return Path.GetFileNameWithoutExtension(path) == HeatmapSettings.ResourceName
                   && Path.GetFileName(Path.GetDirectoryName(path)) == "Resources";
        }

        static bool IsDesktopTarget(BuildTarget target)
        {
            return target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64
                || target == BuildTarget.StandaloneOSX || target == BuildTarget.StandaloneLinux64;
        }

        static string LastBuildHeatmapFolder()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            if (!IsDesktopTarget(target)) return null;
            string location = EditorUserBuildSettings.GetBuildLocation(target);
            if (string.IsNullOrEmpty(location)) return null;

            string folder = Path.GetDirectoryName(Path.GetFullPath(location.TrimEnd('/', '\\')));
            return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, HeatmapIO.FolderName);
        }

        internal static void RevealFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Application.OpenURL(new Uri(Path.GetFullPath(folder)).AbsoluteUri);
        }

        static bool SamePath(string a, string b)
        {
            return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
        }

        internal static string FormatDuration(float seconds)
        {
            if (seconds < 60f) return $"{seconds:0.0}s";
            var span = TimeSpan.FromSeconds(seconds);
            if (span.TotalHours >= 1) return $"{(int)span.TotalHours}h {span.Minutes:00}m";
            return $"{span.Minutes}m {span.Seconds:00}s";
        }

        static string FormatClock(float seconds)
        {
            var span = TimeSpan.FromSeconds(seconds);
            return span.TotalHours >= 1 ? span.ToString(@"h\:mm\:ss") : span.ToString(@"m\:ss");
        }

        static class Styles
        {
            public static readonly GUIStyle RightMiniLabel = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
        }

        #endregion
    }
}
