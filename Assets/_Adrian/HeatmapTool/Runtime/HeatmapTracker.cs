using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HeatmapTool
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Heatmap/Heatmap Tracker")]
    public sealed class HeatmapTracker : MonoBehaviour
    {
        const float TeleportDistance = 10f;

        static readonly List<HeatmapTracker> active = new List<HeatmapTracker>();

        public static IReadOnlyList<HeatmapTracker> Active => active;

        [Header("When to record")]
        [SerializeField] bool recordInEditor = true;
        [SerializeField] bool recordInBuilds = true;
        [Tooltip("In builds, only record when 'Development Build' is ticked.")]
        [SerializeField] bool developmentBuildsOnly = false;

        [Header("Sampling")]
        [Tooltip("Seconds between position samples. 0.2 = five per second. Lower gives more detail and bigger files.")]
        [SerializeField, Range(0.05f, 2f)] float sampleInterval = 0.2f;
        [Tooltip("Stored with each session. Useful when you track more than one character.")]
        [SerializeField] string playerLabel = "Player";
        [Tooltip("Record the bottom of the collider instead of the transform pivot, so heatmaps sit on the floor " +
                 "whatever the pivot. Needs a CharacterController or Collider on this object.")]
        [SerializeField] bool recordFeetPosition = true;

        [Header("Movement detection")]
        [Tooltip("Horizontal speed (m/s) at or above which the player counts as sprinting. " +
                 "Ignored when a component on this object implements IHeatmapStateProvider.")]
        [SerializeField, Min(0f)] float sprintSpeed = 6.5f;
        [Tooltip("Horizontal speed (m/s) below which the player counts as idle.")]
        [SerializeField, Min(0f)] float idleSpeed = 0.25f;
        [Tooltip("How far below the feet still counts as grounded. Only used when there is no CharacterController " +
                 "or IHeatmapStateProvider.")]
        [SerializeField, Min(0f)] float groundCheckDistance = 0.15f;
        [SerializeField] LayerMask groundLayers = ~0;
        [Tooltip("Ignore time off the ground shorter than this. Filters out ground-check flicker on slopes and stairs.")]
        [SerializeField, Min(0f)] float minAirTime = 0.1f;
        [Tooltip("Record Jump, Fall and Land events automatically. Turn this off if your controller calls RecordEvent itself.")]
        [SerializeField] bool autoDetectJumps = true;
        [Tooltip("Upward speed (m/s) on leaving the ground that counts as a jump rather than a fall.")]
        [SerializeField, Min(0f)] float jumpUpwardSpeed = 1.5f;

        [Header("Saving")]
        [Tooltip("Seconds between writes to disk. Anything recorded since the last write is lost if the game crashes.")]
        [SerializeField, Range(1f, 120f)] float flushInterval = 10f;
        [Tooltip("Leave empty to use the project's save location: <persistentDataPath>/Heatmaps, or next to the .exe in " +
                 "builds when that is ticked in the Heatmap Viewer. Relative paths are relative to persistentDataPath.")]
        [SerializeField] string customFolder = "";
        [Tooltip("Sessions shorter than this many seconds are deleted when they end.")]
        [SerializeField, Min(0f)] float minSessionLength = 5f;

        [Header("Custom events")]
        [Tooltip("Record your own events without code: pick an object, then an event or value on one of its scripts.")]
        [SerializeField] List<HeatmapCustomEvent> customEvents = new List<HeatmapCustomEvent>();

        readonly List<HeatmapSample> pendingSamples = new List<HeatmapSample>(64);
        readonly List<HeatmapEvent> pendingEvents = new List<HeatmapEvent>(16);
        readonly Dictionary<string, int> eventIds = new Dictionary<string, int>();
        readonly RaycastHit[] groundHits = new RaycastHit[8];

        IHeatmapStateProvider provider;
        CharacterController characterController;
        Collider body;
        HeatmapSessionWriter writer;
        string sessionScenePath;
        float sessionStart;
        float nextSampleTime;
        float nextFlushTime;
        float lastSampleTime;
        Vector3 lastSamplePosition;
        Vector3 lastFramePosition;
        int sampleCount;
        int eventCount;
        bool customEventsStarted;

        bool wasGrounded;
        bool airborne;
        float leftGroundTime;
        Vector3 takeoffPosition;
        float maxUpwardSpeed;
        float peakHeight;

        public bool IsRecording => writer != null;
        public string CurrentFilePath => writer?.FilePath;
        public int SampleCount => sampleCount;
        public int EventCount => eventCount;
        public float SessionTime => IsRecording ? Time.time - sessionStart : 0f;
        public bool IsAirborne => airborne;
        public List<HeatmapCustomEvent> CustomEvents => customEvents;

        public string PlayerLabel
        {
            get => playerLabel;
            set => playerLabel = value;
        }

        #region Unity messages

        void OnEnable()
        {
            provider = GetComponent<IHeatmapStateProvider>();
            characterController = GetComponent<CharacterController>();
            body = characterController != null ? characterController : GetComponent<Collider>();

            active.Add(this);
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            RefreshRecordingState();
        }

        void OnDisable()
        {
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            active.Remove(this);
            EndSession();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused) Flush();
        }

        void OnApplicationQuit()
        {
            EndSession();
        }

        void LateUpdate()
        {
            if (writer == null || Time.deltaTime <= 0f) return;
            if (!customEventsStarted) StartCustomEvents();

            float now = Time.time;
            Vector3 position = GetTrackedPosition();

            if ((position - lastFramePosition).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                lastSamplePosition = position;
                wasGrounded = CheckGrounded();
                airborne = false;
            }
            else
            {
                UpdateAirState(position, now);
            }
            lastFramePosition = position;

            if (now >= nextSampleTime)
            {
                TakeSample(position, now);
                nextSampleTime += sampleInterval;
                if (nextSampleTime < now) nextSampleTime = now + sampleInterval;
            }

            for (int i = 0; i < customEvents.Count; i++) customEvents[i]?.Poll();

            if (writer != null && now >= nextFlushTime)
            {
                Flush();
                nextFlushTime = now + flushInterval;
            }
        }

        void OnActiveSceneChanged(Scene previous, Scene next)
        {
            if (writer == null || next.path == sessionScenePath) return;
            EndSession();
            RefreshRecordingState();
        }

        #endregion

        #region Public API

        public void RecordEvent(string eventName)
        {
            RecordEvent(eventName, 0f);
        }

        public void RecordEvent(string eventName, float value = 0f)
        {
            RecordEvent(eventName, GetTrackedPosition(), value);
        }

        public void RecordEvent(string eventName, Vector3 position, float value = 0f)
        {
            if (writer == null || string.IsNullOrEmpty(eventName)) return;
            AddEvent(eventName, position, value, Time.time);
        }

        public void Flush()
        {
            if (writer == null) return;
            try
            {
                if (pendingSamples.Count > 0) writer.WriteSamples(pendingSamples);
                if (pendingEvents.Count > 0) writer.WriteEvents(pendingEvents);
                writer.Flush();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Heatmap] Stopped recording, couldn't write to {writer.FilePath}: {e.Message}", this);
                CloseWriter();
            }
            pendingSamples.Clear();
            pendingEvents.Clear();
        }

        public void StartNewSession()
        {
            EndSession();
            RefreshRecordingState();
        }

        #endregion

        internal void RefreshRecordingState()
        {
            if (!isActiveAndEnabled) return;
            if (ShouldRecord()) BeginSession();
            else EndSession();
        }

        bool ShouldRecord()
        {
            if (!Heatmap.RecordingAllowed) return false;
            if (Application.isEditor) return recordInEditor;
            return recordInBuilds && (!developmentBuildsOnly || Debug.isDebugBuild);
        }

        void BeginSession()
        {
            if (writer != null) return;

            Scene scene = gameObject.scene;
            if (!scene.IsValid() || scene.name == "DontDestroyOnLoad") scene = SceneManager.GetActiveScene();

            var header = new HeatmapSession
            {
                sessionId = Guid.NewGuid().ToString("N"),
                sceneName = scene.name,
                scenePath = scene.path,
                playerLabel = playerLabel,
                startTicksUtc = DateTime.UtcNow.Ticks,
                appVersion = Application.version,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                fromEditor = Application.isEditor,
                sampleInterval = sampleInterval,
            };

            string fileName = $"{SafeFileName(scene.name)}_{DateTime.Now:yyyyMMdd_HHmmss}_{header.sessionId.Substring(0, 6)}"
                              + HeatmapIO.BinaryExtension;
            string folder = GetSaveFolder();
            writer = OpenWriter(folder, fileName, header);
            if (writer == null && folder != HeatmapIO.DefaultFolder)
            {
                Debug.LogWarning($"[Heatmap] Saving to {HeatmapIO.DefaultFolder} instead.", this);
                writer = OpenWriter(HeatmapIO.DefaultFolder, fileName, header);
            }
            if (writer == null) return;

            sessionScenePath = scene.path;
            pendingSamples.Clear();
            pendingEvents.Clear();
            eventIds.Clear();
            sampleCount = 0;
            eventCount = 0;
            customEventsStarted = false;

            sessionStart = Time.time;
            nextSampleTime = sessionStart;
            nextFlushTime = sessionStart + flushInterval;
            lastSampleTime = sessionStart;
            lastSamplePosition = lastFramePosition = GetTrackedPosition();
            wasGrounded = CheckGrounded();
            airborne = false;
        }

        void EndSession()
        {
            if (writer == null) return;

            StopCustomEvents();
            Flush();
            if (writer == null) return;

            string path = writer.FilePath;
            bool tooShort = sampleCount == 0 || Time.time - sessionStart < minSessionLength;
            CloseWriter();

            if (tooShort)
            {
                try { File.Delete(path); }
                catch (Exception) { }
            }
        }

        void StartCustomEvents()
        {
            customEventsStarted = true;
            for (int i = 0; i < customEvents.Count; i++) customEvents[i]?.Start(this);
        }

        void StopCustomEvents()
        {
            if (!customEventsStarted) return;
            customEventsStarted = false;
            for (int i = 0; i < customEvents.Count; i++) customEvents[i]?.Stop();
        }

        void CloseWriter()
        {
            try { writer?.Dispose(); }
            catch (Exception) { }
            writer = null;
        }

        HeatmapSessionWriter OpenWriter(string folder, string fileName, HeatmapSession header)
        {
            try
            {
                return new HeatmapSessionWriter(Path.Combine(folder, fileName), header);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Heatmap] Couldn't save to {folder}: {e.Message}", this);
                return null;
            }
        }

        string GetSaveFolder()
        {
            if (string.IsNullOrWhiteSpace(customFolder)) return HeatmapIO.SaveFolder;
            return Path.IsPathRooted(customFolder) ? customFolder : Path.Combine(Application.persistentDataPath, customFolder);
        }

        Vector3 GetTrackedPosition()
        {
            Vector3 position = transform.position;
            if (recordFeetPosition && body != null && body.enabled) position.y = body.bounds.min.y;
            return position;
        }

        void TakeSample(Vector3 position, float now)
        {
            float elapsed = now - lastSampleTime;
            Vector3 delta = position - lastSamplePosition;
            delta.y = 0f;
            float speed = elapsed > 0f ? delta.magnitude / elapsed : 0f;

            HeatmapMoveState state;
            if (airborne) state = HeatmapMoveState.Airborne;
            else if (speed < idleSpeed) state = HeatmapMoveState.Idle;
            else if (provider != null ? provider.IsSprinting : speed >= sprintSpeed) state = HeatmapMoveState.Sprinting;
            else state = HeatmapMoveState.Walking;

            pendingSamples.Add(new HeatmapSample { t = now - sessionStart, position = position, state = state });
            sampleCount++;
            lastSamplePosition = position;
            lastSampleTime = now;
        }

        void UpdateAirState(Vector3 position, float now)
        {
            bool grounded = CheckGrounded();
            float verticalSpeed = (position.y - lastFramePosition.y) / Time.deltaTime;

            if (wasGrounded && !grounded)
            {
                leftGroundTime = now;
                takeoffPosition = lastFramePosition;
                maxUpwardSpeed = verticalSpeed;
                peakHeight = position.y;
            }
            wasGrounded = grounded;

            if (!grounded)
            {
                peakHeight = Mathf.Max(peakHeight, position.y);
                if (airborne) return;

                maxUpwardSpeed = Mathf.Max(maxUpwardSpeed, verticalSpeed);
                if (now - leftGroundTime < minAirTime) return;

                airborne = true;
                if (autoDetectJumps)
                {
                    string name = maxUpwardSpeed >= jumpUpwardSpeed ? HeatmapEvents.Jump : HeatmapEvents.Fall;
                    AddEvent(name, takeoffPosition, 0f, leftGroundTime);
                }
            }
            else if (airborne)
            {
                airborne = false;
                if (autoDetectJumps) AddEvent(HeatmapEvents.Land, position, peakHeight - position.y, now);
            }
        }

        bool CheckGrounded()
        {
            if (provider != null) return provider.IsGrounded;
            if (characterController != null && characterController.enabled) return characterController.isGrounded;

            Vector3 origin;
            float radius, distance;
            if (body != null && body.enabled)
            {
                Bounds bounds = body.bounds;
                radius = Mathf.Max(0.01f, Mathf.Min(bounds.extents.x, bounds.extents.z) * 0.9f);
                origin = bounds.center;
                distance = Mathf.Max(0f, bounds.extents.y - radius) + groundCheckDistance;
            }
            else
            {
                radius = 0.05f;
                origin = transform.position + Vector3.up * 0.1f;
                distance = 0.05f + groundCheckDistance;
            }

            int count = Physics.SphereCastNonAlloc(origin, radius, Vector3.down, groundHits, distance, groundLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (!groundHits[i].collider.transform.IsChildOf(transform)) return true;
            }
            return false;
        }

        void AddEvent(string eventName, Vector3 position, float value, float time)
        {
            if (!eventIds.TryGetValue(eventName, out int id))
            {
                id = eventIds.Count;
                eventIds.Add(eventName, id);
                writer.WriteEventName(id, eventName);
            }
            pendingEvents.Add(new HeatmapEvent { t = time - sessionStart, position = position, type = id, value = value });
            eventCount++;
        }

        static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Untitled";
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name;
        }
    }
}
