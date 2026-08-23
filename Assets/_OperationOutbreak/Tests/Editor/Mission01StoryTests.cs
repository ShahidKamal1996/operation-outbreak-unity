using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.Mission;
using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z.1 — tests for Mission 01's story integration: sequence assets exist and
    /// validate, Mission_01 references them, the opening/outro contain the correct content,
    /// and missions without sequences preserve legacy behavior.
    /// </summary>
    public sealed class Mission01StoryTests
    {
        private const string OpeningPath =
            "Assets/_OperationOutbreak/Resources/StorySequences/Chapter01_Mission01_Opening.asset";
        private const string OutroPath =
            "Assets/_OperationOutbreak/Resources/StorySequences/Chapter01_Mission01_Outro.asset";
        private const string M01Path =
            "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset";

        private static T Load<T>(string path) where T : Object =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);

        [Test]
        public void Mission01HasPreAndPostMissionSequences()
        {
            MissionDefinition m = Load<MissionDefinition>(M01Path);
            Assert.IsNotNull(m, "Mission_01 must exist.");
            Assert.IsNotNull(m.PreMissionSequence, "Mission_01 must reference a pre-mission sequence.");
            Assert.IsNotNull(m.PostMissionSequence, "Mission_01 must reference a post-mission sequence.");
        }

        [Test]
        public void OpeningSequenceValidatesCleanly()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OpeningPath);
            Assert.IsNotNull(seq, "Opening sequence asset must exist.");
            Assert.IsEmpty(StorySequenceDefinition.CollectProblems(seq),
                "Opening must validate cleanly: " + string.Join(" | ", StorySequenceDefinition.CollectProblems(seq)));
        }

        [Test]
        public void OutroSequenceValidatesCleanly()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OutroPath);
            Assert.IsNotNull(seq, "Outro sequence asset must exist.");
            Assert.IsEmpty(StorySequenceDefinition.CollectProblems(seq),
                "Outro must validate cleanly: " + string.Join(" | ", StorySequenceDefinition.CollectProblems(seq)));
        }

        [Test]
        public void OpeningHasBalancedGameplayLock()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OpeningPath);
            int depth = 0;
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.GameplayLock) depth++;
                if (b.beatType == StoryBeatType.GameplayUnlock) depth--;
            }
            Assert.AreEqual(0, depth, "Opening must have balanced GameplayLock/GameplayUnlock beats.");
        }

        [Test]
        public void OpeningContainsKaneReyesAndRavenDialogue()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OpeningPath);
            var speakers = new HashSet<string>();
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.Dialogue && b.dialogue != null)
                    speakers.Add(b.dialogue.speakerId);
            }
            Assert.Contains("adrian_kane", speakers.ToList());
            Assert.Contains("sofia_reyes", speakers.ToList());
            Assert.Contains("raven_ortiz", speakers.ToList());
        }

        [Test]
        public void OutroContainsDistressTransmission()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OutroPath);
            bool hasHook = false;
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.Dialogue && b.dialogue != null
                    && b.dialogue.text.Contains("Don't leave us"))
                {
                    hasHook = true;
                }
            }
            Assert.IsTrue(hasHook, "Outro must contain the 'Don't leave us.' narrative hook.");
        }

        [Test]
        public void OpeningHasRequiredOnSkipHandoffCue()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OpeningPath);
            bool hasRequiredHandoff = false;
            for (int i = 0; i < seq.BeatCount; i++)
            {
                var b = seq.GetBeat(i);
                if (b.beatType == StoryBeatType.CameraCue
                    && b.cueId == "gameplay_handoff"
                    && b.cuePolicy == StoryCuePolicy.RequiredOnSkip)
                {
                    hasRequiredHandoff = true;
                }
            }
            Assert.IsTrue(hasRequiredHandoff,
                "Opening must have a RequiredOnSkip 'gameplay_handoff' camera cue so skip still restores gameplay camera.");
        }

        [Test]
        public void MissionsWithoutPostSequencePreserveLegacyBehavior()
        {
            MissionDefinition m02 = Load<MissionDefinition>(
                "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_02.asset");
            Assert.IsNotNull(m02);
            Assert.IsNull(m02.PostMissionSequence,
                "Missions without post-mission sequences must have null PostMissionSequence (legacy immediate result).");
            Assert.IsNull(m02.PreMissionSequence,
                "Missions without pre-mission sequences must have null PreMissionSequence.");
        }

        [Test]
        public void OutroSequenceTypeIsPostMission()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OutroPath);
            Assert.AreEqual(StorySequenceType.PostMission, seq.SequenceType,
                "Outro must be sequence type PostMission.");
        }

        [Test]
        public void OpeningSequenceTypeIsPreMission()
        {
            StorySequenceDefinition seq = Load<StorySequenceDefinition>(OpeningPath);
            Assert.AreEqual(StorySequenceType.PreMission, seq.SequenceType,
                "Opening must be sequence type PreMission.");
        }
    }

    internal static class HashSetExtensions
    {
        public static System.Collections.Generic.List<T> ToList<T>(this HashSet<T> set) =>
            new System.Collections.Generic.List<T>(set);
    }
}
