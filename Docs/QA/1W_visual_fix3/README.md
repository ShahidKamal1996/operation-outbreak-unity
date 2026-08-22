# Milestone 1W - Visual QA fix #3 preview frames

**These are NOT Unity screenshots.** No Unity Editor is available in the Arena sandbox,
so these frames were produced by an offline software rasteriser that reads the committed
scene, prefab and material YAML directly and reprojects it through the *authored* camera
configuration:

| Camera property | Value (read from `Gameplay_Prototype.unity`) |
| --- | --- |
| Position | `(0, 11, playerZ - 11)` |
| Rotation | pitch `31°`, no yaw/roll |
| Field of view | `44°` **vertical** |
| Aspect | `9:16` portrait (540 x 960) |

Shading is a single Lambert term with no shadows, no post-processing, no URP volume, no
ambient/GI and no textures, so **tone and contrast are approximate**. Geometry, layout,
scale, silhouette and base colours are exact - which is what these frames are for.

| Frame | Player Z | What it shows |
| --- | --- | --- |
| `offline-approximation-section1.png` | 0 | Section 1 - relatively intact evacuation checkpoint |
| `offline-approximation-section2.png` | 30 | Section 2 - damaged and abandoned |
| `offline-approximation-section3-final.png` | 48 | Section 3 / final approach - heavily compromised, collapsed overpass roadblock |

The magenta boxes are the pre-existing `TestTargets` prototype dummies (unchanged by this
fix). Green / orange / yellow / dark boxes are stand-ins for the infected, the Runner, the
projectiles and the player, inserted only to verify readability against the environment
palette; they are not part of the scene.

**Still requires real manual Unity QA in portrait.** Lighting response, URP post-processing,
shadow contact and the actual character/animation silhouettes cannot be validated here.
