using System;
using System.Collections.Generic;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — the kind of story sequence. Determines default lock/skip/UI behaviour.
    /// </summary>
    public enum StorySequenceType
    {
        /// <summary>Plays before a mission starts. May lock gameplay. Typically skippable.</summary>
        PreMission = 0,
        /// <summary>Plays after mission success but before result UI. May lock gameplay.</summary>
        PostMission = 1,
        /// <summary>A mid-mission scripted beat (gameplay may or may not lock).</summary>
        InGame = 2,
        /// <summary>Radio chatter during gameplay. Does NOT lock gameplay by default.</summary>
        Radio = 3
    }

    /// <summary>The kind of beat in a story sequence.</summary>
    public enum StoryBeatType
    {
        /// <summary>A spoken dialogue line (subtitle + optional voice clip).</summary>
        Dialogue = 0,
        /// <summary>Pause the sequence for a configured duration.</summary>
        Wait = 1,
        /// <summary>Suspend gameplay (movement + firing) via the gameplay-lock authority.</summary>
        GameplayLock = 2,
        /// <summary>Release a previously acquired gameplay lock.</summary>
        GameplayUnlock = 3,
        /// <summary>Publish a camera cue event for future Cinemachine/Timeline integration.</summary>
        CameraCue = 4,
        /// <summary>Publish a world/story event cue (explosions, helicopter, NPC, etc.).</summary>
        EventCue = 5
    }

    /// <summary>Whether a cue beat fires even when the sequence is skipped.</summary>
    public enum StoryCuePolicy
    {
        /// <summary>Cosmetic — skipped if the player skips the sequence. Default.</summary>
        Cosmetic = 0,
        /// <summary>Required setup — fires once even on skip, before sequence completion.</summary>
        RequiredOnSkip = 1
    }

    /// <summary>
    /// Milestone 1Z — one spoken line. PURE DATA. If no AudioClip is assigned the line still
    /// works via subtitles + an authored/default duration. Future voice production replaces
    /// clips without changing this model or mission logic.
    /// </summary>
    [Serializable]
    public sealed class StoryDialogueLine
    {
        [Tooltip("Stable line id, unique within the sequence. May be empty for inline beats.")]
        public string lineId = string.Empty;

        [Tooltip("Speaker id (must match a StorySpeakerDefinition). Empty = narrator/system.")]
        public string speakerId = string.Empty;

        [Tooltip("Subtitle text shown to the player. Required.")]
        [TextArea(2, 4)] public string text = string.Empty;

        [Tooltip("Optional voice clip. If null the line uses subtitle + duration only.")]
        public AudioClip voiceClip;

        [Tooltip("Override the line duration (seconds). 0 = voiceClip length, or a default if no clip.")]
        [Min(0f)] public float durationOverride = 0f;

        [Tooltip("If true, present as radio chatter (no portrait, radio styling).")]
        public bool isRadio = false;
    }

    /// <summary>
    /// Milestone 1Z — one beat in a story sequence. PURE DATA. Tagged-data model (no class
    /// hierarchy). The runner interprets the beat type.
    /// </summary>
    [Serializable]
    public sealed class StoryBeatDefinition
    {
        [Tooltip("Beat type.")]
        public StoryBeatType beatType = StoryBeatType.Dialogue;

        // ---- Dialogue fields (beatType == Dialogue) ----
        [Tooltip("The dialogue line (speaker + subtitle + optional voice).")]
        public StoryDialogueLine dialogue;

        [Tooltip("Auto-advance to the next beat after the dialogue duration. If false the player must advance (future tap/space).")]
        public bool autoAdvance = true;

        // ---- Wait / duration fields ----
        [Tooltip("Duration in seconds for Wait beats, or wait-after for Dialogue beats with autoAdvance.")]
        [Min(0f)] public float duration = 2f;

        // ---- Cue fields (CameraCue / EventCue) ----
        [Tooltip("Cue id published for camera or world-event systems to consume (e.g. 'helicopter_arrival').")]
        public string cueId = string.Empty;

        [Tooltip("Whether this cue fires even when the sequence is skipped.")]
        public StoryCuePolicy cuePolicy = StoryCuePolicy.Cosmetic;
    }

    /// <summary>
    /// Milestone 1Z — a reusable speaker/character definition. PURE DATA. No final art or
    /// AI-voice provider coupling; portrait/voice-profile are optional seams.
    /// </summary>
    [CreateAssetMenu(fileName = "Speaker_New", menuName = "Operation Outbreak/Story Speaker")]
    public sealed class StorySpeakerDefinition : ScriptableObject
    {
        [Tooltip("Stable speaker id (e.g. 'adrian_kane').")]
        [SerializeField] private string speakerId = string.Empty;

        [Tooltip("Full display name for menus/debug.")]
        [SerializeField] private string displayName = string.Empty;

        [Tooltip("Short name shown on subtitles (e.g. 'KANE').")]
        [SerializeField] private string subtitleName = string.Empty;

        [Tooltip("Optional portrait/sprite seam for future subtitle UI. May be null.")]
        [SerializeField] private Sprite portrait;

        [Tooltip("If true this speaker is typically heard over radio (affects future UI styling).")]
        [SerializeField] private bool isRadioSpeaker = false;

        [Tooltip("Optional metadata string for future voice-profile/category (not an AI provider id).")]
        [SerializeField] private string voiceProfileHint = string.Empty;

        public string SpeakerId => speakerId;
        public string DisplayName => displayName;
        public string SubtitleName => string.IsNullOrEmpty(subtitleName) ? displayName : subtitleName;
        public Sprite Portrait => portrait;
        public bool IsRadioSpeaker => isRadioSpeaker;
        public string VoiceProfileHint => voiceProfileHint;
    }

    /// <summary>
    /// Milestone 1Z — one ordered story sequence (pre/post-mission cinematic, in-game beat,
    /// or radio chatter). PURE DATA: identity, type, ordered beats, policy flags, optional
    /// next-sequence and mission association. The StorySequenceRunner interprets it.
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

        [Tooltip("If true the sequence auto-starts when loaded (vs. manual start from code).")]
        [SerializeField] private bool autoStart = true;

        [Header("Flow")]
        [Tooltip("Optional next sequence to chain to after this one completes. May be null.")]
        [SerializeField] private StorySequenceDefinition nextSequence;

        [Tooltip("Optional mission this sequence belongs to (for lookup/debug). May be null.")]
        [SerializeField] private OperationOutbreak.Mission.MissionDefinition associatedMission;

        // ---- read-only views ----

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

        // ---- validation ----

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
