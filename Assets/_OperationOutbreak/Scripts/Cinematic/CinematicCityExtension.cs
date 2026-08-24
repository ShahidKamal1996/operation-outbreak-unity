using UnityEngine;
using UnityEngine.Rendering;

namespace OperationOutbreak.Cinematic
{
    /// <summary>
    /// Milestone 1Z.1A Visual QA Fix #2 — REWORKED for aerial destruction readability at 50–80 m.
    /// Larger fire masses, wider/taller smoke plumes, composite damaged buildings, darker stepped
    /// skyline, bigger scorch fields, dramatic destruction landmarks, stronger haze.
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

        private static readonly Vector3[] DestructionZones =
        {
            new Vector3(22f, 0f, 30f), new Vector3(-25f, 0f, 65f),
            new Vector3(30f, 0f, -5f), new Vector3(-20f, 0f, 100f),
            new Vector3(38f, 0f, 55f), new Vector3(-35f, 0f, 20f),
            new Vector3(28f, 0f, 120f),
        };

        public sealed class Materials
        {
            public Material MidConcrete; public Material MidConcreteDark; public Material MidRubble;
            public Material MidRust; public Material MidSteel; public Material Ground; public Material Road;
            public Material Silhouette; public Material Smoke; public Material Fire; public Material Scorch;
            public Material Haze;
        }

        public static bool IsInsideCorridor(float x, float z) =>
            x > CorridorMinX && x < CorridorMaxX && z > CorridorMinZ && z < CorridorMaxZ;

        public static GameObject Build(Transform parent, Materials mats)
        {
            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            var rng = new System.Random(Seed);
            BuildBoundaryFill(MakeGroup(root.transform, GroupBoundaryFill), mats, rng);
            BuildUrbanBlocks(MakeGroup(root.transform, GroupMidground), mats, rng);
            BuildDestructionLandmarks(MakeGroup(root.transform, GroupLandmarks), mats, rng);
            BuildFarCity(MakeGroup(root.transform, GroupFarCity), mats, rng);
            BuildFireZones(MakeGroup(root.transform, GroupFire), mats, rng);
            BuildSmokePlumes(MakeGroup(root.transform, GroupSmoke), mats, rng);
            BuildScorchZones(MakeGroup(root.transform, GroupScorch), mats, rng);
            BuildHaze(MakeGroup(root.transform, GroupHaze), mats, rng);
            return root;
        }

        private static Transform MakeGroup(Transform p, string n)
        { var go = new GameObject(n); go.transform.SetParent(p, false); return go.transform; }

        // ===== Boundary =====
        private static void BuildBoundaryFill(Transform p, Materials m, System.Random r)
        {
            AddBox(p, "G_A", new Vector3(20f, -.35f, 30f), new Vector3(200f, .4f, 240f), Q(r, 4f), m.Ground, false);
            AddBox(p, "G_B", new Vector3(-30f, -.37f, 50f), new Vector3(180f, .4f, 220f), Q(r, -3f), m.Ground, false);
            AddBox(p, "G_C", new Vector3(0f, -.33f, 120f), new Vector3(260f, .4f, 180f), Q(r, 6f), m.Ground, false);
            AddBox(p, "T_Rub", new Vector3(50f, -.22f, 60f), new Vector3(100f, .22f, 160f), Q(r, 2f), m.MidRubble, false);
            AddBox(p, "T_Con", new Vector3(-55f, -.22f, 40f), new Vector3(110f, .22f, 180f), Q(r, -5f), m.MidConcreteDark, false);
            AddBox(p, "T_Rust", new Vector3(0f, -.2f, 140f), new Vector3(200f, .2f, 90f), Q(r, 1f), m.MidRust, false);
        }

        // ===== Urban blocks (composite damaged buildings) =====
        private static void BuildUrbanBlocks(Transform p, Materials m, System.Random r)
        {
            int id = 0;
            for (int side = -1; side <= 1; side += 2)
                for (int row = 0; row < 4; row++)
                    for (int col = 0; col < 3; col++)
                    {
                        float cx = side * (13f + col * 11f + NF(r) * 2.5f);
                        float cz = -22f + row * 28f + NF(r) * 4f;
                        BuildDamagedBlock(p, m, r, id++, new Vector3(cx, 0, cz));
                    }
            for (int i = 0; i < 7; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                AddBox(p, "St_" + i, new Vector3(side * (18f + NF(r) * 18f), .04f, -20f + i * 18f),
                    new Vector3(4.5f, .04f, 30f + NF(r) * 18f), Q(r, NF(r) * 20f - 10f), m.Road, false);
            }
        }

        private static void BuildDamagedBlock(Transform p, Materials m, System.Random r, int id, Vector3 c)
        {
            int bldg = 4 + (int)(NF(r) * 3);
            for (int b = 0; b < bldg; b++)
            {
                float ox = (NF(r) - .5f) * 7f, oz = (NF(r) - .5f) * 7f;
                float w = 2.5f + NF(r) * 3f, d = 2.5f + NF(r) * 3f;
                bool col = NF(r) < 0.3f;
                float h = col ? 1.5f + NF(r) * 2f : 5f + NF(r) * 13f;
                Material mat = col ? m.MidRubble : PickMid(m, r);
                AddBox(p, "B_" + id + "_" + b, c + new Vector3(ox, h * .5f, oz),
                    new Vector3(w, h, d), Q(r, NF(r) * 90f, col ? NF(r) * 12f : 0), mat, !col);

                // Stepped/broken top — shorter offset box creating irregular silhouette.
                if (!col && NF(r) < 0.5f)
                {
                    float sh = h * (0.25f + NF(r) * 0.25f);
                    AddBox(p, "B_" + id + "_" + b + "t",
                        c + new Vector3(ox + NF(r) * 2f - 1f, h + sh * .3f, oz + NF(r) * 2f - 1f),
                        new Vector3(w * .7f, sh, d * .7f), Q(r, NF(r) * 60f), mat, true);
                }
            }
            if (NF(r) < 0.6f)
                AddBox(p, "B_" + id + "_R", c + new Vector3(NF(r) * 3f - 1.5f, .4f, NF(r) * 3f - 1.5f),
                    new Vector3(3.5f, .8f, 3.5f), Q(r, NF(r) * 360f), m.MidRubble, false);
        }

        // ===== Destruction landmarks =====
        private static void BuildDestructionLandmarks(Transform p, Materials m, System.Random r)
        {
            // 1. Collapsed high-rise (tall + broken offset top + rubble).
            LandmarkTower(p, m, r, "L_HighRise", new Vector3(24f, 0, 22f), 8f, 24f, 8, m.MidConcrete, 0, 12f);
            // 2. Burning industrial (dark block + fire zone at base).
            LandmarkTower(p, m, r, "L_Industrial", new Vector3(-28f, 0, 62f), 13f, 18f, -12, m.MidRust, 8f, 0);
            // 3. Heavy rubble district (cluster of collapsed structures).
            for (int i = 0; i < 5; i++)
                AddBox(p, "L_Rubble_" + i, new Vector3(32f + NF(r)*8f-4f, NF(r)*1.5f+.5f, 94f + NF(r)*8f-4f),
                    new Vector3(3f+NF(r)*2f, 2f+NF(r)*2f, 3f+NF(r)*2f), Q(r, NF(r)*360f, NF(r)*15f), m.MidRubble, false);
            AddBox(p, "L_Rubble_Base", new Vector3(32f, .3f, 94f), new Vector3(16f, .5f, 16f), Q(r, 20f), m.MidRubble, false);
            // 4. Leaning tower.
            LandmarkTower(p, m, r, "L_Leaning", new Vector3(38f, 0, -8f), 7f, 32f, 15, m.MidConcreteDark, 14f, 0);
            // 5. Damaged stepped block.
            LandmarkTower(p, m, r, "L_Stepped", new Vector3(-32f, 0, 105f), 10f, 16f, -5, m.MidConcrete, 6f, 0);
            // 6. Destroyed checkpoint.
            AddBox(p, "L_Checkpoint", new Vector3(-26f, 2.5f, 8f), new Vector3(8f, 5f, 8f), Q(r, 0), m.MidSteel, true);
            AddBox(p, "L_Check_Rub", new Vector3(-26f, .6f, 8f), new Vector3(11f, 1.2f, 11f), Q(r, 10f), m.MidRubble, false);
        }

        private static void LandmarkTower(Transform p, Materials m, System.Random r, string name,
            Vector3 pos, float w, float h, float yaw, Material mat, float lean, float stepH)
        {
            AddBox(p, name, new Vector3(pos.x, h * .5f, pos.z), new Vector3(w, h, w), Q(r, yaw, lean), mat, true);
            if (stepH > 0)
                AddBox(p, name + "_Step", new Vector3(pos.x + NF(r)*2-1, h + stepH*.3f, pos.z + NF(r)*2-1),
                    new Vector3(w*.65f, stepH, w*.65f), Q(r, yaw + NF(r)*30, lean*.5f), mat, true);
            AddBox(p, name + "_Rub", new Vector3(pos.x, .6f, pos.z), new Vector3(w + 4f, 1.2f, w + 4f), Q(r, yaw + NF(r)*30), m.MidRubble, false);
        }

        // ===== Far city (stepped/broken dark silhouettes) =====
        private static void BuildFarCity(Transform p, Materials m, System.Random r)
        {
            for (int i = 0; i < 44; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                float x = side * (52f + NF(r) * 78f), z = -60f + NF(r) * 260f;
                float w = 5f + NF(r) * 11f, d = 5f + NF(r) * 11f, h = 10f + NF(r) * 35f;
                float lean = NF(r) < 0.2f ? NF(r) * 10f : 0;
                AddBox(p, "F_" + i, new Vector3(x, h * .5f, z), new Vector3(w, h, d), Q(r, NF(r)*20, lean), m.Silhouette, false);
                // Stepped/broken top
                if (NF(r) < 0.65f)
                {
                    float sh = h * (0.2f + NF(r) * 0.3f);
                    AddBox(p, "F_" + i + "t", new Vector3(x + NF(r)*3-1.5f, h + sh*.2f, z + NF(r)*3-1.5f),
                        new Vector3(w*.65f, sh, d*.65f), Q(r, NF(r)*40, lean*.5f), m.Silhouette, false);
                }
            }
        }

        // ===== Fire zones (large flame masses) =====
        private static void BuildFireZones(Transform p, Materials m, System.Random r)
        {
            // 4 PRIMARY zones — large flame masses (5-7 cubes each, 2-4m).
            for (int i = 0; i < 4 && i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                int flames = 5 + (int)(NF(r) * 3);
                for (int f = 0; f < flames; f++)
                {
                    float fx = z.x + (NF(r)-.5f) * 5f, fz = z.z + (NF(r)-.5f) * 5f;
                    float fh = 2f + NF(r) * 3f, fw = 1.5f + NF(r) * 2f;
                    AddBox(p, "Fire_" + i + "_" + f, new Vector3(fx, fh*.5f, fz),
                        new Vector3(fw, fh, fw), Q(r, NF(r)*360f, NF(r)*20f), m.Fire, false);
                }
            }
            // 3 SECONDARY smoldering points (smaller, 2-3 cubes each, 1-2m).
            for (int i = 4; i < 7 && i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                for (int f = 0; f < 2 + (int)NF(r); f++)
                {
                    float fx = z.x + (NF(r)-.5f)*3f, fz = z.z + (NF(r)-.5f)*3f;
                    AddBox(p, "FireS_" + i + "_" + f, new Vector3(fx, .8f, fz),
                        new Vector3(1f + NF(r), 1.5f, 1f + NF(r)), Q(r, NF(r)*360f), m.Fire, false);
                }
            }
        }

        // ===== Smoke plumes (wide, tall, multi-section tapered) =====
        private static void BuildSmokePlumes(Transform p, Materials m, System.Random r)
        {
            for (int i = 0; i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                float totalH = 25f + NF(r) * 20f; // 25-45m
                int sections = 7;
                for (int s = 0; s < sections; s++)
                {
                    float t = (float)s / sections;
                    float secH = totalH / sections;
                    float yC = secH * (s + .5f);
                    float radius = 2.5f + t * 6f + NF(r) * 1.2f; // wider at top
                    float drift = s * 0.6f;
                    AddCylinder(p, "Sm_" + i + "_" + s,
                        new Vector3(z.x + (NF(r)-.5f)*drift, yC, z.z + (NF(r)-.5f)*drift),
                        new Vector3(radius, secH * 1.15f, radius), Q(r, NF(r)*25f), m.Smoke, false);
                }
            }
        }

        // ===== Scorch zones (large irregular dark patches + wreckage) =====
        private static void BuildScorchZones(Transform p, Materials m, System.Random r)
        {
            for (int i = 0; i < DestructionZones.Length; i++)
            {
                Vector3 z = DestructionZones[i];
                // 4 overlapping rotated irregular scorch patches.
                for (int s = 0; s < 4; s++)
                {
                    float sz = 8f + NF(r) * 6f;
                    AddBox(p, "Sc_" + i + "_" + s,
                        new Vector3(z.x + (NF(r)-.5f)*5f, .03f, z.z + (NF(r)-.5f)*5f),
                        new Vector3(sz, .06f, sz * (.6f + NF(r)*.5f)), Q(r, NF(r)*360f), m.Scorch, false);
                }
                // 5 wreckage debris cubes.
                for (int d = 0; d < 5; d++)
                    AddBox(p, "Wk_" + i + "_" + d,
                        new Vector3(z.x + (NF(r)-.5f)*8f, .3f + NF(r)*.5f, z.z + (NF(r)-.5f)*8f),
                        new Vector3(.5f + NF(r)*1.2f, .5f + NF(r), .5f + NF(r)*1.2f),
                        Q(r, NF(r)*40f, NF(r)*360f, NF(r)*40f), m.MidRust, false);
            }
        }

        // ===== Haze (stronger atmospheric depth) =====
        private static void BuildHaze(Transform p, Materials m, System.Random r)
        {
            // Mid-distance depth haze (taller panels).
            for (int i = 0; i < 6; i++)
            {
                int side = (i % 2 == 0) ? 1 : -1;
                AddBox(p, "Hz_" + i, new Vector3(side * 46f, 12f + NF(r)*6f, -20f + i * 25f),
                    new Vector3(5f, 24f + NF(r)*10f, 55f + NF(r)*25f), Q(r, NF(r)*15f), m.Haze, false);
            }
            // Large dirty atmosphere layer (horizontal thin panels at mid-altitude).
            AddBox(p, "Hz_AtmoA", new Vector3(0f, 18f, 40f), new Vector3(160f, 8f, 6f), Q(r, 5f), m.Haze, false);
            AddBox(p, "Hz_AtmoB", new Vector3(0f, 14f, 80f), new Vector3(140f, 6f, 5f), Q(r, -8f), m.Haze, false);
            // Far boundary drifts (larger).
            AddBox(p, "Hz_Far", new Vector3(0f, 4f, 190f), new Vector3(320f, 8f, 14f), Quaternion.identity, m.Haze, false);
            AddBox(p, "Hz_R", new Vector3(155f, 4f, 70f), new Vector3(14f, 8f, 300f), Quaternion.identity, m.Haze, false);
            AddBox(p, "Hz_L", new Vector3(-155f, 4f, 70f), new Vector3(14f, 8f, 300f), Quaternion.identity, m.Haze, false);
        }

        // ===== Helpers =====
        private static Material PickMid(Materials m, System.Random r)
        { float v = NF(r); return v < .3f ? m.MidConcrete : v < .55f ? m.MidConcreteDark : v < .75f ? m.MidRust : v < .9f ? m.MidSteel : m.MidRubble; }

        private static float NF(System.Random r) => (float)r.NextDouble();
        private static Quaternion Q(System.Random r, float yaw) => Quaternion.Euler(0f, yaw, 0f);
        private static Quaternion Q(System.Random r, float yaw, float lean) => Quaternion.Euler(NF(r)*lean*.3f, yaw, NF(r)*lean*.5f);
        private static Quaternion Q(System.Random r, float x, float y, float z) => Quaternion.Euler(x, y, z);

        private static GameObject AddBox(Transform p, string n, Vector3 pos, Vector3 scl, Quaternion rot, Material mat, bool shadows) =>
            AddPart(p, PrimitiveType.Cube, n, pos, scl, rot, mat, shadows);
        private static GameObject AddCylinder(Transform p, string n, Vector3 pos, Vector3 scl, Quaternion rot, Material mat, bool shadows) =>
            AddPart(p, PrimitiveType.Cylinder, n, pos, scl, rot, mat, shadows);

        private static GameObject AddPart(Transform p, PrimitiveType type, string name, Vector3 pos,
            Vector3 scale, Quaternion rot, Material mat, bool shadows)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name; go.transform.SetParent(p, false);
            go.transform.localScale = scale; go.transform.localPosition = pos; go.transform.localRotation = rot;
            var mr = go.GetComponent<MeshRenderer>();
            if (mat != null) mr.sharedMaterial = mat;
            mr.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            mr.receiveShadows = shadows;
            var col = go.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Object.Destroy(col); else Object.DestroyImmediate(col); }
            return go;
        }
    }
}
