#if UNITY_EDITOR
using System.Collections.Generic;
using OperationOutbreak.Mission;
using OperationOutbreak.Story;
using UnityEditor;
using UnityEngine;

namespace OperationOutbreak.EditorTools
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #3 — generates the Mission 01 Opening and Outro
    /// StorySequenceDefinition assets through Unity's own serialization APIs so the nested
    /// [Serializable] beats/dialogue are correctly serialized. Hand-written YAML proved
    /// unreliable for this model.
    ///
    ///   Tools > Operation Outbreak > Story > Regenerate Mission 01 Story Assets
    ///
    /// Creates valid assets, populates all beats/dialogue, assigns them to Mission_01,
    /// and validates. Safe to re-run (replaces existing assets).
    /// </summary>
    public static class Mission01StoryAssetGenerator
    {
        private const string SeqFolder = "Assets/_OperationOutbreak/Resources/StorySequences";
        private const string OpeningPath = SeqFolder + "/Chapter01_Mission01_Opening.asset";
        private const string OutroPath = SeqFolder + "/Chapter01_Mission01_Outro.asset";
        private const string M01Path = "Assets/_OperationOutbreak/Resources/MissionDefinitions/Mission_01.asset";

        [MenuItem("Tools/Operation Outbreak/Story/Regenerate Mission 01 Story Assets")]
        public static void Regenerate()
        {
            if (!AssetDatabase.IsValidFolder(SeqFolder))
                AssetDatabase.CreateFolder("Assets/_OperationOutbreak/Resources", "StorySequences");

            StorySequenceDefinition opening = CreateOpening();
            StorySequenceDefinition outro = CreateOutro();

            WriteAsset(opening, OpeningPath, "Chapter01_Mission01_Opening");
            WriteAsset(outro, OutroPath, "Chapter01_Mission01_Outro");

            AssignToMission01(opening, outro);

            if (Validate(opening, outro))
                Debug.Log("[STORY AUTHORING] Mission 01 story assets generated and validated.");
            else
                Debug.LogError("[STORY AUTHORING] Validation FAILED — see errors above.");
        }

        private static StorySequenceDefinition CreateOpening()
        {
            var seq = ScriptableObject.CreateInstance<StorySequenceDefinition>();
            SetPrivate(seq, "sequenceId", "chapter01_mission01_opening");
            SetPrivate(seq, "displayName", "Chapter 1 Mission 01 Opening");
            SetPrivate(seq, "sequenceType", StorySequenceType.PreMission);
            SetPrivate(seq, "skippable", true);
            SetPrivate(seq, "autoStart", true);

            seq.GetBeat(-1); // touch to ensure laziness is resolved
            var beats = new List<StoryBeatDefinition>
            {
                Beat(StoryBeatType.GameplayLock),
                CueBeat(StoryBeatType.CameraCue, "establishing_shot", StoryCuePolicy.RequiredOnSkip),
                Dialog("raven_ortiz", "Corridor Seven ahead. No lights. No movement."),
                CueBeat(StoryBeatType.EventCue, "helicopter_approach"),
                WaitBeat(2f),
                Dialog("sofia_reyes", "Kane, Evacuation Corridor Seven went silent eleven minutes ago.", true),
                Dialog("adrian_kane", "Any response from the checkpoint?"),
                Dialog("sofia_reyes", "Nothing. Confirm the route and find out why.", true),
                WaitBeat(1f),
                CueBeat(StoryBeatType.EventCue, "helicopter_insert", StoryCuePolicy.RequiredOnSkip),
                Dialog("raven_ortiz", "I can't stay over this road. You're going in alone."),
                Dialog("adrian_kane", "Copy."),
                Dialog("raven_ortiz", "I'll be listening."),
                CueBeat(StoryBeatType.CameraCue, "gameplay_handoff", StoryCuePolicy.RequiredOnSkip),
                CueBeat(StoryBeatType.EventCue, "helicopter_depart"),
                Beat(StoryBeatType.GameplayUnlock),
            };
            SetPrivate(seq, "beats", beats);
            return seq;
        }

        private static StorySequenceDefinition CreateOutro()
        {
            var seq = ScriptableObject.CreateInstance<StorySequenceDefinition>();
            SetPrivate(seq, "sequenceId", "chapter01_mission01_outro");
            SetPrivate(seq, "displayName", "Chapter 1 Mission 01 Outro");
            SetPrivate(seq, "sequenceType", StorySequenceType.PostMission);
            SetPrivate(seq, "skippable", true);
            SetPrivate(seq, "autoStart", true);

            var beats = new List<StoryBeatDefinition>
            {
                Beat(StoryBeatType.GameplayLock),
                CueBeat(StoryBeatType.CameraCue, "checkpoint_view", StoryCuePolicy.RequiredOnSkip),
                WaitBeat(2f),
                Dialog("adrian_kane", "Corridor Seven is gone."),
                Dialog("sofia_reyes", "Then we're done. Fall back to Raven.", true),
                WaitBeat(2f),
                Dialog("narrator", "...please...", true),
                WaitBeat(1.5f),
                Dialog("narrator", "Don't leave us.", true),
                Dialog("adrian_kane", "Reyes, I have a survivor signal."),
                Dialog("sofia_reyes", "Kane-", true),
                Dialog("adrian_kane", "I'm going after it."),
                CueBeat(StoryBeatType.CameraCue, "gameplay_handoff", StoryCuePolicy.RequiredOnSkip),
                Beat(StoryBeatType.GameplayUnlock),
            };
            SetPrivate(seq, "beats", beats);
            return seq;
        }

        // ---- beat helpers ----

        private static StoryBeatDefinition Beat(StoryBeatType type) =>
            new StoryBeatDefinition { beatType = type, dialogue = new StoryDialogueLine() };

        private static StoryBeatDefinition Dialog(string speaker, string text, bool radio = false) =>
            new StoryBeatDefinition
            {
                beatType = StoryBeatType.Dialogue,
                autoAdvance = true,
                duration = 1f,
                dialogue = new StoryDialogueLine { speakerId = speaker, text = text, isRadio = radio }
            };

        private static StoryBeatDefinition WaitBeat(float dur) =>
            new StoryBeatDefinition { beatType = StoryBeatType.Wait, duration = dur, dialogue = new StoryDialogueLine() };

        private static StoryBeatDefinition CueBeat(StoryBeatType type, string cueId,
            StoryCuePolicy policy = StoryCuePolicy.Cosmetic) =>
            new StoryBeatDefinition { beatType = type, cueId = cueId, cuePolicy = policy, dialogue = new StoryDialogueLine() };

        // ---- I/O ----

        private static void WriteAsset(StorySequenceDefinition asset, string path, string name)
        {
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            asset.name = name;
            EditorUtility.SetDirty(asset);
        }

        private static void AssignToMission01(StorySequenceDefinition opening, StorySequenceDefinition outro)
        {
            MissionDefinition m01 = AssetDatabase.LoadAssetAtPath<MissionDefinition>(M01Path);
            if (m01 == null)
            {
                Debug.LogError("[STORY AUTHORING] Mission_01 not found at " + M01Path);
                return;
            }

            SerializedObject so = new SerializedObject(m01);
            so.FindProperty("preMissionSequence").objectReferenceValue = opening;
            so.FindProperty("postMissionSequence").objectReferenceValue = outro;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(m01);
        }

        private static bool Validate(StorySequenceDefinition opening, StorySequenceDefinition outro)
        {
            bool ok = true;

            var loadedOpen = AssetDatabase.LoadAssetAtPath<StorySequenceDefinition>(OpeningPath);
            var loadedOutro = AssetDatabase.LoadAssetAtPath<StorySequenceDefinition>(OutroPath);

            if (loadedOpen == null) { Debug.LogError("[STORY AUTHORING] Opening loads null."); ok = false; }
            if (loadedOutro == null) { Debug.LogError("[STORY AUTHORING] Outro loads null."); ok = false; }

            var openProblems = StorySequenceDefinition.CollectProblems(loadedOpen);
            var outroProblems = StorySequenceDefinition.CollectProblems(loadedOutro);
            if (openProblems.Count > 0) { Debug.LogError("[STORY AUTHORING] Opening problems: " + string.Join(", ", openProblems)); ok = false; }
            if (outroProblems.Count > 0) { Debug.LogError("[STORY AUTHORING] Outro problems: " + string.Join(", ", outroProblems)); ok = false; }

            MissionDefinition m01 = AssetDatabase.LoadAssetAtPath<MissionDefinition>(M01Path);
            if (m01 != null)
            {
                if (m01.PreMissionSequence != loadedOpen) { Debug.LogError("[STORY AUTHORING] M01 preMissionSequence mismatch."); ok = false; }
                if (m01.PostMissionSequence != loadedOutro) { Debug.LogError("[STORY AUTHORING] M01 postMissionSequence mismatch."); ok = false; }
            }

            return ok;
        }

        private static void SetPrivate(object obj, string fieldName, object value)
        {
            var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field != null) field.SetValue(obj, value);
        }
    }
}
#endif
