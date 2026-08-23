using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — reusable voice/radio playback. One controlled AudioSource. If no clip is
    /// assigned the line still works (subtitles + duration). Clean stop on skip/end. No overlapping
    /// lines. Volume configurable. Audio absent = no errors.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class DialogueAudioController : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] private float volume = 1f;

        private AudioSource _source;

        /// <summary>The controlled dialogue AudioSource.</summary>
        public AudioSource Source => _source;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.volume = volume;
        }

        /// <summary>Plays a voice clip for the current line. Null = silence (subtitle-only).</summary>
        public void PlayLine(AudioClip clip)
        {
            if (_source == null) return;

            // No overlapping: stop any in-progress clip first.
            if (_source.isPlaying) _source.Stop();

            if (clip != null)
            {
                _source.clip = clip;
                _source.volume = volume;
                _source.Play();
            }
        }

        /// <summary>Stops any playing voice clip (skip/end).</summary>
        public void Stop()
        {
            if (_source != null && _source.isPlaying)
            {
                _source.Stop();
            }
        }
    }
}
