using System.Collections.Generic;
using UnityEngine;

namespace Levelstep
{
    /// <summary>
    /// Central, asset-driven settings for the Levelstep climbing setup.
    /// Swap models / rig / layout here and re-run the builder tools (Tools/Levelstep/...)
    /// to regenerate hold prefabs, the climbing wall prefab and the test scene.
    /// Nothing about the climbing logic is hard coded against a specific mesh.
    /// </summary>
    [CreateAssetMenu(fileName = "ClimbingConfig", menuName = "Levelstep/Climbing Config")]
    public class LevelstepClimbingConfig : ScriptableObject
    {
        public const string DefaultConfigPath = "Assets/Levelstep/Settings/ClimbingConfig.asset";

        [Header("Models")]
        [Tooltip("Wall model used when building the Climbing Wall prefab and the test scene. Any FBX or prefab works.")]
        public string wallModelPath = "Assets/Levelstep/Models/ClimbingWall.fbx";

        [Tooltip("Pool of hold models. One climb-ready hold prefab is generated per entry. Add more entries to introduce variety.")]
        public List<string> holdModelPaths = new List<string> { "Assets/Levelstep/Models/sm_holdBM_1.fbx" };

        [Header("Rig")]
        [Tooltip("Rig prefab that provides locomotion (move, turn, teleport, climb provider). Default supports controllers AND hand tracking.")]
        public string rigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        [Header("Hold generation")]
        [Tooltip("Use a convex mesh collider for holds. Convex colliders are robust for arbitrary hold shapes.")]
        public bool holdMeshColliderConvex = true;

        [Tooltip("Orientation tuning applied after aligning the hold to the wall surface normal (degrees).")]
        public float holdYawOffset = 0f;
        public float holdPitchOffset = 0f;
        public float holdRollOffset = 0f;

        [Header("Wall & hold placement")]
        [Tooltip("Number of hold columns across the wall width.")]
        public int columns = 4;

        [Tooltip("Number of hold rows up the wall.")]
        public int rows = 9;

        [Tooltip("Distance between hold columns (metres).")]
        public float columnSpacing = 2.3f;

        [Tooltip("Distance between hold rows (metres).")]
        public float rowSpacing = 0.8f;

        [Tooltip("Height of the lowest row above the wall base (metres).")]
        public float startHeight = 0.55f;

        [Tooltip("Sideways offset applied to alternating rows so holds stagger (metres).")]
        public float staggerOffset = 1.15f;

        [Tooltip("How far to lift each hold off the wall surface (metres) to avoid z-fighting.")]
        public float surfaceOffset = 0.08f;

        [Tooltip("Wall transform applied when built into the test scene.")]
        public Vector3 wallPosition = Vector3.zero;
        public float wallYaw = 0f;

        [Header("Test scene arena")]
        [Tooltip("Adds a ground plane, directional light and skybox around the wall.")]
        public bool buildArena = true;

        [Tooltip("Ground plane size in metres.")]
        public float arenaSize = 30f;

        [Header("Outputs")]
        public string holdPrefabFolder = "Assets/Levelstep/Prefabs";
        public string wallPrefabPath = "Assets/Levelstep/Prefabs/Climbing Wall.prefab";
        public string scenePath = "Assets/Levelstep/Scenes/ClimbingScene.unity";
        public List<string> holdPrefabPaths = new List<string>();

        [System.Serializable]
        public sealed class DifficultyPreset
        {
            public string name = "Medium";
            public Color routeColor = Color.yellow;
            public float maxStamina = 100f;
            public float drainRate = 40f;
            public float regenRate = 45f;
            public float regenDelay = 0.6f;
            public float minStaminaToGrip = 20f;

            public DifficultyPreset() { }

            public DifficultyPreset(string presetName, Color color, float max, float drain, float regen, float delay, float minGrip)
            {
                name = presetName;
                routeColor = color;
                maxStamina = max;
                drainRate = drain;
                regenRate = regen;
                regenDelay = delay;
                minStaminaToGrip = minGrip;
            }
        }

        [Header("Hand detection")]
        [Tooltip("Interactor anchor name keywords used to tell which hand grabbed a hold.")]
        public string leftHandKeyword = "Left";
        public string rightHandKeyword = "Right";

        [Header("Climb stamina")]
        [Tooltip("Starting stamina and the maximum each hand can hold (scaled to 0-100).")]
        public float maxStamina = 100f;

        [Tooltip("Stamina drained per second while a hand is gripping a hold.")]
        public float drainRate = 40f;

        [Tooltip("Stamina regenerated per second while a hand is NOT gripping (the free hand regenerates even while the other hand climbs).")]
        public float regenRate = 45f;

        [Tooltip("Time after releasing before regeneration starts.")]
        public float regenDelay = 0.6f;

        [Tooltip("Stamina level a hand must regenerate to before it can grab a hold again after exhaustion.")]
        public float minStaminaToGrip = 20f;

        [Tooltip("Send a haptic buzz when a hand runs out of stamina. Best effort on the active controller.")]
        public bool hapticsEnabled = true;
        public float hapticAmplitude = 0.6f;
        public float hapticDuration = 0.15f;

        [Header("Per-hand stamina bar")]
        [Tooltip("Show a small world-space stamina bar above each hand.")]
        public bool showStaminaBars = true;
        public Vector2 barSize = new Vector2(0.12f, 0.015f);
        public Vector3 barOffset = new Vector3(0f, 0.08f, 0f);
        [Tooltip("Fill colour of the stamina bar. Stays constant while draining; only the fill amount decreases (the hands redden, not the bar).")]
        public Color barFillColor = new Color(0.42f, 0.9f, 0.65f);
        public Color barBackColor = new Color(0f, 0f, 0f, 0.45f);
        public float barFollowSmoothing = 12f;

        [Header("Hand feedback")]
        [Tooltip("Tint the hand/controller renderers red as stamina depletes.")]
        public bool tintHands = true;
        public float handTintMax = 0.85f;
        [Tooltip("Colour the hands tint toward as stamina depletes (default red).")]
        public Color handTintColor = new Color(1f, 0.2f, 0.2f);

        [Header("Difficulty presets")]
        [Tooltip("Difficulty profiles. Harder difficulties drain stamina faster. Later, a floating menu in the test scene lets the player switch these at any time.")]
        public List<DifficultyPreset> difficultyPresets = new List<DifficultyPreset>
        {
            new DifficultyPreset("Easy", new Color(0.25f, 1f, 0.3f), 120f, 25f, 60f, 0.4f, 15f),
            new DifficultyPreset("Medium", new Color(1f, 0.82f, 0f), 100f, 40f, 45f, 0.6f, 20f),
            new DifficultyPreset("Hard", new Color(1f, 0.25f, 0.25f), 80f, 60f, 30f, 0.8f, 30f)
        };

        [Tooltip("Which difficulty preset the stamina system starts with before the player selects one.")]
        public int defaultDifficultyIndex = 1;

        [Header("Coloured routes")]
        [Tooltip("Number of coloured climb routes across the wall. Each hold is assigned to one route at build time.")]
        public int routeCount = 3;

        [Tooltip("Route assignment pattern: 0 = diagonal stripes (row + col mod routeCount), 1 = whole columns.")]
        public int routePattern = 0;

        [Tooltip("Opacity of holds belonging to the currently selected difficulty's route.")]
        public float fullOpacity = 1f;

        [Tooltip("Opacity of holds on non-selected routes.")]
        public float fadedOpacity = 0.25f;

        [Tooltip("Start each play with the last difficulty chosen (persisted in PlayerPrefs).")]
        public bool rememberDifficulty = true;

        [Header("Difficulty menu")]
        [Tooltip("Floating menu position in the test scene (world space).")]
        public Vector3 menuPosition = new Vector3(-1.6f, 1.4f, 1.0f);

        [Tooltip("Size of each difficulty selection block.")]
        public Vector3 menuBlockScale = new Vector3(0.35f, 0.12f, 0.3f);

        [Tooltip("Vertical gap between difficulty blocks.")]
        public float menuBlockSpacing = 0.5f;

        [Tooltip("Size of the text labels on the menu.")]
        public float menuLabelScale = 1f;

        [Tooltip("Size of the floating difficulty canvas panel in metres (width x height).")]
        public Vector2 menuPanelSize = new Vector2(1.2f, 0.6f);

        [Header("Route markers")]
        [Tooltip("Optional marker prefab (e.g. a small green flag) spawned on every hold marked as the Start of a route. A per-hold 'markerPrefab' on LevelstepHold overrides this.")]
        public GameObject holdStartMarkerPrefab;

        [Tooltip("Optional marker prefab (e.g. a checkered flag) spawned on every hold marked as the Finish of a route. A per-hold 'markerPrefab' on LevelstepHold overrides this.")]
        public GameObject holdFinishMarkerPrefab;

        [Header("Route timer")]
        [Tooltip("Show the route timer (starts on grabbing the Start hold, stops on the Finish hold or a balance fall) in the headset HUD.")]
        public bool showRouteTimer = true;
        [Tooltip("Colour of the running route timer text.")]
        public Color routeTimerColor = Color.white;

        [Header("Balance board (WT9011DCL)")]
        [Tooltip("Name prefix used to identify the sensor while scanning.")]
        public string balanceDeviceNameFilter = "WT9011DCL";

        [Tooltip("COM port the white USB adapter shows up as (e.g. 'COM5'). Leave empty to auto-detect.")]
        public string balanceSerialPortName = "";

        [Tooltip("Automatically start scanning and connect on play.")]
        public bool balanceAutoConnect = true;

        [Tooltip("Automatically calibrate flat shortly after connecting (seconds of averaging).")]
        public float balanceAutoCalibrateSeconds = 1f;

        [Tooltip("Balance thresholds. For convenience these live as live-editable fields on the 'Balance Board' GameObject in the Inspector during a playtest; values below mirror the component defaults (0 = keep component value).")]
        public float balanceStableTiltDeg = 4f;
        public float balanceUnstableTiltDeg = 14f;
        public float balanceHardFailTiltDeg = 30f;
        public float balanceStableRateDegS = 40f;
        public float balanceUnstableRateDegS = 120f;
        public float balanceHardFailRateDegS = 250f;

        [Tooltip("How fast the balance meter fills (0->1) while instability is full.")]
        public float balanceMeterBuildPerSec = 0.5f;

        [Tooltip("How fast the balance meter drains back to zero while fully stable.")]
        public float balanceMeterRecoverPerSec = 0.35f;

        [Tooltip("Minimum seconds between falls so a hard spike can't trigger back-to-back falls.")]
        public float balanceMinSecondsBetweenFalls = 1f;

        [Header("Balance HUD")]
        [Tooltip("Show the headset-view balance meter (empty bar that fills red while unstable).")]
        public bool balanceHudEnabled = true;
        public Color balanceBarBackColor = new Color(0f, 0f, 0f, 0.55f);
        public Color balanceBarFillColor = new Color(0.95f, 0.2f, 0.2f, 0.9f);
    }
}