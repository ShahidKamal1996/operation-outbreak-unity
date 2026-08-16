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

## Milestone 1P.5 decisions

### AD-1P.5-1: The Toon Soldier reuses the single existing animation bridge, unchanged

- **Decision:** No new animation-authority code. `PlayerAnimationBridge` is reused
  verbatim: its `animator` field is pinned (in the committed scene) to the Toon Soldier's
  Animator via a stripped prefab reference. The new `ToonSoldier_Player.controller`
  declares the exact parameter set the bridge already drives (Speed float, IsMoving
  bool, Gunplay trigger, HitReaction trigger, Dead bool), so the bridge's existing
  `ShotFired` / `CurrentPlanarSpeed` / health hooks drive the soldier without a line of
  runtime code changing.
- **Why:** The 1P.5 brief requires reusing existing presentation hooks and forbids a
  second competing gameplay-animation authority. One bridge, one Animator target at a
  time, matches the 1O.5 architecture.
- **Controller shape:** mirrors `Carl_Player.controller` — `NeutralStance`
  (`assault_combat_idle`), `Locomotion` blend tree (idle at 0.15 / `assault_combat_run`
  at 0.85 on Speed), `Gunplay` (`assault_combat_shoot`, non-looping, AnyState trigger
  with self-transition allowed, exit-time return at 0.8). `assault_combat_shoot` was
  chosen over `assault_combat_shoot_burst` because a single-shot clip returns cleanly to
  locomotion under the bridge's 0.18s Gunplay cooldown during 5-shots/s auto-fire; the
  non-looping clip plus exit transitions makes animator lock-up impossible.
- **Known presentation limitation:** the package has no hit-reaction or death clips, so
  the HitReaction/Dead parameters exist (bridge contract, no console warnings) but have
  no states. A dead soldier parks on the idle pose. Enemy-side 1P hit feedback is
  unaffected. Documented rather than papered over.
- **QA fix addendum (2026-08-16):** the first 1P.5 QA run reported "Animator states
  active but the character does not animate, Clip Count: 0". Root cause: the controller
  had been hand-authored as YAML with motion references that reused the internal clip
  fileID observed in Carl's mixamo-derived FBXs; the Toon Soldiers package FBXs are
  3ds-Max/Biped exports whose embedded AnimationClip sub-assets carry DIFFERENT internal
  fileIDs. Unity loaded states and parameters (live-looking state machine) but resolved
  zero clips. **New rule (binding):** never hand-author FBX sub-asset fileIDs in
  controller YAML. The controller is now rebuilt in place by
  `Tools > Operation Outbreak > Rebuild Toon Soldier Animator Controller`
  (`ToonSoldierAnimationSetup`), which resolves the real `AnimationClip` sub-assets via
  AssetDatabase and re-author the states/blend tree/transitions with
  `UnityEditor.Animations` APIs — Unity generates every reference. The rebuild keeps the
  asset GUID stable (the scene wires the controller by GUID) and is idempotent.
  EditMode tests `ToonSoldierAnimatorTests` (3) pin the clip wiring from now on.

### AD-1P.5-2: Presentation swap is committed YAML + idempotent editor tools

- **Decision:** The committed scene wires everything that CAN be authored deterministically
  (bridge → soldier Animator stripped reference, controller via prefab-instance
  override, CarlVisual inactive). The FBX-instantiation edge cases stay in editor tools,
  mirroring the 1O.5 Carl tool: `ToonSoldierVisualSetup` (new) activates the soldier,
  deactivates Carl, applies the URP material, pins the bridge; `CarlVisualSetup` gained
  the mirror toggle so Carl is restorable as a one-click fallback (QA #14). The soldier
  tool REUSES an existing instance instead of re-instantiating, so the committed
  scene-level wiring survives tool runs.
- **Why:** FBX sub-asset fileIDs are machine-generated (the 1O.5 tool's header documents
  this); tools are the established, safe path for those. Committed YAML for the rest
  keeps the pull-and-play flow that the user's own scene prep enabled.

### AD-1P.5-3: URP material conversion for the soldier (package material untouched)

- **Decision:** The package's `basic_soldier_blue.mat` uses the built-in "Standard"
  shader, which renders magenta under URP (this project is URP). A new project material
  `Materials/Player/ToonSoldier_Player.mat` (URP/Lit, shader GUID identical to
  `Carl_Player.mat`, same `basic_soldier_blue.png` texture in `_BaseMap`, white base
  colour) is applied to the soldier's renderers by the setup tool. The package material
  is NOT modified; no global rendering/post-processing/lighting settings changed.
- **Why:** Minimal conversion per the 1P.5 brief; the asset-package file stays pristine.

### AD-1P.5-4: The Toon Soldier's rifle is presentation only

- **Decision:** The model's internal rifle/WeaponContainer is never gameplay authority.
  Firing, projectile origin, targeting and muzzle feedback remain owned by the existing
  `Weapon` GameObject + `MuzzlePoint`, exactly as in 1P. No gameplay muzzle was moved to
  match the model. If the soldier's rifle and the muzzle flash look misaligned during QA,
  the fix is a presentation offset on `ToonSoldierVisual` (or a future visual socket),
  reported to the owner for manual tuning — never a gameplay change.

### AD-1P.5-6: Grounding, visual aim and muzzle binding are presentation-only (QA fix #2)

- **Grounding — decision:** `ToonSoldierVisual.localPosition.y = -1`, leaving
  `ToonSoldier_demo` at its normalized transform. The offset is NOT guessed: the
  `ToonSoldier_demo.FBX` was parsed (binary FBX walker) — the file is centimeter-units,
  Z-up, and the skinned mesh's lowest vertices sit at Z = 0.004 cm, i.e. the model's
  feet are at the model origin. Under the Player root's authored y = 1, -1 places the
  feet exactly on the lane surface — the same offset Carl's tool already establishes.
- **Visual aim — decision:** `ToonSoldierPresentationAim` (Player root) reads the new
  read-only `WeaponController.CurrentTargetTransform` (a presentation accessor; target
  SELECTION stays inside WeaponController/EnemySpawner — no AcquireTarget duplication)
  and rotates only `ToonSoldierVisual`'s yaw. Smoothed via a clamped degrees-per-second
  turn plus snap epsilon (pure helpers `ComputePlanarYaw` / `TurnToward`, unit-tested),
  shortest-path across the 180° wrap, returns to forward when no target. The gameplay
  Player root is never written, so movement/lane/collision semantics are untouched.
- **Muzzle at the rifle — decision:** `WeaponMuzzleSocketBinder` (Weapon GO) re-parents
  the EXISTING authoritative `MuzzlePoint` under the soldier's humanoid Right Hand bone
  (one-time `Animator.GetBoneTransform` resolution in Awake — no per-frame searches, no
  Update loop) with a tunable hand-local `barrelTipOffset`. Rationale: the package's
  `WeaponContainer` node is a vestigial root-level helper at the model's feet (proven by
  the FBX node graph — parent `//RootNode`, translation ~9cm) and does NOT follow the
  skeleton; the rifle is part of the skinned mesh and visually rides the hand, so the
  hand bone is the only stable animated anchor. The hand-relative barrel offset is a
  presentation tuning value (QA item: adjust if the flash is off the barrel).
  Fallback: `ShouldBind` skips when the soldier is inactive or the animator/avatar/hand
  is missing — Carl/prototype restores the muzzle to its authored Weapon position with
  zero code changes. No duplicate weapon authority is created (unit-tested).
- **WeaponController API change:** exactly one addition — read-only
  `CurrentTargetTransform`. No gameplay path reads it back; fire timing, targeting,
  projectile trajectory and cadence are untouched.

## UNKNOWN / open questions

- Pre-1P architecture rationale for gates (1J series) could not be recovered (original
  records absent); the live gameplay path replaced gates with timed pickups in 1L-R.
- The master roadmap (`OPERATION_OUTBREAK_MASTER_ROADMAP.md`) is missing from the repo
  and must be re-authored by the project owner; the 1P brief was treated as the roadmap
  for this milestone.
