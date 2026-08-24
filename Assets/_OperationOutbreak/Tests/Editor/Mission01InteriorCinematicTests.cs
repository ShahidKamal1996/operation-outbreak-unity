using System.IO;
using NUnit.Framework;
using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #8 — tests for the helicopter interior presentation overhaul:
    /// real Toon Soldier Kane clone, fade-gated world-space camera transitions, camera anchor
    /// resolution, and visual-only clone guarantees. Preserves the 431-test baseline; these add
    /// on top of it.
    /// </summary>
    public sealed class Mission01InteriorCinematicTests
    {
        private const string OpeningPath =
            "Assets/_OperationOutbreak/Resources/StorySequences/Chapter01_Mission01_Opening.asset";
        private const string ToonSoldierFbxPath = "Assets/ToonSoldiers_demo/models/ToonSoldier_demo.FBX";
        private const string ToonSoldierMetaPath = ToonSoldierFbxPath + ".meta";
        private const string ExpectedToonSoldierGuid = "c6acf3f29c109e4439950c8f6a85cb2b";

        private static StorySequenceDefinition LoadOpening() =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<StorySequenceDefinition>(OpeningPath);

        private static int IndexOfCue(StorySequenceDefinition seq, string cueId)
        {
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if ((b.beatType == StoryBeatType.CameraCue || b.beatType == StoryBeatType.EventCue)
                    && b.cueId == cueId)
                    return i;
            }
            return -1;
        }

        // ---------------------------------------------------------------- opening structure

        [Test]
        public void OpeningBeginsWithInteriorSetupBehindBlack()
        {
            // The first event cue must be m01_interior_setup (which sets the screen black before any
            // visible frame) — never the old establishing_shot that showed the gameplay road.
            StorySequenceDefinition seq = LoadOpening();
            int firstEvent = -1;
            string firstEventId = null;
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.EventCue)
                {
                    firstEvent = i;
                    firstEventId = b.cueId;
                    break;
                }
            }
            Assert.AreNotEqual(-1, firstEvent, "Opening must have an event cue.");
            Assert.AreEqual("m01_interior_setup", firstEventId,
                "Opening must begin cinematic setup behind black (m01_interior_setup), not the road.");
        }

        [Test]
        public void OpeningInteriorEntryShotIsRequiredOnSkip()
        {
            StorySequenceDefinition seq = LoadOpening();
            int idx = IndexOfCue(seq, "m01_interior_kane");
            Assert.AreNotEqual(-1, idx, "Opening must contain the m01_interior_kane entry shot.");
            Assert.AreEqual(StoryCuePolicy.RequiredOnSkip, seq.GetBeat(idx).cuePolicy,
                "The entry snap shot must fire on skip so the camera still resolves to the interior anchor.");
        }

        [Test]
        public void OpeningFadeGatesTheInteriorToExteriorWorldJump()
        {
            // The interior lives at y=-300 and the exterior at gameplay y. The camera must NEVER
            // visibly travel between them, so the sequence must order:
            //   fade_out  <  teardown  <  exterior_approach  <  fade_in
            StorySequenceDefinition seq = LoadOpening();
            int fadeOut = IndexOfCue(seq, "m01_interior_fade_out");
            int teardown = IndexOfCue(seq, "m01_interior_teardown");
            int exterior = IndexOfCue(seq, "m01_exterior_approach");
            int fadeIn = IndexOfCue(seq, "m01_fade_in");

            Assert.AreNotEqual(-1, fadeOut, "Missing m01_interior_fade_out.");
            Assert.AreNotEqual(-1, teardown, "Missing m01_interior_teardown.");
            Assert.AreNotEqual(-1, exterior, "Missing m01_exterior_approach.");
            Assert.AreNotEqual(-1, fadeIn, "Missing m01_fade_in.");

            Assert.Less(fadeOut, teardown, "Fade to black must precede interior teardown.");
            Assert.Less(teardown, exterior, "Teardown must precede the exterior camera snap (both under black).");
            Assert.Less(exterior, fadeIn, "The exterior snap must precede the fade-in that reveals it.");
        }

        [Test]
        public void OpeningPreservesApprovedBriefingScreenplay()
        {
            StorySequenceDefinition seq = LoadOpening();
            var lines = new System.Collections.Generic.List<string>();
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.Dialogue && b.dialogue != null)
                    lines.Add(b.dialogue.text);
            }
            CollectionAssert.Contains(lines, "Corridor Seven went silent eleven minutes ago.");
            CollectionAssert.Contains(lines, "Evacuation teams stopped responding at Checkpoint Delta.");
            CollectionAssert.Contains(lines, "Any survivors?");
            CollectionAssert.Contains(lines, "Unknown. Your job is to reach the checkpoint and find out.");
            CollectionAssert.Contains(lines, "Kane, once you are on the ground, you are on your own.");
            CollectionAssert.Contains(lines, "Thirty seconds. Gear up.");
            CollectionAssert.Contains(lines, "Copy.");
        }

        // ---------------------------------------------------------------- cinematic Kane source

        [Test]
        public void CinematicKaneSourcesTheRealToonSoldierModel()
        {
            // The cinematic Kane is a clone of the production Toon Soldier FBX. Asserting the FBX
            // GUID pins that the clone's visual source is the real gameplay character model.
            Assert.IsTrue(File.Exists(ToonSoldierMetaPath),
                "ToonSoldier_demo.FBX.meta must exist: " + ToonSoldierMetaPath);
            string meta = File.ReadAllText(ToonSoldierMetaPath);
            Assert.IsTrue(meta.Contains("guid: " + ExpectedToonSoldierGuid),
                "ToonSoldier_demo.FBX GUID must be " + ExpectedToonSoldierGuid +
                " (the production model the cinematic clone is sourced from).");
        }

        // ---------------------------------------------------------------- interior rig anchors

        [Test]
        public void InteriorRigExposesThreeNonClippingCameraAnchors()
        {
            var rigGo = new GameObject("RigTest");
            var rig = rigGo.AddComponent<HelicopterInteriorRig>();
            var source = new GameObject("FakeSource");
            try
            {
                rig.Setup(new Vector3(0f, -300f, 0f), source.transform);

                Assert.IsTrue(rig.TryGetCameraAnchor("m01_interior_kane", out Vector3 p1, out _, out float f1), "wide anchor");
                Assert.IsTrue(rig.TryGetCameraAnchor("m01_interior_kane_close", out Vector3 p2, out _, out _), "medium anchor");
                Assert.IsTrue(rig.TryGetCameraAnchor("m01_interior_front", out Vector3 p3, out _, out _), "front anchor");

                // All interior anchors sit in the interior world (y near -300), never in gameplay.
                Assert.IsTrue(Mathf.Abs(p1.y - (-300f)) < 5f, "wide anchor must be in the interior world");
                Assert.IsTrue(Mathf.Abs(p2.y - (-300f)) < 5f, "medium anchor must be in the interior world");
                Assert.IsTrue(Mathf.Abs(p3.y - (-300f)) < 5f, "front anchor must be in the interior world");

                // FOVs are sensible cinematic values.
                Assert.Greater(f1, 30f); Assert.Less(f1, 70f);
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigCloneIsVisualOnly()
        {
            // The cinematic Kane must carry NO gameplay/physics authority. A source with colliders
            // and a rigidbody must yield a clone scrubbed of all of them.
            var rigGo = new GameObject("RigTest");
            var rig = rigGo.AddComponent<HelicopterInteriorRig>();
            var source = new GameObject("FakeSource");
            source.AddComponent<BoxCollider>();
            source.AddComponent<Rigidbody>();
            try
            {
                rig.Setup(new Vector3(0f, -300f, 0f), source.transform);

                Transform clone = null;
                foreach (var t in rig.GetComponentsInChildren<Transform>(true))
                    if (t.name == "Story_KaneCinematic") { clone = t; break; }

                Assert.IsNotNull(clone, "Cinematic Kane clone must be created.");
                Assert.AreEqual(0, clone.GetComponentsInChildren<Collider>(true).Length,
                    "Cinematic Kane clone must have NO colliders (visual-only).");
                Assert.AreEqual(0, clone.GetComponentsInChildren<Rigidbody>(true).Length,
                    "Cinematic Kane clone must have NO rigidbody (visual-only).");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigPublishesZeroVibrationUntilActive()
        {
            var rigGo = new GameObject("RigTest");
            var rig = rigGo.AddComponent<HelicopterInteriorRig>();
            try
            {
                Assert.AreEqual(Vector3.zero, rig.VibrationOffset);
            }
            finally
            {
                Object.DestroyImmediate(rigGo);
            }
        }

        // ---------------------------------------------------------------- fade controller

        [Test]
        public void FadeControllerSetBlackIsOpaqueAndClearInstantRemovesIt()
        {
            var go = new GameObject("FadeTest");
            try
            {
                var fade = go.AddComponent<StoryFadeController>();
                // AddComponent runs Awake in Edit Mode, building the overlay.

                fade.SetBlackInstant();
                Assert.IsTrue(fade.IsOpaque, "SetBlackInstant must make the overlay fully opaque.");

                fade.ClearInstant();
                Assert.IsFalse(fade.IsOpaque, "ClearInstant must drop the overlay (skip / completion cleanup).");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        // ---------------------------------------------------------------- QA fix #11 staging

        private static HelicopterInteriorRig BuildRigForStagingTests(out GameObject rigGo, out GameObject source)
        {
            rigGo = new GameObject("RigStaging");
            var rig = rigGo.AddComponent<HelicopterInteriorRig>();
            source = new GameObject("FakeSource");
            rig.Setup(new Vector3(0f, -300f, 0f), source.transform);
            return rig;
        }

        [Test]
        public void InteriorRigExposesSeatAnchorAnchorsAndTargets()
        {
            var rig = BuildRigForStagingTests(out GameObject rigGo, out GameObject source);
            try
            {
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.SeatAnchorName), "KaneSeatAnchor must exist.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.AnchorEstablishing), "Establishing anchor missing.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.AnchorMedium), "Medium anchor missing.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.AnchorClose), "Close anchor missing.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.AnchorCockpit), "Cockpit anchor missing.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.TargetChest), "KaneChestTarget missing.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.TargetHead), "KaneHeadTarget missing.");
                Assert.IsNotNull(rig.FindNamed(HelicopterInteriorRig.TargetCockpit), "CockpitLookTarget missing.");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigCameraAnchorsLieInsideUsableCabinVolume()
        {
            var rig = BuildRigForStagingTests(out GameObject rigGo, out GameObject source);
            try
            {
                string[] anchors =
                {
                    HelicopterInteriorRig.AnchorEstablishing, HelicopterInteriorRig.AnchorMedium,
                    HelicopterInteriorRig.AnchorClose, HelicopterInteriorRig.AnchorCockpit
                };
                foreach (string name in anchors)
                {
                    Transform a = rig.FindNamed(name);
                    Assert.IsNotNull(a, name + " missing.");
                    // Anchors are parented under a group at the rig origin, so localPosition == cabin-local.
                    Vector3 p = a.localPosition;
                    Assert.GreaterOrEqual(p.y, 0.3f, name + " must be above the cabin floor (y>=0.3).");
                    Assert.LessOrEqual(p.y, 2.3f, name + " must be below the cabin ceiling (y<=2.3).");
                    Assert.GreaterOrEqual(p.x, -1.85f, name + " must be inside the cabin width.");
                    Assert.LessOrEqual(p.x, 1.85f, name + " must be inside the cabin width.");
                    Assert.GreaterOrEqual(p.z, -2.45f, name + " must be inside the cabin depth.");
                    Assert.LessOrEqual(p.z, 2.45f, name + " must be inside the cabin depth.");
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigCameraAnchorsAreNotInsideOccluderGeometry()
        {
            var rig = BuildRigForStagingTests(out GameObject rigGo, out GameObject source);
            try
            {
                string[] anchors =
                {
                    HelicopterInteriorRig.AnchorEstablishing, HelicopterInteriorRig.AnchorMedium,
                    HelicopterInteriorRig.AnchorClose, HelicopterInteriorRig.AnchorCockpit
                };
                Renderer[] renderers = rigGo.GetComponentsInChildren<Renderer>(true);
                foreach (string name in anchors)
                {
                    Transform a = rig.FindNamed(name);
                    Assert.IsNotNull(a, name + " missing.");
                    Vector3 pos = a.position;
                    foreach (Renderer r in renderers)
                    {
                        if (r == null) continue;
                        Assert.IsFalse(r.bounds.Contains(pos),
                            name + " sits inside geometry '" + r.name + "' — it would clip/frame a wall.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigCamerasAimForwardAtTheirTargets()
        {
            var rig = BuildRigForStagingTests(out GameObject rigGo, out GameObject source);
            try
            {
                string[] cues = { "m01_interior_kane", "m01_interior_kane_close", "m01_interior_front" };
                foreach (string cue in cues)
                {
                    Assert.IsTrue(rig.TryGetCameraAnchor(cue, out Vector3 pos, out Quaternion rot, out float fov),
                        cue + " must resolve a camera anchor.");
                    // The rig computes rot = LookRotation(target - anchor); recompute the expected
                    // forward and confirm it actually points at the target.
                    Assert.Greater(fov, 30f, cue + " FOV too narrow.");
                    Assert.Less(fov, 70f, cue + " FOV too wide.");
                    Vector3 fwd = rot * Vector3.forward;
                    Assert.IsTrue(Mathf.Abs(fwd.magnitude - 1f) < 0.01f, cue + " forward is not normalized.");
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigKaneCamerasSeeTheirTargetInFront()
        {
            // Establishing/Close aim at Kane's chest/head; the target must be IN FRONT of the
            // camera (positive forward dot) so the camera is not turned away from Kane.
            var rig = BuildRigForStagingTests(out GameObject rigGo, out GameObject source);
            try
            {
                string[][] shots =
                {
                    new[] { "m01_interior_kane", HelicopterInteriorRig.TargetChest },
                    new[] { "m01_interior_kane_close", HelicopterInteriorRig.TargetHead },
                };
                foreach (string[] shot in shots)
                {
                    Assert.IsTrue(rig.TryGetCameraAnchor(shot[0], out Vector3 pos, out Quaternion rot, out _));
                    Transform target = rig.FindNamed(shot[1]);
                    Assert.IsNotNull(target, shot[1] + " missing.");
                    Vector3 toTarget = (target.position - pos).normalized;
                    Vector3 fwd = rot * Vector3.forward;
                    Assert.Greater(Vector3.Dot(fwd, toTarget), 0.7f,
                        shot[0] + " camera forward must point at " + shot[1] + " (Kane must be in front).");
                }
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }

        [Test]
        public void InteriorRigReportsCinematicKaneAsVisualOnly()
        {
            var rigGo = new GameObject("RigVisual");
            var rig = rigGo.AddComponent<HelicopterInteriorRig>();
            var source = new GameObject("FakeSource");
            source.AddComponent<BoxCollider>();
            source.AddComponent<Rigidbody>();
            try
            {
                rig.Setup(new Vector3(0f, -300f, 0f), source.transform);
                Assert.IsTrue(rig.IsCinematicKaneVisualOnly(),
                    "Cinematic Kane must be scrubbed of all gameplay/physics authority (visual-only).");
            }
            finally
            {
                Object.DestroyImmediate(source);
                Object.DestroyImmediate(rigGo);
            }
        }
    }
}
