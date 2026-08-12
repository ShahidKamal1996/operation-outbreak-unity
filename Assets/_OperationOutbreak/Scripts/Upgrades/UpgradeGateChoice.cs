using OperationOutbreak.Player;
using OperationOutbreak.Weapons;
using TMPro;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Which runtime upgrade one gate of a pair awards. Every value maps onto an
    /// EXISTING approved runtime hook - no upgrade maths lives in this file.
    ///
    /// The numbering is deliberately append-only: Unity serializes enums by integer, so
    /// 0/1 must keep meaning FireRate/Damage or the approved Pair 1 asset would change
    /// meaning. New kinds are added at the end.
    /// </summary>
    public enum GateUpgradeKind
    {
        FireRateMultiplier = 0,
        DamageBonus = 1,
        MaxHealthBonus = 2,
        MoveSpeedMultiplier = 3
    }

    /// <summary>
    /// Milestone 1J.3 - turns the two upgrade gates into one mutually exclusive choice.
    ///
    /// This coordinator lives on the UpgradeGatePair root and is the ONLY place that
    /// decides anything. The gates themselves still just report that the Player passed
    /// through them (1J.2B); they do not know about weapons, upgrades or each other.
    ///
    /// Upgrades are applied exclusively through existing approved runtime hooks
    /// (WeaponController.ApplyFireRateMultiplier / ApplyDamageBonus, and from 1L
    /// PlayerHealth.ApplyMaxHealthBonus / PlayerController.ApplyMoveSpeedMultiplier),
    /// so no upgrade maths is duplicated here.
    ///
    /// Milestone 1L - a scene may now contain MORE THAN ONE pair. Every decision below is
    /// instance state on one pair root and each pair only ever references its own two
    /// triggers, so pairs are independent by construction: locking Pair 1 cannot touch
    /// Pair 2 and vice versa. There is deliberately no global/static "upgrade taken" flag.
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
        [Tooltip("Trigger for the LEFT gate of this pair.")]
        [SerializeField] private UpgradeGateTrigger leftTrigger;

        [Tooltip("Trigger for the RIGHT gate of this pair.")]
        [SerializeField] private UpgradeGateTrigger rightTrigger;

        [Tooltip("Root of the left gate. Used only to find its renderers/label when locking.")]
        [SerializeField] private Transform leftGateRoot;

        [Tooltip("Root of the right gate. Used only to find its renderers/label when locking.")]
        [SerializeField] private Transform rightGateRoot;

        [Header("Upgrade Kinds")]
        [Tooltip("Which upgrade the LEFT gate of this pair awards.")]
        [SerializeField] private GateUpgradeKind leftUpgrade = GateUpgradeKind.FireRateMultiplier;

        [Tooltip("Which upgrade the RIGHT gate of this pair awards.")]
        [SerializeField] private GateUpgradeKind rightUpgrade = GateUpgradeKind.DamageBonus;

        [Header("Upgrade Targets")]
        [Tooltip("Player weapon that receives weapon upgrades. Resolved at Awake when empty.")]
        [SerializeField] private WeaponController weapon;

        [Tooltip("Player health that receives max-health upgrades. Resolved at Awake when empty.")]
        [SerializeField] private PlayerHealth playerHealth;

        [Tooltip("Player movement that receives move-speed upgrades. Resolved at Awake when empty.")]
        [SerializeField] private PlayerController playerController;

        [Header("Upgrade Values")]
        [Tooltip("Fire rate multiplier. 1.25 = +25% faster firing.")]
        [Min(0.01f)] [SerializeField] private float fireRateMultiplier = 1.25f;

        [Tooltip("Flat projectile damage bonus.")]
        [Min(0)] [SerializeField] private int damageBonus = 1;

        [Tooltip("Milestone 1L - flat maximum health bonus. Current health rises by the same amount.")]
        [Min(0)] [SerializeField] private int maxHealthBonus = 2;

        [Tooltip("Milestone 1L - movement speed multiplier. 1.15 = +15% faster movement.")]
        [Min(0.01f)] [SerializeField] private float moveSpeedMultiplier = 1.15f;

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

            if (playerHealth == null)
            {
                playerHealth = FindAnyObjectByType<PlayerHealth>();
            }

            if (playerController == null)
            {
                playerController = FindAnyObjectByType<PlayerController>();
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
            ApplyUpgradeKind(isLeftGate ? leftUpgrade : rightUpgrade);
        }

        /// <summary>
        /// Every branch delegates to an existing approved runtime hook, so this milestone
        /// adds no upgrade maths of its own and cannot drift from the systems it upgrades.
        /// </summary>
        private void ApplyUpgradeKind(GateUpgradeKind kind)
        {
            switch (kind)
            {
                case GateUpgradeKind.FireRateMultiplier:
                    if (weapon == null)
                    {
                        Debug.LogWarning("UpgradeGateChoice: no WeaponController found, upgrade not applied.", this);
                        return;
                    }

                    // Reuses the approved weapon hook - no upgrade maths is duplicated here.
                    weapon.ApplyFireRateMultiplier(fireRateMultiplier);
                    Debug.Log($"Upgrade selected: FIRE RATE +25% (fire rate x{fireRateMultiplier}).", this);
                    return;

                case GateUpgradeKind.DamageBonus:
                    if (weapon == null)
                    {
                        Debug.LogWarning("UpgradeGateChoice: no WeaponController found, upgrade not applied.", this);
                        return;
                    }

                    weapon.ApplyDamageBonus(damageBonus);
                    Debug.Log($"Upgrade selected: DAMAGE +{damageBonus} (projectile damage +{damageBonus}).", this);
                    return;

                case GateUpgradeKind.MaxHealthBonus:
                    if (playerHealth == null)
                    {
                        Debug.LogWarning("UpgradeGateChoice: no PlayerHealth found, upgrade not applied.", this);
                        return;
                    }

                    playerHealth.ApplyMaxHealthBonus(maxHealthBonus);
                    Debug.Log(
                        $"Upgrade selected: MAX HEALTH +{maxHealthBonus} " +
                        $"({playerHealth.CurrentHealth} / {playerHealth.MaxHealth}).",
                        this);
                    return;

                case GateUpgradeKind.MoveSpeedMultiplier:
                    if (playerController == null)
                    {
                        Debug.LogWarning("UpgradeGateChoice: no PlayerController found, upgrade not applied.", this);
                        return;
                    }

                    playerController.ApplyMoveSpeedMultiplier(moveSpeedMultiplier);
                    Debug.Log($"Upgrade selected: MOVE SPEED +15% (move speed x{moveSpeedMultiplier}).", this);
                    return;
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
