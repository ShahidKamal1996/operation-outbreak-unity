using System.Collections.Generic;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #11 — REBUILT helicopter interior staging.
    ///
    /// QA fix #8/9/10 gave the interior a real Toon Soldier Kane, a fade and a valid controller,
    /// but the SPATIAL STAGING was incoherent: the clone was placed by its PIVOT (which sits at the
    /// model's FEET), so the whole body was lifted to the ceiling ("Kane hanging near the roof"),
    /// and the cameras — aimed at where Kane's chest SHOULD have been — ended up framing the bench
    /// and a structural pillar instead of Kane.
    ///
    /// This version builds ONE coherent local-space stage:
    ///   - the floor is the authoritative y=0; every dimension is authored against it;
    ///   - Kane is anchored FEET-ON-FLOOR using the clone's measured renderer bounds (robust to
    ///     wherever the model pivot actually is), and his pelvis is lowered onto the bench by
    ///     CinematicKanePose (HumanPose.bodyPosition);
    ///   - explicit KaneSeatAnchor / camera anchors / look targets live as real Transforms so they
    ///     are visible in Scene View, addressable by name, and testable;
    ///   - each camera aims at its target and is validated for obstruction against the cabin
    ///     occluders (sampled segment-vs-bounds), logging the offending object if blocked.
    ///
    /// All built in code from primitives + the cloned character; no external scene authoring.
    /// The rig stays isolated at y=-300 so it never touches gameplay.
    /// </summary>
    public sealed class HelicopterInteriorRig : MonoBehaviour
    {
        // ---- public names (Scene View / tests) ----
        public const string AnchorEstablishing = "InteriorEstablishingCamera";
        public const string AnchorMedium = "KaneMediumCamera";
        public const string AnchorClose = "KaneCloseCamera";
        public const string AnchorCockpit = "CockpitCamera";
        public const string TargetChest = "KaneChestTarget";
        public const string TargetHead = "KaneHeadTarget";
        public const string TargetCockpit = "CockpitLookTarget";
        public const string SeatAnchorName = "KaneSeatAnchor";
        public const string KaneName = "Story_KaneCinematic";

        // ---- cabin dimensions (local space, floor = y=0) ----
        private const float HalfWidth = 1.9f;   // interior half-width  -> 3.8 m wide
        private const float CeilingHeight = 2.4f;
        private const float HalfDepth = 2.5f;   // interior half-depth  -> 5.0 m deep
        private const float SeatHeight = 0.5f;  // bench seat surface height
        private const float SeatX = -1.5f;      // Kane / bench seat centre X (against left wall)
        private const float SeatZ = 0f;

        // Kane facing: +X (into the cabin / toward the door & cameras).
        private static readonly Quaternion KaneFacing = Quaternion.Euler(0f, 90f, 0f);

        // ---- per-shot definition: which anchor looks at which target, at what FOV ----
        private struct InteriorShot
        {
            public readonly string CueId;
            public readonly string AnchorName;
            public readonly string TargetName;
            public readonly float Fov;
            public InteriorShot(string cue, string anchor, string target, float fov)
            { CueId = cue; AnchorName = anchor; TargetName = target; Fov = fov; }
        }

        private static readonly InteriorShot[] Shots =
        {
            new InteriorShot("m01_interior_kane",       AnchorEstablishing, TargetChest, 48f),
            new InteriorShot("m01_interior_kane_close", AnchorClose,         TargetHead, 40f),
            new InteriorShot("m01_interior_front",      AnchorCockpit,       TargetCockpit, 50f),
        };

        // Shared vibration constants so the camera mirrors the cabin motion exactly (no drift).
        private const float VibFreqX = 23f;
        private const float VibFreqY = 17f;
        private const float VibAmpX = 0.01f;
        private const float VibAmpY = 0.006f;
        private const float VibRotDeg = 0.35f;

        private GameObject _cabinRoot;
        private GameObject _kaneVisual;
        private readonly Dictionary<string, Transform> _namedChildren = new Dictionary<string, Transform>();
        private readonly List<Transform> _windowStreaks = new List<Transform>();
        private readonly List<Renderer> _occluders = new List<Renderer>();
        private float _streakWrap = 6f;
        private bool _active;

        public bool IsActive => _active;
        public Vector3 VibrationOffset { get; private set; }
        public Quaternion VibrationRotation { get; private set; } = Quaternion.identity;

        /// <summary>Sets up the interior at a world position far from the gameplay lane.</summary>
        public void Setup(Vector3 worldPos, Transform sourceKaneVisual)
        {
            transform.position = worldPos;
            transform.rotation = Quaternion.identity;
            BuildCabin();
            BuildCameraAnchorsAndTargets();
            BuildCinematicKane(sourceKaneVisual);
            AttachTargetsToKaneBones();
            _active = true;
            Diagnose();
            Debug.Log("[STORY M01] Interior cinematic setup complete: real Toon Soldier Kane clone + military cabin.");
        }

        /// <summary>Resolves an interior camera anchor to world pos/rot (aimed at its target) + fov.</summary>
        public bool TryGetCameraAnchor(string cueId, out Vector3 worldPos, out Quaternion worldRot, out float fov)
        {
            worldPos = Vector3.zero;
            worldRot = Quaternion.identity;
            fov = 50f;
            for (int i = 0; i < Shots.Length; i++)
            {
                if (Shots[i].CueId != cueId) continue;
                Transform anchor = FindNamed(Shots[i].AnchorName);
                Transform target = FindNamed(Shots[i].TargetName);
                if (anchor == null || target == null) return false;
                worldPos = anchor.position;
                Vector3 dir = target.position - anchor.position;
                if (dir.sqrMagnitude < 1e-5f) dir = Vector3.forward;
                worldRot = Quaternion.LookRotation(dir, Vector3.up);
                fov = Shots[i].Fov;
                return true;
            }
            return false;
        }

        /// <summary>Finds a named child transform (anchor / target / seat anchor / Kane).</summary>
        public Transform FindNamed(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_namedChildren.TryGetValue(name, out Transform t) && t != null) return t;
            // Fallback search (covers children added after the cache was built).
            var found = FindDeep(transform, name);
            if (found != null) _namedChildren[name] = found;
            return found;
        }

        /// <summary>True if the cinematic Kane clone carries no gameplay/physics authority.</summary>
        public bool IsCinematicKaneVisualOnly()
        {
            if (_kaneVisual == null) return false;
            return _kaneVisual.GetComponentsInChildren<PlayerController>(true).Length == 0
                && _kaneVisual.GetComponentsInChildren<PlayerHealth>(true).Length == 0
                && _kaneVisual.GetComponentsInChildren<WeaponController>(true).Length == 0
                && _kaneVisual.GetComponentsInChildren<Collider>(true).Length == 0
                && _kaneVisual.GetComponentsInChildren<Rigidbody>(true).Length == 0;
        }

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

        // ===================================================================== cabin

        private void BuildCabin()
        {
            _cabinRoot = new GameObject("Cabin");
            _cabinRoot.transform.SetParent(transform, false);
            _currentCabinForLight = _cabinRoot.transform; // AddLight parents to the cabin being built.

            Material metalDark = LitMat(new Color(0.16f, 0.17f, 0.19f), 0.6f, 0.3f);
            Material metalMed = LitMat(new Color(0.23f, 0.24f, 0.22f), 0.45f, 0.4f);
            Material metalFloor = LitMat(new Color(0.12f, 0.12f, 0.13f), 0.15f, 0.2f);
            Material benchMat = LitMat(new Color(0.08f, 0.09f, 0.1f), 0.0f, 0.5f);
            Material ribMat = LitMat(new Color(0.30f, 0.31f, 0.29f), 0.6f, 0.45f);
            Material accentMat = LitMat(new Color(0.6f, 0.14f, 0.08f), 0.1f, 0.3f);
            Material glassMat = UnlitMat(new Color(0.18f, 0.24f, 0.32f, 0.6f));
            Material exteriorMat = UnlitMat(new Color(0.04f, 0.05f, 0.08f));

            float W = HalfWidth, H = CeilingHeight, D = HalfDepth;

            // Floor + ceiling.
            AddOccluder(_cabinRoot.transform, "Floor", new Vector3(W * 2f, 0.1f, D * 2f),
                new Vector3(0f, -0.05f, 0f), metalFloor);
            AddCube(_cabinRoot.transform, "Ceiling", new Vector3(W * 2f, 0.1f, D * 2f),
                new Vector3(0f, H, 0f), metalDark);

            // Left wall (bench side) + right wall (door side). Door opening on the right, z in [-0.8,0.8].
            AddOccluder(_cabinRoot.transform, "LeftWall", new Vector3(0.12f, H, D * 2f),
                new Vector3(-W, H * 0.5f, 0f), metalMed);
            AddOccluder(_cabinRoot.transform, "RightWallFront", new Vector3(0.12f, H, (D - 0.8f) * 2f),
                new Vector3(W, H * 0.5f, 0.8f + (D - 0.8f)), metalMed);
            AddOccluder(_cabinRoot.transform, "RightWallRear", new Vector3(0.12f, H, (D - 0.8f) * 2f),
                new Vector3(W, H * 0.5f, -(0.8f + (D - 0.8f))), metalMed);
            // Slim door frame (kept thin so it never dominates a portrait frame).
            AddCube(_cabinRoot.transform, "DoorFrameTop", new Vector3(0.1f, 0.12f, 1.7f),
                new Vector3(W, H - 0.25f, 0f), ribMat);
            AddCube(_cabinRoot.transform, "DoorPillarFront", new Vector3(0.1f, H - 0.3f, 0.1f),
                new Vector3(W, (H - 0.3f) * 0.5f, 0.85f), ribMat);
            AddCube(_cabinRoot.transform, "DoorPillarRear", new Vector3(0.1f, H - 0.3f, 0.1f),
                new Vector3(W, (H - 0.3f) * 0.5f, -0.85f), ribMat);

            // Cockpit partition (front) + rear bulkhead.
            AddOccluder(_cabinRoot.transform, "CockpitPartition", new Vector3(W * 2f, H, 0.14f),
                new Vector3(0f, H * 0.5f, -D), metalDark);
            AddOccluder(_cabinRoot.transform, "RearBulkhead", new Vector3(W * 2f, H, 0.14f),
                new Vector3(0f, H * 0.5f, D), metalDark);

            // Structural ribs (airframe feel) — only on the left wall + far right segments, NEVER
            // between a camera anchor and Kane.
            for (int i = -2; i <= 2; i++)
            {
                float z = i * 0.95f;
                AddCube(_cabinRoot.transform, "RibL", new Vector3(0.06f, H - 0.1f, 0.1f),
                    new Vector3(-W + 0.07f, H * 0.5f, z), ribMat);
            }

            // Bench seat along the left wall (Kane sits here).
            AddOccluder(_cabinRoot.transform, "SeatBase", new Vector3(0.7f, SeatHeight, 3.2f),
                new Vector3(-W + 0.45f, SeatHeight * 0.5f, 0f), benchMat);
            AddOccluder(_cabinRoot.transform, "SeatBack", new Vector3(0.14f, 0.95f, 3.2f),
                new Vector3(-W + 0.12f, SeatHeight + 0.45f, 0f), benchMat);

            // Floor tread + overhead rail + grab handles.
            for (int i = -2; i <= 2; i++)
                AddCube(_cabinRoot.transform, "FloorStrip", new Vector3(W * 2f - 0.2f, 0.02f, 0.06f),
                    new Vector3(0f, 0.011f, i * 0.9f), metalMed);
            AddCylinder(_cabinRoot.transform, "OverheadRail", new Vector3(0.07f, 0.07f, D * 2f - 0.4f),
                new Vector3(0f, H - 0.22f, 0f), Quaternion.Euler(90f, 0f, 0f), ribMat);
            for (int i = -1; i <= 1; i += 2)
                AddCylinder(_cabinRoot.transform, "Handle", new Vector3(0.05f, 0.4f, 0.05f),
                    new Vector3(0f, H - 0.55f, i * 1.2f), Quaternion.identity, ribMat);

            // Windows (left wall + cockpit) + hazard strip.
            AddCube(_cabinRoot.transform, "WindowLeft", new Vector3(0.04f, 0.9f, 1.7f),
                new Vector3(-W - 0.02f, 1.4f, -0.3f), glassMat);
            AddCube(_cabinRoot.transform, "WindowCockpit", new Vector3(1.6f, 0.75f, 0.04f),
                new Vector3(0f, 1.55f, -D - 0.02f), glassMat);
            AddCube(_cabinRoot.transform, "HazardStrip", new Vector3(0.03f, 0.07f, 1.7f),
                new Vector3(W - 0.02f, 0.32f, 0f), accentMat);

            BuildWindowExterior(exteriorMat);

            // ---- brighter but still atmospheric lighting (Kane must be readable) ----
            // Key: cool light from the door/camera side onto Kane.
            AddLight("KeyLight", new Vector3(0.6f, 1.9f, -0.6f), new Color(0.8f, 0.88f, 1.0f), 1.6f, 9f, 70f);
            // Fill: warm interior glow so shadows are not pure black.
            AddLight("FillLight", new Vector3(-0.6f, 2.0f, 0.8f), new Color(1.0f, 0.9f, 0.78f), 0.9f, 8f, 90f);
            // Soft top-down ambient-ish fill to lift the whole cabin off pure black.
            AddLight("TopFill", new Vector3(SeatX, H - 0.2f, 0f), new Color(0.7f, 0.78f, 0.9f), 0.6f, 6f, 120f);
            // Tiny red emergency accent near the door.
            AddLight("EmergencyLight", new Vector3(W - 0.3f, 0.45f, 0f), new Color(1.0f, 0.25f, 0.18f), 0.4f, 2.5f, 60f);

            // Authoritative Kane seat anchor (pelvis target on the bench).
            var seat = new GameObject(SeatAnchorName);
            seat.transform.SetParent(_cabinRoot.transform, false);
            seat.transform.localPosition = new Vector3(SeatX, SeatHeight, SeatZ);
            seat.transform.localRotation = KaneFacing;
            _namedChildren[SeatAnchorName] = seat.transform;
        }

        private void BuildWindowExterior(Material exteriorMat)
        {
            var ext = new GameObject("WindowExterior");
            ext.transform.SetParent(_cabinRoot.transform, false);
            AddCube(ext.transform, "SkyPanelLeft", new Vector3(0.05f, 6f, 10f),
                new Vector3(-3.2f, 1.4f, 0f), exteriorMat);
            AddCube(ext.transform, "SkyPanelFront", new Vector3(10f, 6f, 0.05f),
                new Vector3(0f, 1.4f, -3.6f), exteriorMat);

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

        // ===================================================================== camera anchors + targets

        private void BuildCameraAnchorsAndTargets()
        {
            var group = new GameObject("CameraAnchors");
            group.transform.SetParent(transform, false);

            // Anchors live in rig-local space (stable; the camera mirrors cabin vibration via
            // VibrationOffset). Each is in OPEN cabin space with clearance from walls/bench so it
            // never sits inside geometry and no occluder lies between it and Kane.
            MakeAnchor(group.transform, AnchorEstablishing, new Vector3(0.5f, 1.25f, -1.35f));
            MakeAnchor(group.transform, AnchorMedium,       new Vector3(-0.1f, 1.2f, -0.95f));
            MakeAnchor(group.transform, AnchorClose,        new Vector3(-0.55f, 1.35f, -0.55f));
            MakeAnchor(group.transform, AnchorCockpit,      new Vector3(-0.2f, 1.4f, 1.7f));

            var targets = new GameObject("Targets");
            targets.transform.SetParent(transform, false);
            // Static fallback positions (overridden to track Kane's bones when available).
            MakeTarget(targets.transform, TargetChest,   new Vector3(SeatX, 1.0f, SeatZ));
            MakeTarget(targets.transform, TargetHead,    new Vector3(SeatX, 1.35f, SeatZ));
            MakeTarget(targets.transform, TargetCockpit, new Vector3(0f, 1.2f, -HalfDepth + 0.1f));
        }

        private void MakeAnchor(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            _namedChildren[name] = go.transform;
        }

        private void MakeTarget(Transform parent, string name, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            _namedChildren[name] = go.transform;
        }

        // ===================================================================== cinematic Kane

        private void BuildCinematicKane(Transform sourceVisual)
        {
            if (sourceVisual == null)
            {
                Debug.LogWarning("[STORY M01] No source Toon Soldier visual found — cinematic Kane skipped.");
                return;
            }

            _kaneVisual = Object.Instantiate(sourceVisual.gameObject);
            _kaneVisual.name = KaneName;
            _kaneVisual.transform.SetParent(_cabinRoot.transform, false);
            _kaneVisual.transform.localScale = Vector3.one;
            _kaneVisual.transform.localRotation = KaneFacing;
            _kaneVisual.transform.localPosition = Vector3.zero;

            ScrubGameplayComponents(_kaneVisual);

            var animator = _kaneVisual.GetComponent<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.enabled = true;
            }

            // Anchor FEET to the floor using the clone's measured bounds — robust to wherever the
            // model pivot actually sits. (The QA fix #8 bug placed the pivot at y=0.5; for a
            // feet-pivot model that lifted the whole body to the ceiling.)
            float feetOffsetFromPivot = MeasureFeetOffsetFromPivot(_kaneVisual);
            // Place the root so the FEET land on the floor (y=0) regardless of where the pivot is.
            float rootY = Mathf.Clamp(-feetOffsetFromPivot, -1f, 2f);
            _kaneVisual.transform.localPosition = new Vector3(SeatX, rootY, SeatZ);

            if (animator != null && !_kaneVisual.GetComponent<CinematicKanePose>())
                _kaneVisual.AddComponent<CinematicKanePose>();

            float modelHeight = MeasureModelHeight(_kaneVisual, feetOffsetFromPivot);
            Debug.Log($"[STORY INTERIOR] Kane seated at KaneSeatAnchor. rootY={rootY:F3} " +
                      $"(feet pivot offset {feetOffsetFromPivot:F3}), model height ~{modelHeight:F2}m, " +
                      $"head ~{modelHeight:F2}m below ceiling {CeilingHeight}m.");
        }

        /// <summary>
        /// Snap KaneHead/KaneChest targets to the clone's actual head/spine bones so every camera
        /// always aims at the real posed character (not a guessed coordinate). Falls back to the
        /// static authored positions if the bones are unavailable.
        /// </summary>
        private void AttachTargetsToKaneBones()
        {
            if (_kaneVisual == null) return;
            var animator = _kaneVisual.GetComponent<Animator>();
            if (animator == null) return;

            Transform head = animator.avatar != null ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            Transform chest = animator.avatar != null ? animator.GetBoneTransform(HumanBodyBones.Chest) : null;
            if (chest == null) chest = animator.GetBoneTransform(HumanBodyBones.Spine);

            if (head != null) ParentKeepWorld(FindNamed(TargetHead), head);
            if (chest != null) ParentKeepWorld(FindNamed(TargetChest), chest);
        }

        private static void ParentKeepWorld(Transform child, Transform newParent)
        {
            if (child == null || newParent == null) return;
            child.SetParent(null, true);          // release to world
            child.position = newParent.position; // snap to the bone
            child.SetParent(newParent, true);     // follow the bone from now on
        }

        // ===================================================================== measurement + diagnostics

        private float MeasureFeetOffsetFromPivot(GameObject go)
        {
            // Clone is at cabin-local (0,0,0). Its renderer bounds min.y in cabin-local = the feet
            // offset from the pivot. Clamp sanity: a real humanoid is 1.2-2.2m tall with feet near
            // or below the pivot.
            Bounds b = CombineBounds(go);
            float rigY = transform.position.y;
            float feetCabinLocal = b.min.y - rigY;
            if (b.size.y < 0.4f || b.size.y > 4f) return 0f; // implausible measurement -> assume feet-pivot
            return feetCabinLocal;
        }

        private float MeasureModelHeight(GameObject go, float feetOffsetFromPivot)
        {
            Bounds b = CombineBounds(go);
            float rigY = transform.position.y;
            float headCabinLocal = b.max.y - rigY;
            return Mathf.Max(0.5f, headCabinLocal - feetOffsetFromPivot);
        }

        private Bounds CombineBounds(GameObject go)
        {
            Bounds b = new Bounds(Vector3.zero, Vector3.zero);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                if (first) { b = r.bounds; first = false; }
                else b.Encapsulate(r.bounds);
            }
            return b;
        }

        private void Diagnose()
        {
            // Kane / seat / clearance.
            var seat = FindNamed(SeatAnchorName);
            if (seat != null && _kaneVisual != null)
            {
                Bounds kb = CombineBounds(_kaneVisual);
                float rigY = transform.position.y;
                float feet = kb.min.y - rigY;
                float head = kb.max.y - rigY;
                if (feet < -0.05f) Debug.LogWarning($"[STORY INTERIOR] WARNING: Kane feet below floor ({feet:F2}).");
                if (head > CeilingHeight - 0.05f)
                    Debug.LogWarning($"[STORY INTERIOR] WARNING: Kane head {head:F2} too close to ceiling {CeilingHeight}.");
                Debug.Log($"[STORY INTERIOR] Kane bounds validated: floor/seat/ceiling clear (feet {feet:F2}, head {head:F2}).");
            }

            // Per-camera obstruction.
            for (int i = 0; i < Shots.Length; i++)
            {
                Transform anchor = FindNamed(Shots[i].AnchorName);
                Transform target = FindNamed(Shots[i].TargetName);
                if (anchor == null || target == null) continue;
                string blockedBy = CheckObstruction(anchor.position, target.position);
                string label = Shots[i].CueId;
                if (blockedBy != null)
                    Debug.LogWarning($"[STORY INTERIOR] WARNING: {Shots[i].AnchorName} obstructed by {blockedBy}.");
                else
                    Debug.Log($"[STORY INTERIOR] {label} camera clear -> {Shots[i].TargetName}.");
            }
        }

        /// <summary>
        /// Samples the camera->target segment and returns the first occluder whose bounds contains a
        /// sample point (excluding the target's own bone region), or null if the line of sight is clear.
        /// Pure bounds test (no Physics) so it can never affect gameplay.
        /// </summary>
        private string CheckObstruction(Vector3 from, Vector3 to)
        {
            const int samples = 24;
            Vector3 delta = to - from;
            float len = delta.magnitude;
            if (len < 1e-4f) return null;
            // Skip the first/last ~10% so the camera body and the target itself don't count.
            for (int s = 1; s < samples; s++)
            {
                float t = (float)s / samples;
                if (t < 0.08f || t > 0.92f) continue;
                Vector3 p = from + delta * t;
                foreach (var oc in _occluders)
                {
                    if (oc == null) continue;
                    if (oc.bounds.Contains(p)) return oc.name;
                }
            }
            // Also ensure the camera itself is not inside any occluder.
            foreach (var oc in _occluders)
            {
                if (oc != null && oc.bounds.Contains(from)) return oc.name + "(camera inside)";
            }
            return null;
        }

        // ===================================================================== runtime

        private void Update()
        {
            if (!_active || _cabinRoot == null) return;

            float t = Time.time;
            Vector3 pos = new Vector3(
                Mathf.Sin(t * VibFreqX) * VibAmpX,
                Mathf.Sin(t * VibFreqY) * VibAmpY, 0f);
            Quaternion rot = Quaternion.Euler(0f, 0f, Mathf.Sin(t * 11f) * VibRotDeg);
            _cabinRoot.transform.localPosition = pos;
            _cabinRoot.transform.localRotation = rot;
            VibrationOffset = pos;
            VibrationRotation = rot;

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

        // ===================================================================== scrub + primitives

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

        private static void DestroyAll<T>(GameObject root) where T : Component
        {
            foreach (var c in root.GetComponentsInChildren<T>(true))
            {
                if (Application.isPlaying) Object.Destroy(c);
                else Object.DestroyImmediate(c);
            }
        }

        /// <summary>Depth-first search for a named descendant transform.</summary>
        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null || string.IsNullOrEmpty(name)) return null;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name) return child;
                var deeper = FindDeep(child, name);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static GameObject AddCube(Transform parent, string name, Vector3 scale, Vector3 pos, Material mat) =>
            AddPart(parent, PrimitiveType.Cube, name, scale, pos, Quaternion.identity, mat, occluder: false);

        private static GameObject AddCylinder(Transform parent, string name, Vector3 scale, Vector3 pos, Quaternion rot, Material mat) =>
            AddPart(parent, PrimitiveType.Cylinder, name, scale, pos, rot, mat, occluder: false);

        private GameObject AddOccluder(Transform parent, string name, Vector3 scale, Vector3 pos, Material mat) =>
            AddPart(parent, PrimitiveType.Cube, name, scale, pos, Quaternion.identity, mat, occluder: true);

        private GameObject AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 pos, Quaternion rot, Material mat, bool occluder)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            if (mat != null) go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            var col = go.GetComponent<Collider>();
            if (occluder)
            {
                // Track the renderer bounds for the dev-only obstruction check. The collider itself
                // is disabled so the rig (at y=-300) never affects gameplay physics.
                _occluders.Add(go.GetComponent<MeshRenderer>());
                if (col != null) col.enabled = false;
            }
            else if (col != null)
            {
                col.enabled = false;
            }
            return go;
        }

        private static void AddLight(string name, Vector3 pos, Color color, float intensity, float range, float spotAngle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_currentCabinForLight, false);
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

        // Set before BuildCabin's AddLight calls so they parent to the cabin being built.
        private static Transform _currentCabinForLight;

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
