# Simulated two-stage reach calibration

## What this feature does

This is an Inspector-configurable, timed **simulation** for the accessibility prototype. It does not sample controller/arm positions, calculate reach, generate fake measurements, or change climbing behaviour. Configured reach values are authoring data consumed by the separate accessibility grappling mechanic, not measured calibration results. No such measurements or prototype captions are displayed to players. See `GrapplingGuide.md` for X-toggled Grapple Mode, either-hand side-grip activation and assisted body movement.

The implementation reuses `SimulatedCalibrationSequence`, `CalibrationPanelView`, the existing Ready/countdown/progress controls, and the existing XR UI input system. Balance and reach have separate UI prefab instances, independent input bindings, timings and appearance. A small shared presentation-owner guard prevents simultaneous calibration panels.

## Player flow and activation

- **Right controller B / secondary face button** opens reach calibration.
- Stage 1: **Extend your arm horizontally as far as comfortably possible.**
- Point at **I'm Ready** and use the front trigger. The default horizontal countdown is **3 seconds**.
- Stage 2 then opens automatically: **Raise your arm vertically as high as comfortably possible.**
- Point at **I'm Ready** again. The default vertical countdown is **3 seconds**.
- **Reach Calibration Complete** is displayed for **2 seconds**, then the panel closes.

Both Ready steps have unlimited waiting. Time does not advance until confirmation. Press B again to cancel/dismiss by default. Skip advances the current countdown to the next Ready step, or to completion on the final stage. Cancel works in either waiting/countdown stage. Each new run starts again at horizontal reach.

**Left Y still opens balance calibration.** Only one calibration may be open, including its completion display. While one is open, activation of the other is ignored; finish or cancel the current one first. Neither panel opens automatically with the supplied defaults.

Existing right primary/A Jump, grip-based climbing, trigger UI, thumbstick locomotion, and Menu-button participant reset retain their bindings. Right secondary/B was unused when this feature was added. The right-controller B binding is separate from keyboard B, which remains real sensor flat calibration.

## Objects and Inspector configuration

Scene object:

**XR Origin (XR Rig) > Camera Offset > Main Camera > Simulated Reach Calibration**

Prefab: `Assets/Levelstep/Prefabs/SimulatedReachCalibration.prefab`.

### Simulated Calibration Sequence

| Setting | Purpose |
| --- | --- |
| Instruction Text | Stage 1 horizontal instruction. |
| Calibration Duration | Stage 1 horizontal countdown, default 3 seconds. |
| Additional Stages > Element 0 > Instruction Text | Stage 2 vertical instruction. |
| Additional Stages > Element 0 > Duration | Stage 2 vertical countdown, default 3 seconds. |
| Completion Text | Default `Reach Calibration Complete`. |
| Completion Message Duration | Default 2 seconds. |
| Activation Input | Default embedded Button action, `<XRController>{RightHand}/secondaryButton`. Can be edited or replaced with an InputActionReference. |
| Activation Cancels Active Sequence | Press activation again to cancel/dismiss, or disable this to ignore repeats while active. |
| Initially Visible | Default off. Optional opt-in opens the first instruction/Ready step, never an automatic countdown. It respects mutual exclusion. |
| Use Unscaled Time | Default on; sequence timing is independent of game time scale. The UI does not pause gameplay. |

The first-stage field names are retained to preserve the existing balance component's serialized settings. **Additional Stages is empty on balance**, giving it the same one-stage behaviour as before. The shared system supports further stages if required, each with a separate Ready gate; reach is configured with exactly two.

The Inspector provides stage-specific Edit Mode instruction previews, and Play Mode Run Demo / I'm Ready / Skip / Cancel controls. Zero-duration stages still require Ready. Repeated confirmation while a countdown is active does not restart it.

### Calibration Panel View

Use the same configuration as balance calibration:

- Title, Ready/Skip/Cancel/Close labels and countdown format.
- Font, text sizes/colours, background opacity and progress colours.
- Button appearance and independent Ready Button Appearance.
- Apply Appearance and authoring previews preserve all geometry.

The reach heading is `REACH CALIBRATION`. The old prototype-caption reference remains for compatibility but is blank/hidden. Do not enter measured-distance text here: this sequence does not measure a participant.

### Scene view layout

Reach has an independent copy of the balance UI hierarchy, with the **same shared component code**. Select any child under **Calibration Panel** and use its RectTransform/Rect Tool to move, resize, rotate or edit anchors/pivots. Move the root Transform to position the whole interface relative to Main Camera. There is no runtime follow, rebuild or layout-writing script; the authored camera parent naturally moves the world-space canvas with the headset.

Edit outside Play Mode and save the scene with Ctrl+S, or save the reach prefab in Prefab Mode. Changes to the reach scene instance/prefab do not rewrite balance geometry. Changing instructions, colours, stages, timing or activation never changes RectTransforms.

## Predetermined reach values

On the same **Simulated Reach Calibration** root, edit **Reach Calibration Settings**:

| Field | Default | Meaning |
| --- | --- | --- |
| Comfortable Horizontal Reach Distance | 0.45 m | Authored comfortable horizontal distance. |
| Comfortable Vertical Reach Distance | 0.60 m | Authored comfortable vertical distance, not a world-space height. |
| Maximum Grappling Distance | 3.00 m | Additional calibration range bound for accessibility grappling; each hand's Inspector maximum and detection radius can shorten it. |

These are editable defaults, not detected participant measurements. They are independent from the countdowns and are accessible before, during and after the simulation, even while the panel is hidden. Repeat/skip/cancel/completion do not replace them. **Accessibility grappling now consumes these values** for comfortable-reach eligibility and maximum connection range; see `GrapplingGuide.md`. The calibration sequence itself still measures nothing and implements no movement.

## Settings interface

Assign the root's `ReachCalibrationSettings` component to a consumer's serialized field. Read its properties directly, or depend on the read-only `IReachCalibrationSettings` interface:

```csharp
using Levelstep;
using UnityEngine;

public class ExampleReachSettingsConsumer : MonoBehaviour
{
    [SerializeField] ReachCalibrationSettings reachSettings;

    void Start()
    {
        IReachCalibrationSettings configured = reachSettings;
        float horizontal = configured.ComfortableHorizontalReachDistance;
        float vertical = configured.ComfortableVerticalReachDistance;
        float maximumDistance = configured.MaximumGrapplingDistance;
        // Read configured limits without depending on the calibration UI or its timing.
    }
}
```

No scene search, UI text parsing, controller-position sampling or global settings singleton is necessary to read these preset values. The current `AccessibilityGrappleController` is a concrete consumer of this same component. `LastOutcome` on the sequence describes only whether its presentation completed/skipped/cancelled; it is not evidence of actual reach measurement. Grappling defaults to usable without running the simulation.

## Setup and preservation

ClimbingScene is already integrated: **no manual Unity setup or button wiring is required**.

For another scene, use **Tools > Levelstep > Calibration > Add Simulated Reach Calibration**. The tool selects an existing reach instance rather than replacing it. When creating a new instance, its initial pose is copied from the balance panel or placed under Main Camera; subsequent authoring is independent. Prefab creation copies existing authored calibration UI through Unity APIs, without modifying the balance prefab. Ready/Skip/Cancel persistent callbacks point to the reach instance's own shared sequence component.

The existing edited `SimpleControls.png` was preserved. No input action assets, gameplay scripts, sensor calculations, tutorial content or balance-prefab data were replaced by this feature.

## Validation

Unity compiled successfully. **49 targeted checks passed**: 13 scene/Edit Mode checks, 34 Play Mode checks and 2 post-Play/save-reopen checks. Checks covered synthetic OpenXR Y/B button events, both tracked UI Ready clicks, two-stage timing and transition, per-stage readiness, mutual exclusion in waiting/countdown/completion and disable/destroy cases, original balance regression, repeat/skip/cancel, initial-visibility gating, settings access/editability, no configuration-value mutation, and all RectTransform fields through Play Mode/save-reopen. Temporary verification code/scenes were removed.

Physical PICO input mapping, headset readability/stereo presentation and preferred placement still need a headset playtest. The automated controller events verify the action path, not hardware tracking or real arm mobility.
