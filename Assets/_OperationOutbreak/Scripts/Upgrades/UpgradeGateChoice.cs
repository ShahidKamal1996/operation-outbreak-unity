using OperationOutbreak.Weapons;
using TMPro;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1J.3 - turns the two upgrade gates into one mutually exclusive choice.
    ///
    /// This coordinator lives on the UpgradeGatePair root and is the ONLY place that
    /// decides anything. The gates themselves still just report that the Player passed
    /// through them (1J.2B); they do not know about weapons, upgrades or each other.
    ///
    /// Upgrades are applied exclusively through the existing WeaponController hooks
    /// (ApplyFireRateMultiplier / ApplyDamageBonus), so no weapon maths is duplicated here.
    ///
    /// RESET: every field below is ordinary instance state on a scene object, and the
    /// Restart button calls SceneManager.LoadScene. Reloading the scene destroys this
    /// component and builds a fresh one with _choiceMade == false, so the next run can
    /// pick either gate again. Nothing is static and nothing is written to an asset,
    /// which is exactly why the choice cannot leak across runs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradeGateChoice : MonoBehaviour
    {
        [Header("Gates")]
        [Tooltip("Trigger for the left gate (FIRE RATE +25%).")]
        [SerializeField] private UpgradeGateTrigger leftTrigger;

        [Tooltip("Trigger for the right gate (DAMAGE +1).")]
        [SerializeField] private UpgradeGateTrigger rightTrigger;

        [Tooltip("Root of the left gate. Used only to find its renderers/label when locking.")]
        [SerializeField] private Transform leftGateRoot;

        [Tooltip("Root of the right gate. Used only to find its renderers/label when locking.")]
        [SerializeField] private Transform rightGateRoot;

        [Header("Weapon")]
        [Tooltip("Player weapon that receives the upgrade. Resolved at Awake when empty.")]
        [SerializeField] private WeaponController weapon;

        [Header("Upgrade Values")]
        [Tooltip("Left gate: fire rate multiplier. 1.25 = +25% faster firing.")]
        [Min(0.01f)] [SerializeField] private float fireRateMultiplier = 1.25f;

        [Tooltip("Right gate: flat projectile damage bonus.")]
        [Min(0)] [SerializeField] private int damageBonus = 1;

        [Header("Locked Gate Treatment")]
        [Tooltip("Visibly grey out the gate that was not chosen.")]
        [SerializeField] private bool dimLockedGate = true;

        [Tooltip("Brightness multiplier applied to the locked gate's posts and top bar.")]
        [Range(0.05f, 1f)] [SerializeField] private float lockedBrightness = 0.35f;

        [Tooltip("Opacity applied to the locked gate's label text.")]
        [Range(0.05f, 1f)] [SerializeField] private float lockedLabelOpacity = 0.3f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        private bool _choiceMade;

        /// <summary>True once either gate has been taken during this scene run.</summary>
        public bool ChoiceMade => _choiceMade;

        private void Awake()
        {
            if (weapon == null)
            {
                weapon = FindAnyObjectByType<WeaponController>();
            }
        }

        private void OnEnable()
        {
            if (leftTrigger != null)
            {
                leftTrigger.Entered += HandleGateEntered;
            }

            if (rightTrigger != null)
            {
                rightTrigger.Entered += HandleGateEntered;
            }
        }

        private void OnDisable()
        {
            if (leftTrigger != null)
            {
                leftTrigger.Entered -= HandleGateEntered;
            }

            if (rightTrigger != null)
            {
                rightTrigger.Entered -= HandleGateEntered;
            }
        }

        /// <summary>
        /// First gate to report an entry wins. The guard also covers the theoretical case of
        /// both gates reporting on the same frame - whichever is delivered first is the choice.
        /// </summary>
        private void HandleGateEntered(UpgradeGateTrigger enteredGate)
        {
            if (_choiceMade || enteredGate == null)
            {
                return;
            }

            _choiceMade = true;

            bool isLeftGate = enteredGate == leftTrigger;
            ApplyUpgrade(isLeftGate);

            UpgradeGateTrigger otherGate = isLeftGate ? rightTrigger : leftTrigger;
            Transform otherRoot = isLeftGate ? rightGateRoot : leftGateRoot;
            LockGate(otherGate, otherRoot);

            // The chosen gate stays physically open, but it must never pay out twice.
            enteredGate.LockUpgrade();
        }

        private void ApplyUpgrade(bool isLeftGate)
        {
            if (weapon == null)
            {
                Debug.LogWarning("UpgradeGateChoice: no WeaponController found, upgrade not applied.", this);
                return;
            }

            if (isLeftGate)
            {
                // Reuses the approved weapon hook - no upgrade maths is duplicated here.
                weapon.ApplyFireRateMultiplier(fireRateMultiplier);
                Debug.Log($"Upgrade selected: FIRE RATE +25% (fire rate x{fireRateMultiplier}).", this);
            }
            else
            {
                weapon.ApplyDamageBonus(damageBonus);
                Debug.Log($"Upgrade selected: DAMAGE +{damageBonus} (projectile damage +{damageBonus}).", this);
            }
        }

        private void LockGate(UpgradeGateTrigger gate, Transform gateRoot)
        {
            if (gate != null)
            {
                gate.LockUpgrade();
                Debug.Log($"Upgrade gate locked: {gate.UpgradeLabel}.", this);
            }

            if (dimLockedGate && gateRoot != null)
            {
                DimGateVisuals(gateRoot);
            }
        }

        /// <summary>
        /// Lightweight prototype "inactive" treatment: grey the gate down and fade its label.
        ///
        /// Colour is pushed through a MaterialPropertyBlock rather than renderer.material or
        /// sharedMaterial. The gate parts share prototype material assets with each other, so
        /// touching sharedMaterial would recolour the OTHER gate too and would dirty the .mat
        /// asset on disk. A property block is per-renderer, allocation free and disappears with
        /// the scene reload.
        /// </summary>
        private void DimGateVisuals(Transform gateRoot)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();

            foreach (MeshRenderer meshRenderer in gateRoot.GetComponentsInChildren<MeshRenderer>(true))
            {
                // TMP renders its text through a MeshRenderer as well. Those are handled below
                // via TMP_Text.color; pushing a colour block at them would fight TMP's own
                // material handling.
                if (meshRenderer.GetComponent<TMP_Text>() != null)
                {
                    continue;
                }

                Material source = meshRenderer.sharedMaterial;
                if (source == null)
                {
                    continue;
                }

                meshRenderer.GetPropertyBlock(block);

                if (source.HasProperty(BaseColorId))
                {
                    block.SetColor(BaseColorId, Dim(source.GetColor(BaseColorId)));
                }

                if (source.HasProperty(ColorId))
                {
                    block.SetColor(ColorId, Dim(source.GetColor(ColorId)));
                }

                meshRenderer.SetPropertyBlock(block);
                block.Clear();
            }

            // Fade the label. Only the runtime vertex colour changes - the authored font
            // sizes, offsets and text tuned in the Editor are never touched.
            foreach (TMP_Text label in gateRoot.GetComponentsInChildren<TMP_Text>(true))
            {
                Color faded = label.color;
                faded.a *= lockedLabelOpacity;
                label.color = faded;

                // Fade the dark rim by the same amount, otherwise the words turn into ghost
                // text inside a still fully opaque outline.
                Color outline = label.outlineColor;
                outline.a *= lockedLabelOpacity;
                label.outlineColor = outline;
            }
        }

        private Color Dim(Color source)
        {
            return new Color(
                source.r * lockedBrightness,
                source.g * lockedBrightness,
                source.b * lockedBrightness,
                source.a);
        }
    }
}
