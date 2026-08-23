using System;
using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — the kind of story sequence. Determines default lock/skip/UI behaviour.
    /// </summary>
    public enum StorySequenceType
    {
        PreMission = 0,
        PostMission = 1,
        InGame = 2,
        Radio = 3
    }

    /// <summary>The kind of beat in a story sequence.</summary>
    public enum StoryBeatType
    {
        Dialogue = 0,
        Wait = 1,
        GameplayLock = 2,
        GameplayUnlock = 3,
        CameraCue = 4,
        EventCue = 5
    }

    /// <summary>Whether a cue beat fires even when the sequence is skipped.</summary>
    public enum StoryCuePolicy
    {
        Cosmetic = 0,
        RequiredOnSkip = 1
    }

    /// <summary>
    /// Milestone 1Z — one spoken line. PURE DATA ([Serializable]). If no AudioClip is assigned
    /// the line still works via subtitles + an authored/default duration.
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
    /// Milestone 1Z — one beat in a story sequence. PURE DATA ([Serializable]).
    /// </summary>
    [Serializable]
    public sealed class StoryBeatDefinition
    {
        [Tooltip("Beat type.")]
        public StoryBeatType beatType = StoryBeatType.Dialogue;

        [Tooltip("The dialogue line (speaker + subtitle + optional voice).")]
        public StoryDialogueLine dialogue;

        [Tooltip("Auto-advance to the next beat after the dialogue duration.")]
        public bool autoAdvance = true;

        [Tooltip("Duration in seconds for Wait beats, or wait-after for Dialogue beats.")]
        [Min(0f)] public float duration = 2f;

        [Tooltip("Cue id published for camera or world-event systems to consume.")]
        public string cueId = string.Empty;

        [Tooltip("Whether this cue fires even when the sequence is skipped.")]
        public StoryCuePolicy cuePolicy = StoryCuePolicy.Cosmetic;
    }
}
