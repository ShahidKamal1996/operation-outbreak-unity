using System;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z — typed cue events published by StorySequenceRunner so future cinematic
    /// content (camera cuts, world events, helicopter arrivals, explosions, NPC spawns) can
    /// subscribe without coupling to the runner. The runner publishes; consumers subscribe.
    /// No content is implemented here — these are the seams Chapter 1 cinematic work will use.
    /// </summary>
    public static class StoryCueEvents
    {
        /// <summary>Raised on a CameraCue beat. Carries the cue id (e.g. "closeup_kane").</summary>
        public static event Action<string> CameraCue;

        /// <summary>Raised on an EventCue beat. Carries the cue id (e.g. "helicopter_arrival").</summary>
        public static event Action<string> EventCue;

        public static void RaiseCameraCue(string cueId)
        {
            if (!string.IsNullOrEmpty(cueId)) CameraCue?.Invoke(cueId);
        }

        public static void RaiseEventCue(string cueId)
        {
            if (!string.IsNullOrEmpty(cueId)) EventCue?.Invoke(cueId);
        }

        /// <summary>Clears all subscribers (scene reload safety).</summary>
        public static void ClearSubscribers()
        {
            CameraCue = null;
            EventCue = null;
        }
    }
}
