using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — a reusable speaker/character definition. MUST live in its OWN .cs file
    /// so Unity's MonoImporter resolves fileID 11500000 to THIS type.
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

        [Tooltip("If true this speaker is typically heard over radio.")]
        [SerializeField] private bool isRadioSpeaker = false;

        [Tooltip("Optional metadata string for future voice-profile/category.")]
        [SerializeField] private string voiceProfileHint = string.Empty;

        public string SpeakerId => speakerId;
        public string DisplayName => displayName;
        public string SubtitleName => string.IsNullOrEmpty(subtitleName) ? displayName : subtitleName;
        public Sprite Portrait => portrait;
        public bool IsRadioSpeaker => isRadioSpeaker;
        public string VoiceProfileHint => voiceProfileHint;
    }
}
