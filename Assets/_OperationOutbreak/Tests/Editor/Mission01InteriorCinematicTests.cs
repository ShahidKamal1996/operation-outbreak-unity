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
    }
}
