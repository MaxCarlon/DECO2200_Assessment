# Levelstep development context

## Authoritative baseline

- Unity VR bouldering project; editor **6000.6.0f1**, URP **17.6.0**, XR Interaction Toolkit **3.6.0**, OpenXR **1.18.0**, Input System **1.20.0**.
- Main/only enabled build scene: `Assets/Levelstep/Scenes/ClimbingScene.unity`.
- User deliberately returned to this stable clone after unwanted Android/PICO build changes. Actual source, scenes, prefabs and settings override claims in the previous conversation.
- History: session `ses_f90ec8673ffe1F3A1udr8M5RTr`, exported in `old-session-backup.json` (UTF-16 JSON). `session-ses_f90e.md` is an additional, shorter export. Historical plans and verification claims are not proof of the current implementation.
- Primary workflow is Windows PC VR streamed to a PICO 4, with a WitMotion sensor connected through the white USB adapter. Native Android BLE code and the earlier APK/build utility are absent from this clone. Do not restore those experiments automatically.

## Architecture and files

Custom code is under `Assets/Levelstep/Scripts/`, namespace `Levelstep`. Editor tools are under `Assets/Levelstep/Editor/`, generally namespace `Levelstep.Editor`. Runtime code uses `Assembly-CSharp`; editor code uses `Assembly-CSharp-Editor`. No custom Levelstep assembly definitions.

### Rig and locomotion

- The scene instantiates `Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab`. The sample directory version differs from the installed package version; both are intentional current facts.
- XRI supplies the CharacterController, body transformer, move/turn/climb providers and near/far interactions. `DynamicMoveProvider` handles head-relative joystick movement; normal collision handling uses `CharacterControllerBodyManipulator`.
- Keep project-specific rig changes as scene-instance overrides rather than editing imported samples or `Library/PackageCache`.
- The scene uses 30-degree left/right snap turns. Both Snap Turn and Continuous Turn have `m_EnableTurnAround` overridden off: the unused backward-stick 180-degree gesture was a plausible source of flips reported while testers walked into the wall. Exact tester input was not recorded. Collision code itself does not rotate the rig.

### Walls, holds and difficulty

- `LevelstepWall` and `LevelstepHold` are model-independent authoring components. Assigning a model in the editor replaces their `Model` child using `ModelTools` in `LevelstepModelTools.cs`.
- Authoring prefabs: `Assets/Levelstep/Prefabs/LevelstepWall.prefab` and `LevelstepHold.prefab`. Empty model slots on these base prefabs are expected; placed instances have model overrides/children.
- Holds have a kinematic Rigidbody, `ClimbInteractable`, `LevelstepHold` and `HoldRouteTag`. Model rebuilding uses non-convex mesh colliders to avoid convex hull limits and inaccurate grab surfaces. Some legacy tooltips still say convex.
- Manual placement is the current workflow: `Tools/Levelstep/Snap Hold (click wall to add)` or the hold inspector's Snap to Wall button. Placement matches the surface normal. Legacy grid builders and `HoldPlacementZone` were removed; config still contains historical generation fields and paths.
- `HoldRouteTag.routeType`: Easy=0, Moderate=1, Hard=2. The menu/config call index 1 Medium.
- Scene snapshot: 26 holds (6 Easy, 10 Moderate, 10 Hard), three Start holds and three Finish holds; 28 explicitly authored mesh colliders, all non-convex.
- `DifficultyRouteController` enables climbing only on the selected route and applies route colours through cached MaterialPropertyBlocks. Inactive routes blend toward the wall tone with alpha kept at 1. **Preserve opaque hold rendering**: true transparency repeatedly caused VR popping.
- `DifficultyMenuButton` drives stamina difficulty, route selection and optional PlayerPrefs persistence. The floating menu uses legacy UGUI Text/Button, XRUIInputModule and trigger-based UI input. Preserve grip-based climbing and separate trigger-based menu input.
- Difficulty buttons add/reuse a built-in UGUI `Outline` on their background Image at runtime. Only the actual selected index enables its persistent border; selection events, startup and menu re-enabling refresh it. Inspector controls on `DifficultyMenuButton`: Selected Border Color (white) and Selected Border Thickness (6 UI units per axis). It uses the existing UI material and does not alter click handling or scene layout. Unity compile passed; headset visibility/thickness still needs visual confirmation.
- Tune route stamina through `Assets/Levelstep/Settings/ClimbingConfig.asset` > difficultyPresets. Serialized asset values, not C# default values, determine the actual tuning. Default difficulty is index 1 (Medium).

### Stamina and completion

- `ClimbStaminaController` tracks each hand independently via XRI selection events; the free hand recovers while the other grips. Exhaustion releases that hand and blocks re-gripping until the recovery delay and `minStaminaToGrip` threshold are satisfied.
- It implements `IXRSelectFilter`, registered on the existing XRInteractionManager. Filtering applies only to `ClimbInteractable`; an exhausted, zero-stamina or balance-locked hand cannot begin a climb selection. Other interactions remain available. Filter and event listeners are removed on destruction.
- **Do not reject exhausted grabs by calling SelectExit inside selectEntered.** XRI `ClimbInteractable.OnSelectEntered` invokes the base select-enter event before calling `ClimbProvider.StartClimbGrab`. A nested exit inside that event can finish before the provider subsequently starts a stale grab, leaving climb movement active without a valid selection. Pre-selection filtering fixes this ordering problem.
- `HandStaminaBar` creates per-hand world-space bars at runtime. Their constant mint fill shrinks; hands/controllers tint red. Preserve these distinct visual behaviours.
- `RouteTimer` starts on the selected route's Start hold and succeeds on its Finish hold only while Running. It aborts on a balance fall or 0.4 seconds without a grip. Grips are tracked as `(interactor, interactable)` pairs so two hands on one hold work correctly.
- Exhausting one hand while the other supports the player does not invalidate the run. After an aborted run, grab Start again to begin a new valid run; merely recovering and reaching Finish does not revive it.
- Completion logic is not disabled by stamina. The former rejected-grab event ordering could desynchronise selection, locomotion and the timer; do not bypass timer validity to mask that issue.

### Tutorial

- `ClimbingScene` contains one stationary **Levelstep Tutorial** instance of `Assets/Levelstep/Prefabs/LevelstepTutorial.prefab`, initially hidden on page 0. Its sibling Toggle canvas remains accessible when the panel canvas is deactivated. Both use `TrackedDeviceGraphicRaycaster` and the existing XRUIInputModule/trigger interaction; no additional input system or physical colliders.
- **Tutorial layout is Scene-authored.** Every UI RectTransform (positions, dimensions, rotation, scale, anchors, pivots) is authoritative. `TutorialPanel` startup, navigation, Apply Configuration and preview change only content/appearance/behaviour, never geometry. Earlier script-based layout values remain serialized in read-only compatibility foldouts; do not re-enable them as automatic layout drivers.
- `TutorialPanelEditor` has an explicit drag-and-drop **Pages — gameplay navigation order** list, with add/remove controls that edit references only. Order is serialized on the component and saved with its scene/prefab. The ClimbingScene instance is ordered Controllers, Paths, Push, Board. Refresh after reordering retains the current page by asset when possible.
- `TutorialPanel` owns initial state, wrap/stop navigation, per-page animation reset/resume policy, global speed/hidden-playback settings, text/image appearance, button style/labels and counter. Inspector preview controls explicitly apply configuration; OnValidate never rebuilds objects. **Reset Layout to Defaults…** is editor-only, explicit, confirmed and Undoable; initial creation uses the same defaults without changing an existing tutorial.
- `TutorialPage` ScriptableObjects live under `Assets/Levelstep/Settings/TutorialPages/`. Four defaults: Controllers (`SimpleControls.png`), Push (6 frames 0–5), Paths (3 frames 0–2), Board (8 frames 0–7). The user has edited page descriptions and animation tuning; preserve those values. **Override Content Appearance** controls fonts, colours, alignment and image scaling only, not positions or sizes.
- TextMeshPro resources are present under `Assets/TextMesh Pro/`; tutorial text uses the existing LiberationSans SDF asset. The older difficulty menu/HUD still use legacy UGUI text.
- `ImageSequencePlayer` swaps pre-imported Sprite references on a UI Image; supports arbitrary frames, static fallback, null skipping, FPS, loop/ping-pong, speed, pause/resume, unscaled time and Fit/Stretch/FillAndCrop. It lives outside the hideable panel, disables ticking when unnecessary, and never creates textures. Hidden playback advances only the current page; off-page state is saved by page asset within the session.
- Image fitting/cropping does not drive RectTransforms. `ImageSequenceCrop` centre-crops sprite UVs on the existing Image for FillAndCrop. The legacy AspectRatioFitter on existing tutorial Images is disabled; its serialized reference is retained. `LevelstepTutorialTools.UpgradeExistingImageRendering` upgrades rendering without rebuilding objects or changing their layout.
- Each `TutorialPage` has **Layout Mode**: Image + Text or Full Image. Controllers and Paths use Full Image; Push and Board use Image + Text. Page order, text, frames and animation tuning are preserved. The original Image Viewport / Frame Image and the added Full Image Viewport / Full Image are independent Scene-authored containers. Runtime switches visibility and the shared player's target Image; it never reparents or rewrites their RectTransforms.
- `TutorialFullImageFit` fits/centres the complete sprite with per-page Left/Right/Top/Bottom render padding, without stretching, cropping, layout drivers or texture creation. Full Image always uses Fit regardless of split-layout scaling settings. Its optional title is hidden by default to free header space. Navigation/toggle remain separate. `LevelstepTutorialTools.UpgradeFullImageLayouts` adds only missing containers/bindings; re-running it preserves existing layouts. Page Inspector edits refresh an already previewed page; **Preview This Page in Open Tutorials** selects/shows it explicitly.
- Editor tools: `TutorialPageEditor` populates frames from a folder using natural numeric sorting without changing source images/import settings. `TutorialPanelEditor` offers explicit Edit Mode previews and live Play Mode refresh. `LevelstepTutorialTools` creates assets only if missing and adds a tutorial without replacing an existing one. Existing page content must not be overwritten.
- Full authoring guide: `Assets/Levelstep/TutorialGuide.md`. Initial feature verification passed 31 scene/asset and 50 Play Mode checks. The Scene-authored layout update passed 52 further checks (21 Edit Mode, 28 Play Mode, 3 post-Play/save-reopen), including all RectTransform fields, list order, Apply/preview preservation, explicit reset/Undo, UV cropping and tracked UI. The four page assets stayed byte-for-byte unchanged during this update. Temporary verification code/scenes were removed. Headset readability, stereo appearance and physical placement still require visual confirmation.
- Full Image verification passed another 68 checks (24 Edit Mode, 42 Play Mode, 2 post-Play/save-reopen): both layouts' geometry, upgrade idempotence, per-page switching, original content/animation preservation, static/animated target routing, hidden playback, tracked UI, and no-crop aspect fit/padding/centring for square/wide/portrait sprites. Temporary verification code/scenes were removed.

### Balance sensor and presets

- `BalanceBoardBridge` is the sole persistent singleton, on the separate root **Balance Board (Sensor)**. `DontDestroyOnLoad` keeps connection/calibration across participant resets.
- Primary transport: Windows Win32 serial P/Invoke, currently **COM5**, **115200 baud**, 2-second probing interval, auto-connect and flat calibration enabled. An empty port field enables port probing; the config's serial-port override is currently empty. Adapter port numbers can differ on another laptop.
- Parses 20-byte `0x55 0x61` frames: signed little-endian int16 rates at +8/+10 scaled by 2000/32768, angles at +14/+16 scaled by 180/32768. Worker-thread snapshots feed main-thread calibration and gameplay.
- `Assets/Witmotion/` retains the modified Windows BLE SDK, namespaces `Assets.Bluetooth`/`Assets.Device`, with `BleWinrtDll.dll`. That BLE route is dormant in the bridge's current Connect path. No Android transport is implemented here.
- `BalanceMeterController` converts calibrated XY tilt magnitude and angular-rate magnitude into instability, builds/recovers the meter and uses `FallFromBalance`/`SetBalanceLock` to release hands and pause recovery until the meter starts draining.
- There is one live meter, not two competing controllers. `BalanceMeterControllerChain` and `BalanceMeterControllerLoop` inherit the settings-only `BalanceMeterPreset`; `BalanceMeterPresetSwitcher` applies them to the live meter without resetting its value. Chain is applied on the first Update.
- Tune the scene's **Balance Board** preset components, not just the live meter or config. Applying a preset disables `readThresholdsFromConfig` and overwrites the live thresholds.
- Current scene tuning (snapshot; re-read before changing):

  | Setting | Chain | Loop |
  | --- | --- | --- |
  | Tilt stable / unstable / hard fail (degrees) | 3 / 4 / 40 | 1 / 2 / 40 |
  | Rate stable / unstable / hard fail (degrees/sec) | 80 / 120 / 250 | 5 / 15 / 250 |
  | Build / recover per second | 0.25 / 0.5 | 0.5 / 0.5 |
  | Smoothing / minimum fall interval | 4 / 1 second | 4 / 1 second |

### HUD, markers and reset

- `BalanceBoardHUD` is an authored world-space canvas in the scene, visible/editable before Play. `BalanceBoardUI` follows the main camera in LateUpdate, currently 0.7 metres forward and -0.1 metres vertically offset. Layout comes from the child RectTransforms.
- `Assets/Levelstep/Shaders/LevelstepHUD.shader` and `Materials/LevelstepHUD.mat` draw the HUD through geometry using ZTest Always. Material instances preserve individual UI font/texture handling.
- **Preserve Standalone OpenXR MultiPass** (`Assets/XR/Settings/OpenXRPackageSettings.asset`, Standalone `m_renderMode: 0`). Single-pass optimisation and attempted shader fixes caused one-eye/doubled HUD rendering and were reverted. Current custom HUD shader has no stereo-instancing macros; Android settings still use mode 1.
- Start/Finish markers are simple authored prefabs, `sm_labelStart.prefab` and `sm_labelFinish.prefab`, assigned per hold via `markerPrefab`/`markerOffset`. The hold spawns its marker, billboards it and shows it only for the selected route. Edit the prefabs to style labels globally; historical runtime-generated text experiments were replaced.
- `RouteSessionReset`: P reloads the active build scene and deletes `Levelstep.Difficulty`, restoring Medium; holding both controller Menu buttons for 2 seconds is a fallback. Keep the persistent sensor separate from scene-scoped meter, timer and HUD.

### Prototype simulated calibration

- ClimbingScene's Main Camera has a scene-authored child **Simulated Balance Calibration (Prototype)**, instantiated from `Assets/Levelstep/Prefabs/SimulatedBoardCalibration.prefab`. It is a world-space canvas using existing XRUIInputModule/tracked pointer/trigger input; no additional EventSystem or colliders.
- `SimulatedCalibrationSequence` is explicitly UI-only prototype simulation. It never samples/fabricates sensor data, calls real sensor calibration, alters balance/stamina calculations, or changes sensitivity. `CalibrationPanelView` is a reusable presentation component for a future reach calibration.
- Default activation is **left secondary face button / PICO Y**, via Inspector-configurable `InputActionProperty` path `<XRController>{LeftHand}/secondaryButton`. Existing right primary/A is Jump, both Menu buttons reset, and no secondary button was used in the original XRI bindings. Activation again cancels/dismisses by default; repeat presses can instead be configured to be ignored. Skip/Cancel buttons use normal pointer + front trigger.
- Always starts idle/hidden. Activation opens instructions and **I'm Ready**, with unlimited waiting and no countdown/progress/Skip shown. Only `ConfirmReady` starts the existing 3-second countdown/progress; then "Calibration Complete" for 2 seconds and hide. Ready disappears while counting down. Repeated confirmation cannot restart it; even zero durations require Ready. Cancel works while waiting; Skip applies only during countdown. Repeated runs, zero durations and unscaled timing are supported. Outcomes distinguish simulation Completed/Skipped/Cancelled. Owned actions are cleaned up; referenced shared actions are not disabled/disposed.
- `CalibrationPanelView.readyLabel` and `readyButtonAppearance` expose independent Ready text/state colours/font/border configuration. The Ready button is an authored child under Calibration Panel, repositionable/resizable in the Scene view. The old visible prototype caption is blank/hidden, with its object and reference retained for compatibility; simulation code/behaviour remains explicit and unchanged in purpose. `LevelstepCalibrationTools.UpgradeReadyControls` adds missing controls/bindings without resetting existing geometry. Inspector previews include the waiting and countdown states.
- Calibration UI geometry is fully Scene-authored. Root camera parenting/pose is set once in editor setup; runtime code never follows/repositions/rebuilds the UI. Text/colours/fonts/timing/input are Inspector-configurable. Progress changes Image.fillAmount, not RectTransforms. Authoring previews and appearance refresh preserve layout.
- `LevelstepCalibrationTools` adds/selects the feature without replacing an existing instance. Existing TMP font, built-in UI sprite and default materials are reused; no sensitivity multiplier was added. Guide: `Assets/Levelstep/SimulatedCalibrationGuide.md`.
- Unity compiled; 52 verification checks passed (16 Edit Mode/scene, 34 Play Mode, 2 post-Play/save-reopen), including synthetic OpenXR controller button events, shared input reference lifecycle, tracked UI buttons, layout preservation and unchanged stamina/meter/timer state. Temporary verification code/scenes removed. Physical headset/controller presentation still needs playtesting.
- Ready-step verification passed another 34 checks (10 Edit Mode, 23 Play Mode, 1 post-Play/save-reopen), including waiting/confirmation gating, tracked UI Ready clicks, duplicate-start prevention, preserved bindings/timing/geometry, Ready appearance configuration, caption hiding and editor upgrade idempotence. Pre-existing QualitySettings changes were preserved. Temporary verification code/scenes removed.

## Operator controls

- C: connect when disconnected. B: calibrate flat.
- Space (held): manual instability/meter failsafe, including without sensor data.
- K: chain preset. L: loop preset. T: debug overlay.
- [ / ]: transient stable tilt adjustments. - / =: transient unstable tilt adjustments.
- P: next-participant reset. Both controller Menu buttons held 2 seconds: reset fallback.
- Left controller Y / secondary face button: simulated calibration; press again to cancel/dismiss. This is separate from real sensor calibration on keyboard B.
- Arrow-key sensor simulation only operates when `SimulateEnabled` is explicitly enabled.

## Rendering and performance baseline

- Both URP assets retain MSAA 2, main shadow resolution 2048, HDR off. Scene camera clip planes are 0.03 to 60 metres. `LevelstepFrameRate` requests 90 fps and disables monitor vsync; actual XR pacing depends on the runtime.
- Baked lighting data/lightmaps and reflection probe exist under `Assets/Levelstep/Scenes/ClimbingScene/`. Preserve them and their GUIDs. The Global Volume references `LevelstepFairVisuals.asset` but is currently inactive.
- Prior performance work also caches UI/tint writes and reduces repeated scene searches. Preserve the public tuning interfaces; avoid broad performance rewrites without measurement.

## Conventions and verification

- Prefer minimal changes that address a demonstrated cause. Preserve the user's hand-authored scene/layout/tuning. Do not regenerate the scene or restore historical systems based only on conversation summaries.
- Preserve `.meta` files, GUIDs, serialized field names and prefab overrides. One MonoBehaviour class per matching `.cs` file is important: a previous multi-class preset file bound scene references incorrectly.
- Use the new Input System for keyboard/controller actions. Do not introduce legacy `UnityEngine.Input` polling.
- Keep UnityEditor-only helpers behind `#if UNITY_EDITOR` when used by runtime components. Do not edit generated `.csproj`, `.sln`, Library or package-cache files.
- Scene-writing editor tools are not harmless diagnostics: Place Preview Wall removes matching existing wall roots; Add Balance Board/EnsureBalancePresets reseeds **both** presets from the live controller, overwriting distinct tuning. Inspect tools before invoking them on this authored scene.
- For automated Unity runs, first ensure the interactive editor has saved and closed this project. Editor executable on the inspected machine: `C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe`. Temporary logs can go under `C:\Users\poiso\AppData\Local\Temp\opencode`.
- Latest targeted verification: Unity compiled the changes; 7 scene checks and 23 Play Mode integration checks passed using actual XRI selection/climbing, exhaustion, recovery, timer completion and character-controller collision. Scene loaded with no missing scripts. Temporary verification code was removed afterward. Headset/controller hardware behaviour still requires user playtesting; headless OpenXR cannot validate display/tracking.
- Existing uncommitted changes at session start: `Assets/Settings/Project Configuration/UniversalRenderPipelineGlobalSettings.asset` and the package settings JSON files for `com.unity.learn.iet-framework` and `com.unity.testtools.codecoverage`. Preserve these and the session exports. Do not commit unless requested.

## Outstanding items and discrepancies

- User testing flow mentioned three attempts and a successful result visible until reset. Current code has **no attempt cap** and hides successful/failed results after **4 seconds** (`BalanceBoardUI.ResultShowSeconds`). This is a requirement discrepancy, not permission to change it automatically.
- Hard-fail tilt/rate thresholds bypass the once-per-full-meter gate and may retrigger after the cooldown. Historical claims of strictly one fall until recovery are broader than the current code.
- The hold inspector/config advertise global marker fallback fields, but current `LevelstepHold.Start` requires a per-hold marker prefab; all six scene role holds are assigned. Do not promise the fallback works.
- Historical claims that exhaustion disables whole interactors do not describe this implementation. The targeted fix uses pre-selection filtering, leaving head tracking, UI and other interactor functionality intact.
- Native PICO BLE/build portability and COM-port deployment require a separate, explicit task. Earlier Android implementation details, APK paths and hardware guarantees from the export do not apply to this clone.
- Next practical verification: headset test walking into the wall, normal left/right turning, prolonged grip through exhaustion, recovery, balance fall and a fresh successful Start-to-Finish run.
