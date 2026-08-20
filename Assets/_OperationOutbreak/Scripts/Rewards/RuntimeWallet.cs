using System;

namespace OperationOutbreak.Rewards
{
    /// <summary>
    /// Milestone 1V - the smallest reusable RUNTIME currency foundation needed for the
    /// rewards flow. Plain runtime state (NOT serialized, NOT permanent): it holds the
    /// session's Coins and Supplies balances with non-negative, overflow-safe arithmetic.
    ///
    /// Persistence boundary (documented): this wallet is deliberately session/runtime
    /// only. Permanent save/load, first-completion flags and progression belong to
    /// Milestone 2C - the future SaveService will consume the RewardService's grant
    /// output (and this wallet's balances) cleanly rather than this class growing a
    /// save system.
    ///
    /// Deliberately NOT implemented here: shops, spending, Armory purchases, Base
    /// upgrades, cloud/backend economy, IAP, advertisements.
    /// </summary>
    public sealed class RuntimeWallet
    {
        /// <summary>Coins held this session. Non-negative; saturates at long.MaxValue.</summary>
        public long Coins { get; private set; }

        /// <summary>Supplies held this session. Non-negative; saturates at long.MaxValue.</summary>
        public long Supplies { get; private set; }

        /// <summary>Raised after a successful grant, carrying the final balances.</summary>
        public event Action<long, long> BalancesChanged;

        public RuntimeWallet()
        {
            Coins = 0;
            Supplies = 0;
        }

        /// <summary>
        /// Grants the given amounts. Negative amounts are REJECTED (return false, no
        /// change). Positive amounts saturate at long.MaxValue so an overflow can never
        /// wrap into a negative or incorrect balance. Returns true when the grant was
        /// applied (even a zero grant is a valid applied grant).
        /// </summary>
        public bool Grant(long coins, long supplies)
        {
            if (coins < 0 || supplies < 0)
            {
                return false;
            }

            Coins = SaturatingAdd(Coins, coins);
            Supplies = SaturatingAdd(Supplies, supplies);

            BalancesChanged?.Invoke(Coins, Supplies);
            return true;
        }

        /// <summary>Addition that clamps at long.MaxValue instead of overflowing.</summary>
        private static long SaturatingAdd(long current, long amount)
        {
            if (amount <= 0)
            {
                return current;
            }

            if (current > long.MaxValue - amount)
            {
                return long.MaxValue;
            }

            return current + amount;
        }
    }
}
