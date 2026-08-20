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
- **Presentation-complete deactivation (QA fix #9):** deactivation is gated on a
  live completion condition, not a clip timer alone. The bridge reports
  `IsDeathPresentationComplete` = clip finished (normalized ≥ 0.999) AND grounding
  settled within `deathGroundingCompletionTolerance` (0.015), with snap-to-target
  once inside tolerance. ZombieController waits on it (plus a 0.15 s production
  hold), bounded by a 4 s safety timeout; the prototype fallback keeps the exact
  pre-1Q timer-only behavior.
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

### AD-1Q-8: Corpse grounding is driven by the death animation's normalized time (QA fix #10)

- **Decision:** The production zombie and its death clip are FIXED assets, so the
  final grounded death Y is a static per-asset constant, not a runtime
  measurement. The setup tool samples the near-final death pose ONCE
  (normalized 0.95) inside prefab contents and serializes the resulting stable
  `EnemyAnimationBridge.deathGroundedVisualY` (world-space-delta formula; the
  transforms sampled are recorded and restored so the prefab always saves in
  its standing pose). At runtime the bridge blends
  `standingVisualY → deathGroundedVisualY` as a smoothstep of the Death clip's
  normalized time between 0.25 and 0.85, with a per-frame downward-only clamp.
- **Why not keep the fix #6–#9 system:** a post-animation correction is
  VISIBLY wrong by construction — sampling at 0.9 / refining at 0.99 and then
  MoveTowards-settling after the clip finished means the lowering can only
  happen once the body has stopped moving, which reads as "sinking in water".
  Grounding must be INSIDE the animation window: the blend finishes at 0.85,
  before the clip-finish gate (0.999), so the final lying pose already rests on
  the road and nothing moves after the animation ends.
- **What was removed:** runtime pose sampling, the refinement pass, the chased
  target and the time-based MoveTowards settle are all DELETED (reflection-pinned
  by tests) — no dual grounding systems can fight. Completion keeps the fix #9
  contract (clip finished AND grounded Y reached within 0.015 tolerance), the
  production-only hold (0.15 s) and the safety timeout (4 s). Collider
  lifecycle (fix #7), standing grounding (−1.005), locomotion cadence, materials
  and all gameplay are untouched. Fallback: a documented constant (−1.5) is used
  only when the setup measurement is unavailable, and a misconfigured value can
  never lift the corpse (downward-only clamp).

### AD-1Q-9: The final grounded Y is calibrated at the true near-end pose with a small contact margin (QA fix #11)

- **Decision:** the fix #10 calibration sampled the death pose at normalized
  0.95 — slightly too early, because the death clip keeps changing vertically
  through its tail; the serialized Y therefore left the final corpse pose a
  little above the road. The setup-time calibration now samples the TRUE
  near-end pose at normalized 0.999 (1.0 minus a tiny epsilon, the last
  evaluable instant), and the tool logs a vertical profile (0.95 / 0.99 /
  0.999) of the lowest corpse world Y so the tail movement is visible in the
  console. A small configurable DOWNWARD contact margin
  (`deathGroundingContactMargin`, default 0.02, runtime-clamped to [0, 0.05])
  is serialized alongside the measured Y and subtracted at runtime
  (`ApplyDeathGroundingContactMargin`); the blend target, completion gate and
  misconfiguration warning all use the margin-adjusted value.
- **Visual rule:** prefer a very slight contact/intersection with the road over
  visible hovering — but never sink the body deeply (hence the 0.05 ceiling).
- **Why setup-time only:** the production zombie and its death clip are fixed
  assets; the runtime must never resample poses or chase targets (that was the
  QA fix #6–#9 "sinking" failure). Calibration drift like this is corrected by
  re-running the setup tool, not by runtime logic.

### AD-1Q-10: Hybrid animation → ragdoll death (1Q FINAL production direction)

- **Decision:** animation-only corpse grounding (fixes #6–#11) was still
  visibly hovering in manual QA, so the production death is now a two-stage
  HYBRID: the one-shot `Base Layer.Death` clip plays as an animation LEAD-IN
  for a configurable `deathRagdollHandoffSeconds` (default 0.30 s, inside the
  required 0.25–0.40 s band — the clip's body is already falling there), then
  the bridge hands the skeleton to physics exactly once. Physics naturally
  completes the fall and establishes ground contact: the corpse can no longer
  hover because the ground contact is produced by the physics engine itself,
  not by a calibrated Y offset. The presentation completes after the settle
  window (0.6 s); the existing hold (0.15 s) and safety timeout (4 s) still
  bound the corpse lifetime.
- **Mobile strategy:** 11 major humanoid bones only (Hips, Spine, Head,
  upper/lower arms, upper/lower legs — no fingers/toes/hands/feet), ONE
  primitive collider per bone (capsule along long bones, sphere for the head;
  radius derived from bone length, capped 0.3), ConfigurableJoints with hard
  symmetric limits (spine 45°, head 80°, shoulder 100°, elbow 120°, hip/knee
  90°), locked linear motion, no projection, no interpolation, discrete
  collision detection. While alive every ragdoll body is KINEMATIC and every
  ragdoll collider is DISABLED — zero physics cost for living enemies, and the
  gameplay CapsuleCollider stays the only live collider. Corpse-vs-corpse
  physics is practically avoided: connected joints never self-collide, live
  capsules disable the moment their owner dies, and the corpse lives only for
  the short settle window.
- **Reset/reuse:** `EnemyRagdoll` captures each bone's authored pose at runtime
  (prefab-independent) and `RestoreForReuse` (called by the bridge on
  OnDisable) restores poses parent-before-child, zeroes velocities, restores
  kinematic states, disables ragdoll colliders, re-enables the Animator and
  clears the latch — a pooled enemy can never spawn collapsed.
- **The animation grounding does not fight the ragdoll:** when the ragdoll is
  configured, the setup tool zeroes the grounding window (0 → 0), the bridge's
  grounding gate is ragdoll-aware, and once physics owns the corpse the bridge
  writes nothing at all (no Animator parameters, no ProductionVisual Y).
- **Authoring:** `Tools > Operation Outbreak > Set Up Basic Infected Ragdoll`
  (also called by the production visual setup tool) resolves bones from the
  imported StylizedZombieAvatar via `Animator.GetBoneTransform` —
  no hand-authored FBX fileIDs, no manual collider/joint creation. It is
  validation-first: if any bone is missing, nothing is modified.
- **Preserved:** full-path `Animator.Play("Base Layer.Death")`, one-shot latch,
  death clip speed 1, root motion OFF (alive), gameplay CapsuleCollider
  lifecycle, standing grounding −1.005, walk/attack cadence, gameplay speed
  2.5, materials, immediate death accounting, kill/section/mission flow,
  prototype fallback (no production visual → animation-only path), Toon
  Soldier.

### AD-1Q-11: Stable anatomically aligned ragdoll authoring (Hybrid Ragdoll QA fix #1)

- **Decision:** the first ragdoll authoring was physically unstable ("random
  dance" after handoff). Root causes and their fixes, all in authoring —
  the hybrid animation→ragdoll architecture itself was kept:
  1. **Collider orientation:** capsules now live on per-bone `RagdollCollider`
     child holders rotated with `ComputeColliderAlignmentRotation`, so the
     capsule axis follows the ACTUAL bone→child vector measured per bone
     (head stays a sphere). No fixed local-Y assumption — the skeleton's real
     frames decide.
  2. **Collider sizes:** conservative per-group radius table (0.05–0.17 m)
     replaces the aggressive `boneLength * 0.9 / 2` formula; capsule height =
     measured bone length with a full-diameter minimum. Connected pairs taper
     (radius ratio ≤ 2.5, `ComputeAdjacentOverlapRatio` policy-pinned) so the
     solver no longer kicks overlapping colliders apart at activation.
  3. **Self-collision OFF:** all ragdoll colliders go on the committed
     `OO_Ragdoll` layer (TagManager layer 8). `EnemyRagdoll` disables
     layer-vs-itself collision at runtime (`Physics.IgnoreLayerCollision`,
     guarded by `ShouldUseLayerSelfCollisionPolicy`) and re-asserts the
     collider layers in Awake. Corpse parts interact ONLY with the
     environment/road — never with each other, never corpse-vs-corpse.
  4. **Anatomical joints:** axes are computed from the real bone chain
     (`ComputeJointAxes`: twist axis = bone direction; hinge axis =
     cross(parent, child) with collinear/childless fallbacks) and stored
     child-local. Per-axis limits replace the symmetric ±90–120° freedom:
     elbows/knees are HINGE-LIKE (bend 100–110°, twist ≤ 15°, lateral ≤ 10°),
     shoulders/hips wide but controlled (≤ 80°), spine modest (≤ 30°),
     head controlled (≤ 45°). Zero-freedom axes are LOCKED.
  5. **Stable handoff:** `ActivateRagdoll` zeroes every body's linear/angular
     velocity (kinematic bodies moved by the Animator carry residual velocity
     into the first simulated step — the "kick"), disables the Animator,
     enables the colliders, verifies the pure `IsActivationPrepared` gate,
     then frees the bodies in parent-before-child (hips-first) order. The
     bridge's handoff gate is one-shot and ragdoll-aware
     (`ShouldTriggerRagdollHandoff`).
  6. **Physics tuning:** `maxAngularVelocity` 7 rad/s (no spin-kicks), angular
     drag 0.4 (damps flailing), linear drag 0, discrete detection, no
     interpolation, no projection; masses rebalanced so connected ratios stay
     ≤ 2.4 (ceiling 4x policy-pinned) — the joint solver no longer fights
     itself.
- **Why not scripted grounding:** rejected by the production direction — real
  ground contact must come from physics; the fix is making physics stable,
  not replacing it.
- **Validation:** the setup tool logs a per-bone collider report and flags
  PROBLEMATIC connected overlap/mass pairs after generation; the read-only
  `Tools > Operation Outbreak > Debug Basic Infected Ragdoll` menu prints the
  same report.
- **AD-1Q-11 amendment — Unity 6 kinematic-velocity write ordering (1S QA fix
  #2):** Unity 6 discards (and warns about) any `linearVelocity` /
  `angularVelocity` assignment while `isKinematic == true`. All ragdoll
  velocity writes therefore live in ONE guarded site —
  `EnemyRagdoll.ZeroVelocitiesWhereLegal` (per-body
  `IsVelocityWriteAllowed(!isKinematic)` gate). Lifecycle orderings:
  ACTIVATION frees the bodies first (parent-before-child), then zeroes in the
  same frame before any FixedUpdate — the first simulated step starts at zero
  velocity (the fix #1 stabilization is now genuinely effective instead of
  discarded); REUSE RESET zeroes first (bodies are still non-kinematic after
  the ragdoll; never-ragdolled bodies are skipped), then re-kinematic-es.
  Warnings are never suppressed; tests replay both orderings against real
  Rigidbodies and pin the absence of the warning with
  `LogAssert.NoUnexpectedReceived`.

## Milestone 1S decisions

### AD-1S-1: Variant differences are DATA, not classes

- **Decision:** there remains exactly ONE reusable enemy gameplay framework.
  `EnemyArchetypeDefinition` (ScriptableObject) is pure variant data:
  identity (stable id + display name), gameplay tuning (health, speed, damage,
  attack interval/range, separation), presentation references (production
  visual source, locomotion profile, controller load path, cadence reference,
  requires-ragdoll). `ZombieController.ApplyArchetype` and
  `EnemyAnimationBridge.ApplyArchetype` READ the definition at spawn and
  nothing else changes — no `BasicZombieController` / `RunnerZombieController`
  may ever exist (reflection-pinned by tests). Applying null is a no-op, so
  the verified Basic defaults stay authoritative for every spawn path that
  never heard of archetypes.
- **Ownership:** gameplay authority = ZombieController; presentation =
  EnemyAnimationBridge/EnemyRagdoll; variant tuning = the definition asset;
  spawner = EnemySpawner (composition + bookkeeping). Death/ragdoll timings
  deliberately stay PREFAB-owned (shared by every variant) — they are not
  variant data and are not exposed on the definition.

### AD-1S-2: Locomotion presentation is a per-archetype controller profile, not a code branch

- **Decision:** a variant's locomotion is an AnimatorController authored by
  the SAME tool as the Basic controller (`EnemyAnimationSetup` generalized:
  `ResolveLocomotionClipPath("Walk"|"Run")` + parameterized rebuild and
  validation). The state machine contract — Speed/Attack/Dead/
  LocomotionSpeedMultiplier parameters, state names, and the
  `Base Layer.Death` full path — is identical across profiles; only the
  locomotion state's clip differs (walk for Basic, the reserved run clip for
  Runner). At spawn the bridge swaps `animator.runtimeAnimatorController` to
  the profile the definition declares (loaded through Resources) and writes
  the profile's cadence reference. No `if (runner) play X` exists in gameplay
  code.
- **Basic keeps the prefab default:** the Basic definition declares an empty
  controller path, so the verified prefab controller is never even re-assigned
  (zero regression surface). The Runner controller lives under
  `Resources/EnemyArchetypes/` so the runtime can load it by path without
  scene wiring or per-variant code; until the tool generates it, validation
  and the runtime both fail LOUDLY (never silently). The generated Runner
  controller (`OO_Runner.controller`, identical state machine to Basic with the
  reserved run clip in the locomotion state) is COMMITTED to source control
  (1T QA fix #1), so a fresh checkout passes `Validate Enemy Archetypes`
  without running the rebuild tool.

### AD-1S-3: One shared gameplay prefab for all production variants

- **Decision:** every production archetype spawns the SAME
  `Zombie_Prototype.prefab` (production visual, hybrid ragdoll and bridge
  baked in) and differs only by the applied definition. No prefab duplication
  for variants. The legacy `Runner_Prototype.prefab` remains solely for the
  current scene's untouched 1N composition (prototype fallback); the 1T
  mission foundation will migrate compositions to the 1S seam.
- **Spawner seam:** `EnemySpawner.SpawnEnemy(id/definition)` and
  `SpawnEnemyWithDefinition(definition, position)` instantiate the shared
  prefab, apply the definition, then run the exact bookkeeping every other
  spawn runs (SetTarget, Died subscription, tracking, EnemySpawned report).
  The existing 1N section path is byte-untouched in this milestone.

### AD-1S-4: The hybrid ragdoll death is a shared system, validated per archetype

- **Decision:** the verified hybrid death (lead-in → stabilized ragdoll
  handoff → settle → despawn; collider lifecycle; reuse reset) is identical
  for every production archetype because it lives on the shared prefab.
  A definition declares `requiresRagdoll`, and the editor validator checks
  the shared prefab actually carries a configured `EnemyRagdoll` — a
  production archetype can never silently ship without the death system it
  declares. Future variants may override death ONLY through explicit new
  data fields, never by forking the system.

### AD-1S-5: Archetypes fail loudly, never silently

- **Decision:** three layers of validation: (1) pure definition checks
  (`CollectDefinitionProblems`: id/name/ranges/locomotion setup), (2) pure
  duplicate-id detection (`FindDuplicateArchetypeIds`), (3) editor asset
  checks (`Tools > Operation Outbreak > Validate Enemy Archetypes`: production
  prefab resolves, shared ragdoll configured, declared controller exists).
  At runtime the registry logs errors for duplicate or unknown ids and the
  bridge logs a clear error for a missing locomotion controller; every
  failure path degrades to the verified Basic behaviour rather than stalling
  or spawning incorrectly.

## Milestone 1T decisions

### AD-1T-1: Mission data describes WHAT; gameplay systems execute it

- **Decision:** mission configuration lives in a pure-data `MissionDefinition`
  ScriptableObject (identity + ordered sections + per-section enemy composition
  by 1S stable id). `MissionSectionController` owns progression only and now
  reads a serialized `missionDefinition` reference instead of its own section
  table; `EnemySpawner` receives the composition and resolves/spawns it through
  its serialized per-archetype library; `EnemyArchetypeRegistry` stays the
  id→definition resolution authority; `ZombieController` stays the shared enemy
  gameplay authority.
- **Why:** the brief's core rule forbids `Mission1Controller`/`Mission2Controller`
  duplication. A normal future mission must be creatable by configuring an asset,
  not by writing a new runtime class.
- **What was NOT collapsed:** no single `MissionManager`. The four systems keep
  their established ownership boundaries.

### AD-1T-2: Composition uses the 1S stable ids; the spawner resolves them to the verified spawn configuration

- **Decision:** `EnemyCompositionEntry.archetypeId` carries the 1S STABLE id
  (`basic_infected` / `runner`). The spawner's per-archetype library (which has
  always mapped an id to a prefab + spawn offset + standoff) gained an additive
  `stableId` field, so one library entry resolves BOTH the legacy id (`BASIC` /
  `RUNNER`, still pinned by the 1N scene-configuration tests) and the 1S stable id.
  The mission therefore requests by stable id and the spawner executes the exact
  verified spawn (Basic = Zombie_Prototype 2.5/3 at the band; Runner =
  Runner_Prototype 3.5/2 with its verified offset/standoff) — byte-equivalent to
  the pre-1T mission.
- **Why the Runner was NOT re-routed through the shared-prefab seam:** the verified
  mission runner is `Runner_Prototype` (speed 3.5, health 2, prototype visual),
  while the 1S `EnemyArchetype_Runner` definition (speed 4.5, production Run
  profile whose `OO_Runner.controller` is not yet committed) would change its
  speed, visuals and console cleanliness. Routing it through `SpawnEnemyWithDefinition`
  would regress the verified baseline, which the brief forbids. Mission data is
  1S-stable-id based; the spawn execution stays on the existing verified pipeline.
- **No variant branching:** the mapping is data (library entries), never `if (runner)`.

### AD-1T-3: Derived information only (no duplicated totals)

- **Decision:** `TotalEnemyCount`, `GetArchetypeCount(id)` and the section totals
  are computed from sections → compositions → counts. No independent stored total
  exists to drift out of sync.

### AD-1T-4: Fallback policy — fail loudly AND stay playable

- **Decision:** a missing/empty `MissionDefinition` reference logs a loud actionable
  `[1T]` error and falls back to the verified prototype mission built in memory
  (`MissionDefinition.CreateVerifiedPrototypeMission`). This combines the brief's
  option A (loud diagnostic) with option B (documented safe fallback) so a setup
  error can never produce unpredictable or partially unplayable gameplay.
- **Why not silent:** malformed mission data must be loud, never silent. Runtime
  never repairs mission assets; the editor validator reports the exact correction.

### AD-1T-5: Mission validation is read-only and side-effect-free

- **Decision:** `MissionDefinition.CollectProblems(definition, knownArchetypeIds)`
  is a pure static (testable without an asset database); the editor tool
  `Tools > Operation Outbreak > Validate Mission Definitions` supplies the known
  1S ids and reports mission + section + exact problem + correction. It detects
  empty ids, invalid numbers, zero sections, null/duplicate section ids, empty
  composition, null/empty/unknown archetype ids, non-positive counts and
  structurally impossible progression. It never silently repairs data.

### AD-1T-6: The scene receives the mission by serialized reference

- **Decision:** the gameplay scene's `MissionSectionController.missionDefinition`
  references the committed `Mission_01` asset by GUID. No scene-name checks, no
  `if (missionNumber == 1)` reconstruction. The runtime executes whatever
  MissionDefinition it is given.

## Milestone 1U decisions

### AD-1U-1: Objectives are plain serializable tagged data, not a class hierarchy

- **Decision:** `MissionObjectiveDefinition` is a single `[Serializable]` data class
  (stable `objectiveId`, `title`, `objectiveType`, `required`) held in an ordered
  list on `MissionDefinition`. There is NO per-objective-type class hierarchy and
  no `Mission01ObjectiveController`-style runtime duplication.
- **Why:** the brief's core rule — mission data defines objectives, runtime systems
  evaluate them. Future types extend the `MissionObjectiveType` enum and the runtime
  evaluator; a simple tagged-data model is cleaner than speculative inheritance for
  the foundation milestone.

### AD-1U-2: Required progress DERIVES from mission structure (never stored)

- **Decision:** `ClearAllSections` has no stored completion value — its
  `RequiredProgress` is the mission's section count at evaluation time, so an
  objective can never drift out of sync with the sections it depends on.

### AD-1U-3: ONE completion authority, four clean owners

- **Decision (completion chain):** `MissionSectionController` publishes progress
  (`SectionCleared` / `MissionCompleted`); `MissionObjectiveController` evaluates
  required objectives and is the single completion gate; `MissionCompleteController`
  presents the final state (unchanged, listening to
  `EnemySpawner.EncounterCompleted`). `MissionSectionController` NO LONGER calls
  `CompleteEncounter()` — it only publishes progress, so no two systems can declare
  victory.
- **Boundaries:** the objective controller does NOT spawn, fight, duplicate the
  section controller, or own rewards/save/progression.
- **QA fix #2 addendum (deferred completion boundary):** the completion evaluation
  is NOT performed reentrantly inside the `SectionCleared` dispatch. The objective
  controller records progress synchronously and defers `EvaluateRequiredObjectives`
  to `LateUpdate` (end of frame), so every `SectionCleared` observer (notably
  `GameplayDiagnostics`, which marks the final section cleared) commits its state
  before the single completion path (`CompleteEncounter` → report) runs. This is a
  deferred boundary, not an arbitrary delay, and the flag is never polled for
  progress.

### AD-1U-4: Event-driven evaluation, no polling, no duplicate events

- **Decision:** the objective runtime reuses the existing
  `MissionSectionController.SectionCleared` event (the project already publishes
  exactly the progress information objectives need). No new global events were
  added for information that already exists, and no per-frame scene polling.

### AD-1U-5: Fail loud on missing objective data

- **Decision:** a missing MissionDefinition, an objective list with no REQUIRED
  objective, or a null objective logs a loud `[1U]` error and completion is NEVER
  triggered. The committed Mission_01 always carries explicit objective data (and
  the in-memory prototype fallback carries the same objective), so the normal path
  is fully defined; malformed data can never silently complete or silently hang.
- Editor validation (`CollectProblems` + `Validate Mission Definitions`) rejects
  the same defects up front: null/empty/duplicate/unsupported objectives and a
  mission with no required completion objective.

### AD-1U-6: Runtime progress is never serialized into the mission asset

- **Decision:** `MissionObjectiveRuntime` is a plain runtime class (not a
  `UnityEngine.Object`, not serialized). `MissionDefinition` holds only static
  objective DEFINITIONS; progress lives in scene-lifetime state, so a reload
  resets objectives exactly like the rest of the mission flow.

## Milestone 1V decisions

### AD-1V-1: Reward data is static mission configuration; the service grants it

- **Decision:** `MissionRewardDefinition { coins, supplies }` is a `[Serializable]`
  data class on `MissionDefinition`; `MissionRewardService` (one
  `DisallowMultipleComponent` component) reads it, grants into `RuntimeWallet`
  and produces `MissionResultData`. No currency calculations live in
  `MissionCompleteController` / `MissionObjectiveController` /
  `MissionSectionController` / `EnemySpawner`, and no
  `Mission01RewardController`-style classes exist.
- **Why:** the brief's core rule - mission data defines rewards; a service
  calculates/grants them; result data reports; UI presents; navigation requests.

### AD-1V-2: The wallet is session runtime only (2C owns persistence)

- **Decision:** `RuntimeWallet` holds non-negative, overflow-safe (`long`,
  saturating) Coins/Supplies balances for the session and is reset by a scene
  reload exactly like every other run-scoped system. NO PlayerPrefs/JSON/save
  schema, no first-completion flags, no unlock persistence.
- **2C seam:** `MissionRewardService.RewardGranted` carries the result and the
  wallet carries the balances; a future SaveService subscribes to persist that
  output. Nothing in 1V writes permanent data.

### AD-1V-3: One reward authority, driven by outcome events, idempotent per run

- **Decision:** the service subscribes to the authoritative outcome events only
  (`EnemySpawner.EncounterCompleted` = success, `PlayerHealth.Died` = failure) -
  never polls UI state, never derives grant from section progress. A run-scoped
  latch (reset in `OnEnable` = new run) guarantees at most one grant per run.
  This is NOT persistent first-completion protection (documented; 2C owns it).

### AD-1V-4: Result data is immutable and never serialized into the mission

- **Decision:** `MissionResultData` is a plain, immutable runtime object.
  `MissionDefinition` stays static configuration - no result/grant/wallet state
  is ever serialized into the asset. The result is deliberately small (identity,
  outcome, reward, sections) - not an analytics framework and not a duplicate of
  GameplayDiagnostics.

### AD-1V-5: Retry routes through the existing authoritative reset

- **Decision:** `MissionResultNavigation.RequestRetry()` reloads the active scene
  (the same `SceneManager.LoadScene(activeBuildIndex)` the verified restart
  buttons already used), which resets objectives, section progression, spawner,
  temporary upgrades, the reward latch and result state. No second restart
  system was created.

### AD-1V-6: Return/Next is an intent seam, not a fake destination

- **Decision:** `ReturnRequested` / `NextRequested` are instance events on
  `MissionResultNavigation` that future Base/Map systems consume. No Base/Map
  scene exists yet, so Return/Next emit the intent and log a documented
  development fallback - no invented scene, no fragile hard-coded scene names.

## UNKNOWN / open questions

- Pre-1P architecture rationale for gates (1J series) could not be recovered (original
  records absent); the live gameplay path replaced gates with timed pickups in 1L-R.
- The master roadmap (`OPERATION_OUTBREAK_MASTER_ROADMAP.md`) is missing from the repo
  and must be re-authored by the project owner; the 1P brief was treated as the roadmap
  for this milestone.
