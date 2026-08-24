using UnityEngine;
using UnityEngine.Rendering;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1A — a CINEMATIC-ONLY visual city extension built around the Chapter 1
    /// playable corridor, so a future high-oblique helicopter flyover can show a large damaged
    /// city without exposing the small playable level, empty Unity space, or map edges.
    ///
    /// This is the testable, runtime-agnostic core. The editor tool
    /// (<see cref="OperationOutbreak.EditorTools.CinematicCityExtensionBuilder"/>) loads the real
    /// Chapter 1 materials and calls <see cref="Build"/> to author the hierarchy into the scene.
    ///
    /// Three visual distance layers:
    ///   A. the existing playable corridor (untouched — everything here is placed OUTSIDE it);
    ///   B. Midground — mid-distance ruined structures reusing the Chapter 1 material vocabulary;
    ///   C. FarCity — lightweight low-poly silhouettes for skyline/depth (no colliders, no shadows).
    /// Plus Landmarks, Smoke columns, a BoundaryFill ground skirt, and Haze drifts that hide edges.
    ///
    /// Every generated object is VISUAL ONLY: no gameplay scripts, no colliders, no physics.
    /// Placement is deterministic (seeded) so the layout is stable across builds.
    /// </summary>
    public static class CinematicCityExtension
    {
        public const string RootName = "[Cinematic] City Extension";
        public const string GroupMidground = "Midground";
        public const string GroupFarCity = "FarCity";
        public const string GroupLandmarks = "Landmarks";
        public const string GroupSmoke = "Smoke";
        public const string GroupBoundaryFill = "BoundaryFill";
        public const string GroupHaze = "Haze";

        /// <summary>
        /// Groups whose contents are vertical STRUCTURES and therefore must never be placed inside
        /// the playable corridor (the ground skirt / haze legitimately span the corridor area).
        /// </summary>
        public static readonly string[] StructureGroups =
            { GroupMidground, GroupFarCity, GroupLandmarks, GroupSmoke };

        // Playable corridor keep-out band (Chapter 1 lane + roadsides). Structures avoid this.
        public const float CorridorMinX = -7f;
        public const float CorridorMaxX = 7f;
        public const float CorridorMinZ = -12f;
        public const float CorridorMaxZ = 92f;

        private const int DeterministicSeed = 91020;

        /// <summary>Material slots consumed by <see cref="Build"/>. Null-safe (tests pass nulls).</summary>
        public sealed class Materials
        {
            public Material MidConcrete;
            public Material MidConcreteDark;
            public Material MidRubble;
            public Material MidRust;
            public Material MidSteel;
            public Material Ground;      // boundary ground skirt
            public Material Road;        // scenery roads
            public Material Silhouette;  // far-city silhouettes (hazy)
            public Material Smoke;       // smoke columns
            public Material Haze;        // far haze drifts
        }

        /// <summary>True when an XZ point lies inside the playable corridor keep-out band.</summary>
        public static bool IsInsideCorridor(float x, float z) =>
            x > CorridorMinX && x < CorridorMaxX && z > CorridorMinZ && z < CorridorMaxZ;

        /// <summary>Builds the full extension hierarchy under <paramref name="parent"/>.</summary>
        public static GameObject Build(Transform parent, Materials mats)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;

            var rng = new System.Random(DeterministicSeed);

            Transform midground = MakeGroup(root.transform, GroupMidground);
            Transform farCity = MakeGroup(root.transform, GroupFarCity);
            Transform landmarks = MakeGroup(root.transform, GroupLandmarks);
            Transform smoke = MakeGroup(root.transform, GroupSmoke);
            Transform boundary = MakeGroup(root.transform, GroupBoundaryFill);
            Transform haze = MakeGroup(root.transform, GroupHaze);

            BuildBoundaryFill(boundary, mats);
            BuildMidground(midground, mats, rng);
            BuildLandmarks(landmarks, mats, rng);
            BuildFarCity(farCity, mats, rng);
            BuildSmoke(smoke, mats, rng);
            BuildHaze(haze, mats);
            return root;
        }

        private static Transform MakeGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // --------------------------------------------------------------- layers

        private static void BuildBoundaryFill(Transform parent, Materials mats)
        {
            // One large dark ground skirt covering the whole cinematic area so no empty Unity space
            // or map edge is visible from a high camera. Sits just below the playable road (y=0).
            AddBox(parent, "Ground_Skirt", new Vector3(0f, -0.35f, 40f),
                new Vector3(340f, 0.4f, 420f), Quaternion.identity, mats.Ground, castShadows: false);
            // Large low terrain slabs for ground-color variation (rubble flats / concrete aprons).
            AddBox(parent, "Terrain_Slab_Rubble", new Vector3(55f, -0.22f, 70f),
                new Vector3(90f, 0.22f, 150f), Quaternion.identity, mats.MidRubble, castShadows: false);
            AddBox(parent, "Terrain_Slab_Concrete", new Vector3(-58f, -0.22f, 30f),
                new Vector3(100f, 0.22f, 170f), Quaternion.identity, mats.MidConcreteDark, castShadows: false);
            AddBox(parent, "Terrain_Slab_Steel", new Vector3(0f, -0.2f, 150f),
                new Vector3(200f, 0.2f, 80f), Quaternion.identity, mats.MidRust, castShadows: false);
        }

        private static void BuildMidground(Transform parent, Materials mats, System.Random rng)
        {
            const int structures = 42;
            for (int i = 0; i < structures; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                float x = side * (11f + NextFloat(rng) * 35f);     // |x| in [11,46] -> outside corridor
                float z = -30f + NextFloat(rng) * 165f;            // [-30,135]
                float w = 3f + NextFloat(rng) * 5f;
                float d = 3f + NextFloat(rng) * 5f;
                bool rubble = NextFloat(rng) < 0.25f;
                float h = rubble ? 1f + NextFloat(rng) * 2.5f : 4f + NextFloat(rng) * 15f;
                Material m = rubble ? mats.MidRubble : PickMidMaterial(mats, rng);
                AddBox(parent, "Mid_" + i, new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, d), Quaternion.Euler(0f, NextFloat(rng) * 360f, 0f), m, castShadows: true);
            }

            // A few scenery-only intersecting roads (no gameplay use) — thin asphalt strips.
            for (int i = 0; i < 5; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                float x = side * (16f + NextFloat(rng) * 24f);
                float z = -25f + NextFloat(rng) * 150f;
                float len = 22f + NextFloat(rng) * 34f;
                AddBox(parent, "SceneryRoad_" + i, new Vector3(x, 0.06f, z),
                    new Vector3(3.2f, 0.06f, len), Quaternion.Euler(0f, NextFloat(rng) * 90f, 0f),
                    mats.Road, castShadows: false);
            }
        }

        private static void BuildLandmarks(Transform parent, Materials mats, System.Random rng)
        {
            // A handful of larger, visually interesting ruined landmarks (all outside the corridor).
            Landmark(parent, "Landmark_CollapsedTower", new Vector3(24f, 0f, 22f),
                new Vector3(8f, 24f, 8f), 8f, mats.MidConcrete, mats.MidRubble, rng);
            Landmark(parent, "Landmark_IndustrialBlock", new Vector3(-28f, 0f, 62f),
                new Vector3(12f, 16f, 10f), -12f, mats.MidRust, mats.MidRubble, rng);
            Landmark(parent, "Landmark_RubbleZone", new Vector3(32f, 0f, 94f),
                new Vector3(15f, 3f, 15f), 20f, mats.MidRubble, mats.MidRubble, rng);
            Landmark(parent, "Landmark_AbandonedCheckpoint", new Vector3(-26f, 0f, 8f),
                new Vector3(8f, 4.5f, 8f), 0f, mats.MidSteel, mats.MidRubble, rng);
            Landmark(parent, "Landmark_RuinedTower", new Vector3(36f, 0f, -12f),
                new Vector3(7f, 30f, 7f), 15f, mats.MidConcreteDark, mats.MidRubble, rng);
        }

        private static void Landmark(Transform parent, string name, Vector3 basePos, Vector3 size,
            float yaw, Material structureMat, Material rubbleMat, System.Random rng)
        {
            AddBox(parent, name, new Vector3(basePos.x, size.y * 0.5f, basePos.z), size,
                Quaternion.Euler(0f, yaw, 0f), structureMat, castShadows: true);
            // A rubble collar around the base for destruction density.
            AddBox(parent, name + "_Rubble", new Vector3(basePos.x, 0.6f, basePos.z),
                new Vector3(size.x + 3f, 1.2f, size.z + 3f),
                Quaternion.Euler(0f, yaw + NextFloat(rng) * 30f, 0f), rubbleMat, castShadows: false);
        }

        private static void BuildFarCity(Transform parent, Materials mats, System.Random rng)
        {
            const int silhouettes = 34;
            for (int i = 0; i < silhouettes; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                float x = side * (55f + NextFloat(rng) * 75f);     // |x| in [55,130]
                float z = -65f + NextFloat(rng) * 260f;            // [-65,195]
                float w = 6f + NextFloat(rng) * 11f;
                float d = 6f + NextFloat(rng) * 11f;
                float h = 15f + NextFloat(rng) * 32f;
                AddBox(parent, "Far_" + i, new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, d), Quaternion.identity, mats.Silhouette, castShadows: false);
            }
        }

        private static void BuildSmoke(Transform parent, Materials mats, System.Random rng)
        {
            Vector3[] spots =
            {
                new Vector3(21f, 0f, 30f),
                new Vector3(-23f, 0f, 72f),
                new Vector3(29f, 0f, 102f),
                new Vector3(-31f, 0f, 2f),
                new Vector3(41f, 0f, 52f),
                new Vector3(-18f, 0f, 112f),
                new Vector3(46f, 0f, -18f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                Vector3 s = spots[i];
                float h = 18f + NextFloat(rng) * 16f;
                float r = 2f + h * 0.06f;
                AddCylinder(parent, "Smoke_" + i, new Vector3(s.x, h * 0.5f, s.z),
                    new Vector3(r, h, r), Quaternion.identity, mats.Smoke, castShadows: false);
            }
        }

        private static void BuildHaze(Transform parent, Materials mats)
        {
            // Large, thin, dark drifts at the far boundary — read as distant haze / fog banks that
            // help conceal where the city ends. Ground-hugging so they never read as walls up close.
            AddBox(parent, "Haze_Drift_Far", new Vector3(0f, 2.5f, 185f),
                new Vector3(300f, 5f, 10f), Quaternion.identity, mats.Haze, castShadows: false);
            AddBox(parent, "Haze_Drift_Right", new Vector3(150f, 2.5f, 70f),
                new Vector3(10f, 5f, 280f), Quaternion.identity, mats.Haze, castShadows: false);
            AddBox(parent, "Haze_Drift_Left", new Vector3(-150f, 2.5f, 70f),
                new Vector3(10f, 5f, 280f), Quaternion.identity, mats.Haze, castShadows: false);
        }

        // --------------------------------------------------------------- helpers

        private static Material PickMidMaterial(Materials mats, System.Random rng)
        {
            float r = NextFloat(rng);
            if (r < 0.30f) return mats.MidConcrete;
            if (r < 0.55f) return mats.MidConcreteDark;
            if (r < 0.75f) return mats.MidRust;
            if (r < 0.90f) return mats.MidSteel;
            return mats.MidRubble;
        }

        private static float NextFloat(System.Random rng) => (float)rng.NextDouble();

        private static GameObject AddBox(Transform parent, string name, Vector3 localPos,
            Vector3 scale, Quaternion rot, Material mat, bool castShadows) =>
            AddPart(parent, PrimitiveType.Cube, name, localPos, scale, rot, mat, castShadows);

        private static GameObject AddCylinder(Transform parent, string name, Vector3 localPos,
            Vector3 scale, Quaternion rot, Material mat, bool castShadows) =>
            AddPart(parent, PrimitiveType.Cylinder, name, localPos, scale, rot, mat, castShadows);

        private static GameObject AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 localPos, Vector3 scale, Quaternion rot, Material mat, bool castShadows)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = castShadows;
            // Visual-only: strip the collider CreatePrimitive adds.
            RemoveCollider(go);
            return go;
        }

        private static void RemoveCollider(GameObject go)
        {
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
        }
    }
}
