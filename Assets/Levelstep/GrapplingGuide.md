# Assisted accessibility grappling

## Controls and player flow

1. **Press left X** to toggle shared **Grapple Mode** on. Pressing X alone never grabs or moves the player.
2. Point **either controller approximately toward nearby holds**. Broad overlap/cone assistance selects a suitable selected-route hold beyond comfortable reach, preferring the nearest. A stable mint guide line identifies it; precise ray alignment is unnecessary.
3. **Hold that controller's normal side grip** to lock the displayed hold. The body smoothly approaches it using the existing XRI body transformer and CharacterController. Moving/rotating the controller or looking away cannot lose the lock.
4. Once the **actual tracked controller** reaches the hold's unchanged normal grab distance, a real XRI grab starts. The original controller model stays tracked and visible. The line turns green and optional haptics confirm attachment.
5. Keep holding the grip and continue normal physical pulling/climbing. **Release grip** to release the hold.

Releasing grip during travel cancels without attaching. X off also cancels travel, but **preserves already-held selections** until their normal grip release. Normal nearby grabbing takes priority even with Grapple Mode on. Only one body approach runs at a time; either hand may initiate it. An existing other-hand grip can remain selected while approaching, and both hands can end up holding different holds.

Both hands read their existing Near/Far Interactor **Select input**; there are no duplicate grip bindings/actions. Left X remains a configurable mode-toggle action. Y/B calibration, right A Jump, triggers for UI, and the existing locomotion/turning/reset bindings retain their assignments. Competing locomotion is temporarily paused only during the assisted approach.

## Scene objects and Inspector configuration

**XR Origin (XR Rig) > Accessibility Grappling (X Mode)** contains:

- **Accessibility Grapple Controller**: left-hand targeting and the shared mode owner.
- **Accessibility Grapple Movement**: shared body-movement settings and exclusive approach ownership.
- **Right Assisted Grapple** child: independent right-hand targeting, line and XRI selection helper. Its Mode Controller references the left-hand owner, and its Movement references the same movement component. Legacy projected-model objects/references remain inactive for compatibility; they are not used to display the controller.

The previous left-hand objects and authored tuning are retained. The right hand initially copies the left's reach/targeting/visual/gameplay tuning, with its own physical references and mirrored approximate shoulder offset. Subsequent settings are independently editable.

ClimbingScene is already wired. For another compatible scene or to repair missing bindings, use **Tools > Levelstep > Accessibility > Add or Upgrade Assisted Grappling**. Re-running upgrades/selects the existing feature without recreating its existing visuals or overwriting movement tuning. Imported samples and the calibration prefabs are not edited.

### Mode, reach and targeting — Accessibility Grapple Controller

| Setting | Meaning |
| --- | --- |
| Grappling Enabled | Per-hand feature switch. The mode owner's switch also gates starting the other hand's approaches. |
| Grapple Mode Enabled By Default | On the mode owner: launch with mode already on. Default off; otherwise press X once. |
| Activation Input | Mode-toggle action, default `<XRController>{LeftHand}/primaryButton`. Used only by the mode owner. |
| Physical Interactor | Existing hand's Near/Far Interactor; its Select reader supplies both press and release. Do not replace its grip binding with X. |
| Enable Without Calibration | Default on. Use predetermined reach values immediately, without running calibration every launch. |
| Reach Settings | Existing `ReachCalibrationSettings` on Simulated Reach Calibration. Defaults: horizontal 0.45 m, vertical 0.60 m, maximum 3 m. |
| Override Calibrated Values | Use the three local distance fields instead of the shared reach profile. Missing profile also uses local fallback values. |
| Comfortable Horizontal / Vertical Reach | Local override comfort envelope in tracking-space metres. Effective values are displayed read-only below the Inspector. |
| Maximum Grapple Distance | **1.5 m** acquisition cap measured from this tracked controller. Calibration can shorten it. Moving beyond range after grip locks does not cancel the approach; only the line hides until back in range. |
| Reach Origin Offset | Approximate shoulder offset relative to tracked head yaw; defaults left `(-0.18, -0.2, 0)`, right `(0.18, -0.2, 0)`. Not a measured shoulder position. |
| Detection Radius / Targeting Angle | Defaults **1.5 m / 60-degree half-angle** (120-degree-wide acquisition area). Effective search range is capped by maximum distance. No direct hit is required. |
| Maximum Head Target Angle | Head-yaw cone half-angle, default 135 degrees. Allows side/cross-body holds, including slightly behind the head's forward plane, but rejects directly behind-head targets. 90 restores the former strict front hemisphere. |
| Target Priority | **Nearest** by default; Closest To Aim and Balanced remain optional. Nearby eligible holds outrank farther precisely aimed ones with Nearest. |
| Target Switching Sensitivity | Default **0.2**: another hold must have a 20% better score before replacing a valid preview. Higher is stickier; zero switches freely. |
| Target Retention Angle | Default **75-degree half-angle**, never narrower than acquisition. Existing previews tolerate more drift than initial selection. |
| Target Retention Grace Duration | Default **0.4 s** after leaving the retention/head cone. Only aim gets grace; range, routes, stamina and collision restrictions remain immediate before grip. |
| Valid Hold Layers / Obstacle Layers | Hold search and controller-to-hold line-of-sight masks. |
| Targeting Refresh Interval | Default 0.05 seconds, i.e. 20 Hz. Lines still update every frame. |

Only enabled `ClimbInteractable` objects with Levelstep hold/route tags on the selected route are eligible. Targets outside the head/controller cones, occluded, invalid or comfortably reachable holds are rejected. Searches use fixed buffers rather than allocating overlap/raycast arrays each frame.

**Cross-body targeting is symmetric:** there is no left/right X-coordinate veto. A bounded, non-allocating overlap search finds nearby colliders, deduplicated by hold. Broad cone filtering uses each hold's normal XRI grab point, so no narrow aim-ray intersection is required and the endpoint does not jump between mesh faces as the hand jitters. One-sided non-convex holds use the same logic as other holds. Rays are used for pre-grip obstruction checks, not precise pointing. No global back-face physics setting is changed.

**Why 1.5 metres:** the 26 authored ClimbingScene holds have nearest same-route root-position gaps of about 0.48–1.46 m, with route medians about 0.88–1.08 m. The scene verification measured the largest at 1.459395 m. A 1.5 m cap halves the previous 3 m range while supporting nearby transitions. This is a geometric tuning baseline, not an authored route-order system: actual reach is measured from the current hand to the normal grab point, and Nearest preference avoids choosing farther holds within the broad cone. Both scene hands have explicit serialized short-range/forgiving defaults; the calibration profile's existing 3 m maximum is preserved as an additional upper bound.

### Shared eligibility and honest guidance

`AccessibilityGrappleController.CanGrapple` is the pending-target eligibility entry point used by preview and grip activation. Detection supplies a hold/collider/point; validation decides whether it is usable. New candidates use the acquisition cone; the current preview uses its wider retention/grace policy. Checks include mode/feature/calibration, current selections, other-hand movement ownership, cooldown, stamina/balance/support, valid holds/routes/layers, XRI interaction-layer compatibility and occupied single-select holds, comfort/aim/head cones, visibility and collision-safe body preflight. Grip uses the currently displayed valid preview rather than forcibly rescoring/replacing it.

The effective acquisition/visible-line range is:

```text
overrides on:  min(Detection Radius, Maximum Grapple Distance)
overrides off: min(Detection Radius, Maximum Grapple Distance, Reach Settings.MaximumGrapplingDistance)
```

Both the drawn endpoint and the hold's normal grab point must be inside that range. A large nearby mesh cannot advertise a grab point beyond it. World-space distances are used consistently; scaling the rig does not silently enlarge the Inspector's world-metre cap. The custom Inspector shows both Effective Maximum Distance and Effective Search / Grip Range.

Before grip, the current preview is revalidated in LateUpdate between throttled scans. Minor aim drift and marginal score differences do not make it disappear or flicker between holds. Leaving the wider cone starts the short grace timer; staying away beyond grace loses the preview. Hard invalidations (range, route/layers, stamina/balance, unavailable selection or blocked body approach) still hide it immediately. Grace never enables an out-of-range acquisition.

After grip, **the hold is locked independently of aim and grace**. No controller/head angle, nearest-target rescoring, comfortable-reach threshold, acquisition range or changing hand-ray visibility can cancel that lock. The provider continues calculating the actual tracked-hand-to-locked-hold movement, with real body/head collision checks intact. Already-held selections likewise never release because of aiming.

**Agreed range-feedback policy:** if hand movement puts the locked/held hold beyond effective range, keep pulling/holding but hide its line/highlight. Restore them when back in range, regardless of aiming direction. Out-of-range new targets remain forbidden. Connected green feedback denotes a real held selection, not permission to start a second grapple. X off hides feedback and cancels an unfinished approach, while preserving an already-held grip.

Normal nearby grabs keep first priority before initiation. During a locked approach only, a narrowly scoped physical-interactor select filter prevents incidental climbable holds from stealing that hand's already-held grip input; the aligned attachment helper performs the actual intended grab. It does not disable the interactor, tracking, UI or the other hand, and is registered/removed with the component lifecycle. No nested select-enter rejection exits are used.

### Optional rejection diagnostics

Enable **Debug Log Rejections** on either hand. **Debug Log Interval** defaults to 0.75 seconds and throttles output. Messages identify the hand, rejected hold, reason (e.g. OutOfRange, AimAngle, WrongRoute, NormalGrabPriority, Exhausted, MovementBusy, ApproachBlocked), measured distance, effective cap and aim angle. Logging is off by default; strings are formatted only when a message is emitted. The Play Mode Inspector also shows **Last Eligibility Rejection**.

### Movement — Accessibility Grapple Movement

| Setting | Default | Meaning |
| --- | --- | --- |
| Grapple Movement Speed | 0.65 m/s | Maximum body translation speed. |
| Movement Smoothing | 0.2 s | Acceleration/deceleration smoothing time. Zero selects constant-speed movement. |
| Movement Duration Limit | 8 s | Cancel an unfinished approach after this duration; zero disables the limit. |
| Movement Obstacle Layers | All | Geometry used for body/head path and endpoint checks. Keep the wall and other blocking geometry included. |
| Head Clearance Radius | 0.14 m | Extra head-sphere collision clearance. |
| Collision Padding | 0.01 m | Extra body-path clearance. |

The movement component has references to the **existing** Locomotion Mediator, Climb Provider and Gravity Provider. The rig must retain its enabled CharacterController and constrained body manipulator: assisted movement refuses to fall back to collision-free transform translation.

The old Comfortable Stopping Distance / Reach Margin values remain serialized but are hidden compatibility data, not movement drivers. Stopping at a shoulder envelope left the real controller short of the hold. The hold's existing `maxInteractionDistance` now controls valid attachment, with its distance filter and authored values unchanged.

No separate vignette or camera effect is added. Conservative speed, smoothing, bounded translation steps and unchanged facing provide the built-in comfort controls; tune their feel on the headset.

## How the destination and movement are calculated

For **eligibility**, the target and tracked head are expressed in Camera Offset / tracking-space coordinates. A configurable head-yaw-relative shoulder offset defines the reach origin. A hold is beyond comfort if either its horizontal XZ distance or absolute vertical distance exceeds the configured threshold. Absolute world height and a projected hand are never used as the reach reference. These calibration values determine when assistance is offered, not where the body stops short of the hold.

For **movement**, every update calculates:

```text
normalGrabPoint = targetHold.GetDistance(trackedController.position).point
remainingBodyTranslation = normalGrabPoint - trackedController.position
bodyDestination = currentOriginPosition + remainingBodyTranslation
```

Both points are world-space; the hold's existing distance convention supports its authored collider pivots as well as ordinary hold positions. The entire XYZ difference is used. A low physical hand therefore requires the appropriate upward body movement, without adding an arbitrary vertical boost or applying a local-space delta as a world-space motion.

The remaining movement is recalculated from current tracking, including physical hand movement during travel. No previous destination, proxy displacement or model offset becomes the reference for another approach. The XRI selection helper copies the tracked controller's world position/rotation exactly in every state, including while the other hand approaches. It never stays remotely anchored at a different visual position. Original controller models are never moved, hidden or swapped by grappling.

Body/head clearance remains mandatory for the calculated approach. If geometry prevents the aligned approach, it cancels rather than stopping short and snapping a virtual hand onto the hold. The system never reports an assisted grab just because the hold is within the head/shoulder comfort envelope: the actual tracked hand must satisfy the hold's normal grab-distance check and ordinary XRI filters.

The provider queues guarded `XROriginMovement` through the existing XRI mediator/body transformer. The CharacterController performs actual collision-constrained movement. Body capsule and head sphere sweeps/endpoint overlaps reject obstructed paths; new obstructions cancel travel. The provider never writes head/controller tracking transforms or rig rotation, teleports to an endpoint, disables collision, or routes around geometry. An obstructed straight-line approach is cancelled rather than forcing the player through the wall.

## Existing climb and stamina integration

- During travel, other locomotion providers are temporarily suspended, while gravity is locked off through its existing API. Tracking, input, UI and physical interactors remain enabled.
- Normal grabs are processed first. An already-selected object or a valid nearby physical grab prevents that hand from starting a grapple.
- **Hand stamina now defaults off globally:** Climb Stamina Controller > Enable Hand Stamina. With it off, travel/grips never deplete hand stamina or trigger fatigue exhaustion, and fatigue UI is hidden. Balance falls/BalanceLock remain independent and still cancel/release as before. See `ClimbCompletionGuide.md` for optional stamina and the new success-only app prompt.
- When global hand stamina is enabled, travel is **effort, not a fake grip**: Grappling Consumes Stamina uses the current difficulty's normal drain and no recovery during travel. Per-hand no-cost mode prevents travel/assisted-grip drain without bypassing enabled fatigue exhaustion, zero stamina or the always-active balance lock. The global switch does not change existing rate presets or selection tracking.
- At normal grabbing distance, the retained selection helper is still exactly at the tracked controller, not moved to a remote hold. `XRInteractionManager.IsSelectPossible` and ordinary `SelectEnter` run existing filters and callbacks. This legitimately updates stamina, ClimbProvider and RouteTimer. Hold interaction distances are not enlarged.
- The original ClimbProvider resumes after attachment or cancellation. Its active anchor is rebased through its public grab methods so prior support cannot undo assisted body movement. All hands continue following tracking during other-hand approaches. Completion after a movement step occurs before ordinary XRI selection processing, and the paused climb provider cannot also apply that frame's assisted translation.
- Grip release uses ordinary XRI exit. There is no nested rejection exit inside selectEntered. Exhaustion, balance falls, route changes, controller/feature disabling and blocked movement cancel safely. Release/re-press grip to try again after a cancellation; a held grip never auto-retries.
- Requires Existing Handhold optionally requires other-hand support at initiation. Difficulty availability never overrides the selected-route restriction. Cooldown remains configurable per hand.

**Timer validity is preserved:** a running route still aborts after 0.4 seconds with no real hold, or on a balance fall. Travel alone does not invent support or revive an aborted run. Keep a real other-hand grip during longer timed approaches; grab Start again after an abort. No attempt limits or result-display timing were changed.

## Presentation setup

1. Keep **Enable Without Calibration** on for both hands.
2. Enable **Grapple Mode Enabled By Default** on the mode owner if you want immediate launch availability; otherwise demonstrate pressing X.
3. To emphasise limited vertical reach, enable each hand's **Override Calibrated Values**, choose a generous horizontal threshold (e.g. 2 m) and vertical threshold around 0.2 m. Keep maximum distance / detection radius at the short-range **1.5 m** defaults.
4. Point approximately toward a nearby hold with clear body/head approach space. Hold its side grip until green attachment feedback. You may move/rotate the controller or look elsewhere after locking without maintaining aim. Then continue normal climbing.
5. Enable **Debug Visualize Eligible Targets** plus Scene Gizmos to show candidates. Per-hand Play Mode Inspector controls expose Refresh Target, Pull (requires actual grip held), Cancel / Release, shared mode toggle and status feedback.
6. Save persistent tuning outside Play Mode. Unity's normal Play Mode temporary-edit rules apply.

Line colours/width, opaque highlight colour, connected colour, indicators, haptics and debug sphere size remain Inspector-configurable. Both hands share highlight ownership safely when aiming at the same hold. Original physical controller renderers stay visible/unchanged throughout grab and release; there is no release-time controller-model swap or pose reset.

## Files and verification

The forgiving-targeting revision changes `AccessibilityGrappleController`, its custom Inspector, and only the two scene grapple components' targeting/range tuning. Setup tools automatically use the new class defaults for new objects and preserve authored tuning on upgrades. The existing movement provider, zero-offset helper, stamina, timer, calibration, scene geometry, imported samples and package locomotion remain intact. No manual rewiring is required.

**Forgiving targeting/lock verification: 71 checks passed (10 scene, 61 Play Mode/integration).** Checks cover serialized defaults and real hold-spacing measurement; actual X and both grip inputs; off-axis/cross-body targets that a direct ray misses; jitter stability; nearest-over-precise preference; switching threshold, retention cone and grace expiry/grip activation; complete aim/head reversal throughout travel and holding; incidental nearby hold selection prevention; hidden out-of-range locked lines with continued pulling and restoration; distant acquisition rejection; changed hand-ray occlusion; grip cancellation/no residual motion; genuine body obstruction, stamina depletion and balance fall; normal grabbing, valid supported Start-to-Finish, no body rollback, and four consecutive off-aim grapples with continued climbing, unchanged local tracking and zero accumulated offsets. Temporary fixtures are removed after verification. Historical checks below describe earlier revisions; their narrow targeting/live range-cancellation policies are superseded.

**95 checks passed for the alignment fix:** 6 scene checks and 89 Play Mode/integration checks. Three consecutive rising holds were tested independently for each hand, without resetting virtual climbing height between holds. Each checked current tracked-hand destination math, primarily upward travel to high holds, exact helper/tracking alignment throughout travel and grabbing, unchanged renderer visibility/local tracking/facing, normal grabbing distance, continued physical pulling and snap-free release. Small tracked hand movements between holds deliberately cleared normal-grab priority and verified that new poses, not stored offsets, drive successive grapples.

Further checks covered lowering the hand mid-pull, clear-aim targets with collision-blocked ascent for both controllers, cancellation/no residual movement, stamina/balance release, normal Start selection, legitimate assisted Finish, support handoff/no rollback, X toggling, and an actual authored non-convex hold with its original distance convention/filter. Earlier revisions passed 63 remote-hand and 71 assisted-movement checks; their remote-hand stopping/visual behaviour is superseded by this fix. Temporary verification code and scenes are removed after checking.

**Targeting/indicator verification:** 9 pre-fix reproduction checks confirmed back-face-only mesh rejection, collider-centre rejection despite a direct hit, the strict head-plane side veto, and stale out-of-range guidance. **74 post-fix Play Mode/integration checks passed**, covering mirrored cross-body targeting and actual grip/attachment, one-sided meshes, wide/near holds, side/behind-head limits, immediate line invalidation, Inspector/calibration/detection caps, rig-scale-independent world range, occupied/interaction-layer-incompatible holds, normal grabs, collision preflight, diagnostic throttling, consecutive aligned cross-body grapples and legitimate assisted Finish. Temporary verification fixtures are removed afterward.

**Manual headset checks:** PICO control mapping, both aim directions, line/highlight/hand appearance in stereo, haptics, preferred speed/smoothing, real posture changes, and wall/ceiling clearance in the authored scene. Headless synthetic input and collision tests do not establish physical comfort or headset presentation.
