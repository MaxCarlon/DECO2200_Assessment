# Levelstep tutorial: authoring and operation

## Where everything is

- Scene: `Assets/Levelstep/Scenes/ClimbingScene.unity` > **Levelstep Tutorial**.
- Reusable UI prefab: `Assets/Levelstep/Prefabs/LevelstepTutorial.prefab`.
- Initial page assets: `Assets/Levelstep/Settings/TutorialPages/`.
- Original artwork: `Assets/Levelstep/TutorialAnimations/` (unchanged).

The panel is stationary in the world at its authored Transform. It starts **hidden**, on page **0 (Controllers)**. Point at **SHOW TUTORIAL** and use the same front/index trigger used by the difficulty menu. The toggle is on its own canvas and remains accessible when the panel is hidden. Hiding preserves the current page.

The prefab is authored and visible in Edit Mode. Its **Initially Visible** setting controls what happens when Play starts; it does not prevent you from previewing the panel in the editor.

**Scene view owns layout.** Positions, sizes, scale, rotation, anchors and pivots are saved on the actual RectTransforms. Runtime configuration, animation, navigation and Inspector preview buttons never reapply stored geometry. The Inspector owns content, ordering, animation, colours and behaviour.

## Reorder pages

1. In the Hierarchy select **Levelstep Tutorial** (the root object).
2. In its **Tutorial Panel** component, find the list headed **Pages — gameplay navigation order** at the top of the custom Inspector.
3. Drag the handle on the **left of a row** up or down. The row order is the actual gameplay Previous/Next order.
4. **+** adds an empty slot to assign a TutorialPage asset; **−** removes the selected reference without deleting its asset.
5. Save the scene with **Ctrl+S**. For a shared prefab order, edit the prefab in Prefab Mode and save it, or deliberately apply the instance's Pages override to the prefab.

The current ClimbingScene instance is ordered **Controllers → Paths → Push → Board**. Numeric asset filenames do not control navigation and do not need renaming. Reordering preserves the referenced page assets, their text, animations and settings. During Play Mode, reordering keeps the currently displayed page selected by asset and updates its counter/index. Make persistent order changes outside Play Mode.

## Quick start: enter your own instructions

1. Select a page in `Settings/TutorialPages`.
2. Edit **Page Title** and **Description**. Descriptions are deliberately blank for you to author.
3. Edit **Image** > **Frames Per Second**, **Loop**, **Ping Pong**, **Playback Speed**, and **Auto Play** as needed.
4. Select **Levelstep Tutorial** in the scene and click **Preview Initial Page (Show Panel)** or navigate with its Inspector buttons.
5. Enter Play to test frame playback and pointer interaction. Inspector changes to the panel and the currently displayed page apply during Play. Normal Unity Play Mode edits are temporary; make persistent tuning changes outside Play.

Text is independent TextMeshPro UI, never baked into an image by this system. Any text already inside your original artwork remains part of that artwork.

## Add a page or replace frames

### From a folder

1. Select a folder of imported sprites in the Project window.
2. Choose **Assets > Create > Levelstep > Tutorial Page From Selected Folder** and choose where to save it.
3. Edit the title/description/playback settings.
4. Add the page asset to the scene's **Tutorial Panel > Pages** list using **+**, then drag it to the desired row.

Alternatively create a blank page using **Assets > Create > Levelstep > Tutorial Page**, assign **Source Folder** at the bottom of its Inspector, then click **Populate Frames From Folder (Natural Order)**.

The utility reads existing Sprite sub-assets, including subfolders, sorted by natural numeric filename order (`frame2` before `frame10`). It does not change the images or their import settings. New images must first be imported as **Sprite (2D and UI)** using Unity's Texture Inspector. Re-run Populate after adding/removing frames in the folder; there is no folder scanning in a build.

### Manual sequence or static image

- Drag Sprite assets into **Image > Frames** in the desired order.
- For a static page, leave Frames empty and assign **Static Image**.
- Frames take priority over Static Image. Null frame entries are skipped; Static Image is a fallback if no valid frames remain.
- Add/remove/reorder page assets in **Tutorial Panel > Pages**. There is no fixed page count.
- The initial artwork order is Controllers (1 static), Push (6 frames, 0–5), Paths (3 frames, 0–2), Board (8 frames, 0–7).

## Inspector settings

### General

| Setting | Meaning |
| --- | --- |
| Initially Visible | Open or closed at scene startup. |
| Initial Page | Zero-based page index; out-of-range indices are safely clamped. |
| Wrap Navigation | Wrap at either end; otherwise stop and disable the unavailable button. |
| Restart Animation On Page Change | On: restart on each visit. Off: remember each page asset's frame position and paused state. |
| Default Animation Speed | Global multiplier applied to every page's Playback Speed. Zero freezes playback. |
| Play While Hidden | Advance the current page's animation while the panel is hidden. Off-page animations do not advance. |
| Use Unscaled Time | Playback continues when the game's time scale is paused. |

Hiding and showing never selects a different page. With hidden playback off, it also preserves the exact fractional animation position. Page state is retained within this scene session; participant reset/scene reload restores the initial tutorial configuration.

### Scene view: permanent layout editing

1. Work **outside Play Mode** and expand **Levelstep Tutorial** in the Hierarchy.
2. If necessary, click **Preview Initial Page (Show Panel)** in the root Inspector. This only previews content/visibility; it does not reset your layout.
3. Select the element you want to arrange and use the **Rect Tool (T)** in the Scene view. Move/Rotation/Scale tools and the element's normal RectTransform Inspector also work.
4. Save the scene with **Ctrl+S** before entering Play Mode.

Useful objects:

| Object | What to arrange |
| --- | --- |
| Levelstep Tutorial root Transform | Whole interface's world position, rotation and scale. |
| Tutorial Panel | Main canvas position, rotation, scale, width and height. |
| Background | Backing bounds and anchors. |
| Page Title / Description | Independent text boxes and their anchors/pivots. |
| Image Viewport | Image area and clipping bounds. |
| Frame Image (inside Image Viewport) | Image rectangle, position, anchors, pivot, rotation and scale. |
| Previous / Next / Page Counter | Button/counter arrangement and dimensions. |
| Tutorial Toggle (Always Accessible) | Toggle canvas placement, orientation, scale and dimensions. |
| Show Hide Tutorial / Label | Toggle button and label geometry. |

All these RectTransforms are authoritative. Changing page content, colours, FPS, image scaling, or clicking Apply Configuration keeps them intact. There are no runtime layout groups or aspect fitters driving them. Fit/Stretch/Fill And Crop change rendered pixels/UVs, not the Frame Image rectangle. The viewport still clips content outside its authored bounds.

Default canvas scale is **0.001**; layout dimensions are UI units (1000 units = 1 metre at this scale). Stretch anchors still respond to their parent resizing as normal Unity UI behaviour. Keep custom changes as scene-instance overrides, or save them on the prefab for reuse. Play Mode changes follow Unity's usual temporary-edit behaviour; permanent visual design should happen in Edit Mode.

**Background Color**, **Background Opacity**, and **Default Font** remain on the root's Tutorial Panel component.

The tutorial is not parented to the headset. It uses ordinary world-space depth testing, like the difficulty menu, so position it in front of geometry rather than inside a wall. It has no physical colliders.

### Inspector: content appearance

Edit **Default Content Appearance** on the Tutorial Panel for shared fonts, colours, alignment and image scaling. Enable **Override Content Appearance** on a page to use that page's corresponding appearance settings.

- Image Scaling: **Fit** (preserve full image, possible empty margins), **Stretch**, or **Fill And Crop** (preserve aspect and crop pixels to fill the authored Image rectangle).
- Title and description Font Size, Color and Alignment.
- Optional Font Override.

Appearance overrides never reposition or resize elements. Every page uses the Scene view's shared layout. Old script-based positions/sizes remain available in the read-only **Legacy layout values** foldouts for compatibility, but are never automatically applied. Existing serialized settings have not been deleted.

Text is wrapped, with auto-sizing off. Oversized copy uses ellipsis rather than covering the navigation controls: increase the text area or reduce Font Size to show more. Standard TMP rich-text tags can be used in descriptions.

### Navigation and toggle appearance

- **Previous** and **Next** expose their labels. Arrange and resize their actual RectTransforms in the Scene view.
- **Navigation Appearance** exposes normal, hovered, pressed, selected and disabled colours; label colour/font size; border colour/thickness.
- **Show Page Counter**, font size and colour control the optional counter. **Counter Format** uses `{0}` for the current page and `{1}` for the total. Counter geometry is Scene-authored.
- **Show Label**, **Hide Label**, **Toggle Hidden Color**, **Toggle Visible Color** and **Toggle Appearance** control the toggle's presentation. Its geometry is Scene-authored.
- The toggle also shows a persistent outline while the panel is open. The text indicates the available Show/Hide action.
- Buttons use normal UGUI colour transitions and pointer selection. Explicit navigation avoids automatically connecting them to the difficulty menu.

## Editor preview workflow

On **Tutorial Panel**, the custom Inspector provides:

- **Apply Configuration (Content & Appearance Only)**: update text, sprites, colours, fonts, animation settings, visibility labels and navigation state. It preserves the current page/playback state and every RectTransform. It does not recreate UI objects.
- **Preview Initial Page (Show Panel)**: explicitly show the configured initial page in Edit Mode.
- **Previous / Next / Show / Hide**: preview page/visibility changes.
- **Restart Animation / Pause / Resume**: control playback during Play Mode.

On a page asset, **Refresh Open Tutorial Previews** updates any open panel currently displaying that page. Edit Mode changes are explicit and Undoable. No prefab/scene rebuilding runs from `OnValidate`.

For a reusable layout, edit the tutorial prefab's RectTransforms in Prefab Mode and save. For scene-specific tuning, edit the scene instance's RectTransforms and save the scene.

### Reset layout explicitly

**Reset Layout to Defaults…** is a separate button at the bottom of the Tutorial Panel Inspector. It is available only outside Play Mode and asks for confirmation. It restores the known UI elements' creation geometry without rebuilding objects or changing the root's world placement, page order, content, animation or colour settings. Use **Ctrl+Z** to undo it.

This reset runs only when explicitly requested, or during creation of a brand-new tutorial prefab. Startup, Apply Configuration, preview, page changes and content edits never trigger it.

## Standalone sequence player

`ImageSequencePlayer` can also be added to another GameObject and assigned a target UI Image. Its standalone Sequence settings support static images, arbitrary Sprite arrays, FPS, looping, ping-pong, playback speed, auto-play, unscaled time, and fit/stretch/crop scaling. Fill And Crop uses `ImageSequenceCrop` on the existing Image to crop UVs without resizing it; the effect is reused or added if needed. No AspectRatioFitter is required. Disable any independent layout driver if you want that Image's RectTransform fully hand-authored.

Public controls: `Pause`, `Resume`, `RestartPlayback`, `SetSequence`, `SetPresentationVisible`. The tutorial uses these same controls. Non-looping animations stop on their final frame; non-looping ping-pong plays one full out-and-back trip. Resume does not restart a completed animation; use Restart.

The player stays on the tutorial root, outside the hideable panel, to support hidden playback. Only the current page's sequence runs. Static, paused, completed and hidden-paused sequences disable the player's Update. Playback swaps existing Sprite references and creates no textures or GIFs.

## Integration tools

- **Tools > Levelstep > Tutorial > Add Tutorial to Active Scene** instantiates the existing tutorial prefab. If that scene already has a tutorial, it selects it without replacing content/layout.
- **Create Assets and Prefab If Missing** creates defaults only when the prefab is missing. Existing page assets are reused without overwriting your text or frames.
- These tools do not create another EventSystem, input action asset, XR rig or interaction manager. A new scene needs the same XR UI input infrastructure as ClimbingScene.
- **Upgrade Image Rendering (Preserve Layout)** upgrades older tutorial prefabs/instances by disabling the old AspectRatioFitter and adding the UV crop effect to the same Image. It does not change RectTransforms or rebuild objects. This upgrade has already been applied to ClimbingScene and its tutorial prefab; no manual migration is required there.
- **Advanced: authored UI references** in the panel Inspector is for deliberately replacing UI objects. The supplied prefab is already fully wired; ordinary authoring does not require touching this foldout.

## Validation and remaining visual checks

The Scene-authored layout update passed **52 targeted checks**: 21 Edit Mode/preservation checks, 28 Play Mode checks, and 3 post-Play/save-reopen checks. Checks included every tutorial RectTransform's position, size, rotation, scale, anchors and pivot; serialized list add/remove/reorder; gameplay navigation order; non-rebuilding Apply Configuration; explicit reset/Undo; all image scaling modes; UV cropping; animation; and tracked UI interaction. The four existing page assets remained byte-for-byte unchanged, preserving authored text, frames and playback settings.

The initial tutorial implementation also passed 31 scene/asset and 50 Play Mode checks for frame playback policies, empty/missing content and UI interaction. The integration retains the existing single XR EventSystem, three difficulty buttons, and 26 climbing holds. No original images or unrelated gameplay scripts were changed.

Headless checks cannot establish headset legibility, binocular rendering, pointer comfort, or the best physical placement. In the PICO headset, check all four pages, the size of the controller diagram, hover/pressed/selected contrast, and the toggle's reachability from the actual participant start pose. All these presentation values are Inspector-editable.
