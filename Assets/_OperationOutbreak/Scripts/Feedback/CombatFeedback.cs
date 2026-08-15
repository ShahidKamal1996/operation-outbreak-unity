using UnityEngine;
using UnityEngine.Rendering;

namespace OperationOutbreak.Feedback
{
    /// <summary>
    /// Milestone 1H established this as the single entry point for combat feedback; the
    /// projectile still calls SpawnHitSpark here on a confirmed damaging hit.
    ///
    /// Milestone 1P upgrades the internals without changing that contract:
    ///   - Visuals are pooled (FeedbackObjectPool) so rapid auto-fire no longer
    ///     instantiates/destroys GameObjects per shot and can never leak past a hard cap.
    ///   - One shared material per visual kind is created once (Shader.Find + cache), so
    ///     no material instance is created per shot or per hit anymore.
    ///   - The muzzle flash that WeaponController used to build and toggle by hand is now
    ///     spawned here by the new MuzzleFlashFeedback listener, so no weapon gameplay
    ///     code contains flash presentation.
    ///
    /// Everything here remains PRESENTATION, not GAMEPLAY AUTHORITY: visuals carry no
    /// collider, no light, no physics body, deal no damage and never write to gameplay
    /// state. Gameplay raises its events first; this layer only paints on top. Replacing
    /// these procedural prototypes with production VFX later means swapping the factory
    /// functions below, not the weapon or projectile logic.
    /// </summary>
    public static class CombatFeedback
    {
        // One pool per visual kind. Created lazily on first use and reused for the scene run.
        private static FeedbackObjectPool _sparkPool;
        private static FeedbackObjectPool _muzzlePool;

        // Shared materials: created exactly once per kind, then reused by every visual.
        private static Material _sparkMaterial;
        private static Material _muzzleMaterial;
        private static Material _trailMaterial;

        /// <summary>Hard cap on pooled visuals per kind; beyond this they are destroyed.</summary>
        private const int MaxRetainedPerPool = 8;

        // Tuning kept identical to the pre-1P values so 1P does not re-tune gameplay read.
        private const float SparkDuration = 0.18f;
        private const float MuzzleFlashDuration = 0.09f;
        private const float SparkScale = 0.34f;
        private const float MuzzleFlashScale = 0.38f;
        private const float MuzzleForwardOffset = 0.16f;

        private static readonly Color SparkColor = new Color(1f, 0.82f, 0.2f, 1f);
        private static readonly Color MuzzleFlashColor = new Color(1f, 0.85f, 0.25f, 1f);
        private static readonly Color TrailColor = new Color(1f, 0.72f, 0.08f, 1f);

        /// <summary>
        /// Milestone 1H contract, kept intact for Projectile: a small, short-lived impact
        /// visual at or near the hit point. No collider, no damage, no physics force.
        /// </summary>
        public static void SpawnHitSpark(Vector3 position)
        {
            GameObject spark = EnsurePool(ref _sparkPool, CreateSparkVisual).Acquire();
            spark.transform.position = position + Vector3.up * 0.28f;
            spark.transform.localScale = Vector3.one * SparkScale;

            // Lifecycle order (Milestone 1P QA fix): acquire -> configure -> ACTIVATE ->
            // start lifecycle coroutine. Activation is explicit here even though the pool
            // and Play also guarantee it, so the required sequence is visible at the call
            // site and survives future refactors.
            spark.SetActive(true);

            FeedbackVisualLifecycle lifecycle = spark.GetComponent<FeedbackVisualLifecycle>();

            if (lifecycle != null)
            {
                lifecycle.Play(SparkDuration, 0.5f, SparkScale, finished =>
                    EnsurePool(ref _sparkPool, CreateSparkVisual).Release(finished));
            }
            else
            {
                // Defensive fallback only; the factory always adds the lifecycle component.
                Object.Destroy(spark, SparkDuration);
            }
        }

        /// <summary>
        /// Milestone 1P - short muzzle flash at the existing firing/projectile origin.
        /// Called by MuzzleFlashFeedback after WeaponController has fully committed a shot,
        /// so this can never affect fire timing, targeting or cadence.
        /// </summary>
        public static void SpawnMuzzleFlash(Vector3 position, Vector3 forward)
        {
            SpawnMuzzleFlash(position, forward, MuzzleFlashDuration);
        }

        /// <summary>Duration-tunable overload used by MuzzleFlashFeedback.</summary>
        public static void SpawnMuzzleFlash(Vector3 position, Vector3 forward, float duration)
        {
            GameObject flash = EnsurePool(ref _muzzlePool, CreateMuzzleVisual).Acquire();
            Vector3 safeForward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            flash.transform.position = position + safeForward * MuzzleForwardOffset;
            flash.transform.localScale = Vector3.one * MuzzleFlashScale;

            // Lifecycle order (Milestone 1P QA fix): acquire -> configure -> ACTIVATE ->
            // start lifecycle coroutine. See SpawnHitSpark for the full reasoning.
            flash.SetActive(true);

            FeedbackVisualLifecycle lifecycle = flash.GetComponent<FeedbackVisualLifecycle>();

            if (lifecycle != null)
            {
                lifecycle.Play(duration, 0.4f, MuzzleFlashScale, finished =>
                    EnsurePool(ref _muzzlePool, CreateMuzzleVisual).Release(finished));
            }
            else
            {
                Object.Destroy(flash, Mathf.Max(0.02f, duration));
            }
        }

        /// <summary>
        /// Milestone 1P - shared material for the projectile's readability trail.
        /// Created once, reused by every projectile, never instantiated per shot.
        /// </summary>
        public static Material SharedTrailMaterial =>
            EnsureSharedMaterial(ref _trailMaterial, TrailColor, "CombatFeedback_Trail");

        /// <summary>
        /// Destroys all pooled visuals and forgets the pools. Available for scene teardown
        /// and tests; gameplay never needs to call it because pooled visuals are bounded.
        /// </summary>
        public static void ClearRuntimePools()
        {
            _sparkPool?.Drain();
            _muzzlePool?.Drain();
            _sparkPool = null;
            _muzzlePool = null;
        }

        // ------------------------------------------------------------------ factories

        private static GameObject CreateSparkVisual()
        {
            return CreatePooledSphere("CombatHitSpark", ref _sparkMaterial, SparkColor, "CombatFeedback_Spark");
        }

        private static GameObject CreateMuzzleVisual()
        {
            return CreatePooledSphere("CombatMuzzleFlash", ref _muzzleMaterial, MuzzleFlashColor, "CombatFeedback_Muzzle");
        }

        private static GameObject CreatePooledSphere(string name, ref Material cache, Color color, string materialName)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = name;

            // Feedback visuals must never participate in gameplay collision.
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null)
            {
                Object.Destroy(collider);
            }

            Renderer renderer = visual.GetComponent<Renderer>();
            renderer.sharedMaterial = EnsureSharedMaterial(ref cache, color, materialName);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            visual.AddComponent<FeedbackVisualLifecycle>();

            // Milestone 1P QA fix: the factory must hand out ACTIVE visuals. Deactivation
            // belongs exclusively to the pool's Release; a factory-created inactive visual
            // was the root cause of the "Coroutine couldn't be started" regression.
            return visual;
        }

        // ------------------------------------------------------------------ helpers

        private static FeedbackObjectPool EnsurePool(ref FeedbackObjectPool pool, System.Func<GameObject> factory)
        {
            if (pool == null)
            {
                pool = new FeedbackObjectPool(factory, MaxRetainedPerPool, null);
            }

            return pool;
        }

        private static Material EnsureSharedMaterial(ref Material cache, Color color, string materialName)
        {
            if (cache != null)
            {
                return cache;
            }

            // URP is the active pipeline, so prefer its unlit shader for a bright, cheap,
            // unlit prototype look. The fallbacks keep the editor and tests alive in any
            // pipeline configuration without a per-shot instantiated material.
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                            ?? Shader.Find("Unlit/Color")
                            ?? Shader.Find("Sprites/Default");

            cache = new Material(shader);
            cache.name = materialName;
            cache.color = color;
            return cache;
        }
    }
}
