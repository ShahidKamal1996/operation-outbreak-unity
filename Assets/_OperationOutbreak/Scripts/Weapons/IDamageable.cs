namespace OperationOutbreak.Weapons
{
    /// <summary>
    /// Minimal damage contract shared by prototype projectiles and future combat targets.
    /// It intentionally makes no assumptions about enemies, AI, teams, hit reactions or UI.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }

        void TakeDamage(int amount);
    }
}
