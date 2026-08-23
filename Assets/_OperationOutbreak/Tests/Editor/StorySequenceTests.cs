using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using OperationOutbreak.Story;
using UnityEngine;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1Z — focused EditMode tests for the cinematic/dialogue foundation:
    /// sequence validation, dialogue data, gameplay-lock count semantics.
    /// </summary>
    public sealed class StorySequenceTests
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _created.Count; i++)
                if (_created[i] != null) Object.DestroyImmediate(_created[i]);
            _created.Clear();
            StoryCueEvents.ClearSubscribers();
        }

        // ---- helpers ----

        private static StoryBeatDefinition Beat(StoryBeatType type) => new StoryBeatDefinition { beatType = type };

        private static StoryBeatDefinition DialogueBeat(string speaker, string text) =>
            new StoryBeatDefinition
            {
                beatType = StoryBeatType.Dialogue,
                dialogue = new StoryDialogueLine { speakerId = speaker, text = text },
                autoAdvance = true
            };

        private StorySequenceDefinition Sequence(params StoryBeatDefinition[] beats)
        {
            StorySequenceDefinition seq = ScriptableObject.CreateInstance<StorySequenceDefinition>();
            SetField(seq, "sequenceId", "seq_test");
            SetField(seq, "displayName", "Test Sequence");
            SetField(seq, "beats", new List<StoryBeatDefinition>(beats));
            _created.Add(seq);
            return seq;
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo f = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(f, "Field '" + name + "' missing.");
            f.SetValue(target, value);
        }

        private static bool HasProblem(List<string> problems, string fragment)
        {
            foreach (string p in problems) if (p.Contains(fragment)) return true;
            return false;
        }

        // ---- validation ----

        [Test]
        public void ValidSequencePassesValidation()
        {
            StorySequenceDefinition seq = Sequence(
                DialogueBeat("adrian_kane", "We need to move."),
                Beat(StoryBeatType.Wait) );
            seq.GetBeat(1).duration = 2f;

            Assert.IsEmpty(StorySequenceDefinition.CollectProblems(seq));
        }

        [Test]
        public void EmptySequenceIdRejected()
        {
            StorySequenceDefinition seq = Sequence(DialogueBeat("narrator", "text"));
            SetField(seq, "sequenceId", "");
            Assert.IsTrue(HasProblem(StorySequenceDefinition.CollectProblems(seq), "missing stable sequence id"));
        }

        [Test]
        public void EmptyBeatsRejected()
        {
            StorySequenceDefinition seq = Sequence();
            Assert.IsTrue(HasProblem(StorySequenceDefinition.CollectProblems(seq), "no beats"));
        }

        [Test]
        public void DialogueWithoutSpeakerOrTextRejected()
        {
            StorySequenceDefinition seq = Sequence(
                new StoryBeatDefinition { beatType = StoryBeatType.Dialogue, dialogue = new StoryDialogueLine() });

            List<string> problems = StorySequenceDefinition.CollectProblems(seq);
            Assert.IsTrue(HasProblem(problems, "dialogue text is empty"));
            Assert.IsTrue(HasProblem(problems, "no speaker id"));
        }

        [Test]
        public void InvalidWaitDurationRejected()
        {
            StorySequenceDefinition seq = Sequence(Beat(StoryBeatType.Wait));
            seq.GetBeat(0).duration = 0f;
            Assert.IsTrue(HasProblem(StorySequenceDefinition.CollectProblems(seq), "Wait duration must be > 0"));
        }

        [Test]
        public void GameplayLockImbalanceRejected()
        {
            StorySequenceDefinition seq = Sequence(Beat(StoryBeatType.GameplayLock));
            Assert.IsTrue(HasProblem(StorySequenceDefinition.CollectProblems(seq), "lock/unlock imbalance"));
        }

        [Test]
        public void GameplayUnlockWithoutLockRejected()
        {
            StorySequenceDefinition seq = Sequence(Beat(StoryBeatType.GameplayUnlock));
            Assert.IsTrue(HasProblem(StorySequenceDefinition.CollectProblems(seq), "without matching"));
        }

        [Test]
        public void CueBeatWithoutCueIdRejected()
        {
            StorySequenceDefinition seq = Sequence(Beat(StoryBeatType.CameraCue));
            Assert.IsTrue(HasProblem(StorySequenceDefinition.CollectProblems(seq), "no cueId"));
        }

        [Test]
        public void OptionalVoiceClipNullIsValid()
        {
            StorySequenceDefinition seq = Sequence(
                new StoryBeatDefinition
                {
                    beatType = StoryBeatType.Dialogue,
                    dialogue = new StoryDialogueLine { speakerId = "narrator", text = "No voice.", voiceClip = null }
                });

            Assert.IsEmpty(StorySequenceDefinition.CollectProblems(seq),
                "A dialogue line with null voiceClip must validate cleanly.");
        }

        // ---- gameplay lock authority ----

        [Test]
        public void NestedLocksDoNotUnlockEarly()
        {
            GameObject go = new GameObject("LockAuthority");
            _created.Add(go);
            GameplayLockAuthority lockAuth = go.AddComponent<GameplayLockAuthority>();

            Assert.IsFalse(lockAuth.IsLocked);

            lockAuth.Lock();
            Assert.IsTrue(lockAuth.IsLocked, "First lock should suspend.");

            lockAuth.Lock();
            Assert.IsTrue(lockAuth.IsLocked, "Nested lock should stay locked.");

            lockAuth.Unlock();
            Assert.IsTrue(lockAuth.IsLocked, "Single unlock should NOT resume (nested).");

            lockAuth.Unlock();
            Assert.IsFalse(lockAuth.IsLocked, "Final unlock should resume.");
        }

        [Test]
        public void ForceUnlockReleasesAllLocks()
        {
            GameObject go = new GameObject("LockAuthority");
            _created.Add(go);
            GameplayLockAuthority lockAuth = go.AddComponent<GameplayLockAuthority>();

            lockAuth.Lock();
            lockAuth.Lock();
            Assert.IsTrue(lockAuth.IsLocked);

            lockAuth.ForceUnlock();
            Assert.IsFalse(lockAuth.IsLocked, "ForceUnlock must release all locks.");
        }

        // ---- mission integration seam ----

        [Test]
        public void MissionDefinitionSequenceReferencesAreOptional()
        {
            MissionDefinition mission = ScriptableObject.CreateInstance<MissionDefinition>();
            _created.Add(mission);

            // Pre/post sequence references default to null. This must be valid.
            Assert.IsNull(mission.PreMissionSequence);
            Assert.IsNull(mission.PostMissionSequence);
        }
    }
}
