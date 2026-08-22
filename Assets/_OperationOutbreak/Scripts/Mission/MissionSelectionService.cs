using System;
using System.Collections.Generic;

namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X - the CODE/DATA FOUNDATION for selecting a mission to play.
    ///
    /// It sits on top of MissionProgressionService and adds the single piece of ephemeral
    /// state selection needs: WHICH mission is currently selected. Everything else (the
    /// mission list, locked/unlocked, completed) is delegated to the progression service, so
    /// there is exactly ONE authority for each fact and no parallel state.
    ///
    /// Public API (the milestone's required surface):
    ///   * Missions            - the ordered Chapter 1 mission list
    ///   * IsUnlocked/IsCompleted - per-mission state
    ///   * SelectedMission     - the currently selected mission (or null)
    ///   * CanSelect/Select    - select an UNLOCKED mission only
    ///   * CanStartSelected/StartSelected - begin a run of the selected mission
    ///
    /// StartSelected does NOT itself load the gameplay scene (that would couple this pure
    /// service to a scene name/build settings). It makes the selected mission authoritative
    /// via ActiveMissionContext and raises MissionStarting; the caller (the debug UI today, a
    /// future Base/Map screen later) performs the scene transition. This keeps the selection
    /// contract testable and stable as the UI layer evolves.
    /// </summary>
    public sealed class MissionSelectionService
    {
        private readonly MissionProgressionService _progression;
        private MissionDefinition _selected;

        /// <summary>The progression authority this selection layer reads from.</summary>
        public MissionProgressionService Progression => _progression;

        /// <summary>The ordered Chapter 1 missions available to select.</summary>
        public IReadOnlyList<MissionDefinition> Missions => _progression.Missions;

        /// <summary>The currently selected mission, or null when nothing is selected.</summary>
        public MissionDefinition SelectedMission => _selected;

        /// <summary>True when a mission is currently selected.</summary>
        public bool HasSelection => _selected != null;

        /// <summary>Raised when Select changes the selected mission (carries the new selection).</summary>
        public event Action<MissionDefinition> SelectionChanged;

        /// <summary>
        /// Raised by StartSelected once the selected mission has been made authoritative
        /// (ActiveMissionContext set). Carries the mission the run is about to play. The caller
        /// listens to perform the scene transition.
        /// </summary>
        public event Action<MissionDefinition> MissionStarting;

        public MissionSelectionService(MissionProgressionService progression)
        {
            _progression = progression ?? throw new ArgumentNullException(nameof(progression));
        }

        // ------------------------------------------------------------------ state queries

        public bool IsUnlocked(MissionDefinition mission)
        {
            return _progression.IsUnlocked(mission);
        }

        public bool IsCompleted(MissionDefinition mission)
        {
            return _progression.IsCompleted(mission);
        }

        // ------------------------------------------------------------------ selection

        /// <summary>
        /// True when <paramref name="mission"/> may be selected: it must be part of the chapter
        /// AND unlocked. Locked missions can never be selected through the normal API.
        /// </summary>
        public bool CanSelect(MissionDefinition mission)
        {
            return mission != null && _progression.IsUnlocked(mission);
        }

        /// <summary>
        /// Selects <paramref name="mission"/> when it is unlocked; otherwise the selection is
        /// left unchanged and the method returns false. A null argument clears the selection.
        /// </summary>
        public bool Select(MissionDefinition mission)
        {
            if (mission == null)
            {
                bool changed = _selected != null;
                _selected = null;
                if (changed)
                {
                    SelectionChanged?.Invoke(null);
                }

                return true;
            }

            if (!_progression.IsUnlocked(mission))
            {
                return false;
            }

            if (ReferenceEquals(_selected, mission))
            {
                return true;
            }

            _selected = mission;
            SelectionChanged?.Invoke(mission);
            return true;
        }

        /// <summary>Clears the current selection (no mission selected).</summary>
        public void ClearSelection()
        {
            Select(null);
        }

        // ------------------------------------------------------------------ starting

        /// <summary>
        /// True when StartSelected can begin a run right now: a mission is selected AND it is
        /// still unlocked (defensive - a selected mission cannot normally become locked, but
        /// the check keeps the contract explicit).
        /// </summary>
        public bool CanStartSelected
        {
            get
            {
                if (_selected == null)
                {
                    return false;
                }

                return _progression.IsUnlocked(_selected);
            }
        }

        /// <summary>
        /// Makes the selected mission authoritative for the next gameplay run and raises
        /// MissionStarting. Returns false (and changes nothing) when no unlocked mission is
        /// selected - locked missions cannot be started through the normal API. The caller is
        /// responsible for the scene transition after MissionStarting fires.
        /// </summary>
        public bool StartSelected()
        {
            if (!CanStartSelected)
            {
                return false;
            }

            ActiveMissionContext.SetForRun(_selected);
            MissionStarting?.Invoke(_selected);
            return true;
        }
    }
}
