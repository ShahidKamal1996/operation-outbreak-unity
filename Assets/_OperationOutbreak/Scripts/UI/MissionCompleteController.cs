using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace OperationOutbreak.UI
{
    /// <summary>
    /// Milestone 1K - closes the successful run loop.
    ///
    /// Listens to the ONE existing encounter-complete path on EnemySpawner
    /// (EnemySpawner.EncounterCompleted, raised where "Encounter complete" was already
    /// logged). No second wave-completion system is introduced here.
    ///
    /// The overlay is built in code with the same prototype quality and style as
    /// GameOverController: a ScreenSpaceOverlay canvas scaled to 1080x1920, a dimmed
    /// full-screen panel, a centred title and one green RESTART button. Restart uses the
    /// same reliable SceneManager.LoadScene call that GameOverController already uses.
    ///
    /// Exclusivity: EnemySpawner cancels the encounter when the Player dies, so the
    /// completion event can never fire after death. This component additionally refuses
    /// to show while PlayerHealth reports dead, and refuses to react to a death that
    /// arrives after victory. Game Over and Mission Complete can therefore never both
    /// appear in one run.
    ///
    /// Reset: every field is plain instance state on a scene object. Reloading the scene
    /// rebuilds this component with the panel hidden again. No persistent state is created.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionCompleteController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Encounter whose completion triggers victory. Resolved at Awake when empty.")]
        [SerializeField] private EnemySpawner enemySpawner;

        [Tooltip("Player health, used only to keep victory and Game Over exclusive.")]
        [SerializeField] private PlayerHealth playerHealth;

        [Tooltip("Player movement, stopped on victory. Resolved at Awake when empty.")]
        [SerializeField] private PlayerController playerController;

        [Tooltip("Player weapon, stopped on victory. Resolved at Awake when empty.")]
        [SerializeField] private WeaponController weapon;

        private GameObject _panel;
        private bool _shown;
        private bool _playerDied;

        /// <summary>True once the victory state has been entered during this scene run.</summary>
        public bool IsVictory => _shown;

        private void Awake()
        {
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
            if (weapon == null) weapon = FindAnyObjectByType<WeaponController>();

            Build();
        }

        private void OnEnable()
        {
            if (enemySpawner != null) enemySpawner.EncounterCompleted += HandleEncounterCompleted;
            if (playerHealth != null)
            {
                playerHealth.Died += HandlePlayerDied;
                _playerDied = playerHealth.IsDead;
            }
        }

        private void OnDisable()
        {
            if (enemySpawner != null) enemySpawner.EncounterCompleted -= HandleEncounterCompleted;
            if (playerHealth != null) playerHealth.Died -= HandlePlayerDied;
        }

        /// <summary>
        /// Remembers a death so a late completion event can never open this overlay.
        /// Game Over itself is owned by GameOverController and is not touched here.
        /// </summary>
        private void HandlePlayerDied()
        {
            _playerDied = true;
        }

        private void HandleEncounterCompleted()
        {
            // Victory triggers exactly once, and never after the Player has died.
            if (_shown || _playerDied || (playerHealth != null && playerHealth.IsDead))
            {
                return;
            }

            _shown = true;
            EnterVictoryState();
            _panel.SetActive(true);
            Debug.Log("Mission complete", this);
        }

        /// <summary>
        /// Stops combat without destroying the Player or the camera: the weapon stops
        /// firing, movement comes to rest, and the spawner halts further waves and
        /// suspends any zombie still alive.
        /// </summary>
        private void EnterVictoryState()
        {
            if (weapon != null) weapon.SuspendFiring();
            if (playerController != null) playerController.SuspendMovement();
            if (enemySpawner != null) enemySpawner.StopEncounter();
        }

        private void Build()
        {
            var canvas = new GameObject("MissionCompleteCanvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvas.transform.SetParent(transform, false);
            var c = canvas.GetComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 30;
            var sc = canvas.GetComponent<CanvasScaler>();
            sc.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            sc.referenceResolution = new Vector2(1080, 1920);

            // Matches GameOverController: the scene needs exactly one EventSystem for clicks.
            if (FindAnyObjectByType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                es.transform.SetParent(transform, false);
            }

            _panel = new GameObject("MissionCompletePanel", typeof(RectTransform), typeof(Image));
            _panel.transform.SetParent(canvas.transform, false);
            var r = (RectTransform)_panel.transform;
            r.anchorMin = Vector2.zero;
            r.anchorMax = Vector2.one;
            r.offsetMin = r.offsetMax = Vector2.zero;
            _panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, .72f);

            Text("MISSION COMPLETE", _panel.transform, new Vector2(0, 130), 64);

            var b = new GameObject("RestartButton", typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(_panel.transform, false);
            var br = (RectTransform)b.transform;
            br.anchorMin = br.anchorMax = new Vector2(.5f, .5f);
            br.sizeDelta = new Vector2(340, 110);
            br.anchoredPosition = new Vector2(0, -40);
            b.GetComponent<Image>().color = new Color(.2f, .75f, .32f, 1f);
            b.GetComponent<Button>().onClick.AddListener(Restart);
            Text("RESTART", b.transform, Vector2.zero, 38);

            // Hidden during normal gameplay.
            _panel.SetActive(false);
        }

        private static void Text(string value, Transform parent, Vector2 pos, float size)
        {
            var go = new GameObject(value, typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            // Wider than the Game Over title box so "MISSION COMPLETE" stays on one
            // readable line inside the 1080 wide portrait reference.
            r.sizeDelta = new Vector2(1000, 100);
            r.anchoredPosition = pos;
            var t = go.GetComponent<TextMeshProUGUI>();
            t.font = TMP_Settings.defaultFontAsset;
            t.text = value;
            t.fontSize = size;
            t.fontStyle = FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = Color.white;
        }

        /// <summary>Same reliable restart used by GameOverController.</summary>
        private void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
