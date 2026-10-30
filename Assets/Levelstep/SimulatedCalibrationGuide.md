# Prototype simulated balance calibration

## Purpose

This is a **prototype presentation simulation**, not sensor-based calibration. It shows timed instructions, a countdown/progress bar, and a completion message. It never measures balance, fabricates sensor readings, calls `BalanceBoardBridge.CalibrateFlat`, changes stamina, changes balance thresholds, or applies sensitivity adjustments.

Real sensor connection/calibration and the existing Chain/Loop balance tuning remain separate. In particular, the existing **B** keyboard calibration is the sensor feature, not this demonstration.

## Controller operation

- Press **Y on the left PICO controller** (left secondary face button) to open the instructions and **I'm Ready** button. Opening the panel does not start the timer.
- Stand comfortably on the board, point at **I'm Ready** and press the normal front trigger. The button disappears and the default **3-second** countdown/progress begins.
- **Calibration Complete** is displayed for **2 seconds**, then the panel closes.
- Press Y again while waiting or counting down to cancel; during the completion message it dismisses the panel.
- Point at **SKIP** during the countdown and use the normal front trigger to jump to completion early. Skip is unavailable while waiting for Ready.
- Point at **CANCEL / CLOSE** and use the front trigger to cancel or dismiss.
- Repeat as many times as needed. The sequence always starts idle when a scene loads; it does not automatically launch.

The existing XRI bindings use right primary/A for Jump and both Menu buttons for participant reset. No secondary face-button binding was present in the existing XRI action asset; left Y was selected to avoid these controls and the grip/trigger/thumbstick interactions.

Right-controller **B / secondary** now opens the separate two-stage reach calibration. Both reuse the same sequence/view code and cannot be open simultaneously. Finish or cancel the active panel before opening the other. See `ReachCalibrationGuide.md` for reach timing and predetermined settings.

## Scene and prefab

- Scene: `Assets/Levelstep/Scenes/ClimbingScene.unity`.
- Hierarchy: **XR Origin (XR Rig) > Camera Offset > Main Camera > Simulated Balance Calibration (Prototype)**.
- Prefab: `Assets/Levelstep/Prefabs/SimulatedBoardCalibration.prefab`.
- Root components: **Simulated Calibration Sequence** and **Calibration Panel View**.
- The child **Calibration Panel** is an authored World Space Canvas with the standard `TrackedDeviceGraphicRaycaster`. Ready, Skip and Cancel/Close use the existing XRUIInputModule/EventSystem and trigger interaction. No extra input system or physical colliders are added.

The canvas follows the headset through its normal scene-authored camera parent, not through runtime transform writes. The root is initially authored at local position `(0, -0.05, 1.2)` metres relative to Main Camera. The sequence component remains active while only the child panel is hidden, so activation keeps working after every run.

## Timing, messages and activation

Select the root object and edit **Simulated Calibration Sequence**:

| Field | Use |
| --- | --- |
| Calibration Duration | Countdown time after Ready is selected; default 3 seconds. Waiting time is unlimited and does not count toward it. |
| Completion Message Duration | Time to display completion before closing; default 2 seconds. |
| Instruction Text | Default: `Stand comfortably on the balance board.` |
| Completion Text | Default: `Calibration Complete`. |
| Use Unscaled Time | Default on: the demo's clock continues if game time is paused. The demo itself never pauses the game. |
| Additional Stages | Leave empty for balance's original one-stage flow. Reach uses one additional vertical stage after its first horizontal stage. Every stage requires Ready. |
| Initially Visible | Default off. Optional opt-in only opens instructions/Ready at startup, never a countdown, and respects the one-panel-at-a-time rule. |
| Activation Input | Embedded Button action by default; edit its binding or choose **Use Reference** and assign an InputActionReference. |
| Activation Cancels Active Sequence | On: press activation again to cancel/dismiss. Off: further presses are ignored while the current sequence runs. |

Default activation path: **`<XRController>{LeftHand}/secondaryButton`**.

Use a **Button** action when changing the binding. Embedded actions are enabled/disabled by this component. Shared referenced actions are enabled if necessary but are never disabled or disposed by it, preserving other systems that may use them. Play Mode Inspector edits rebind the activation action without starting another sequence.

During Play Mode, **Run Demo** opens the waiting step, **I'm Ready** confirms it, and **Skip / Cancel / Close** let you test the rest without a headset. Zero durations are supported, but even a zero-duration countdown still requires Ready before completing. Zero completion duration closes immediately.

There is deliberately no cosmetic sensitivity multiplier: completion is UI-only and does not imply any balance-system adjustment.

## UI layout and appearance

### Scene view owns geometry

Work **outside Play Mode**, then save the scene with **Ctrl+S**:

1. Select the root Transform to change the overall position, rotation and scale relative to the camera.
2. Expand **Calibration Panel** and select any text, button, background or progress element. The **I'm Ready** object is the new waiting-step button; its child **Label** contains the caption.
3. Move/resize with the **Rect Tool (T)** or use its normal RectTransform Inspector. Anchors, pivot, rotation and scale are editable too.
4. Save scene-instance overrides or edit/save the prefab in Prefab Mode for shared styling.

No startup, activation, progress, appearance-refresh or preview code assigns RectTransform geometry. The progress bar changes `Image.fillAmount` (rendered mesh only), not width/anchors. Normal parent movement/stretch-anchor behaviour still applies. Play Mode layout edits follow Unity's ordinary temporary-edit rules.

For a stationary interface, you can deliberately reparent the root out of Main Camera and position it in the world. No script will reattach or reposition it at runtime.

### Inspector owns appearance

On **Calibration Panel View**, edit:

- Heading. The former prototype caption is blank and hidden; its object/reference is retained for compatibility and its RectTransform has not been deleted or moved. The simulation remains explicitly identified in the code.
- Countdown Format (`{0}` is the remaining whole-second count).
- Skip, Cancel and Close labels.
- Existing TMP font, title/message/countdown/prototype text sizes and colours.
- Completion-message colour.
- Background colour and opacity.
- Progress colours.
- Button normal/hover/pressed/selected/disabled colours, label style and border appearance.
- **Ready Button > Ready Label** (default `I'm Ready`) and **Ready Button Appearance** independently control the new button's text, font size, text colour, state colours and border.

**Apply Appearance (Keep Layout)** refreshes appearance without moving any element. **Preview Instructions / Ready** shows the new button for Scene-view editing. **Preview Countdown**, **Preview Completion**, and **Hide Preview** show the other authoring states without starting a running sequence. A preview saved as visible still starts hidden when Play begins. Advanced UI references are already wired and do not need ordinary editing.

The prefab reuses the existing LiberationSans SDF font and Unity's built-in UI sprite/default materials. No visual assets, shaders or external packages were generated.

## Reusing the UI

- `CalibrationPanelView` is presentation-only. A future reach calibration can call `ShowReady`, `ShowInstructions`, `UpdateCountdown`, `ShowComplete`, and `Hide` without depending on the board sensor.
- `SimulatedCalibrationSequence` is a generic timed prototype controller. A different prototype can reuse it with different text, timings and activation binding.
- The code distinguishes `Completed`, `Skipped` and `Cancelled` outcomes. These are outcomes of the **simulation**, never evidence of actual sensor calibration.
- `TryBeginCalibration` opens `WaitingForReady` and rejects another start while a sequence is active. Only `ConfirmReady` transitions that waiting state into the countdown; calls during any other state do nothing. Neither elapsed time nor Skip bypasses the ready gate. A single state machine is used rather than overlapping coroutines.
- A shared presentation owner prevents balance/reach overlap, including completion displays. It is released on auto-hide, cancel, disable or destruction and is reset at play-session startup. It does not lock gameplay or create another persistent singleton.
- Disabling the sequence cancels its UI and removes its input subscription. The scene-scoped object resets naturally during participant/scene reset.

## Setup tools and validation

The feature is already integrated and wired in ClimbingScene. No manual Unity setup is required there.

For another scene, **Tools > Levelstep > Calibration > Add Simulated Board Calibration** adds the prefab under Main Camera if present. If a sequence already exists in that scene, the tool selects it without replacing its layout or settings. A new scene still needs the normal XRI UI infrastructure.

**Add Ready Button (Preserve Layout)** upgrades older copies by adding/wiring only the missing Ready control and clearing/hiding the old visible caption. It does not reposition existing elements and does not reset an already placed Ready button. The current prefab/ClimbingScene instance is already upgraded; no manual wiring is required.

Verification passed **52 focused checks**: 16 scene/Edit Mode checks, 34 Play Mode checks, and 2 post-Play/save-reopen checks. They cover the actual Input System controller event path, repeat activation, overlap prevention, cancellation/skip, default/custom/zero timing, shared action references and lifecycle, unscaled timing, XRI tracked pointer clicks, hidden-panel input exclusion, complete RectTransform preservation, and unchanged stamina/meter/timer state. Temporary test files/scenes were removed.

The Ready-step update passed **34 additional checks** (10 Edit Mode, 23 Play Mode, 1 post-Play/save-reopen): preservation of existing scene/prefab geometry and binding/timing, indefinite waiting, no Skip bypass, tracked-pointer Ready clicks, duplicate-confirmation protection, countdown/completion timing, repeat/cancel/skip behaviour, configurable Ready styling, hidden caption, zero-duration handling, and Ready RectTransform persistence. Temporary verification code/scenes were removed.

Physical PICO button mapping, headset visibility/stereo appearance, pointer comfort and preferred placement still need a headset playtest. The automated input checks use the OpenXR controller layout and synthetic button events; they do not establish hardware tracking/rendering behaviour.
