using System;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1L-R - which approved runtime hook an upgrade drives.
    ///
    /// Serialized by integer, so the numbering is append-only: new upgrades are added at
    /// the end and existing scene data keeps its meaning.
    /// </summary>
    public enum UpgradeKind
    {
        FireRateMultiplier = 0,
        DamageBonus = 1,
        MaxHealthBonus = 2,
        MoveSpeedMultiplier = 3
    }

    /// <summary>Prototype geometry used to make each upgrade recognisable at a glance.</summary>
    public enum UpgradePickupShape
    {
        Cube = 0,
        Sphere = 1,
        Capsule = 2,
        Cylinder = 3
    }

    /// <summary>
    /// Milestone 1L-R - pure DATA describing one upgrade: what it does, what it is called
    /// and how its pickup looks. It contains no lifecycle, no timing and no placement, so
    /// adding a future upgrade means adding one entry - never touching the pickup or
    /// director code.
    /// </summary>
    [Serializable]
    public sealed class UpgradeDefinition
    {
        [Tooltip("First HUD line, e.g. FIRE RATE.")]
        public string displayName = "FIRE RATE";

        [Tooltip("Value shown after the name, e.g. +25%.")]
        public string displayValue = "+25%";

        [Tooltip("Which approved runtime hook this upgrade drives.")]
        public UpgradeKind kind = UpgradeKind.FireRateMultiplier;

        [Tooltip("Multiplier used by FireRateMultiplier / MoveSpeedMultiplier. 1.25 = +25%.")]
        [Min(0.01f)] public float multiplier = 1.25f;

        [Tooltip("Flat amount used by DamageBonus / MaxHealthBonus.")]
        [Min(0)] public int amount = 1;

        [Tooltip("Prototype shape of this upgrade's pickup.")]
        public UpgradePickupShape shape = UpgradePickupShape.Capsule;

        [Tooltip("Prototype colour of this upgrade's pickup.")]
        public Color tint = new Color(1f, 0.55f, 0.12f, 1f);

        /// <summary>Single line shown on the HUD, e.g. "FIRE RATE +25%".</summary>
        public string DisplayLine =>
            string.IsNullOrEmpty(displayValue) ? displayName : $"{displayName} {displayValue}";
    }
}
