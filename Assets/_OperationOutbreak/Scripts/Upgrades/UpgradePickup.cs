using System;
using UnityEngine;

namespace OperationOutbreak.Upgrades
{
    /// <summary>
    /// Milestone 1L-R - one small, timed upgrade pickup.
    ///
    /// This component owns PRESENTATION and its own lifetime only. It never applies an
    /// upgrade and never decides what comes next: it raises <see cref="Collected"/> or
    /// <see cref="Expired"/> exactly once and lets <see cref="UpgradePickupDirector"/>
    /// decide. That keeps effect / presentation / progression cleanly separated.
    ///
    /// The whole visual is built from prototype primitives at runtime (no external art):
    /// a coloured shape plus a larger transparent "glow" shell, floating above the road,
    /// bobbing and rotating, pulsing gently and then blinking faster near expiry.
    ///
    /// DETECTION: like the approved gates, this polls the authored Player transform
    /// against a radius instead of using OnTriggerEnter. Unity only raises trigger
    /// messages when a Rigidbody is involved, and the approved Player deliberately has
    /// neither Rigidbody nor Collider. Polling one known transform also means zombies,
    /// projectiles and hit sparks can never collect an upgrade. The pickup carries NO
    /// collider at all, so it can never block movement or be hit by the projectile
    /// SphereCast.
    ///
    /// RESET: everything here is instance state on a runtime-created object. Restart
    /// reloads the scene, which destroys every pickup and rebuilds the director fresh.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UpgradePickup : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        /// <summary>Raised once when the Player reaches this pickup in time.</summary>
        public event Action<UpgradePickup> Collected;

        /// <summary>Raised once when the lifetime ran out untouched.</summary>
        public event Action<UpgradePickup> Expired;

        /// <summary>The upgrade this pickup awards. Never applied by this component.</summary>
        public UpgradeDefinition Definition { get; private set; }

        private Transform _playerTransform;
        private Transform _visualRoot;
        private Renderer _coreRenderer;
        private Renderer _glowRenderer;
        private MaterialPropertyBlock _coreBlock;
        private MaterialPropertyBlock _glowBlock;

        private float _lifetime = 5f;
        private float _collectRadius = 1.1f;
        private float _bobAmplitude = 0.22f;
        private float _bobSpeed = 2.4f;
        private float _spinSpeed = 75f;
        private float _hoverHeight;
        private float _age;
        private bool _resolved;
        private bool _initialised;
        private bool _fadingOut;
        private float _fadeTimer;
        private float _fadeDuration = 0.18f;
        private Vector3 _basePosition;
        private Vector3 _coreScale;
        private Vector3 _glowScale;

        /// <summary>Seconds remaining before this pickup expires (0 once resolved).</summary>
        public float TimeRemaining => Mathf.Max(0f, _lifetime - _age);

        /// <summary>
        /// Builds the prototype visual and starts the timer. Called by the director
        /// immediately after the object is created.
        /// </summary>
        public void Initialise(
            UpgradeDefinition definition,
            Transform playerTransform,
            Material coreMaterial,
            Material glowMaterial,
            float lifetime,
            float collectRadius,
            float hoverHeight,
            float scale)
        {
            Definition = definition;
            _playerTransform = playerTransform;
            _lifetime = Mathf.Max(0.1f, lifetime);
            _collectRadius = Mathf.Max(0.1f, collectRadius);
            _hoverHeight = hoverHeight;
            _basePosition = transform.position;
            _age = 0f;
            _resolved = false;
            _fadingOut = false;

            BuildVisual(coreMaterial, glowMaterial, scale);
            _initialised = true;
        }

        private void BuildVisual(Material coreMaterial, Material glowMaterial, float scale)
        {
            GameObject visual = new GameObject("Visual");
            _visualRoot = visual.transform;
            _visualRoot.SetParent(transform, false);
            _visualRoot.localPosition = Vector3.zero;

            PrimitiveType primitive = ToPrimitive(Definition != null ? Definition.shape : UpgradePickupShape.Capsule);

            // Core shape - the identifiable icon.
            GameObject core = GameObject.CreatePrimitive(primitive);
            core.name = "Core";
            StripCollider(core);
            core.transform.SetParent(_visualRoot, false);
            _coreScale = CoreScaleFor(primitive, scale);
            core.transform.localScale = _coreScale;
            _coreRenderer = core.GetComponent<Renderer>();

            // Glow shell - a larger, softer copy that pulses.
            GameObject glow = GameObject.CreatePrimitive(primitive);
            glow.name = "Glow";
            StripCollider(glow);
            glow.transform.SetParent(_visualRoot, false);
            _glowScale = _coreScale * 1.55f;
            glow.transform.localScale = _glowScale;
            _glowRenderer = glow.GetComponent<Renderer>();

            // The glow shell needs a TRANSPARENT material or it would simply hide the core.
            if (coreMaterial != null)
            {
                _coreRenderer.sharedMaterial = coreMaterial;
            }

            if (glowMaterial != null)
            {
                _glowRenderer.sharedMaterial = glowMaterial;
            }

            // The glow must never occlude the core or the player behind it.
            _glowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _glowRenderer.receiveShadows = false;
            _coreRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _coreRenderer.receiveShadows = false;

            _coreBlock = new MaterialPropertyBlock();
            _glowBlock = new MaterialPropertyBlock();
            ApplyColours(1f, 1f);
        }

        private static void StripCollider(GameObject primitive)
        {
            // CreatePrimitive always adds a Collider. The pickup must not collide with
            // anything: no blocking the player, no intercepting projectile SphereCasts.
            Collider collider = primitive.GetComponent<Collider>();
            if (collider != null)
            {
                Destroy(collider);
            }
        }

        private static Vector3 CoreScaleFor(PrimitiveType primitive, float scale)
        {
            // Capsules and cylinders are 2 units tall by default; squash them so every
            // shape reads at roughly the same small size next to the player.
            return primitive == PrimitiveType.Capsule || primitive == PrimitiveType.Cylinder
                ? new Vector3(scale, scale * 0.5f, scale)
                : new Vector3(scale, scale, scale);
        }

        private static PrimitiveType ToPrimitive(UpgradePickupShape shape)
        {
            switch (shape)
            {
                case UpgradePickupShape.Cube: return PrimitiveType.Cube;
                case UpgradePickupShape.Sphere: return PrimitiveType.Sphere;
                case UpgradePickupShape.Cylinder: return PrimitiveType.Cylinder;
                default: return PrimitiveType.Capsule;
            }
        }

        private void Update()
        {
            if (!_initialised)
            {
                return;
            }

            float deltaTime = Time.deltaTime;

            if (_fadingOut)
            {
                TickFadeOut(deltaTime);
                return;
            }

            _age += deltaTime;
            Animate();

            if (_resolved)
            {
                return;
            }

            if (IsPlayerInRange())
            {
                Resolve(true);
                return;
            }

            if (_age >= _lifetime)
            {
                Resolve(false);
            }
        }

        private bool IsPlayerInRange()
        {
            if (_playerTransform == null)
            {
                return false;
            }

            // Horizontal distance only: the player walks on the road while the pickup
            // floats above it, so height must not prevent collection.
            Vector3 delta = _playerTransform.position - _basePosition;
            delta.y = 0f;
            return delta.sqrMagnitude <= _collectRadius * _collectRadius;
        }

        private void Animate()
        {
            float bob = Mathf.Sin(_age * _bobSpeed) * _bobAmplitude;
            transform.position = _basePosition + new Vector3(0f, _hoverHeight + bob, 0f);

            if (_visualRoot != null)
            {
                _visualRoot.localRotation = Quaternion.Euler(0f, _age * _spinSpeed, 0f);
            }

            // Gentle idle pulse, accelerating into an urgent blink near expiry.
            float remaining = TimeRemaining;
            bool urgent = remaining <= Mathf.Min(1.5f, _lifetime * 0.35f);
            float pulseSpeed = urgent ? 14f : 3.2f;
            float pulse = (Mathf.Sin(_age * pulseSpeed) + 1f) * 0.5f;

            float glowAlpha = urgent
                ? Mathf.Lerp(0.05f, 0.42f, pulse)
                : Mathf.Lerp(0.16f, 0.32f, pulse);
            float emission = urgent
                ? Mathf.Lerp(0.2f, 2.4f, pulse)
                : Mathf.Lerp(0.55f, 1.25f, pulse);

            float scalePulse = urgent
                ? Mathf.Lerp(0.94f, 1.12f, pulse)
                : Mathf.Lerp(0.99f, 1.05f, pulse);
            if (_glowRenderer != null)
            {
                _glowRenderer.transform.localScale = _glowScale * scalePulse;
            }

            ApplyColours(emission, glowAlpha);
        }

        private void ApplyColours(float emissionStrength, float glowAlpha)
        {
            Color tint = Definition != null ? Definition.tint : Color.white;

            if (_coreRenderer != null && _coreBlock != null)
            {
                _coreRenderer.GetPropertyBlock(_coreBlock);
                _coreBlock.SetColor(BaseColorId, tint);
                _coreBlock.SetColor(ColorId, tint);
                _coreBlock.SetColor(EmissionColorId, tint * emissionStrength);
                _coreRenderer.SetPropertyBlock(_coreBlock);
            }

            if (_glowRenderer != null && _glowBlock != null)
            {
                Color glow = tint;
                glow.a = glowAlpha;
                _glowRenderer.GetPropertyBlock(_glowBlock);
                _glowBlock.SetColor(BaseColorId, glow);
                _glowBlock.SetColor(ColorId, glow);
                _glowBlock.SetColor(EmissionColorId, tint * (emissionStrength * 0.6f));
                _glowRenderer.SetPropertyBlock(_glowBlock);
            }
        }

        /// <summary>
        /// Fires Collected or Expired exactly once. The guard means a pickup can never pay
        /// out twice even if collection and expiry land on the same frame.
        /// </summary>
        private void Resolve(bool collected)
        {
            if (_resolved)
            {
                return;
            }

            _resolved = true;

            if (collected)
            {
                // Collection flash: a brief bright pop before the object disappears.
                ApplyColours(4f, 0.75f);
                Collected?.Invoke(this);
            }
            else
            {
                Expired?.Invoke(this);
            }

            _fadingOut = true;
            _fadeTimer = 0f;
            _fadeDuration = collected ? 0.14f : 0.28f;
        }

        private void TickFadeOut(float deltaTime)
        {
            _fadeTimer += deltaTime;
            float t = _fadeDuration <= 0f ? 1f : Mathf.Clamp01(_fadeTimer / _fadeDuration);

            if (_visualRoot != null)
            {
                // Collected pops outward, expired shrinks away - both read instantly.
                float scale = Mathf.Lerp(1f, _fadeDuration <= 0.2f ? 1.5f : 0.2f, t);
                _visualRoot.localScale = Vector3.one * scale;
            }

            ApplyColours(Mathf.Lerp(2f, 0f, t), Mathf.Lerp(0.4f, 0f, t));

            if (t >= 1f)
            {
                Destroy(gameObject);
            }
        }
    }
}
