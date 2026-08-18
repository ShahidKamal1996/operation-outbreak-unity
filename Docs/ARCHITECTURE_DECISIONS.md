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
- **QA fix #3 addendum:** `TurnToward` outputs raw degrees that may exceed ±180 (e.g.
  181° for a -179° target) — Euler angles are periodic, so this is representationally
  equivalent and drives the same orientation through `Quaternion.Euler`. The runtime
  yaw maths was verified correct against the ±179/±170 wrap matrix; the failing
  EditMode test was corrected to compare angular difference via `Mathf.DeltaAngle`
  rather than raw floats. No runtime change was made.
- **QA fix #4 addendum (barrel-tip measurement):** FBX forensics (skin clusters,
  TransformLink matrices) proved the barrel tip sits ~1.25 m from the right hand's
  bind origin in a rotated bone frame — no hand-authored hand-local offset can be
  correct without Unity on-screen tuning. Decision: the muzzle socket position is
  MEASURED at runtime, not authored. `WeaponMuzzleSocketBinder` bakes the soldier's
  `SkinnedMeshRenderer` once at startup, picks the forward-most vertex along the
  soldier root's facing (the package's rifle points forward; FrontAxis +Z in the FBX),
  and parents the existing MuzzlePoint to a runtime socket
  (`Right Hand → ToonSoldierMuzzleSocket → MuzzlePoint`) at the measured hand-local
  position. The authored `barrelTipOffset` is demoted to fallback/override (used only
  when measurement is disabled or unavailable), and `Unbind()` restores the muzzle's
  original parent/local transform for the Carl/prototype fallback. Bounded frame
  retries cover late Animator/avatar initialization without per-frame work.
- **QA fix #5 addendum:** the barrel-tip selection EditMode test was corrected from
  exact `Vector3` equality to a 1e-4 positional-tolerance comparison plus a
  selection-correctness guard. Reason: Unity 2021.2+ `Vector3.Equals` is exact
  per-component equality, while `InverseTransformPoint` output carries inherent
  float32 transform noise (~1e-8..1e-6 at unit scale) — the two vectors display
  identically but are never bit-equal. Production selection logic was verified correct
  and is unchanged.
- **QA fix #6 (hand-cluster muzzle measurement):** the global forward-most heuristic
  is retired. FBX forensics proved the package's rifle is rigid on the Bip001 R Hand
  (153 verts at skin weight 1.0; muzzle 53.4 cm from the hand) and that the bind pose
  holds the rifle sideways — so the bind-pose (or pre-animation) global forward-most
  vertex is the helmet/face, which is exactly where the socket landed. New rule: the
  muzzle is the vertex FARTHEST FROM THE HAND among vertices whose dominant skin
  weight is the hand bone (≥ 0.9, read from `sharedMesh.boneWeights` via the hand's
  index in `renderer.bones`). Because the rifle is rigid on the hand, this selection
  is pose-independent and immune to animation timing — it works in the bind pose, in
  idle, run and shoot alike. The socket's +Z is aligned hand→muzzle so the muzzle
  flash's authored forward offset stays on the barrel line; `barrelTipOffset` remains
  as a documented last-resort fallback only.
- **QA fix #8/#9 (follow architecture + single weapon presentation):**
  - **MuzzlePoint is never re-parented again.** It stays under the Weapon forever; a
    runtime socket (`Right Hand → ToonSoldierMuzzleSocket`) carries the measured
    muzzle pose and the muzzle FOLLOWS it each frame (binder Update at
    `DefaultExecutionOrder(-100)`, before WeaponController.Update so shots spawn from
    the current hand pose). This removes every SetParent from the deactivation path:
    the "Cannot set the parent ... while activating or deactivating" Console error is
    structurally impossible, the muzzle can never be orphaned, and the Carl/prototype
    fallback is simply "stop following" — the muzzle's authored local pose under the
    Weapon was never modified, so its world position snaps back automatically.
  - **One visible weapon:** the obsolete prototype gun (`Weapon > WeaponModel`) is
    hidden whenever the Toon Soldier VISUAL LAYER is active
    (`ShouldHidePrototypeWeapon`), independent of muzzle binding; the Carl/prototype
    fallback restores it. All logical gameplay components (WeaponController,
    MuzzlePoint, MuzzleFlashFeedback) remain untouched.
  - **QA fix #11 correction:** the first implementation serialized
    `prototypeWeaponRoot` with the WeaponModel **GameObject** fileID (210010) into a
    Transform-typed field. Unity resolves Transform fields only from `!u!4` Transform
    fileIDs, so the reference was null at runtime and the hide never executed. The
    scene now uses WeaponModel's Transform (210011). Rule recorded: scene YAML
    references must match the field's component type exactly (GameObject `!u!1` vs
    Transform `!u!4` vs MonoBehaviour `!u!114`/stripped). Additionally, visibility
    was decoupled from bind success (`RefreshPrototypeWeaponVisibility`, write-on-
    change each Update) so a slow/failed Animator bind can never leave the old gun
    visible while the soldier presentation is active.
  - **Deterministic FBX-derived socket constants:** the muzzle position and barrel
    direction are now derived from the actual FBX rifle geometry (hand-rigid tube,
    corrected matrix/axis conventions: vertices Z-up + `PreRotation −90°X`, column-major
    link matrices). Result: hand-local muzzle **(0.543, −0.0327, 0.0765) m** (54.9 cm
    from the hand) and barrel direction **(0.9885, −0.0595, 0.1392)**. These are
    serialized as `fbxBarrelTipOffset`/`fbxBarrelDirection`; the default runtime path
    recomputes the same quantity in Unity's exact frames from the hand-rigid cluster
    (pose-independent). The old blind `barrelTipOffset (0,0,0.6)` was removed.

### AD-1P.5-7: Layered shooting - locomotion and firing are separate animation layers (QA fix #12)

- **Problem:** the full-body `assault_combat_shoot` clip lived on the Animator BASE
  Layer next to the locomotion blend tree. Every Gunplay trigger swapped the whole
  base layer into the shoot state, freezing the legs while the code-driven Player
  root kept moving ("dragged" look).
- **Decision:** two-layer controller, authored by the existing rebuild tool (never
  hand-written YAML):
  - **Base Layer:** `NeutralStance` (idle) + `Locomotion` blend tree only. The legs
    are owned exclusively by this layer and are never interrupted by firing.
  - **Shoot Layer** (weight 1, `Override` blending, avatar mask
    `ToonSoldier_UpperBodyMask.mask`): `Gunplay` plays `assault_combat_shoot`;
    the mask includes torso (Body), head and both arms and EXCLUDES the hips
    (`SetTransformActive("Hips", false)`), legs and fingers, so the pelvis and legs
    keep the base-layer pose. The layer's default state is **Empty** (no motion):
    under the mask, an empty state passes the base-layer pose through, so idle/run
    show normally when not firing (no T-pose, no freeze). `Gunplay -> Empty` is an
    exit-time transition (0.9 / 0.15s) that smoothly blends the upper body back to
    locomotion when firing stops; the AnyState trigger keeps self-transition enabled
    for continuous auto-fire.
- **Contract preserved:** the bridge, parameters (Speed/IsMoving/Gunplay/
  HitReaction/Dead), aiming, muzzle follow and Carl fallback are unchanged. Root
  motion stays off; the player controller remains the movement authority.
- **QA fix #12A correction:** Unity's AvatarMask transform APIs are index-based —
  `SetTransformActive(int, bool)`, `GetTransformActive(int)`, `GetTransformPath(int)`
  (paths are exposed per index). Bone-name strings must be resolved through the mask's
  own transform list first; nothing is hard-coded and a missing path is a safe no-op.
  The humanoid body-part APIs (`SetHumanoidBodyPartActive(AvatarMaskBodyPart, bool)`)
  were already correct and are unchanged.
- **QA fix #12B correction (persistence):** a LAYER state machine is a separate Unity
  object — it must be persisted as a controller sub-asset
  (`AssetDatabase.AddObjectToAsset(stateMachine, controller)`, `HideInHierarchy`)
  or the serialized layer keeps `m_StateMachine: {fileID: 0}` and Unity logs
  "Statemachine for layer 'Shoot Layer' is missing" after every reload. Rule recorded:
  nested `AnimatorStateMachine` objects created by controller-authoring tools are
  ALWAYS added as sub-assets, and rebuild cleanup removes orphaned nested machines
  (never the base root).

## Milestone 1Q decisions

### AD-1Q-1: Enemy animation is a one-way presentation bridge, mirroring the player bridge

- **Decision:** `EnemyAnimationBridge` (on the enemy prefab root) reads the new
  read-only `ZombieController.CurrentPlanarSpeed` and observes the EXISTING
  gameplay events `DamagedPlayer` (→ Attack trigger) and `Died` (→ Dead latch).
  It never moves the enemy, applies no damage and feeds nothing back into
  gameplay; deleting it leaves the prototype visual fully playable.
- **Why:** the 1O.5/1P.5 player contract ("gameplay raises events for its own
  reasons; presentation observes without write-back") is the proven pattern, and
  the 1Q brief demands gameplay state stay separate from visuals. No new
  gameplay authority was introduced; `ZombieController` remains the single
  movement/attack/health authority.

### AD-1Q-2: The enemy controller is authored by Unity, never hand-written

- **Decision:** `OO_BasicInfected.controller` is built in place by
  `Tools > Operation Outbreak > Rebuild Basic Infected Animator Controller`
  (`EnemyAnimationSetup`), resolving the Mixamo clips via AssetDatabase and
  authoring states/transitions with UnityEditor.Animations APIs — the binding
  rule from 1P.5 QA fix #1 (never hand-author FBX sub-asset fileIDs).
- **State machine:** Speed (float) / Attack (trigger) / Dead (bool) — the exact
  `EnemyAnimationBridge` contract. Idle (default) ↔ Walk on Speed > 0.1;
  Attack via AnyState trigger (self-transition allowed, exit-time return to
  Idle/Walk by Speed); Death via AnyState with NO exits. The zombie run clip is
  deliberately unwired and validated absent: it is reserved for future Runner
  variants and must not redefine Basic Infected locomotion.
- **Root motion:** OFF, enforced in the bridge's Awake and by the setup tool —
  gameplay is the only movement authority.

### AD-1Q-3: Production visual is installed by an idempotent prefab-editing tool

- **Decision:** `Tools > Operation Outbreak > Set Up Basic Infected Production
  Visual` (`EnemyVisualSetup`) edits the `Zombie_Prototype.prefab` ASSET through
  `PrefabUtility.LoadPrefabContents/SaveAsPrefabAsset` (the FBX-instantiation
  pattern from 1O.5/1P.5). It instantiates `StylizedZombie_01` under a new
  `ProductionVisual` child, assigns controller + `StylizedZombieAvatar`
  (AlwaysAnimate), hides the prototype `Visual` renderers (never deleted) and
  wires the bridge.
- **Fallback:** if the production prefab cannot be resolved the tool aborts and
  modifies nothing — the prototype visual keeps working, so gameplay/debugging
  can never break. The prototype prefab keeps its ZombieController and Visual
  child regardless (pinned by an EditMode test).
- **Placement:** production child at identity transform, scale 1 (tuning fields
  `ProductionVisualPosition/RotationEuler/Scale` are tool-level constants — QA
  verifies grounding/scale, never gameplay collision changes).

### AD-1Q-6: Grounding, animation-safe hit feedback, and clip-derived death window (QA fix #1B)

- **Bug 1 — grounding (deterministic, FBX-derived):** the vendor zombie's lowest
  vertex sits +0.536 cm above its model root (parsed from `StylizedZombie.fbx`,
  Y 0.536–198.8 cm) while the enemy root rides at y = 1; the old zero offset left
  the zombie floating a full unit. QA fix #2: the renderer-bounds measurement was
  removed (it read the vendor's EDITOR/reference pose, not the animated idle
  stance — the QA run measured -0.628 and feet still floated). The tool now
  applies the static FBX-derived offset `ProductionVisualGroundingOffsetY =
  -(1 + 0.00536) = -1.005` every run. Deterministic, pose-independent, never a
  gameplay-root/collider/lane change.
- **Bug 2 — animation-safe hit feedback:** continuous fire spawned one overlapping
  legacy `HitReaction` coroutine per bullet whose white/clear races flickered every
  renderer (perceived as head/body vibration) and ran the prototype scale punch.
  New rule: ONE flash coroutine per enemy (`StartHitFeedback`/`StopHitFeedback`),
  the legacy transform punch applies only when the PROTOTYPE visual is the active
  presentation (`ShouldApplyLegacyTransformPunch`), and — QA fix #2 — a hit-flash
  COOLDOWN (`hitFlashCooldownSeconds` = 0.35, gated by `ShouldStartHitFlash`)
  prevents the flash from restarting per bullet, so sustained auto-fire produces
  one readable pulse per window instead of a fire-rate strobe. The prototype
  fallback keeps its legacy behavior; the white material flash remains the
  animation-safe hit readability.
- **Bug 3 — death presentation:** the imported death clip is ~2.8–3.0 s (FBX take
  LocalTime 2.97 s / ReferenceTime 2.80 s), far longer than the old 1.15 s
  constant, so the enemy deactivated mid-animation after a final flash burst. The
  tool now writes `deathPresentationDuration = clip.length + 0.3 s`
  (`ComputeDeathPresentationDuration`); `TakeDamage` stops and clears hit feedback
  BEFORE raising `Died`; the bridge death latch blocks further Attack triggers
  (`ShouldPlayAttackAnimation`); the Death state has no exits. QA fix #2: the
  bridge additionally performs a DIRECT `CrossFadeInFixedTime` into the Death
  state (name shared via `EnemyAnimationBridge.DeathStateName`, also used by the
  controller tool) so the death clip starts immediately even if a same-frame
  `AnyState → Attack` self-transition races the parameter-driven one, and freezes
  the locomotion multiplier at 1. Gameplay accounting (`Died`, kill counting,
  section clear, mission completion) is unchanged — only the visual deactivation
  is delayed.

### AD-1Q-7: Deterministic death entry and source-controlled URP materials (QA fix #3)

- **Death entry:** `CrossFadeInFixedTime` is a TRANSITION REQUEST whose start can be
  delayed by same-frame state machine evaluation (e.g. the AnyState Attack
  self-transition). Deterministic rule: the bridge calls
  `animator.Play(StringToHash("Base Layer.Death"), 0, 0f)` — an immediate,
  transition-independent switch of the base layer into the terminal Death state at
  normalized time 0 — with the Dead bool kept as the parameter-driven backup.
  QA fix #4: Animator.Play targets states by the FULL PATH hash, not the short
  state name (Unity's documented contract); the bridge and the controller tool
  share `BaseLayerName` / `DeathStateFullPath` / `DeathPlayLayer` constants, the
  tool pins the base machine's name to "Base Layer" and validates it, and an
  editor isolation diagnostic (`EnemyDeathDiagnostics`) forces death without any
  gameplay involvement. The setup tool validates the Death state + clip resolve
  before finishing and warns otherwise.
- **Death grounding (QA fix #6, corrected in #7):** the standing ProductionVisual
  offset is authored for the standing pose only. When the corpse lies down, the
  bridge waits for the death clip to reach a late sample threshold (0.9 normalized),
  measures the actual lying pose's lowest point, and smoothly blends the
  ProductionVisual's local Y toward the grounded target. QA fix #7 correction:
  ALL measurements are now WORLD-SPACE — lowest corpse vertex measured as a world
  Y, lane derived as `rootWorldY - enemyRootGroundHeight`, target =
  `currentVisualLocalY + (groundWorldY - lowestCorpseWorldY)` (a world delta
  applied to local Y; valid because the parent chain is identity-rotated/scaled).
  A refinement pass at normalized 0.99 re-targets from the true resting pose, and
  one diagnostic log per pass prints the full calculation. Standing Y is captured
  at Awake and restored on disable/reset; a serialized fallback offset applies only
  when measurement is unavailable. Gameplay root and root motion stay untouched.
- **Collider lifecycle (QA fix #7):** the dead corpse is presentation-only. The
  bridge captures the ROOT-level gameplay colliders' authored enabled states in
  Awake, disables them once at death, and restores the snapshot on OnEnable for
  reuse. The visual death animation is unaffected.
- **Monotonic downward-only settle (QA fix #8):** the death-grounding target starts
  at the standing ceiling and may only ever move downward:
  `min(previousTarget, min(computedTarget, standingCeiling))` applied to every
  measurement/refinement pass, re-asserted against the current visual Y each frame.
  Upward corrections (corpse already below ground) are discarded — a small sink is
  preferred to an upward pop. Genuine downward corrections still reach the lane.
- **One-shot rule (QA fix #5):** the death presentation must be idempotent.
  `Animator.Play` with normalizedTime 0 runs exactly once per death, gated by
  `ShouldStartDeathPresentation(deathLatched, presentationStarted)`; repeated
  `Died` callbacks and repeated diagnostic invocations refuse to re-Play, and the
  diagnostic logs the current normalized time instead (so clip progression can be
  verified without restarting). The Death state is pinned to speed 1 with no speed
  parameter and `AnyState → Death` has `canTransitionToSelf = false`, so the state
  machine itself can never re-enter Death and restart the clip.
- **Materials:** the vendor `.mat` files use the BUILT-IN Standard shader (renders
  magenta under URP); local vendor-material conversions are NOT portable and are
  forbidden. Operation Outbreak owns URP/Lit materials
  (`Art/Materials/Enemies/OO_Zombie_01/02.mat`) with the vendor textures wired, and
  the setup tool assigns them to every production renderer (both LODs) each run,
  selected deterministically from the vendor material name. Vendor package assets
  stay untouched; a clean clone renders identically without manual fixing.

### AD-1Q-5: Locomotion cadence is synchronized with a per-state speed parameter (Bug 4)

- **Problem:** the Mixamo walk clip plays at its native cadence (~1.3 u/s worth of
  foot motion) while ZombieController moves the enemy at the approved 2.5 u/s, so
  the feet visibly slide.
- **Decision:** a dedicated `LocomotionSpeedMultiplier` Animator float parameter
  drives ONLY the Walk state's playback speed (`speedParameterActive` /
  `speedParameter` on Walk). Idle, Attack and Death keep their authored fixed
  speed, and `Animator.speed` is never touched — attack and death timing are
  therefore unchanged by construction.
- **Multiplier derivation:** `EnemyAnimationBridge.ComputeLocomotionSpeedMultiplier`
  = `clamp(CurrentPlanarSpeed / walkReferenceSpeed, 0.5, 2.5)`. The reference is
  NOT hand-guessed: the visual setup tool reads the walk clip's own
  `averageSpeed` and serializes it onto the prefab bridge (fallback 1.3 when the
  clip reports no measurable speed). QA fix #1C: `AnimationClip.averageSpeed` is a
  Vector3 in Unity (average root-motion velocity), so the tool uses its
  `.magnitude` as the cadence scalar. Gameplay speed values are unchanged (still
  pinned by tests); the future Runner variant reuses the identical mechanism at
  higher speeds.

### AD-1Q-4: Death presentation delays only the visual, never the accounting

- **Decision:** `ZombieController.DeathFeedback` now waits a serialized
  `deathPresentationDuration` (default 0.38 s = the pre-1Q behavior
  byte-for-byte; the setup tool raises it to 1.15 s on the prefab so the death
  clip plays). The `Died` event, kill counting, `EnemySpawner` bookkeeping,
  section clear and mission completion all still fire IMMEDIATELY at zero
  health — only the GameObject deactivation is delayed. No ragdoll physics.

## UNKNOWN / open questions

- Pre-1P architecture rationale for gates (1J series) could not be recovered (original
  records absent); the live gameplay path replaced gates with timed pickups in 1L-R.
- The master roadmap (`OPERATION_OUTBREAK_MASTER_ROADMAP.md`) is missing from the repo
  and must be re-authored by the project owner; the 1P brief was treated as the roadmap
  for this milestone.
