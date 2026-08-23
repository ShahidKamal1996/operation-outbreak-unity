using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #7 — manages the M1 helicopter interior cinematic rig.
    /// Created by MissionStoryDirector on m01_interior_setup cue. Contains the cabin
    /// environment + cinematic Kane visual. Hidden/destroyed on teardown/skip.
    /// All build-in-code from primitives — no external assets.
    /// </summary>
    public sealed class HelicopterInteriorRig : MonoBehaviour
    {
        private GameObject _cabinRoot;
        private GameObject _kaneVisual;
        private bool _active;

        public bool IsActive => _active;

        /// <summary>Sets up the interior at a world position far from the gameplay lane.</summary>
        public void Setup(Vector3 position)
        {
            transform.position = position;
            BuildCabin();
            BuildCinematicKane();
            _active = true;
            Debug.Log("[STORY M01] Interior cinematic setup. Cabin + cinematic Kane created.");
        }

        /// <summary>Hides the interior rig entirely.</summary>
        public void Teardown()
        {
            if (!_active) return;
            _active = false;

            if (_cabinRoot != null) _cabinRoot.SetActive(false);
            if (_kaneVisual != null) _kaneVisual.SetActive(false);
            Debug.Log("[STORY M01] Interior rig cleaned up.");
        }

        private void BuildCabin()
        {
            _cabinRoot = new GameObject("Story_M01_Cabin");
            _cabinRoot.transform.SetParent(transform, false);

            Material metalDark = CreateMat(new Color(0.18f, 0.19f, 0.21f, 1f));
            Material metalMed = CreateMat(new Color(0.28f, 0.26f, 0.22f, 1f));
            Material metalLight = CreateMat(new Color(0.4f, 0.38f, 0.32f, 1f));
            Material glassMat = CreateMat(new Color(0.3f, 0.4f, 0.5f, 0.5f));

            // Floor
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "Floor",
                new Vector3(3f, 0.1f, 4f), new Vector3(0f, 0f, 0f), metalDark);

            // Ceiling
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "Ceiling",
                new Vector3(3f, 0.1f, 4f), new Vector3(0f, 2.2f, 0f), metalDark);

            // Left wall
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "LeftWall",
                new Vector3(0.1f, 2.2f, 4f), new Vector3(-1.5f, 1.1f, 0f), metalMed);

            // Right wall
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "RightWall",
                new Vector3(0.1f, 2.2f, 4f), new Vector3(1.5f, 1.1f, 0f), metalMed);

            // Rear wall / bulkhead
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "RearBulkhead",
                new Vector3(3f, 2.2f, 0.1f), new Vector3(0f, 1.1f, 2f), metalMed);

            // Cockpit partition (behind camera, establishes depth)
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "CockpitPartition",
                new Vector3(3f, 2.2f, 0.1f), new Vector3(0f, 1.1f, -2f), metalMed);

            // Windows (left + right) — translucent, lets exterior light in
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "WindowLeft",
                new Vector3(0.05f, 0.8f, 1.5f), new Vector3(-1.5f, 1.3f, -0.3f), glassMat);
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "WindowRight",
                new Vector3(0.05f, 0.8f, 1.5f), new Vector3(1.5f, 1.3f, -0.3f), glassMat);

            // Bench seat (where Kane sits, along the left wall)
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "SeatBase",
                new Vector3(0.7f, 0.5f, 2f), new Vector3(-1f, 0.25f, 0f), metalLight);
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "SeatBack",
                new Vector3(0.2f, 1f, 2f), new Vector3(-1.2f, 0.75f, 0f), metalLight);

            // Overhead rail (tactical helicopter detail)
            AddPart(_cabinRoot.transform, PrimitiveType.Cylinder, "OverheadRail",
                new Vector3(0.08f, 0.08f, 3.5f), new Vector3(0f, 2f, 0f),
                Quaternion.Euler(90f, 0f, 0f), metalLight);

            // Door frame (right side, open door)
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "DoorFrame",
                new Vector3(0.15f, 1.8f, 0.15f), new Vector3(1.5f, 0.9f, 1f), metalLight);
            AddPart(_cabinRoot.transform, PrimitiveType.Cube, "DoorFrameTop",
                new Vector3(0.15f, 0.15f, 0.8f), new Vector3(1.5f, 1.8f, 1f), metalLight);

            // Subtle interior point light
            GameObject lightGo = new GameObject("InteriorLight");
            lightGo.transform.SetParent(_cabinRoot.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 1.8f, 0f);
            Light interiorLight = lightGo.AddComponent<Light>();
            interiorLight.type = LightType.Point;
            interiorLight.intensity = 0.6f;
            interiorLight.range = 5f;
            interiorLight.color = new Color(1f, 0.92f, 0.75f, 1f); // warm emergency lighting
        }

        private void BuildCinematicKane()
        {
            // Visual-only Kane clone — no gameplay components. Seated on the bench.
            _kaneVisual = new GameObject("Story_KaneCinematic");
            _kaneVisual.transform.SetParent(transform, false);

            // Seated position on the bench (left side, facing center/door)
            _kaneVisual.transform.localPosition = new Vector3(-0.9f, 0.55f, 0f);
            _kaneVisual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f); // facing right (toward door)

            Material uniformMat = CreateMat(new Color(0.2f, 0.25f, 0.18f, 1f));
            Material skinMat = CreateMat(new Color(0.7f, 0.58f, 0.45f, 1f));
            Material gearMat = CreateMat(new Color(0.12f, 0.12f, 0.1f, 1f));

            // Torso (leaning slightly forward — alert seated posture)
            var torso = AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "Torso",
                new Vector3(0.45f, 0.5f, 0.3f), new Vector3(0f, 0.35f, 0.05f), uniformMat);
            torso.transform.localRotation = Quaternion.Euler(10f, 0f, 0f); // slight forward lean

            // Head
            AddPart(_kaneVisual.transform, PrimitiveType.Sphere, "Head",
                new Vector3(0.22f, 0.22f, 0.22f), new Vector3(0f, 0.75f, 0.1f), skinMat);

            // Helmet
            AddPart(_kaneVisual.transform, PrimitiveType.Sphere, "Helmet",
                new Vector3(0.26f, 0.2f, 0.26f), new Vector3(0f, 0.78f, 0.1f), gearMat);

            // Upper arms (resting on knees / gear)
            AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "ArmL",
                new Vector3(0.12f, 0.35f, 0.12f), new Vector3(-0.22f, 0.3f, 0.15f),
                Quaternion.Euler(60f, 0f, 0f), uniformMat);
            AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "ArmR",
                new Vector3(0.12f, 0.35f, 0.12f), new Vector3(0.22f, 0.3f, 0.15f),
                Quaternion.Euler(60f, 0f, 0f), uniformMat);

            // Thighs (horizontal — seated)
            AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "ThighL",
                new Vector3(0.14f, 0.4f, 0.14f), new Vector3(-0.12f, 0.15f, 0.15f),
                Quaternion.Euler(80f, 0f, 0f), uniformMat);
            AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "ThighR",
                new Vector3(0.14f, 0.4f, 0.14f), new Vector3(0.12f, 0.15f, 0.15f),
                Quaternion.Euler(80f, 0f, 0f), uniformMat);

            // Lower legs (vertical from knee)
            AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "ShinL",
                new Vector3(0.12f, 0.35f, 0.12f), new Vector3(-0.12f, -0.2f, 0.35f), uniformMat);
            AddPart(_kaneVisual.transform, PrimitiveType.Capsule, "ShinR",
                new Vector3(0.12f, 0.35f, 0.12f), new Vector3(0.12f, -0.2f, 0.35f), uniformMat);

            // Rifle resting across lap (lowered, not aiming)
            var rifle = AddPart(_kaneVisual.transform, PrimitiveType.Cube, "Rifle",
                new Vector3(0.08f, 0.08f, 0.6f), new Vector3(0.15f, 0.2f, 0.15f),
                Quaternion.Euler(0f, 20f, 0f), gearMat);

            // Scale to match Toon Soldier approximate size
            _kaneVisual.transform.localScale = Vector3.one * 1.2f;

            Debug.Log("[STORY M01] Cinematic Kane seated (visual-only, no gameplay components).");
        }

        // Cabin vibration for flight feel
        public void Update()
        {
            if (!_active || _cabinRoot == null) return;

            float t = Time.time;
            float vibX = Mathf.Sin(t * 23f) * 0.008f;
            float vibY = Mathf.Sin(t * 17f) * 0.005f;
            float rotZ = Mathf.Sin(t * 11f) * 0.3f;

            _cabinRoot.transform.localPosition = new Vector3(vibX, vibY, 0f);
            _cabinRoot.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        }

        private static Material CreateMat(Color color)
        {
            Material mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
                mat = new Material(Shader.Find("Unlit/Color"));
            mat.color = color;
            return mat;
        }

        private static GameObject AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 scale, Vector3 pos, Material mat) =>
            AddPart(parent, type, name, scale, pos, Quaternion.identity, mat);

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
    }
}
