using System.Collections.Generic;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #8 — the Mission 01 helicopter interior cinematic rig.
    ///
    /// What changed vs QA fix #7:
    ///   - The "Kane" is no longer a crude primitive humanoid. It is a VISUAL-ONLY clone of the
    ///     PRODUCTION Toon Soldier (same FBX, same URP material, same avatar, same controller),
    ///     found live in the scene on the gameplay Player's Animator and instantiated here. The
    ///     clone is scrubbed of every gameplay authority component and driven into a seated pose
    ///     by <see cref="CinematicKanePose"/>. The player instantly recognises "that is my Kane".
    ///   - The cabin is a proper dark military metal interior: ribbed structural frames, bench
    ///     seating, cockpit partition with window, an open side door, windows with a moving night
    ///     exterior (flight motion cue), overhead rail + grab handles, restrained cool key light
    ///     + low warm fill + a tiny red emergency accent (no muddy all-yellow fill).
    ///   - Camera anchor definitions live HERE (rig-relative) and are exposed to
    ///     <see cref="StoryCameraController"/>, which no longer hard-codes interior offsets. This
    ///     makes the anchors single-sourced and lets them be re-authored around the real character
    ///     dimensions (no clipping, no object cutting vertically through Kane).
    ///   - The cabin's subtle flight vibration is published so the camera can apply the IDENTICAL
    ///     offset each frame — Kane never drifts in frame.
    ///
    /// All built in code from primitives + the cloned character; no external scene authoring.
    /// </summary>
    public sealed class HelicopterInteriorRig : MonoBehaviour
    {
        // ---- camera anchor definitions (rig-local) ----
        private struct InteriorShot
        {
            public readonly string CueId;
            public readonly Vector3 LocalPos;
            public readonly Vector3 LocalLook;
            public readonly float Fov;
            public InteriorShot(string cue, Vector3 pos, Vector3 look, float fov)
            { CueId = cue; LocalPos = pos; LocalLook = look; Fov = fov; }
        }

        // Anchors re-authored around the real Toon Soldier (pelvis ~ (-1.0, 0.5, 0), facing +X).
        // Each is placed in open cabin space with clearance from walls/ribs so nothing clips and
        // no vertical element cuts through Kane.
        private static readonly InteriorShot[] Shots =
        {
            // SHOT 1 — WIDE: Kane seated + cabin context (Kane left third, bench/window behind).
            new InteriorShot("m01_interior_kane",
                new Vector3(1.3f, 1.45f, -0.7f), new Vector3(-0.95f, 1.0f, 0.0f), 46f),
            // SHOT 2 — MEDIUM: chest / helmet framing with cabin sides.
            new InteriorShot("m01_interior_kane_close",
                new Vector3(0.55f, 1.2f, -0.35f), new Vector3(-0.9f, 1.15f, 0.0f), 40f),
            // SHOT 3 — FRONT/COCKPIT: looking down-cabin toward cockpit + door (Kane foreground left).
            new InteriorShot("m01_interior_front",
                new Vector3(0.85f, 1.4f, 1.7f), new Vector3(0.2f, 1.1f, -2.2f), 48f),
        };

        // Shared vibration constants so the camera can mirror the cabin motion exactly (no drift).
        private const float VibFreqX = 23f;
        private const float VibFreqY = 17f;
        private const float VibAmpX = 0.01f;
        private const float VibAmpY = 0.006f;
        private const float VibRotDeg = 0.35f;

        private GameObject _cabinRoot;
        private GameObject _kaneVisual;
        private readonly List<Transform> _windowStreaks = new List<Transform>();
        private float _streakWrap = 4.5f;
        private bool _active;

        public bool IsActive => _active;
        /// <summary>Current cabin vibration position offset (rig-local). Camera mirrors this.</summary>
        public Vector3 VibrationOffset { get; private set; }
        /// <summary>Current cabin vibration rotation. Camera mirrors this.</summary>
        public Quaternion VibrationRotation { get; private set; } = Quaternion.identity;

        /// <summary>Sets up the interior at a world position far from the gameplay lane.</summary>
        /// <param name="sourceKaneVisual">The live gameplay Toon Soldier transform to clone (visual-only).</param>
        public void Setup(Vector3 worldPos, Transform sourceKaneVisual)
        {
            transform.position = worldPos;
            BuildCabin();
            BuildCinematicKane(sourceKaneVisual);
            _active = true;
            LogAnchors();
            ValidateAnchorClearance();
            Debug.Log("[STORY M01] Interior cinematic setup complete: real Toon Soldier Kane clone + military cabin.");
        }

        /// <summary>Resolves a rig-relative interior camera anchor to world space.</summary>
        public bool TryGetCameraAnchor(string cueId, out Vector3 worldPos, out Quaternion worldRot, out float fov)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            fov = 50f;
            for (int i = 0; i < Shots.Length; i++)
            {
                if (Shots[i].CueId != cueId) continue;
                Vector3 localPos = Shots[i].LocalPos;
                Vector3 localLook = Shots[i].LocalLook;
                worldPos = transform.position + localPos;
                Vector3 worldLook = transform.position + localLook;
                worldRot = Quaternion.LookRotation(worldLook - worldPos, Vector3.up);
                fov = Shots[i].Fov;
                return true;
            }
            return false;
        }

        /// <summary>Hides the interior rig entirely.</summary>
        public void Teardown()
        {
            if (!_active) return;
            _active = false;

            if (_cabinRoot != null) _cabinRoot.SetActive(false);
            if (_kaneVisual != null) _kaneVisual.SetActive(false);
            VibrationOffset = Vector3.zero;
            VibrationRotation = Quaternion.identity;
            Debug.Log("[STORY M01] Interior rig torn down.");
        }

        private void BuildCabin()
        {
            _cabinRoot = new GameObject("Story_M01_Cabin");
            _cabinRoot.transform.SetParent(transform, false);

            Material metalDark = LitMat(new Color(0.13f, 0.14f, 0.16f), 0.65f, 0.25f);   // gunmetal
            Material metalMed = LitMat(new Color(0.19f, 0.20f, 0.18f), 0.5f, 0.3f);      // olive-grey
            Material metalFloor = LitMat(new Color(0.09f, 0.09f, 0.10f), 0.2f, 0.15f);   // dark tread
            Material benchMat = LitMat(new Color(0.05f, 0.05f, 0.06f), 0.0f, 0.4f);      // black seat
            Material ribMat = LitMat(new Color(0.22f, 0.23f, 0.21f), 0.7f, 0.35f);       // bright frame
            Material accentMat = LitMat(new Color(0.55f, 0.10f, 0.07f), 0.1f, 0.3f);     // hazard red
            Material glassMat = UnlitMat(new Color(0.12f, 0.16f, 0.22f, 0.55f));
            Material exteriorMat = UnlitMat(new Color(0.03f, 0.04f, 0.07f));

            const float W = 1.7f;   // half width
            const float H = 2.5f;   // ceiling height
            const float D = 2.3f;   // half depth

            // Floor + ceiling
            AddCube(_cabinRoot.transform, "Floor", new Vector3(W * 2f, 0.1f, D * 2f),
                new Vector3(0f, -0.05f, 0f), metalFloor);
            AddCube(_cabinRoot.transform, "Ceiling", new Vector3(W * 2f, 0.1f, D * 2f),
                new Vector3(0f, H, 0f), metalDark);

            // Left wall (solid, has bench against it) + window cutout suggested by a recessed pane.
            AddCube(_cabinRoot.transform, "LeftWall", new Vector3(0.12f, H, D * 2f),
                new Vector3(-W, H * 0.5f, 0f), metalMed);
            // Right wall in two segments leaving a door opening (z from -0.9 to 0.9 open).
            AddCube(_cabinRoot.transform, "RightWallFront", new Vector3(0.12f, H, (D - 0.9f) * 2f),
                new Vector3(W, H * 0.5f, 0.9f + (D - 0.9f)), metalMed);
            AddCube(_cabinRoot.transform, "RightWallRear", new Vector3(0.12f, H, (D - 0.9f) * 2f),
                new Vector3(W, H * 0.5f, -(0.9f + (D - 0.9f))), metalMed);
            // Door frame around the opening
            AddCube(_cabinRoot.transform, "DoorFrameTop", new Vector3(0.16f, 0.18f, 1.9f),
                new Vector3(W, H - 0.35f, 0f), ribMat);
            AddCube(_cabinRoot.transform, "DoorPillarFront", new Vector3(0.16f, H - 0.4f, 0.16f),
                new Vector3(W, (H - 0.4f) * 0.5f, 0.95f), ribMat);
            AddCube(_cabinRoot.transform, "DoorPillarRear", new Vector3(0.16f, H - 0.4f, 0.16f),
                new Vector3(W, (H - 0.4f) * 0.5f, -0.95f), ribMat);

            // Cockpit partition (front, z=-D) with a recessed window.
            AddCube(_cabinRoot.transform, "CockpitPartition", new Vector3(W * 2f, H, 0.14f),
                new Vector3(0f, H * 0.5f, -D), metalDark);
            // Rear bulkhead
            AddCube(_cabinRoot.transform, "RearBulkhead", new Vector3(W * 2f, H, 0.14f),
                new Vector3(0f, H * 0.5f, D), metalDark);

            // Structural ribs along both side walls (break up the flat panels → reads as airframe).
            for (int i = -2; i <= 2; i++)
            {
                float z = i * 0.95f;
                AddCube(_cabinRoot.transform, "RibL", new Vector3(0.08f, H - 0.1f, 0.12f),
                    new Vector3(-W + 0.08f, H * 0.5f, z), ribMat);
                // Right ribs only where the wall exists (skip the door opening zone).
                if (Mathf.Abs(z) > 1.0f)
                    AddCube(_cabinRoot.transform, "RibR", new Vector3(0.08f, H - 0.1f, 0.12f),
                        new Vector3(W - 0.08f, H * 0.5f, z), ribMat);
            }

            // Floor tread strips (subtle panelling cue).
            for (int i = -2; i <= 2; i++)
            {
                AddCube(_cabinRoot.transform, "FloorStrip", new Vector3(W * 2f - 0.2f, 0.02f, 0.06f),
                    new Vector3(0f, 0.01f, i * 0.9f), metalMed);
            }

            // Bench seat along the left wall (where Kane sits).
            AddCube(_cabinRoot.transform, "SeatBase", new Vector3(0.7f, 0.5f, 3.2f),
                new Vector3(-W + 0.45f, 0.25f, 0f), benchMat);
            AddCube(_cabinRoot.transform, "SeatBack", new Vector3(0.16f, 1.0f, 3.2f),
                new Vector3(-W + 0.12f, 0.95f, 0f), benchMat);

            // Overhead rail + grab handles.
            AddCylinder(_cabinRoot.transform, "OverheadRail", new Vector3(0.08f, 0.08f, D * 2f - 0.4f),
                new Vector3(0f, H - 0.25f, 0f), Quaternion.Euler(90f, 0f, 0f), ribMat);
            for (int i = -1; i <= 1; i += 2)
            {
                AddCylinder(_cabinRoot.transform, "Handle", new Vector3(0.05f, 0.4f, 0.05f),
                    new Vector3(0f, H - 0.55f, i * 1.2f), Quaternion.identity, ribMat);
            }

            // Windows: recessed panes on the left wall + cockpit partition.
            AddCube(_cabinRoot.transform, "WindowLeft", new Vector3(0.04f, 0.85f, 1.6f),
                new Vector3(-W - 0.02f, 1.35f, -0.3f), glassMat);
            AddCube(_cabinRoot.transform, "WindowCockpit", new Vector3(1.4f, 0.7f, 0.04f),
                new Vector3(0f, 1.55f, -D - 0.02f), glassMat);

            // Hazard strip near the door (restrained emergency accent).
            AddCube(_cabinRoot.transform, "HazardStrip", new Vector3(0.04f, 0.08f, 1.8f),
                new Vector3(W - 0.02f, 0.35f, 0f), accentMat);

            // Moving night exterior outside the windows (flight motion cue).
            BuildWindowExterior(exteriorMat);

            // ---- restrained cinematic lighting ----
            // Key: cool light streaming in through the open door / windows (lights Kane's right side).
            AddLight(_cabinRoot.transform, "KeyLight", new Vector3(1.5f, 1.9f, 0.4f),
                new Color(0.72f, 0.82f, 0.96f), 1.3f, 7f, 55f);
            // Fill: low warm interior glow so shadows are not pure black (NOT a full yellow wash).
            AddLight(_cabinRoot.transform, "FillLight", new Vector3(-0.3f, 2.0f, 0.6f),
                new Color(1.0f, 0.86f, 0.7f), 0.32f, 6f, 75f);
            // Accent: tiny red emergency indicator near the door (very restrained).
            AddLight(_cabinRoot.transform, "EmergencyLight", new Vector3(W - 0.2f, 0.4f, 0f),
                new Color(1.0f, 0.22f, 0.16f), 0.28f, 2.2f, 60f);
        }

        private void BuildWindowExterior(Material exteriorMat)
        {
            // A dark panel outside the left + cockpit windows; thin emissive streaks drift across
            // to suggest the helicopter is flying past a dark city at night.
            var ext = new GameObject("WindowExterior");
            ext.transform.SetParent(_cabinRoot.transform, false);

            // Big dark panels well outside the cabin so they read as distant sky/ground.
            AddCube(ext.transform, "SkyPanelLeft", new Vector3(0.05f, 6f, 10f),
                new Vector3(-3.2f, 1.4f, 0f), exteriorMat);
            AddCube(ext.transform, "SkyPanelFront", new Vector3(10f, 6f, 0.05f),
                new Vector3(0f, 1.4f, -3.6f), exteriorMat);

            // Drifting light streaks (passing buildings / ground lights).
            Material streakMat = UnlitMat(new Color(0.85f, 0.78f, 0.55f));
            _streakWrap = 6f;
            for (int i = 0; i < 4; i++)
            {
                var left = AddCube(ext.transform, "StreakL", new Vector3(0.03f, 0.18f, 0.06f),
                    new Vector3(-3.15f, 0.5f + i * 0.9f, -2.5f + i * 1.4f), streakMat);
                _windowStreaks.Add(left.transform);

                var front = AddCube(ext.transform, "StreakF", new Vector3(0.06f, 0.16f, 0.03f),
                    new Vector3(-2.5f + i * 1.4f, 0.7f + i * 0.8f, -3.55f), streakMat);
                _windowStreaks.Add(front.transform);
            }
        }

        private void BuildCinematicKane(Transform sourceVisual)
        {
            if (sourceVisual == null)
            {
                Debug.LogWarning("[STORY M01] No source Toon Soldier visual found — cinematic Kane skipped.");
                return;
            }

            // Clone the LIVE production character (same FBX / material / avatar / controller as
            // the gameplay Kane). This guarantees an exact visual match.
            _kaneVisual = Object.Instantiate(sourceVisual.gameObject);
            _kaneVisual.name = "Story_KaneCinematic";
            _kaneVisual.transform.SetParent(_cabinRoot.transform, false);

            // Seated on the bench, back against the left wall, facing into the cabin (+X / door).
            _kaneVisual.transform.localPosition = new Vector3(-1.05f, 0.5f, 0f);
            _kaneVisual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            _kaneVisual.transform.localScale = Vector3.one;

            // Scrub EVERY gameplay authority from the clone — it is visual-only.
            ScrubGameplayComponents(_kaneVisual);

            // Ensure the Animator runs the production idle (NeutralStance) so the upper body is the
            // recognisable, alive Kane. applyRootMotion off so the pose never walks the clone away.
            var animator = _kaneVisual.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.enabled = true;
            }

            // Drive the legs into a seated pose without touching the gameplay controller.
            if (animator != null && !_kaneVisual.GetComponent<CinematicKanePose>())
                _kaneVisual.AddComponent<CinematicKanePose>();

            Debug.Log("[STORY M01] Cinematic Kane = visual-only clone of production Toon Soldier (seated pose).");
        }

        /// <summary>Removes any gameplay/physics authority from the clone (visual-only guarantee).</summary>
        private static void ScrubGameplayComponents(GameObject root)
        {
            DestroyAll<PlayerController>(root);
            DestroyAll<PlayerHealth>(root);
            DestroyAll<PlayerAnimationBridge>(root);
            DestroyAll<PlayerInputReader>(root);
            DestroyAll<PlayerLaneBounds>(root);
            DestroyAll<ToonSoldierPresentationAim>(root);
            DestroyAll<WeaponController>(root);
            DestroyAll<Collider>(root);
            DestroyAll<Rigidbody>(root);
        }

        /// <summary>
        /// Destroys every component of type T under root. Uses DestroyImmediate in Edit Mode (no
        /// frame loop processes deferred Destroy there) and Destroy at runtime.
        /// </summary>
        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            foreach (var c in root.GetComponentsInChildren<T>(true))
            {
                if (Application.isPlaying) Object.Destroy(c);
                else Object.DestroyImmediate(c);
            }
        }

        private void Update()
        {
            if (!_active || _cabinRoot == null) return;

            float t = Time.time;
            // Compute + apply the cabin vibration (flight feel).
            Vector3 pos = new Vector3(
                Mathf.Sin(t * VibFreqX) * VibAmpX,
                Mathf.Sin(t * VibFreqY) * VibAmpY,
                0f);
            Quaternion rot = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 11f) * VibRotDeg);
            _cabinRoot.transform.localPosition = pos;
            _cabinRoot.transform.localRotation = rot;

            // Publish the exact same offset so the camera mirrors it (no subject drift).
            VibrationOffset = pos;
            VibrationRotation = rot;

            // Drift the window streaks for a subtle motion cue, wrapping around.
            if (_windowStreaks.Count > 0)
            {
                float drift = Time.deltaTime * 1.6f;
                for (int i = 0; i < _windowStreaks.Count; i++)
                {
                    var tr = _windowStreaks[i];
                    Vector3 lp = tr.localPosition;
                    lp.z += drift;
                    if (lp.z > 3f) lp.z -= _streakWrap + 3f;
                    tr.localPosition = lp;
                }
            }
        }

        private void LogAnchors()
        {
            for (int i = 0; i < Shots.Length; i++)
            {
                Vector3 wp = transform.position + Shots[i].LocalPos;
                Debug.Log($"[STORY M01] Interior anchor '{Shots[i].CueId}' world {wp} fov {Shots[i].Fov} " +
                          $"(look {(transform.position + Shots[i].LocalLook)}).");
            }
        }

        /// <summary>
        /// Camera collision safety (QA fix #8 section H): read-only check that no interior camera
        /// anchor sits inside a cabin renderer's bounds (which would cause clipping). Logs a warning
        /// per offending anchor. Cannot throw — failures are swallowed so setup never breaks.
        /// </summary>
        private void ValidateAnchorClearance()
        {
            var renderers = GetComponentsInChildren<Renderer>();
            for (int i = 0; i < Shots.Length; i++)
            {
                Vector3 anchor = transform.position + Shots[i].LocalPos;
                for (int r = 0; r < renderers.Length; r++)
                {
                    Renderer rend = renderers[r];
                    if (rend == null) continue;
                    Bounds b = rend.bounds;
                    if (b.Contains(anchor))
                    {
                        Debug.LogWarning($"[STORY M01] Camera anchor '{Shots[i].CueId}' is INSIDE renderer " +
                                         $"'{rend.name}' bounds — will clip. Reposition the anchor.");
                    }
                }
            }
        }

        // ---- primitive helpers ----

        private static GameObject AddCube(Transform parent, string name, Vector3 scale, Vector3 pos, Material mat) =>
            AddPart(parent, PrimitiveType.Cube, name, scale, pos, Quaternion.identity, mat);

        private static GameObject AddCylinder(Transform parent, string name, Vector3 scale, Vector3 pos, Quaternion rot, Material mat) =>
            AddPart(parent, PrimitiveType.Cylinder, name, scale, pos, rot, mat);

        private static GameObject AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 pos, Quaternion rot, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (col != null) col.enabled = false;
            return go;
        }

        private static void AddLight(Transform parent, string name, Vector3 pos, Color color,
            float intensity, float range, float spotAngle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = spotAngle * 0.5f;
            light.shadows = LightShadows.Soft;
        }

        private static Material LitMat(Color color, float metallic, float smoothness)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
            return mat;
        }

        private static Material UnlitMat(Color color)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat == null || mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            return mat;
        }
    }
}
