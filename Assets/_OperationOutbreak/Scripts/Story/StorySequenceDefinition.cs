using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — one ordered story sequence (pre/post-mission cinematic, in-game beat,
    /// or radio chatter). PURE DATA ScriptableObject. MUST live in its OWN .cs file so Unity's
    /// MonoImporter resolves fileID 11500000 to THIS type (multiple ScriptableObjects per file
    /// cause the importer to assign 11500000 to the wrong type → assets load as null).
    /// </summary>
    [CreateAssetMenu(fileName = "StorySequence_New", menuName = "Operation Outbreak/Story Sequence")]
    public sealed class StorySequenceDefinition : ScriptableObject
    {
        [Header("Identity")]
        [Tooltip("Stable sequence id (e.g. 'mission_01_pre').")]
        [SerializeField] private string sequenceId = string.Empty;

        [Tooltip("Human-readable name for menus/debug.")]
        [SerializeField] private string displayName = string.Empty;

        [Tooltip("Sequence type — determines default lock/skip behaviour.")]
        [SerializeField] private StorySequenceType sequenceType = StorySequenceType.PreMission;

        [Header("Beats")]
        [Tooltip("Ordered beats. The runner executes them in order.")]
        [SerializeField] private List<StoryBeatDefinition> beats = new List<StoryBeatDefinition>();

        [Header("Policy")]
        [Tooltip("If true the sequence can be skipped by the player.")]
        [SerializeField] private bool skippable = true;

        [Tooltip("If true the sequence auto-starts when loaded.")]
        [SerializeField] private bool autoStart = true;

        [Header("Flow")]
        [Tooltip("Optional next sequence to chain to after this one completes.")]
        [SerializeField] private StorySequenceDefinition nextSequence;

        [Tooltip("Optional mission this sequence belongs to.")]
        [SerializeField] private OperationOutbreak.Mission.MissionDefinition associatedMission;

        public string SequenceId => sequenceId;
        public string DisplayName => displayName;
        public StorySequenceType SequenceType => sequenceType;
        public IReadOnlyList<StoryBeatDefinition> Beats => beats;
        public int BeatCount => beats != null ? beats.Count : 0;
        public bool Skippable => skippable;
        public bool AutoStart => autoStart;
        public StorySequenceDefinition NextSequence => nextSequence;
        public OperationOutbreak.Mission.MissionDefinition AssociatedMission => associatedMission;

        public StoryBeatDefinition GetBeat(int index)
        {
            if (beats == null || index < 0 || index >= beats.Count) return null;
            return beats[index];
        }

        public static List<string> CollectProblems(StorySequenceDefinition seq)
        {
            List<string> problems = new List<string>();
            if (seq == null) { problems.Add("Sequence is null."); return problems; }

            string label = !string.IsNullOrEmpty(seq.displayName)
                ? seq.displayName
                : (!string.IsNullOrEmpty(seq.sequenceId) ? seq.sequenceId : seq.name);

            if (string.IsNullOrEmpty(seq.sequenceId))
                problems.Add(label + ": missing stable sequence id.");

            if (seq.beats == null || seq.beats.Count == 0)
            { problems.Add(label + ": sequence has no beats."); return problems; }

            HashSet<string> seenLineIds = new HashSet<string>();
            int lockDepth = 0;

            for (int i = 0; i < seq.beats.Count; i++)
            {
                StoryBeatDefinition beat = seq.beats[i];
                string where = label + " / beat " + (i + 1);

                if (beat == null) { problems.Add(where + ": beat is null."); continue; }

                switch (beat.beatType)
                {
                    case StoryBeatType.Dialogue:
                        if (beat.dialogue == null)
                        { problems.Add(where + ": dialogue beat has no line data."); break; }
                        if (string.IsNullOrEmpty(beat.dialogue.text))
                            problems.Add(where + ": dialogue text is empty.");
                        if (string.IsNullOrEmpty(beat.dialogue.speakerId))
                            problems.Add(where + ": dialogue has no speaker id (use 'narrator' for system).");
                        if (!string.IsNullOrEmpty(beat.dialogue.lineId) && !seenLineIds.Add(beat.dialogue.lineId))
                            problems.Add(where + ": duplicate line id '" + beat.dialogue.lineId + "'.");
                        break;
                    case StoryBeatType.Wait:
                        if (beat.duration <= 0f)
                            problems.Add(where + ": Wait duration must be > 0 (got " + beat.duration + ").");
                        break;
                    case StoryBeatType.GameplayLock:
                        lockDepth++;
                        break;
                    case StoryBeatType.GameplayUnlock:
                        lockDepth--;
                        if (lockDepth < 0)
                            problems.Add(where + ": GameplayUnlock without matching GameplayLock.");
                        break;
                    case StoryBeatType.CameraCue:
                    case StoryBeatType.EventCue:
                        if (string.IsNullOrEmpty(beat.cueId))
                            problems.Add(where + ": cue beat has no cueId.");
                        break;
                }
            }

            if (lockDepth != 0)
                problems.Add(label + ": gameplay lock/unlock imbalance (depth " + lockDepth + " at end).");

            return problems;
        }
    }
}
