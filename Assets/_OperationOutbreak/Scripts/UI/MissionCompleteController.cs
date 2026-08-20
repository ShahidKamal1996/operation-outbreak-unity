using OperationOutbreak.Enemies;
using OperationOutbreak.Player;
using OperationOutbreak.Rewards;
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
    /// full-screen panel, a centred title, the earned Coins/Supplies (Milestone 1V) and
    /// RETRY + RETURN buttons. Retry uses the same reliable SceneManager.LoadScene call
    /// that GameOverController already uses.
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

        [Tooltip("Milestone 1V - the reward authority producing the run's MissionResultData.")]
        [SerializeField] private MissionRewardService rewardService;

        [Tooltip("Milestone 1V - the result-navigation seam for Retry / Return.")]
        [SerializeField] private MissionResultNavigation resultNavigation;

        private GameObject _panel;
        private TMP_Text _coinsText;
        private TMP_Text _suppliesText;
        private bool _shown;
        private bool _playerDied;

        /// <summary>True once the victory state has been entered during this scene run.</summary>
        public bool IsVictory => _shown;

        /// <summary>
        /// Milestone 1O - raised once, the moment the victory screen is shown. Diagnostics
        /// uses it as the end-of-run checkpoint for a successful mission. Notification
        /// only: it is raised after the victory state is fully entered.
        /// </summary>
        public event System.Action VictoryShown;

        private void Awake()
        {
            if (enemySpawner == null) enemySpawner = FindAnyObjectByType<EnemySpawner>();
            if (playerHealth == null) playerHealth = FindAnyObjectByType<PlayerHealth>();
            if (playerController == null) playerController = FindAnyObjectByType<PlayerController>();
            if (weapon == null) weapon = FindAnyObjectByType<WeaponController>();
            if (rewardService == null) rewardService = FindAnyObjectByType<MissionRewardService>();
            if (resultNavigation == null) resultNavigation = FindAnyObjectByType<MissionResultNavigation>();

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

            if (rewardService != null) rewardService.ResultCreated += HandleResultCreated;
        }

        private void OnDisable()
        {
            if (enemySpawner != null) enemySpawner.EncounterCompleted -= HandleEncounterCompleted;
            if (playerHealth != null) playerHealth.Died -= HandlePlayerDied;
            if (rewardService != null) rewardService.ResultCreated -= HandleResultCreated;
        }

        /// <summary>
        /// Milestone 1V - the reward authority produces the run result (event-driven,
        /// raised inside the same EncounterCompleted dispatch this overlay listens to);
        /// this handler only fills in the displayed reward numbers.
        /// </summary>
        private void HandleResultCreated(MissionResultData result)
        {
            if (result == null)
            {
                return;
            }

            if (_coinsText != null)
            {
                _coinsText.text = "COINS  +" + result.CoinsEarned;
            }

            if (_suppliesText != null)
            {
                _suppliesText.text = "SUPPLIES  +" + result.SuppliesEarned;
            }
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

            // Milestone 1O - end-of-run checkpoint for the diagnostics report.
            VictoryShown?.Invoke();
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

            Text("MISSION COMPLETE", _panel.transform, new Vector2(0, 150), 64);

            // Milestone 1V - reward summary lines (filled when the result is created).
            _coinsText = Text("CoinsLine", _panel.transform, new Vector2(0, 60), 40);
            _suppliesText = Text("SuppliesLine", _panel.transform, new Vector2(0, 10), 40);

            // Milestone 1V - Retry (functional) + Return (navigation intent seam).
            Button retry = Button("RetryButton", _panel.transform, new Vector2(-180, -110), new Vector2(320, 110));
            retry.onClick.AddListener(RequestRetry);
            Text("RETRY", retry.transform, Vector2.zero, 38);

            Button returnButton = Button("ReturnButton", _panel.transform, new Vector2(180, -110), new Vector2(320, 110));
            returnButton.onClick.AddListener(RequestReturn);
            Text("RETURN", returnButton.transform, Vector2.zero, 38);

            // Hidden during normal gameplay.
            _panel.SetActive(false);
        }

        private void RequestRetry()
        {
            // Milestone 1V - route through the navigation seam; it reloads the scene
            // (the existing authoritative reset), giving the NEW run a fresh grant latch.
            if (resultNavigation != null)
            {
                resultNavigation.RequestRetry();
            }
            else
            {
                Restart();
            }
        }

        private void RequestReturn()
        {
            if (resultNavigation != null)
            {
                resultNavigation.RequestReturn();
            }
            else
            {
                Debug.Log("[1V] Return requested - no Base/Map scene exists yet (2C+).", this);
            }
        }

        private static Button Button(string objectName, Transform parent, Vector2 pos, Vector2 size)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var r = (RectTransform)go.transform;
            r.anchorMin = r.anchorMax = new Vector2(.5f, .5f);
            r.sizeDelta = size;
            r.anchoredPosition = pos;
            go.GetComponent<Image>().color = new Color(.2f, .75f, .32f, 1f);
            return go.GetComponent<Button>();
        }

        private static TMP_Text Text(string value, Transform parent, Vector2 pos, float size)
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
            return t;
        }

        /// <summary>Same reliable restart used by GameOverController.</summary>
        private void Restart()
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
    }
}
