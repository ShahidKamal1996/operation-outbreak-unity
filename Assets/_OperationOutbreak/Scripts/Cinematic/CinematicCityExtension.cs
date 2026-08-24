using UnityEngine;
using UnityEngine.Rendering;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1A Visual QA Fix #1 — REWORKED aerial city composition + recent-deestruction
    /// atmosphere. The extension now builds coherent urban blocks (not scattered cubes), visible
    /// fire zones, tapered translucent smoke plumes, scorch/wreckage zones, and denser atmospheric
    /// haze — so a future high-oblique helicopter flyover reads as a RECENTLY DEVASTATED city.
    /// </summary>
    public static class CinematicCityExtension
    {
        public const string RootName = "[Cinematic] City Extension";
        public const string GroupMidground = "Midground";
        public const string GroupFarCity = "FarCity";
        public const string GroupLandmarks = "Landmarks";
        public const string GroupSmoke = "Smoke";
        public const string GroupFire = "Fire";
        public const string GroupScorch = "Scorch";
        public const string GroupBoundaryFill = "BoundaryFill";
        public const string GroupHaze = "Haze";

        public static readonly string[] StructureGroups =
            { GroupMidground, GroupFarCity, GroupLandmarks, GroupSmoke, GroupFire, GroupScorch };

        public const float CorridorMinX = -7f;
        public const float CorridorMaxX = 7f;
        public const float CorridorMinZ = -12f;
        public const float CorridorMaxZ = 92f;

        private const int Seed = 91020;

        // Fire / smoke / scorch zone centres (all outside the playable corridor).
        private static readonly Vector3[] DestructionZones =
        {
            new Vector3(22f, 0f, 30f),    // right intersection
            new Vector3(-25f, 0f, 65f),   // left industrial
            new Vector3(30f, 0f, -5f),    // right collapsed
            new Vector3(-20f, 0f, 100f),  // left far
            new Vector3(38f, 0f, 55f),    // right wrecks
            new Vector3(-35f, 0f, 20f),   // left checkpoint
            new Vector3(28f, 0f, 120f),   // right far
        };

        public sealed class Materials
        {
            public Material MidConcrete;
            public Material MidConcreteDark;
            public Material MidRubble;
            public Material MidRust;
            public Material MidSteel;
            public Material Ground;
            public Material Road;
            public Material Silhouette;
            public Material Smoke;
            public Material Fire;
            public Material Scorch;
            public Material Haze;
        }

        public static bool IsInsideCorridor(float x, float z) =>
            x > CorridorMinX && x < CorridorMaxX && z > CorridorMinZ && z < CorridorMaxZ;

        public static GameObject Build(Transform parent, Materials mats)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = Vector3.zero;
            var rng = new System.Random(Seed);

            BuildBoundaryFill(MakeGroup(root.transform, GroupBoundaryFill), mats, rng);
            BuildUrbanBlocks(MakeGroup(root.transform, GroupMidground), mats, rng);
            BuildLandmarks(MakeGroup(root.transform, GroupLandmarks), mats, rng);
            BuildFarCity(MakeGroup(root.transform, GroupFarCity), mats, rng);
            BuildFireZones(MakeGroup(root.transform, GroupFire), mats, rng);
            BuildSmokePlumes(MakeGroup(root.transform, GroupSmoke), mats, rng);
            BuildScorchZones(MakeGroup(root.transform, GroupScorch), mats, rng);
            BuildHaze(MakeGroup(root.transform, GroupHaze), mats, rng);
            return root;
        }

        private static Transform MakeGroup(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        // ===================================================================== boundary

        private static void BuildBoundaryFill(Transform parent, Materials mats, System.Random rng)
        {
            // Overlapping irregular slabs (not a single rectangle) to avoid a visible perimeter.
            AddBox(parent, "Ground_A", new Vector3(20f, -0.35f, 30f),
                new Vector3(200f, 0.4f, 240f), Q(rng, 4f), mats.Ground, false);
            AddBox(parent, "Ground_B", new Vector3(-30f, -0.37f, 50f),
                new Vector3(180f, 0.4f, 220f), Q(rng, -3f), mats.Ground, false);
            AddBox(parent, "Ground_C", new Vector3(0f, -0.33f, 120f),
                new Vector3(260f, 0.4f, 180f), Q(rng, 6f), mats.Ground, false);
            // Terrain colour variation slabs.
            AddBox(parent, "Terrain_Rubble", new Vector3(50f, -0.22f, 60f),
                new Vector3(100f, 0.22f, 160f), Q(rng, 2f), mats.MidRubble, false);
            AddBox(parent, "Terrain_Concrete", new Vector3(-55f, -0.22f, 40f),
                new Vector3(110f, 0.22f, 180f), Q(rng, -5f), mats.MidConcreteDark, false);
            AddBox(parent, "Terrain_Rust", new Vector3(0f, -0.2f, 140f),
                new Vector3(200f, 0.2f, 90f), Q(rng, 1f), mats.MidRust, false);
        }

        // ===================================================================== urban blocks

        private static void BuildUrbanBlocks(Transform parent, Materials mats, System.Random rng)
        {
            // Coherent city BLOCKS (clusters of adjacent buildings + rubble), not scattered cubes.
            int id = 0;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int row = 0; row < 4; row++)
                {
                    for (int col = 0; col < 3; col++)
                    {
                        float cx = side * (13f + col * 11f + NF(rng) * 2.5f);
                        float cz = -22f + row * 28f + NF(rng) * 4f;
                        BuildBlock(parent, mats, rng, id++, new Vector3(cx, 0, cz));
                    }
                }
            }
            // Scenery streets between blocks.
            for (int i = 0; i < 7; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                float x = side * (18f + NF(rng) * 18f);
                float z = -20f + i * 18f;
                AddBox(parent, "Street_" + i, new Vector3(x, 0.04f, z),
                    new Vector3(4.5f, 0.04f, 30f + NF(rng) * 18f), Q(rng, NF(rng) * 20f - 10f),
                    mats.Road, false);
            }
        }

        private static void BuildBlock(Transform parent, Materials mats, System.Random rng, int id, Vector3 center)
        {
            int buildings = 4 + (int)(NF(rng) * 3); // 4-6
            for (int b = 0; b < buildings; b++)
            {
                float ox = (NF(rng) - 0.5f) * 7f;
                float oz = (NF(rng) - 0.5f) * 7f;
                float w = 2.5f + NF(rng) * 3f;
                float d = 2.5f + NF(rng) * 3f;
                bool collapsed = NF(rng) < 0.3f;
                float h = collapsed ? 1.5f + NF(rng) * 2f : 5f + NF(rng) * 13f;
                float lean = collapsed ? NF(rng) * 12f : 0f;
                AddBox(parent, "B_" + id + "_" + b,
                    center + new Vector3(ox, h * 0.5f, oz),
                    new Vector3(w, h, d), Q(rng, NF(rng) * 90f, lean),
                    collapsed ? mats.MidRubble : PickMid(mats, rng), !collapsed);
            }
            // Rubble between buildings.
            if (NF(rng) < 0.6f)
                AddBox(parent, "B_" + id + "_Rub", center + new Vector3(NF(rng)*3f-1.5f, 0.4f, NF(rng)*3f-1.5f),
                    new Vector3(3.5f, 0.8f, 3.5f), Q(rng, NF(rng)*360f), mats.MidRubble, false);
        }

        // ===================================================================== landmarks

        private static void BuildLandmarks(Transform parent, Materials mats, System.Random rng)
        {
            Landmark(parent, mats, rng, "L_CollapsedTower", new Vector3(24f, 0f, 22f),
                new Vector3(8f, 26f, 8f), 8f, mats.MidConcrete, 15f);
            Landmark(parent, mats, rng, "L_IndustrialFire", new Vector3(-28f, 0f, 62f),
                new Vector3(13f, 18f, 11f), -12f, mats.MidRust, 8f);
            Landmark(parent, mats, rng, "L_RubbleZone", new Vector3(32f, 0f, 94f),
                new Vector3(16f, 4f, 16f), 20f, mats.MidRubble, 0f);
            Landmark(parent, mats, rng, "L_Checkpoint", new Vector3(-26f, 0f, 8f),
                new Vector3(8f, 5f, 8f), 0f, mats.MidSteel, 0f);
            Landmark(parent, mats, rng, "L_LeaningTower", new Vector3(38f, 0f, -8f),
                new Vector3(7f, 34f, 7f), 15f, mats.MidConcreteDark, 18f);
            Landmark(parent, mats, rng, "L_BurningBlock", new Vector3(-32f, 0f, 105f),
                new Vector3(10f, 12f, 10f), -5f, mats.MidConcreteDark, 10f);
        }

        private static void Landmark(Transform parent, Materials mats, System.Random rng,
            string name, Vector3 pos, Vector3 size, float yaw, Material mat, float lean)
        {
            AddBox(parent, name, new Vector3(pos.x, size.y * 0.5f, pos.z), size,
                Q(rng, yaw, lean), mat, true);
            AddBox(parent, name + "_Rubble", new Vector3(pos.x, 0.6f, pos.z),
                new Vector3(size.x + 3f, 1.2f, size.z + 3f), Q(rng, yaw + NF(rng) * 30f),
                mats.MidRubble, false);
        }

        // ===================================================================== far city

        private static void BuildFarCity(Transform parent, Materials mats, System.Random rng)
        {
            const int count = 44;
            for (int i = 0; i < count; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                float x = side * (52f + NF(rng) * 78f);
                float z = -60f + NF(rng) * 260f;
                float w = 5f + NF(rng) * 12f;
                float d = 5f + NF(rng) * 12f;
                float h = 12f + NF(rng) * 38f;
                AddBox(parent, "Far_" + i, new Vector3(x, h * 0.5f, z),
                    new Vector3(w, h, d), Q(rng, NF(rng) * 45f), mats.Silhouette, false);
            }
        }

        // ===================================================================== fire zones

        private static void BuildFireZones(Transform parent, Materials mats, System.Random rng)
        {
            for (int i = 0; i < 4 && i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                int flames = 3 + (int)(NF(rng) * 2);
                for (int f = 0; f < flames; f++)
                {
                    float fx = z.x + (NF(rng) - 0.5f) * 3.5f;
                    float fz = z.z + (NF(rng) - 0.5f) * 3.5f;
                    float fh = 0.8f + NF(rng) * 1.6f;
                    float fw = 0.5f + NF(rng) * 0.4f;
                    AddBox(parent, "Fire_" + i + "_" + f, new Vector3(fx, fh * 0.5f, fz),
                        new Vector3(fw, fh, fw), Q(rng, NF(rng) * 360f), mats.Fire, false);
                }
            }
        }

        // ===================================================================== smoke plumes

        private static void BuildSmokePlumes(Transform parent, Materials mats, System.Random rng)
        {
            for (int i = 0; i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                float totalH = 16f + NF(rng) * 14f;
                int sections = 4;
                for (int s = 0; s < sections; s++)
                {
                    float t = (float)s / sections;
                    float secH = totalH / sections;
                    float yC = secH * (s + 0.5f);
                    float radius = 1.5f + t * 4.5f + NF(rng) * 0.8f;
                    float drift = s * 0.4f;
                    AddCylinder(parent, "Smoke_" + i + "_" + s,
                        new Vector3(z.x + (NF(rng) - 0.5f) * drift, yC, z.z + (NF(rng) - 0.5f) * drift),
                        new Vector3(radius, secH * 1.15f, radius), Q(rng, NF(rng) * 20f),
                        mats.Smoke, false);
                }
            }
        }

        // ===================================================================== scorch zones

        private static void BuildScorchZones(Transform parent, Materials mats, System.Random rng)
        {
            for (int i = 0; i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                AddBox(parent, "Scorch_" + i, new Vector3(z.x, 0.03f, z.z),
                    new Vector3(6f + NF(rng) * 3f, 0.06f, 6f + NF(rng) * 3f),
                    Q(rng, NF(rng) * 360f), mats.Scorch, false);
                // Wreckage debris.
                for (int d = 0; d < 3; d++)
                {
                    AddBox(parent, "Wreck_" + i + "_" + d,
                        new Vector3(z.x + (NF(rng) - 0.5f) * 6f, 0.3f, z.z + (NF(rng) - 0.5f) * 6f),
                        new Vector3(0.4f + NF(rng), 0.4f + NF(rng), 0.4f + NF(rng)),
                        Q(rng, NF(rng) * 30f, NF(rng) * 360f, NF(rng) * 30f),
                        mats.MidRust, false);
                }
            }
        }

        // ===================================================================== haze

        private static void BuildHaze(Transform parent, Materials mats, System.Random rng)
        {
            // Mid-distance depth haze (thin panels between midground and far city).
            for (int i = 0; i < 4; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                AddBox(parent, "HazeMid_" + i,
                    new Vector3(side * 48f, 8f + NF(rng) * 4f, -20f + i * 35f),
                    new Vector3(4f, 16f + NF(rng) * 8f, 60f + NF(rng) * 30f),
                    Q(rng, NF(rng) * 15f), mats.Haze, false);
            }
            // Far boundary drifts.
            AddBox(parent, "Haze_Far", new Vector3(0f, 3f, 185f),
                new Vector3(300f, 6f, 12f), Quaternion.identity, mats.Haze, false);
            AddBox(parent, "Haze_R", new Vector3(150f, 3f, 70f),
                new Vector3(12f, 6f, 280f), Quaternion.identity, mats.Haze, false);
            AddBox(parent, "Haze_L", new Vector3(-150f, 3f, 70f),
                new Vector3(12f, 6f, 280f), Quaternion.identity, mats.Haze, false);
        }

        // ===================================================================== helpers

        private static Material PickMid(Materials mats, System.Random rng)
        {
            float r = NF(rng);
            if (r < 0.30f) return mats.MidConcrete;
            if (r < 0.55f) return mats.MidConcreteDark;
            if (r < 0.75f) return mats.MidRust;
            if (r < 0.90f) return mats.MidSteel;
            return mats.MidRubble;
        }

        private static float NF(System.Random rng) => (float)rng.NextDouble();
        private static Quaternion Q(System.Random rng, float yaw) => Quaternion.Euler(0f, yaw, 0f);
        private static Quaternion Q(System.Random rng, float yaw, float lean) =>
            Quaternion.Euler(NF(rng) * lean * 0.3f, yaw, NF(rng) * lean * 0.5f);
        private static Quaternion Q(System.Random rng, float x, float y, float z) => Quaternion.Euler(x, y, z);

        private static GameObject AddBox(Transform parent, string name, Vector3 localPos,
            Vector3 scale, Quaternion rot, Material mat, bool shadows) =>
            AddPart(parent, PrimitiveType.Cube, name, localPos, scale, rot, mat, shadows);

        private static GameObject AddCylinder(Transform parent, string name, Vector3 localPos,
            Vector3 scale, Quaternion rot, Material mat, bool shadows) =>
            AddPart(parent, PrimitiveType.Cylinder, name, localPos, scale, rot, mat, shadows);

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
            var col = go.GetComponent<Collider>();
            if (col != null)
            {
                if (Application.isPlaying) Object.Destroy(col);
                else Object.DestroyImmediate(col);
            }
            return go;
        }
    }
}
