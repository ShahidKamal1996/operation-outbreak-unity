using NUnit.Framework;
using OperationOutbreak.Player;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1O.5 - EditMode tests for the deterministic decision logic behind the
    /// Carl animation bridge.
    ///
    /// Only the pure static helpers are covered. They need no scene, no Animator, no
    /// Avatar and no Play Mode, which is exactly why the decisions were factored out of
    /// the MonoBehaviour. Whether the mesh visually looks right is a Play Mode / human
    /// judgement and is intentionally NOT asserted here.
    /// </summary>
    public sealed class PlayerAnimationBridgeTests
    {
        private const float IdleThreshold = 0.15f;
        private const float ReferenceSpeed = 6f;

        // ------------------------------------------------------------- standing still

        [Test]
        public void AStationaryPlayerIsNotConsideredMoving()
        {
            Assert.IsFalse(
                PlayerAnimationBridge.IsConsideredMoving(0f, IdleThreshold),
                "A player at exactly zero speed must not be treated as moving, otherwise Walking loops on the spot.");
        }

        [Test]
        public void SpeedExactlyAtTheIdleThresholdIsNotConsideredMoving()
        {
            Assert.IsFalse(
                PlayerAnimationBridge.IsConsideredMoving(IdleThreshold, IdleThreshold),
                "The threshold itself must count as standing still so residual drift cannot start a walk cycle.");
        }

        [Test]
        public void ResidualDriftBelowTheThresholdIsNotConsideredMoving()
        {
            Assert.IsFalse(
                PlayerAnimationBridge.IsConsideredMoving(0.05f, IdleThreshold),
                "Smoothed velocity decays asymptotically, so tiny leftover speed must still read as stopped.");
        }

        [Test]
        public void SpeedAboveTheThresholdIsConsideredMoving()
        {
            Assert.IsTrue(
                PlayerAnimationBridge.IsConsideredMoving(0.9f, IdleThreshold),
                "Deliberate movement must drive the locomotion blend.");
        }

        // ---------------------------------------------------------- blend normalisation

        [Test]
        public void AStationaryPlayerNormalisesToExactlyZero()
        {
            Assert.AreEqual(
                0f,
                PlayerAnimationBridge.NormaliseSpeed(0f, IdleThreshold, ReferenceSpeed),
                "Standing still must collapse to a neutral pose, not a partial walk.");
        }

        [Test]
        public void DriftBelowTheThresholdNormalisesToExactlyZero()
        {
            Assert.AreEqual(
                0f,
                PlayerAnimationBridge.NormaliseSpeed(0.1f, IdleThreshold, ReferenceSpeed),
                "Sub-threshold speed must not creep the blend tree off the neutral pose.");
        }

        [Test]
        public void FullSpeedNormalisesToOne()
        {
            Assert.AreEqual(
                1f,
                PlayerAnimationBridge.NormaliseSpeed(ReferenceSpeed, IdleThreshold, ReferenceSpeed),
                0.0001f,
                "Moving at the reference speed must select the fastest locomotion clip.");
        }

        [Test]
        public void SpeedAboveTheReferenceIsClampedToOne()
        {
            Assert.AreEqual(
                1f,
                PlayerAnimationBridge.NormaliseSpeed(ReferenceSpeed * 3f, IdleThreshold, ReferenceSpeed),
                0.0001f,
                "A move-speed upgrade must not push the blend parameter outside the authored 0..1 range.");
        }

        [Test]
        public void MidSpeedNormalisesProportionally()
        {
            Assert.AreEqual(
                0.5f,
                PlayerAnimationBridge.NormaliseSpeed(3f, IdleThreshold, ReferenceSpeed),
                0.0001f,
                "Half the reference speed should sit halfway along the Walking / Slow Run blend.");
        }

        [Test]
        public void NormalisationSurvivesAZeroReferenceSpeed()
        {
            Assert.AreEqual(
                1f,
                PlayerAnimationBridge.NormaliseSpeed(5f, IdleThreshold, 0f),
                0.0001f,
                "A misconfigured reference speed must clamp rather than divide by zero.");
        }

        // -------------------------------------------------------------- trigger cooldown

        [Test]
        public void TheFirstTriggerIsAlwaysAllowed()
        {
            Assert.IsTrue(
                PlayerAnimationBridge.HasCooldownElapsed(0f, float.NegativeInfinity, 0.18f),
                "The very first shot of a run must play Gunplay.");
        }

        [Test]
        public void ASecondTriggerInsideTheCooldownIsSuppressed()
        {
            Assert.IsFalse(
                PlayerAnimationBridge.HasCooldownElapsed(10.05f, 10f, 0.18f),
                "Auto-fire at 5 shots/second must not re-trigger Gunplay faster than the animation can settle.");
        }

        [Test]
        public void ATriggerAfterTheCooldownIsAllowed()
        {
            Assert.IsTrue(
                PlayerAnimationBridge.HasCooldownElapsed(10.2f, 10f, 0.18f),
                "Once the guard window has passed the next shot must be able to re-trigger Gunplay.");
        }

        [Test]
        public void ATriggerExactlyOnTheCooldownBoundaryIsAllowed()
        {
            Assert.IsTrue(
                PlayerAnimationBridge.HasCooldownElapsed(10.18f, 10f, 0.18f),
                "The boundary is inclusive so a steady fire rate does not drop every other animation.");
        }

        [Test]
        public void AZeroCooldownNeverSuppressesATrigger()
        {
            Assert.IsTrue(
                PlayerAnimationBridge.HasCooldownElapsed(10f, 10f, 0f),
                "Tuning the guard to zero must disable it rather than block every trigger.");
        }
    }
}
