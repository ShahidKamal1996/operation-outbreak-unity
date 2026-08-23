namespace OperationOutbreak.Mission
{
    /// <summary>
    /// Milestone 1X.5 - a tiny static counter of currently-alive destroyable objective targets
    /// (barricades), read by WeaponController so it can fire straight ahead at a barricade when
    /// there is no zombie to auto-aim at. Without this, a barricade with no enemy behind it could
    /// never be hit (the weapon only fires at a locked zombie target).
    ///
    /// This is deliberately a counter (not a target list): the weapon does not aim at barricades,
    /// it simply keeps firing forward so the EXISTING projectile-vs-IDamageable damage path
    /// (Projectile.SphereCast -> BarricadeTarget.TakeDamage) reaches them. The counter is gated,
    /// so Mission 1/2/3/5 (no barricades) leave the weapon's behaviour byte-identical. Barricade
    /// targets register/unregister as they spawn and die.
    /// </summary>
    public static class ObjectiveTargetAimProvider
    {
        /// <summary>Number of currently-alive destroyable objective targets in the scene.</summary>
        public static int ActiveDamageableCount { get; private set; }

        public static void RegisterDamageable()
        {
            ActiveDamageableCount++;
        }

        public static void UnregisterDamageable()
        {
            if (ActiveDamageableCount > 0)
            {
                ActiveDamageableCount--;
            }
        }
    }
}
