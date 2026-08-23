using UnityEngine;

namespace OperationOutbreak.Story
{
    /// <summary>
    /// Milestone 1Z.1 QA fix #7 — temporarily hides gameplay HUD elements during cinematic
    /// sequences and restores them on handoff. Lightweight: finds HUD components and toggles
    /// their Canvas/GameObject visibility. Does NOT destroy anything.
    /// </summary>
    public sealed class StoryHudVisibilityController : MonoBehaviour
    {
        private GameObject _healthHudCanvas;
        private GameObject _objectiveHudCanvas;
        private GameObject _sectionHudCanvas;
        private bool _hudHidden;

        private void Awake()
        {
            // Resolve HUD canvases lazily when first needed.
        }

        public void HideGameplayHud()
        {
            if (_hudHidden) return;
            _hudHidden = true;

            _healthHudCanvas = FindCanvas("CombatHUD");
            _objectiveHudCanvas = FindCanvas("ObjectiveHudCanvas");
            _sectionHudCanvas = FindCanvas("SectionHudCanvas");

            SetActiveSafe(_healthHudCanvas, false);
            SetActiveSafe(_objectiveHudCanvas, false);
            Debug.Log("[STORY M01] Gameplay HUD hidden for cinematic.");
        }

        public void RestoreGameplayHud()
        {
            if (!_hudHidden) return;
            _hudHidden = false;

            SetActiveSafe(_healthHudCanvas, true);
            SetActiveSafe(_objectiveHudCanvas, true);
            Debug.Log("[STORY M01] Gameplay HUD restored.");
        }

        private static GameObject FindCanvas(string name)
        {
            var t = GameObject.Find(name);
            return t;
        }

        private static void SetActiveSafe(GameObject go, bool active)
        {
            if (go != null) go.SetActive(active);
        }
    }
}
