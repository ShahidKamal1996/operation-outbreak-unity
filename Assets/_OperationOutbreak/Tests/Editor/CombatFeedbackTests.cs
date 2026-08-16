using System.Collections.Generic;
using NUnit.Framework;
using OperationOutbreak.Enemies;
using OperationOutbreak.Feedback;
using OperationOutbreak.Weapons;
using UnityEngine;
using UnityEngine.TestTools;

namespace OperationOutbreak.Tests
{
    /// <summary>
    /// Milestone 1P - EditMode tests for the non-visual logic behind the combat feedback
    /// layer: pool lifecycle, envelope and hit-punch curves, muzzle flash duration safety
    /// and the projectile's runtime trail + configuration clamping.
    ///
    /// Milestone 1P QA fix - the manual QA regression ("Coroutine couldn't be started
    /// because the game object is inactive") is now pinned here: activation order, safe
    /// reuse of inactive pooled visuals, exactly-once completion/return-to-pool, and
    /// error-free repeated muzzle flash / hit spark cycles. The fixture-level TearDown
    /// fails any test during which Unity logs an unexpected error, which is what catches
    /// the coroutine error even if no assertion touches it.
    ///
    /// The visual "does it look right" judgement is deliberately NOT asserted here; that
    /// belongs to the manual Unity QA pass.
    /// </summary>
    public sealed class CombatFeedbackTests
    {
        [TearDown]
        public void FailOnUnexpectedUnityErrors()
        {
            // Catches "Coroutine couldn't be started because the game object ... is
            // inactive" and any other error/exception logged during a test.
            LogAssert.NoUnexpectedReceived();
        }
        // ===================================================== FeedbackObjectPool

        [Test]
        public void PoolUsesFactoryOnlyWhileEmpty()
        {
            int creations = 0;
            var pool = new FeedbackObjectPool(
                () => { creations++; return new GameObject("Visual"); }, 4, DestroyImmediate);

            GameObject first = pool.Acquire();
            Assert.IsNotNull(first, "Factory must produce a visual on the first acquire.");
            Assert.AreEqual(1, creations, "The first acquire must build exactly one visual.");

            pool.Release(first);
            GameObject second = pool.Acquire();

            Assert.AreEqual(first, second, "The released visual must be reused, not rebuilt.");
            Assert.AreEqual(1, creations, "Acquiring from the pool must not call the factory.");
            pool.Drain();
        }

        [Test]
        public void ReleasingBeyondTheRetainedCapDiscardsInsteadOfStoring()
        {
            var discarded = new List<GameObject>();
            var pool = new FeedbackObjectPool(
                () => new GameObject("Visual"), 2, discarded.Add);

            GameObject a = pool.Acquire();
            GameObject b = pool.Acquire();
            GameObject c = pool.Acquire();

            pool.Release(a);
            pool.Release(b);
            pool.Release(c);

            Assert.AreEqual(2, pool.RetainedCount, "The pool must never grow past its cap.");
            Assert.AreEqual(1, discarded.Count, "The third release must be discarded.");
            Assert.AreEqual(c, discarded[0], "The overflow visual must be the discarded one.");

            pool.Drain();
            discarded.ForEach(DestroyImmediate);
        }

        [Test]
        public void DrainDestroysEveryRetainedVisual()
        {
            var discarded = new List<GameObject>();
            var pool = new FeedbackObjectPool(
                () => new GameObject("Visual"), 4, discarded.Add);

            // Acquire both objects BEFORE releasing either. The previous version of this
            // test interleaved Release(Acquire()) twice, so the second Acquire correctly
            // REUSED the object the first Release had just stored and the pool therefore
            // held exactly one distinct visual - the old assertion demanded two objects
            // against the pool's intended reuse behaviour.
            GameObject first = pool.Acquire();
            GameObject second = pool.Acquire();

            Assert.AreNotEqual(first, second,
                "Two acquires from an empty pool must build two distinct visuals.");

            pool.Release(first);
            pool.Release(second);

            Assert.AreEqual(2, pool.RetainedCount,
                "Two released visuals must both be retained while the pool is below its cap.");

            pool.Drain();

            Assert.AreEqual(0, pool.RetainedCount, "Drain must empty the pool.");
            Assert.AreEqual(2, discarded.Count, "Every retained visual must be discarded on drain.");
            discarded.ForEach(DestroyImmediate);
        }

        [Test]
        public void RetentionDecisionDiscardsExactlyAtTheCapBoundary()
        {
            Assert.IsTrue(
                FeedbackObjectPool.ShouldDiscardOnRelease(4, 4),
                "Releasing with the pool already full must discard.");
            Assert.IsFalse(
                FeedbackObjectPool.ShouldDiscardOnRelease(3, 4),
                "Releasing with room left must store.");
            Assert.IsTrue(
                FeedbackObjectPool.ShouldDiscardOnRelease(0, 0),
                "A zero cap must discard everything rather than grow unbounded.");
        }

        [Test]
        public void AcquireActivatesEvenAFactoryThatCreatesInactiveObjects()
        {
            var pool = new FeedbackObjectPool(
                () =>
                {
                    GameObject visual = new GameObject("Visual");
                    visual.SetActive(false);
                    return visual;
                },
                4, DestroyImmediate);

            GameObject visual = pool.Acquire();

            Assert.IsTrue(visual.activeSelf,
                "Acquire must guarantee an active object no matter what the factory does - " +
                "this was the Milestone 1P QA failure path.");
            pool.Drain();
        }

        [Test]
        public void InactivePooledObjectsAreReusedActively()
        {
            var pool = new FeedbackObjectPool(() => new GameObject("Visual"), 4, DestroyImmediate);

            GameObject visual = pool.Acquire();
            pool.Release(visual);

            Assert.IsFalse(visual.activeSelf,
                "Release must deactivate the visual while it is stored.");

            GameObject reused = pool.Acquire();

            Assert.AreEqual(visual, reused, "The stored visual must be reused, not rebuilt.");
            Assert.IsTrue(reused.activeSelf,
                "Acquire must reactivate a stored visual before handing it out.");
            pool.Drain();
        }

        [Test]
        public void LifecycleCompletionReturnsTheVisualToThePoolExactlyOnce()
        {
            var pool = new FeedbackObjectPool(
                () =>
                {
                    GameObject visual = new GameObject("Visual");
                    visual.AddComponent<FeedbackVisualLifecycle>();
                    return visual;
                },
                4, DestroyImmediate);

            GameObject visual = pool.Acquire();
            FeedbackVisualLifecycle lifecycle = visual.GetComponent<FeedbackVisualLifecycle>();

            Assert.AreEqual(0, pool.RetainedCount);

            lifecycle.Play(0.09f, 0.4f, 0.38f, pool.Release);

            Assert.AreEqual(0, pool.RetainedCount,
                "The visual must stay checked out while the envelope is playing.");

            lifecycle.CompleteNow();

            Assert.AreEqual(1, pool.RetainedCount,
                "Completion must return the visual to the pool.");
            Assert.IsFalse(visual.activeSelf,
                "A returned visual must be deactivated.");

            lifecycle.CompleteNow();

            Assert.AreEqual(1, pool.RetainedCount,
                "A second completion must never double-return the visual to the pool.");

            GameObject reused = pool.Acquire();

            Assert.AreEqual(visual, reused, "The returned visual must be reused.");
            Assert.IsTrue(reused.activeSelf, "A reused visual must come back active.");

            pool.Drain();
        }

        // ===================================================== FeedbackVisualLifecycle

        [Test]
        public void PulseEnvelopeStartsAndEndsAtExactlyOne()
        {
            Assert.AreEqual(1f, FeedbackVisualLifecycle.ComputePulseScale(0f, 0.5f), 0.0001f,
                "The envelope must start at the base scale.");
            Assert.AreEqual(1f, FeedbackVisualLifecycle.ComputePulseScale(1f, 0.5f), 0.0001f,
                "The envelope must end at the base scale so a pooled visual is reusable.");
        }

        [Test]
        public void PulseEnvelopePeaksAtTheMidpoint()
        {
            Assert.AreEqual(1.5f, FeedbackVisualLifecycle.ComputePulseScale(0.5f, 0.5f), 0.0001f,
                "The sine pulse must peak at its midpoint.");
        }

        [Test]
        public void PulseEnvelopeNeverDipsBelowTheBaseScale()
        {
            for (int i = 0; i <= 100; i++)
            {
                float progress = i / 100f;
                Assert.GreaterOrEqual(
                    FeedbackVisualLifecycle.ComputePulseScale(progress, 0.5f), 1f - 0.0001f,
                    "A feedback visual must never shrink below its base scale.");
            }
        }

        [Test]
        public void PulseEnvelopeClampsProgressOutsideTheUnitRange()
        {
            Assert.AreEqual(1f, FeedbackVisualLifecycle.ComputePulseScale(-0.5f, 0.5f), 0.0001f);
            Assert.AreEqual(1f, FeedbackVisualLifecycle.ComputePulseScale(1.5f, 0.5f), 0.0001f);
        }

        [Test]
        public void PlayActivatesAnInactiveVisualBeforeStartingItsCoroutine()
        {
            GameObject visual = new GameObject("Visual");
            FeedbackVisualLifecycle lifecycle = visual.AddComponent<FeedbackVisualLifecycle>();

            try
            {
                visual.SetActive(false);

                // Pre-fix, this threw "Coroutine couldn't be started because the game
                // object ... is inactive". Post-fix Play activates first; the TearDown
                // LogAssert would also fail the test if the error were still logged.
                lifecycle.Play(0.09f, 0.4f, 0.38f, _ => { });

                Assert.IsTrue(visual.activeSelf,
                    "A pooled visual must be active before its lifecycle coroutine starts.");
            }
            finally
            {
                Object.DestroyImmediate(visual);
            }
        }

        [Test]
        public void RepeatedMuzzleFlashPlaysNeverProduceCoroutineErrors()
        {
            GameObject visual = new GameObject("CombatMuzzleFlash");
            FeedbackVisualLifecycle lifecycle = visual.AddComponent<FeedbackVisualLifecycle>();

            try
            {
                for (int i = 0; i < 12; i++)
                {
                    // Simulate the pooled-release state each cycle: a stored muzzle flash
                    // is inactive when the next shot acquires it.
                    visual.SetActive(false);
                    visual.transform.localScale = Vector3.one * 0.38f;

                    lifecycle.Play(0.09f, 0.4f, 0.38f, _ => { });

                    Assert.IsTrue(visual.activeSelf,
                        "Every Play must leave the flash active; an inactive flash is the " +
                        "exact QA regression that broke rapid fire.");
                }
            }
            finally
            {
                Object.DestroyImmediate(visual);
            }
        }

        [Test]
        public void RepeatedHitSparkPlaysNeverProduceCoroutineErrors()
        {
            GameObject visual = new GameObject("CombatHitSpark");
            FeedbackVisualLifecycle lifecycle = visual.AddComponent<FeedbackVisualLifecycle>();

            try
            {
                for (int i = 0; i < 12; i++)
                {
                    visual.SetActive(false);
                    visual.transform.localScale = Vector3.one * 0.34f;

                    lifecycle.Play(0.18f, 0.5f, 0.34f, _ => { });

                    Assert.IsTrue(visual.activeSelf,
                        "Every Play must leave the spark active so hit feedback can never " +
                        "die on the first impact of a session.");
                }
            }
            finally
            {
                Object.DestroyImmediate(visual);
            }
        }

        // ===================================================== MuzzleFlashFeedback

        [Test]
        public void MuzzleFlashDurationIsClampedIntoTheShortReadableRange()
        {
            Assert.AreEqual(
                MuzzleFlashFeedback.MinimumFlashDuration,
                MuzzleFlashFeedback.ClampFlashDuration(0f),
                "A zero duration must clamp up to the minimum.");
            Assert.AreEqual(
                MuzzleFlashFeedback.MaximumFlashDuration,
                MuzzleFlashFeedback.ClampFlashDuration(10f),
                "An absurd duration must clamp down so rapid fire can never stack flashes.");
            Assert.AreEqual(
                0.09f,
                MuzzleFlashFeedback.ClampFlashDuration(0.09f),
                0.0001f,
                "The authored default must pass through untouched.");
        }

        // ===================================================== ZombieController hit punch

        [Test]
        public void HitPunchStartsAndEndsAtExactlyOne()
        {
            Assert.AreEqual(1f, ZombieController.ComputeHitPunchScale(0f), 0.0001f,
                "The punch must start from the authored visual scale.");
            Assert.AreEqual(1f, ZombieController.ComputeHitPunchScale(1f), 0.0001f,
                "The punch must return to the authored visual scale.");
        }

        [Test]
        public void HitPunchNeverShrinksAndNeverExaggerates()
        {
            for (int i = 0; i <= 100; i++)
            {
                float scale = ZombieController.ComputeHitPunchScale(i / 100f);
                Assert.GreaterOrEqual(scale, 1f - 0.0001f,
                    "Hit feedback must never shrink the enemy visual.");
                Assert.LessOrEqual(scale, 1.08f,
                    "Hit feedback must stay a tiny punch, not an exaggerated scale-up.");
            }
        }

        [Test]
        public void HitPunchPeaksAtTheMidpoint()
        {
            Assert.AreEqual(1.07f, ZombieController.ComputeHitPunchScale(0.5f), 0.0001f,
                "The authored punch strength is 7% at the midpoint.");
        }

        // ===================================================== Projectile presentation

        [Test]
        public void ProjectileTrailIsAddedOnceAndCarriesNoCollision()
        {
            GameObject projectileObject = new GameObject("TestProjectile");

            // Adding Projectile auto-adds its required SphereCollider (RequireComponent
            // since long before 1P). That collider is the projectile's legitimate
            // gameplay hit-detection volume - the trail must never touch it.
            Projectile projectile = projectileObject.AddComponent<Projectile>();

            // Snapshot the pre-trail gameplay collider state as the baseline.
            Collider[] collidersBefore = projectileObject.GetComponents<Collider>();
            SphereCollider sphere = projectileObject.GetComponent<SphereCollider>();
            Assert.IsNotNull(sphere,
                "Projectile must carry its required gameplay SphereCollider.");

            float radiusBefore = sphere.radius;
            Vector3 centerBefore = sphere.center;
            bool triggerBefore = sphere.isTrigger;
            bool enabledBefore = sphere.enabled;

            try
            {
                // Awake runs on AddComponent in EditMode, but call explicitly so the test
                // does not depend on that timing.
                projectile.EnsureTrailPresentation();
                projectile.EnsureTrailPresentation();

                TrailRenderer[] trails = projectileObject.GetComponents<TrailRenderer>();
                Assert.AreEqual(1, trails.Length,
                    "The trail must be added exactly once, no matter how often it is ensured.");

                TrailRenderer trail = trails[0];
                Assert.IsTrue(trail.emitting, "A live projectile's trail must be emitting.");
                Assert.AreEqual(
                    UnityEngine.Rendering.ShadowCastingMode.Off,
                    trail.shadowCastingMode,
                    "The trail must not cast shadows on mobile.");
                Assert.IsNotNull(trail.sharedMaterial,
                    "The trail must share the cached combat feedback material, not a per-shot instance.");
                Assert.LessOrEqual(trail.time, 0.2f,
                    "The trail must be short-lived so rapid fire cannot stack long trails.");

                // Correct invariant (the pre-fix test wrongly asserted that NO collider
                // could exist on the projectile object, but the required gameplay
                // SphereCollider has always lived there): trail setup must not add,
                // remove, reorder, replace, disable, resize or otherwise mutate any
                // gameplay collider.
                Collider[] collidersAfter = projectileObject.GetComponents<Collider>();
                Assert.AreEqual(collidersBefore.Length, collidersAfter.Length,
                    "Trail setup must not add or remove gameplay colliders.");

                for (int i = 0; i < collidersBefore.Length; i++)
                {
                    Assert.AreEqual(collidersBefore[i], collidersAfter[i],
                        "Trail setup must not replace or reorder gameplay colliders.");
                }

                Assert.AreEqual(radiusBefore, sphere.radius, 0.0001f,
                    "Trail setup must not resize the projectile's gameplay collider.");
                Assert.AreEqual(centerBefore, sphere.center,
                    "Trail setup must not move the projectile's gameplay collider.");
                Assert.AreEqual(triggerBefore, sphere.isTrigger,
                    "Trail setup must not change the projectile collider's trigger role.");
                Assert.AreEqual(enabledBefore, sphere.enabled,
                    "Trail setup must not enable or disable the projectile's gameplay collider.");
            }
            finally
            {
                Object.DestroyImmediate(projectileObject);
            }
        }

        [Test]
        public void ProjectileInitializationClampsInvalidConfiguration()
        {
            GameObject projectileObject = new GameObject("TestProjectile");
            Projectile projectile = projectileObject.AddComponent<Projectile>();

            try
            {
                projectile.Initialize(Vector3.zero, -10f, 0f, -2);

                Assert.AreEqual(Vector3.forward, projectile.Direction,
                    "A zero direction must fall back to the combat lane forward (+Z).");
                Assert.AreEqual(0f, projectile.Speed, "A negative speed must clamp to zero.");
                Assert.GreaterOrEqual(projectile.Lifetime, 0.01f,
                    "A zero lifetime must clamp up so the projectile can still despawn safely.");
                Assert.GreaterOrEqual(projectile.Damage, 1,
                    "Damage must never be configured below 1.");
            }
            finally
            {
                Object.DestroyImmediate(projectileObject);
            }
        }

        private static void DestroyImmediate(Object target)
        {
            if (target != null)
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
