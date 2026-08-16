# OPERATION OUTBREAK — ARCHITECTURE DECISIONS

> **PROVENANCE (2026-08-14):** The original decisions file was absent from the entire
> repository history at the start of Milestone 1P (verified with `git rev-list --all`).
> Pre-1P decisions are therefore summarised from the code itself (component headers are
> detailed and self-documenting); the 1P decisions below are recorded at the time they
> were made. Anything that could not be verified is marked UNKNOWN.

## Pre-1P conventions observed in the codebase (reconstructed)

- **One gameplay authority per concern.** `PlayerController` owns movement,
  `WeaponController` owns firing/targeting, `EnemySpawner` owns spawning,
  `MissionSectionController` owns progression, `PlayerHealth` owns health.
- **Events are notifications, not control flow.** `ShotFired`, `DamageTaken`, `Died`,
  `SectionCleared`, `EncounterCompleted` are raised AFTER the state change has completed;
  listeners cannot alter outcomes. Cosmetic listeners (PlayerAnimationBridge) subscribe
  without any write-back path.
- **No static/global run state.** Scene reload is the reset. The one pre-existing static
  helper (CombatFeedback, Milestone 1H) holds no gameplay state.
- **Archetypes are data, not classes.** Every enemy type runs the same ZombieController;
  a type is only "which prefab, under which name" (Milestone 1N).
- **Pure functions are factored out for EditMode tests** (EnemySpawnMath,
  PlayerAnimationBridge helpers, DiagnosticRules), so decisions are testable without a
  scene or Play Mode.

## Milestone 1P decisions

### AD-1P-1: Combat feedback is a presentation layer wired to gameplay events

- **Decision:** Muzzle flash presentation was extracted out of `WeaponController` (where
  it had been embedded since Milestone 1C/1H: a sphere created in `Start()` and toggled
  by a coroutine) into `MuzzleFlashFeedback`, a dedicated component that listens to the
  weapon's existing `ShotFired` event and places a pooled visual at the weapon's
  read-only `MuzzlePoint`. The impact spark contract (`CombatFeedback.SpawnHitSpark`,
  called from `Projectile`) was kept, and the projectile gained a runtime trail.
- **Why:** The 1P brief demands that prototype feedback be replaceable by production
  muzzle VFX / weapon models / impact VFX / audio / animation / camera feedback "without
  rewriting weapon mechanics". Keeping presentation out of gameplay code is what makes
  that swap possible.
- **Alternatives considered:** (a) richer presentation embedded directly in
  `WeaponController` — rejected, re-couples gameplay and VFX; (b) scene-authored
  ParticleSystem prefabs — rejected, requires production art that 1P explicitly forbids;
  (c) a global feedback manager MonoBehaviour — rejected, adds a new singleton-style
  authority for what is currently a two-callsite concern.

### AD-1P-2: Pooled procedural visuals with shared materials (no per-shot allocations)

- **Decision:** `FeedbackObjectPool` (bounded stack, injected factory, injected discard)
  backs muzzle flashes and hit sparks; one shared material per visual kind is created
  once via `Shader.Find` (URP/Unlit preferred, with fallbacks) and reused. Visuals carry
  no collider, no light, no rigidbody, and never write gameplay state.
- **Why:** The 1P brief is Android/mobile-first and explicitly forbids per-shot
  instantiated materials, leaked objects and realtime per-shot lights. The pre-1P spark
  instantiated a primitive AND a material per hit; the pre-1P muzzle flash was a per-run
  sphere with a per-run material. Both are now pooled/shared.
- **Trade-off accepted:** pooled visuals are runtime-created primitives (procedural
  prototype look), which the brief explicitly allows as "temporary/procedural prototype
  visual".
- **Failure mode guarded:** a release against a full pool discards (destroy) instead of
  storing, so the pools are hard-capped at 8 visuals per kind and can never accumulate.

### AD-1P-3: Enemy hit reaction refines the existing flash instead of adding a system

- **Decision:** `ZombieController`'s existing white `HitFlash` coroutine was extended in
  place into `HitReaction` — same white flash plus a 7% sine scale punch applied ONLY to
  the `Visual` child transform. The curve (`ComputeHitPunchScale`) is pure and can never
  go below 1 (no shrinking). The reaction aborts and clears the flash the moment `_isDying`
  is set, and death begins by clearing any stale flash colour.
- **Why:** The brief says to REUSE/REFINE any existing hit-feedback system rather than
  create a competing duplicate, and to guarantee visual-only presentation (never
  authoritative position, collider, navigation, hit detection or attack range).
- **Alternative considered:** a separate HitReactionFeedback component on the enemy
  prefab — rejected, would duplicate the existing flash path and split one reaction
  across two systems.

### AD-1P-4: Tests cover non-visual logic; Unity must run them (sandbox limitation)

- **Decision:** `CombatFeedbackTests` asserts pool lifecycle (reuse, cap, drain), curve
  invariants (endpoints at 1, never below 1, bounded punch), muzzle flash duration
  clamping, projectile trail idempotency/no-collision/shared-material, and projectile
  `Initialize` clamping. Visual quality is deliberately NOT asserted — that is manual QA.
- **Constraint recorded:** the sandbox that implemented 1P has no Unity Editor, so the
  EditMode suite could not be executed there. Running
  `Window > General > Test Runner > EditMode` in Unity is part of the 1P QA gate. All new
  C# was validated with a C# grammar parser (syntax only, not Unity API semantics).
- **Status — resolved (2026-08-16):** the project owner ran the full suite in Unity 6.5:
  109/109 passed, which closes this entry's open constraint. The 1P QA gate is satisfied
  and Milestone 1P is VERIFIED. (See DEVELOPMENT_CHECKPOINT.md and MILESTONE_LEDGER.md
  for the complete evidence.)

### AD-1P-5: Camera feedback and audio are declared future work, not implemented

- **Decision:** No camera shake/impulse, no camera transform/FOV change, no audio system.
- **Why:** The 1P brief freezes camera behaviour and forbids inventing a large audio
  system; a tiny shot/hit camera impulse is flagged as a possible future enhancement
  (production-polish milestone), and weapon/impact audio is a production-polish
  dependency to be scheduled when final assets exist.

## UNKNOWN / open questions

- Pre-1P architecture rationale for gates (1J series) could not be recovered (original
  records absent); the live gameplay path replaced gates with timed pickups in 1L-R.
- The master roadmap (`OPERATION_OUTBREAK_MASTER_ROADMAP.md`) is missing from the repo
  and must be re-authored by the project owner; the 1P brief was treated as the roadmap
  for this milestone.
