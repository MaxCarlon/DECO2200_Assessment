# Optional hand stamina and post-climb completion

## Enable / disable hand stamina

Select **XR Interaction Manager** in ClimbingScene and find **Climb Stamina Controller > Optional Hand Fatigue > Enable Hand Stamina**.

The checkbox is **off by default**, including the saved scene. Keep the component itself enabled: it still tracks real XRI hand grips, prevents conflicting same-hand selections and receives the balance board's fall/recovery lock.

With hand stamina off:

- Holding and assisted movement never drain hand stamina or cause fatigue exhaustion/recovery restrictions.
- Hand bars are not created at launch. If already created, they are hidden and original controller/hand colours are restored.
- Grip tracking, normal climbing, grappling and valid route completion continue normally.
- Balance-board instability remains independent: a balance fall still releases hands, aborts a running route and blocks re-gripping until the balance meter starts recovering. This does not drain the disabled hand-fatigue resource or emit a hand-exhaustion event.

Turn the checkbox on to restore the existing per-hand drain, free-hand recovery, exhaustion release, recovery delay/threshold, mint bars and red hand tint. Runtime transitions start with full hand stamina and clear stale fatigue state without releasing an otherwise valid grip or clearing a balance lock. Bars are reused rather than duplicated. The original rates and difficulty presets are untouched.

Tune enabled stamina using **Assets/Levelstep/Settings/ClimbingConfig.asset > Difficulty Presets**. Current selected difficulty continues to control those rates. `SetHandStaminaEnabled(bool)` is available to future settings/UI code; the Inspector checkbox also works during Play Mode. Save outside Play Mode for persistent configuration.

## Completion screen behaviour

After a **real successful Start-to-Finish run**, the existing timer/HUD completion behaviour runs normally. A separate screen waits **0.5 seconds** by default, then shows:

> **Climb Complete!**
>
> Would you like to view your latest attempt on the LevelStep app?

Buttons:

- **LEFT: No Thanks** — close without an app request.
- **RIGHT: Yes, Open Attempt** — close and invoke the optional app-request action.

Use the existing controller pointer and **front trigger**, as with calibration/tutorial/menu UI. Side grips keep their climbing behaviour. The existing Near/Far Interactor can suppress UI while that hand is selecting/hovering a nearby physical hold; free a hand and move it a little clear of the hold before pointing at a button. No input bindings or interactor settings were changed.

The screen starts hidden every launch, even if authoring preview was saved visible. Failed/balance-aborted/abandoned runs never schedule it. One local run can schedule at most one screen; closing it does not reopen it. A new Start immediately cancels a pending or visible screen. Disabling the screen/component or reloading the scene also cancels pending work. Re-enabling never replays an old success. Existing attempt limits, HUD result timing, resets, difficulty persistence and sensor connection behaviour are unchanged.

## Objects and Inspector configuration

Scene hierarchy:

**XR Origin (XR Rig) > Camera Offset > Main Camera > Climb Completion Screen**

Prefab: **Assets/Levelstep/Prefabs/ClimbCompletionScreen.prefab**.

On **Climb Completion Screen**:

| Setting | Purpose |
| --- | --- |
| Enable Completion Screen | Master presentation switch; completion logic still runs when off. |
| Appearance Delay | Seconds after success, default **0.5** in code, prefab and scene; zero shows on the next UI update. Changes affect future scheduling. |
| Use Unscaled Time | Default on; presentation delay is independent of game time scale. |
| Route Timer | Existing scene timer, already wired. |
| Title / Description | Player-facing text. |
| Yes Label / No Label | Independent button labels. |
| Font (Tutorial / Calibration) / Title Font Size / Description Font Size | Existing **LiberationSans SDF** TMP asset used by the tutorial/calibrations. Title/body sizes are **42**, copied from the calibration view. The obsolete built-in legacy Font reference is retained hidden for compatibility. |
| Title / Description / Background Color | Panel/content colours and opacity. |
| Yes Appearance / No Appearance | Each button's font size, text colour, normal/hovered/pressed/selected/disabled colours and border style. Reuses the existing TutorialButtonAppearance data type. |
| Overlay Material / Text Overlay Shader | Existing LevelstepHUD material is used for panel/button **images only**. TMP text clones its own SDF font material/atlas and uses the existing TMP mobile SDF overlay shader. This keeps text white and readable through nearby geometry without applying a bitmap-UI shader to SDF glyphs or editing shared font materials. Owned copies are destroyed on cleanup; no new shader/texture asset. |
| On Open Attempt Requested | Optional UnityEvent for a future app bridge. |
| Log Placeholder Request | Default on; logs the placeholder request. Turn off when connecting a real action if no demo log is desired. |

### Reused styling values and timing

The existing balance calibration scene component is the style source (its prefab is the fallback for new-screen creation). Tutorial and reach calibration use the same font and base palette. Values were read/copied from those components rather than sampled or guessed:

- Font: `Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset`.
- Title/body/button text: white `(1, 1, 1, 1)`; title/body size 42, button size 32.
- Background: RGB `(0.055, 0.08, 0.11)` with opacity **0.96**, including the calibration's plain-image style.
- Button normal `(0.12, 0.22, 0.27, 1)`, hovered `(0.2, 0.48, 0.48, 1)`, pressed `(0.4, 0.7, 0.5, 1)`, selected `(0.2, 0.55, 0.38, 1)`, disabled `(0.16, 0.18, 0.2, 0.5)`.
- Border `(0.85, 1, 0.95, 1)` at `(4, 4)` UI units, with the same UGUI Colour Tint / Outline feedback.

The existing title/message objects, panel dimensions, button sizes and row spacing were retained. Four legacy Text components were converted to TMP on the **same GameObjects/RectTransforms**. No now occupies the left slot and Yes the right slot, with equal centre spacing and aligned Y. Buttons, callbacks and optional app-event wiring retain their identity.

`RouteTimer` publishes success immediately when a valid Finish is selected; no automatic reset wait is inserted. The HUD's **4-second result display** is independent of this prompt, and participant reset is manually activated. The former 3-second prompt delay was the additional wait. It is now 0.5 seconds after the existing success event, without altering completion/reset logic.

### Scene-authored placement and size

Select **Completion Panel** under the feature root and use Unity's Rect Tool / RectTransform to reposition, rotate, resize or scale it. All child Text/Button RectTransforms are independently editable too.

The component's custom Inspector also exposes **Local Position**, **Local Rotation**, **Local Scale** and **Panel Size**, with Undo, plus **Select Panel for Scene Rect Tool**. These fields directly edit the authored panel; they are not runtime layout targets. Default placement is 1 metre forward and 0.05 metres below Main Camera, with a 1000 × 560 UI-unit panel at 0.0008 scale.

The camera parent is set once by editor setup and naturally carries the authored world-space UI with the headset. Runtime never reparents/repositions/resizes the panel, writes its anchors/pivots or rebuilds its objects. Startup, showing/hiding, navigation callbacks, appearance refresh and previews change content/visibility only. Save Scene changes or prefab edits outside Play Mode to retain them.

**Preview Completion Screen**, **Hide Preview**, and **Apply Appearance (Keep Layout)** are explicit authoring tools. Preview does not fabricate a completed run or invoke the app action. TrackedDeviceGraphicRaycaster and the scene's existing XRUIInputModule handle controller buttons; no additional EventSystem, physical collider or input system is added. Standalone OpenXR MultiPass is preserved.

The existing screen/prefab are already restyled. **Tools > Levelstep > Completion > Match Calibration Style (Preserve Layout)** explicitly reapplies the current calibration palette/font/button settings and the 0.5-second default to existing completion screens, correcting the button order without rebuilding objects. Do not invoke it merely to select a panel if you want to retain later custom styling/timing; use Add or Select for that. Runtime never re-copies calibration styling or reapplies button positions.

## Current Yes action and future app integration

Inspection found no app transport, completed-attempt API or supported deep link in this Unity project. The supplied Yes action therefore **does not open a real app or contact a server**. It closes the panel, logs a clearly labelled placeholder request, then invokes **On Open Attempt Requested**.

To connect a future integration, add its component and assign a public method to that UnityEvent in the Inspector. The screen exposes read-only `CompletedElapsedSeconds`, `CompletedRouteIndex` and `CompletedRunSequence` properties for the completed local run. **CompletedRunSequence is scene-local deduplication data, not a mobile-app attempt identifier.** No URL, server contract, remote record or upload success is assumed.

Both responses are guarded so repeated clicks/callbacks after dismissal cannot invoke the action again. The panel closes before the optional external action runs.

## Setup and implementation

**ClimbingScene is already integrated; no manual wiring is required.** For another compatible scene, use **Tools > Levelstep > Completion > Add or Select Climb Completion Screen**. The tool requires the existing Main Camera and RouteTimer and selects an existing instance rather than replacing authored UI, tuning or event wiring. The prefab is created only if missing.

Modified runtime scripts:

- `ClimbStaminaController`: optional fatigue flag, clean runtime transitions, off-mode balance release while preserving balance lock, hidden/reused bars.
- `HandStaminaBar`: presentation hiding and original tint restoration.
- `AccessibilityGrappleController`: fatigue checks respect the global hand-stamina switch; balance checks stay active.
- `RouteTimer`: adds local RunSequence and RunStarted notification for reliable UI deduplication/cancellation. Existing validity, success/failure and selection logic is unchanged.

Added `ClimbCompletionScreen`, its custom Inspector, `LevelstepCompletionTools`, the prefab and one scene instance. The appearance/timing revision modifies only these completion-specific scripts, the existing completion prefab/instance and this documentation. Existing calibration/tutorial assets, hand stamina, difficulty tuning, balance-meter algorithms/presets, app action, completion/reset detection, grappling and imported samples are not modified by the restyle.

## Verification

**65 checks passed: 13 scene/Edit Mode and 52 Play Mode/integration checks.** Tests covered default-off fatigue, holding longer than Medium's normal depletion time, toggling during real grips, enabled drain/exhaustion/recovery, hidden/reused bars and restored tint, unchanged difficulty rates, actual balance-meter fall/recovery with fatigue off, failure suppression, real Start/Finish scheduling and delay/zero-delay handling, duplicate/stale success callbacks, new-run cancellation of pending and visible panels, disabling/re-enabling, actual right/left controller pointer and front-trigger clicks on both buttons, one-shot app event, preserved custom panel/all-child geometry through appearance/lifecycle/save/reload, continued aligned grappling without fatigue, scene reset defaults and persistent sensor survival. Temporary verification code/scenes are removed afterward.

**Appearance/timing revision: 38 further checks passed (13 scene/Edit Mode, 25 Play Mode/integration)** for exact calibration font/palette/button states/borders, plain background/opacity, TMP conversion, symmetric non-overlapping No-left/Yes-right ordering and callback preservation, idempotent styling without object recreation, unchanged shared font asset, owned native SDF overlay materials/atlas and white glyph vertices, measured appearance around 0.5 seconds after actual Finish, real left No/right Yes ray/front-trigger interaction and pressed colour, unchanged placeholder/one-shot action, full custom RectTransform preservation during appearance/lifecycle, and saved defaults/layout after reload. Temporary verification fixtures were removed. Earlier default-delay/font descriptions are superseded by this revision.

Headset readability, stereo appearance, comfortable placement, and real PICO pointer/haptics presentation still need a headset playtest. Automated tracked UI/button tests verify input/event integration, not the display or a mobile-app connection.
