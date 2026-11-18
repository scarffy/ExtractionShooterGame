using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Visuals;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using static ExtractionShooter.Gameplay.Visuals.PrimitiveParts;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// A third-person character built from primitives and posed procedurally: the <see cref="HumanoidRig"/> in a
    /// <see cref="VisualProfile"/>'s colours, dressed in an <see cref="Outfit"/> (helmet, armor, rig, backpack, knife,
    /// slung and holstered weapons, the drawn weapon in both hands), animated from a <see cref="BodyPose"/>:
    /// <list type="bullet">
    /// <item>Walk cycle from the velocity (feet placed by IK, so they keep pace at any speed and direction).</item>
    /// <item>Crouch: knees bend, hips drop and the torso leans so the head ends where the crouched head hitbox is.</item>
    /// <item>Pitch: spine, head and the weapon follow the look direction; the arms reach the weapon's grips by IK.</item>
    /// <item>Aim / ready / lowered / sprint weapon carries, blended.</item>
    /// </list>
    /// Purely visual: no colliders, no gameplay state. Shared by <c>PlayerVisuals</c>, <c>BotVisuals</c> and the lobby's
    /// character preview. Built on demand (<see cref="Build"/>), so dedicated servers never build one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterBody : MonoBehaviour
    {
        private const float DefaultCrouchRatio = 0.65f;
        private const float MinCrouchHip = 0.3f;

        [SerializeField] private VisualProfile _profile;

        [Tooltip("Whether the body casts shadows when visible.")]
        [SerializeField] private bool _castShadows = true;

        [Header("Smoothing (seconds)")]
        [SerializeField, Min(0f)] private float _crouchSmoothTime = 0.1f;
        [SerializeField, Min(0f)] private float _carrySmoothTime = 0.09f;

        [Tooltip("Observers receive pitch a few times a second: this hides the steps.")]
        [SerializeField, Min(0f)] private float _pitchSmoothTime = 0.08f;

        [Header("Posture")]
        [Tooltip("Forward lean of the torso when fully crouched (degrees).")]
        [SerializeField, Range(0f, 40f)] private float _crouchLean = 18f;

        [Tooltip("Forward lean when sprinting (degrees).")]
        [SerializeField, Range(0f, 30f)] private float _sprintLean = 12f;

        [Tooltip("How far the torso turns side-on behind a long gun (degrees).")]
        [SerializeField, Range(0f, 45f)] private float _stanceTwist = 25f;

        [SerializeField, Min(0.01f)] private float _muzzleFlashSeconds = 0.05f;

        private readonly List<Renderer> _renderers = new List<Renderer>();
        private readonly List<Transform> _gear = new List<Transform>();
        private readonly List<Transform> _carried = new List<Transform>();

        private HumanoidRig _rig;
        private Transform _held;
        private WeaponModel _heldModel;
        private Transform _useItem;
        private UseItemVisual _useItemKind;
        private string _gearKey;
        private string _weaponKey;
        private GearShape _armorShape;
        private float _backpackDepth;
        private BodyRenderMode _mode = BodyRenderMode.Visible;

        private BodyPose _target;
        private bool _snap = true;
        private float _crouch, _crouchVelocity;
        private float _aim, _aimVelocity;
        private float _sprint, _sprintVelocity;
        private float _lowered, _loweredVelocity;
        private float _pitch, _pitchVelocity;
        private Vector3 _velocity;
        private float _phase;
        private int _lastFlashFrame = -1;

        /// <summary>A weapon carry: grip position and weapon rotation relative to the neck, in the look (or body) frame.</summary>
        private readonly struct Carry
        {
            public Carry(Vector3 position, Vector3 euler)
            {
                Position = position;
                Rotation = Quaternion.Euler(euler.x, euler.y, euler.z);
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

        private static readonly Carry LongAim = new Carry(new Vector3(0.12f, 0.0f, 0.24f), new Vector3(0f, -2f, 0f));
        private static readonly Carry LongReady = new Carry(new Vector3(0.14f, -0.07f, 0.23f), new Vector3(14f, -6f, 0f));
        private static readonly Carry LongLowered = new Carry(new Vector3(0.11f, -0.22f, 0.25f), new Vector3(42f, -14f, 0f));
        private static readonly Carry LongSprint = new Carry(new Vector3(0.06f, -0.22f, 0.24f), new Vector3(22f, -55f, -30f));
        private static readonly Carry PistolAim = new Carry(new Vector3(0.04f, 0.04f, 0.44f), Vector3.zero);
        private static readonly Carry PistolReady = new Carry(new Vector3(0.09f, -0.16f, 0.3f), new Vector3(38f, 0f, 0f));
        private static readonly Carry PistolLowered = new Carry(new Vector3(0.08f, -0.24f, 0.26f), new Vector3(55f, 0f, 0f));
        private static readonly Carry PistolSprint = new Carry(new Vector3(0.17f, -0.4f, 0.1f), new Vector3(75f, 0f, 0f));

        public VisualProfile Profile => _profile;
        public bool IsBuilt => _rig != null;

        /// <summary>The weapon in hand, or null.</summary>
        public WeaponModel HeldWeapon => _heldModel;

        /// <summary>Sets the profile on a body created at runtime (before <see cref="Build"/>).</summary>
        public void Initialize(VisualProfile profile, bool castShadows)
        {
            if (IsBuilt) return;
            _profile = profile;
            _castShadows = castShadows;
        }

        /// <summary>Builds the bare body once (no gear). Safe to call again.</summary>
        public void Build()
        {
            if (IsBuilt) return;
            if (_profile == null)
            {
                Debug.LogError($"[{nameof(CharacterBody)}] Visual profile is not assigned on {name}.", this);
                return;
            }

            _rig = HumanoidRig.Build(transform, _profile);
            _held = Node(transform, "HeldWeapon", Vector3.zero);
            _snap = true;
            ApplyRenderMode();
        }

        /// <summary>Dresses the body. Only groups whose items changed are rebuilt.</summary>
        public void ApplyOutfit(Outfit outfit)
        {
            if (!IsBuilt || outfit == null) return;

            string gearKey = outfit.KeyOf(EquipmentSlot.Helmet, EquipmentSlot.BodyArmor, EquipmentSlot.ChestRig,
                EquipmentSlot.Backpack, EquipmentSlot.Melee);
            string weaponKey = gearKey + outfit.KeyOf(EquipmentSlot.Primary1, EquipmentSlot.Primary2, EquipmentSlot.Pistol) +
                               (outfit.Drawn.HasValue ? ((int)outfit.Drawn.Value).ToString() : "-");
            bool changed = false;
            if (gearKey != _gearKey)
            {
                _gearKey = gearKey;
                RebuildGear(outfit);
                changed = true;
            }
            if (weaponKey != _weaponKey)
            {
                _weaponKey = weaponKey;
                RebuildWeapons(outfit);
                changed = true;
            }
            if (changed) ApplyRenderMode();
        }

        /// <summary>The pose to move toward from now on.</summary>
        public void SetPose(in BodyPose pose) => _target = pose;

        /// <summary>Jumps straight to the target pose on the next frame (spawn, teleport).</summary>
        public void Snap() => _snap = true;

        public void SetRenderMode(BodyRenderMode mode)
        {
            if (_mode == mode) return;
            _mode = mode;
            if (mode != BodyRenderMode.Hidden) _snap = true;
            ApplyRenderMode();
        }

        /// <summary>Flashes the held weapon's muzzle (once per frame, however many pellets).</summary>
        public void Flash()
        {
            if (_heldModel == null || _lastFlashFrame == Time.frameCount) return;
            _lastFlashFrame = Time.frameCount;
            _heldModel.Flash(_muzzleFlashSeconds);
        }

        // ---------------- Outfit ----------------

        private void RebuildGear(Outfit outfit)
        {
            foreach (Transform piece in _gear)
                if (piece != null) Destroy(piece.gameObject);
            _gear.Clear();

            ArmorDefinition helmet = outfit.Get<ArmorDefinition>(EquipmentSlot.Helmet);
            ArmorDefinition armor = outfit.Get<ArmorDefinition>(EquipmentSlot.BodyArmor);
            ContainerItemDefinition rig = outfit.Get<ContainerItemDefinition>(EquipmentSlot.ChestRig);
            ContainerItemDefinition backpack = outfit.Get<ContainerItemDefinition>(EquipmentSlot.Backpack);

            _rig.HeadCover.gameObject.SetActive(helmet == null);
            if (helmet != null)
                _gear.Add(GearBuilder.Helmet(_rig.Head, VisualClassifier.Of(helmet, _profile), _profile.TintOr(helmet.Id, _profile.Helmet), _profile));

            _armorShape = armor != null ? VisualClassifier.Of(armor, _profile) : GearShape.None;
            if (armor != null)
                _gear.Add(GearBuilder.Armor(_rig.Chest, _armorShape, _profile.TintOr(armor.Id, _profile.Armor), _profile));
            if (rig != null)
                _gear.Add(GearBuilder.Rig(_rig.Chest, VisualClassifier.Of(rig, _profile), _armorShape, _profile.TintOr(rig.Id, _profile.Rig), _profile));

            _backpackDepth = 0f;
            if (backpack != null)
                _gear.Add(GearBuilder.Backpack(_rig.Chest, VisualClassifier.Of(backpack, _profile), _armorShape,
                    _profile.TintOr(backpack.Id, _profile.Backpack), _profile, out _backpackDepth));

            if (outfit.Get(EquipmentSlot.Melee) != null) _gear.Add(GearBuilder.Knife(_rig.Hips, _profile));
        }

        private void RebuildWeapons(Outfit outfit)
        {
            foreach (Transform piece in _carried)
                if (piece != null) Destroy(piece.gameObject);
            _carried.Clear();
            _heldModel = null;

            int slung = 0;
            foreach (EquipmentSlot slot in EquipmentRules.WeaponSlots)
            {
                WeaponDefinition weapon = outfit.Get<WeaponDefinition>(slot);
                if (weapon == null) continue;

                if (outfit.Drawn == slot)
                {
                    _heldModel = WeaponModelBuilder.Build(_held, weapon, outfit.ModsOf(slot), _profile);
                    if (_heldModel != null) _carried.Add(_heldModel.transform);
                    continue;
                }

                if (slot == EquipmentSlot.Pistol)
                {
                    Transform holster = Node(_rig.Legs[1].Upper, "Holster", new Vector3(0.105f, -0.12f, 0.01f),
                        Quaternion.LookRotation(Vector3.down, Vector3.right));
                    Box(holster, "HolsterBody", new Vector3(0f, 0.01f, 0.06f), new Vector3(0.045f, 0.06f, 0.16f), _profile.Lit(_profile.Pads));
                    WeaponModelBuilder.Build(holster, weapon, outfit.ModsOf(slot), _profile);
                    _carried.Add(holster);
                    continue;
                }

                // Primaries on the back, crossed diagonally, outside the backpack.
                float side = slung == 0 ? 1f : -1f;
                float z = GearBuilder.BackOf(_armorShape) - _backpackDepth - 0.04f - slung * 0.045f;
                Transform sling = Node(_rig.Chest, "Sling", new Vector3(0f, 0.1f, z),
                    Quaternion.LookRotation(new Vector3(0.45f * side, 0.9f, 0f), Vector3.back));
                WeaponModel model = WeaponModelBuilder.Build(sling, weapon, outfit.ModsOf(slot), _profile);
                if (model != null) model.transform.localPosition = -model.Center;
                _carried.Add(sling);
                slung++;
            }
        }

        private void ApplyRenderMode()
        {
            if (!IsBuilt) return;
            GetComponentsInChildren(true, _renderers);
            bool visible = _mode != BodyRenderMode.Hidden;
            ShadowCastingMode shadows = _mode == BodyRenderMode.ShadowsOnly ? ShadowCastingMode.ShadowsOnly
                : _castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            foreach (Renderer part in _renderers)
            {
                part.enabled = visible;
                part.shadowCastingMode = shadows;
            }
            if (_heldModel != null)
            {
                foreach (Renderer flash in _heldModel.FlashRenderers)
                {
                    flash.enabled = _mode == BodyRenderMode.Visible;
                    flash.shadowCastingMode = ShadowCastingMode.Off;
                }
                _heldModel.SetFlashAllowed(_mode == BodyRenderMode.Visible);
            }
        }

        // ---------------- Pose ----------------

        private void LateUpdate()
        {
            if (!IsBuilt || _mode == BodyRenderMode.Hidden) return;

            float deltaTime = Time.deltaTime;
            float crouch = Mathf.Clamp01(_target.Crouch01);
            float aim = Mathf.Clamp01(_target.Aim01);
            float sprint = _target.Sprinting ? 1f : 0f;
            float lowered = _target.Lowered ? 1f : 0f;
            if (_snap)
            {
                _snap = false;
                _crouch = crouch;
                _aim = aim;
                _sprint = sprint;
                _lowered = lowered;
                _pitch = _target.Pitch;
                _velocity = _target.LocalVelocity;
                _crouchVelocity = _aimVelocity = _sprintVelocity = _loweredVelocity = _pitchVelocity = 0f;
            }
            else
            {
                _crouch = ViewmodelMotion.SmoothDamp(_crouch, crouch, ref _crouchVelocity, _crouchSmoothTime, deltaTime);
                _aim = ViewmodelMotion.SmoothDamp(_aim, aim, ref _aimVelocity, _carrySmoothTime, deltaTime);
                _sprint = ViewmodelMotion.SmoothDamp(_sprint, sprint, ref _sprintVelocity, _carrySmoothTime * 1.5f, deltaTime);
                _lowered = ViewmodelMotion.SmoothDamp(_lowered, lowered, ref _loweredVelocity, _carrySmoothTime * 1.5f, deltaTime);
                _pitch = ViewmodelMotion.SmoothDamp(_pitch, _target.Pitch, ref _pitchVelocity, _pitchSmoothTime, deltaTime);
                _velocity = Vector3.Lerp(_velocity, _target.LocalVelocity, 1f - Mathf.Exp(-10f * deltaTime));
            }

            PoseBody(deltaTime, out float moveBlend, out float swing);
            if (_heldModel != null) PoseWeapon();
            else PoseFreeArms(moveBlend, swing);
            PoseUseItem();
        }

        /// <summary>Healing: the left hand leaves the weapon to hold the item in front of the chest (or at the leg).</summary>
        private void PoseUseItem()
        {
            UseItemVisual kind = _target.UseItem;
            if (kind != _useItemKind)
            {
                _useItemKind = kind;
                if (_useItem != null) Destroy(_useItem.gameObject);
                _useItem = UseItemModel.Build(transform, kind, _profile);
                if (_useItem != null) ApplyRenderMode();
            }
            if (_useItem == null) return;

            Quaternion body = transform.rotation;
            Vector3 rest = _rig.Neck.position + body * new Vector3(-0.06f, -0.3f, 0.3f);
            Quaternion restRotation = body * Quaternion.Euler(-20f, 0f, 0f);
            UseItemModel.Pose(_useItem, rest, restRotation, UseItemMotion.Evaluate(kind, _target.UseSeconds, _target.UseProgress));

            LimbChain left = _rig.Arms[0];
            left.Solve(_useItem.position, transform.TransformDirection(new Vector3(-0.8f, -1f, -0.3f)));
            left.End.rotation = _useItem.rotation;
            _rig.Gloves[0].localPosition = Vector3.zero;
        }

        private void PoseBody(float deltaTime, out float moveBlend, out float swing)
        {
            var flat = new Vector3(_velocity.x, 0f, _velocity.z);
            float speed = flat.magnitude;
            moveBlend = Mathf.Clamp01(speed / 0.8f);
            Vector3 moveDirection = speed > 0.01f ? flat / speed : Vector3.forward;
            float stride = GaitCycle.StrideLength(speed, 0.3f, 0.3f, 0.85f);
            _phase = GaitCycle.Advance(_phase, speed * deltaTime, stride);
            float lift = Mathf.Lerp(0.11f, 0.07f, _crouch) * (1f + 0.3f * _sprint);
            swing = GaitCycle.ArmSwing(_phase) * moveBlend;

            // Hips: crouch drop, bob, a little sway with the stride.
            float ratio = _target.CrouchHeadRatio > 0.1f ? _target.CrouchHeadRatio : DefaultCrouchRatio;
            float crouchHip = CrouchPose.HipHeight(HumanoidRig.HeadHeight * ratio, HumanoidRig.HeadAboveHip, _crouchLean, MinCrouchHip);
            // Moving, the hips drop enough for straight legs to reach the feet at the ends of the stride, and bob down
            // (never up: the legs are already straight) once per step.
            float legLength = HumanoidRig.ThighLength + HumanoidRig.ShinLength;
            float standingHip = HumanoidRig.HipHeight - GaitCycle.HipDrop(legLength, stride * moveBlend);
            const float bob = 0.012f;
            float hipY = CrouchPose.Blend(standingHip, crouchHip, _crouch) + (GaitCycle.HipBob(_phase, bob) - bob) * moveBlend;
            float hipZ = -CrouchPose.HipShiftBack(HumanoidRig.HeadAboveHip, _crouchLean) * _crouch;
            float hipYaw = swing * 6f * (1f - _crouch);
            _rig.Hips.localPosition = new Vector3(0f, hipY, hipZ);
            _rig.Hips.localRotation = Quaternion.Euler(0f, hipYaw, 0f);

            // Torso: lean, turn side-on behind a long gun, bend with the look pitch; the head takes the rest.
            HoldStyle hold = _heldModel != null ? _heldModel.Hold : HoldStyle.None;
            float twist = hold == HoldStyle.Long ? _stanceTwist * (1f - _sprint) * (1f - 0.5f * _lowered) : 0f;
            float lean = _crouchLean * _crouch + _sprintLean * _sprint;
            float pitch = Mathf.Clamp(_pitch, -80f, 80f);
            _rig.Spine.localRotation = Quaternion.Euler(lean * 0.55f, twist * 0.5f - hipYaw, 0f);
            _rig.Chest.localRotation = Quaternion.Euler(lean * 0.45f - pitch * 0.45f, twist * 0.5f, 0f);
            _rig.Neck.localRotation = Quaternion.Euler(-pitch * 0.55f - lean, -twist, 0f);

            // Legs: feet on the walk cycle (or the crouch stance), knees forward.
            for (int side = 0; side < 2; side++)
            {
                float sign = side == 0 ? -1f : 1f;
                float phase = side == 0 ? _phase : GaitCycle.Opposite(_phase);
                GaitCycle.FootOffset foot = GaitCycle.Foot(phase, stride, lift);
                Vector3 local = new Vector3(sign * (0.11f + 0.03f * _crouch), HumanoidRig.AnkleHeight,
                                    side == 0 ? 0.12f * _crouch : -0.16f * _crouch)
                                + moveDirection * (foot.Forward * moveBlend) + Vector3.up * (foot.Lift * moveBlend);
                LimbChain leg = _rig.Legs[side];
                leg.Solve(transform.TransformPoint(local), transform.TransformDirection(new Vector3(sign * 0.15f, 0f, 1f)));
                leg.End.rotation = transform.rotation;
            }
        }

        private void PoseWeapon()
        {
            bool pistol = _heldModel.Hold == HoldStyle.Pistol;
            Carry ready = pistol ? PistolReady : LongReady;
            Carry aimed = pistol ? PistolAim : LongAim;
            Carry lowered = pistol ? PistolLowered : LongLowered;
            Carry sprint = pistol ? PistolSprint : LongSprint;

            Vector3 pivot = _rig.Neck.position;
            Quaternion body = transform.rotation;
            Quaternion look = body * Quaternion.Euler(-Mathf.Clamp(_pitch, -80f, 80f), 0f, 0f);
            Quaternion half = body * Quaternion.Euler(-Mathf.Clamp(_pitch, -80f, 80f) * 0.5f, 0f, 0f);

            Vector3 position = pivot + look * Vector3.Lerp(ready.Position, aimed.Position, _aim);
            Quaternion rotation = look * Quaternion.Slerp(ready.Rotation, aimed.Rotation, _aim);
            position = Vector3.Lerp(position, pivot + half * lowered.Position, _lowered);
            rotation = Quaternion.Slerp(rotation, half * lowered.Rotation, _lowered);
            position = Vector3.Lerp(position, pivot + body * sprint.Position, _sprint);
            rotation = Quaternion.Slerp(rotation, body * sprint.Rotation, _sprint);

            _held.rotation = rotation;
            _held.position = position - rotation * _heldModel.Grip.localPosition;

            LimbChain right = _rig.Arms[1];
            right.Solve(_heldModel.Grip.position, transform.TransformDirection(new Vector3(0.7f, -1f, -0.5f)));
            right.End.rotation = rotation;
            _rig.Gloves[1].localPosition = Vector3.zero;

            LimbChain left = _rig.Arms[0];
            left.Solve(_heldModel.Support.position, transform.TransformDirection(new Vector3(pistol ? -0.6f : -0.9f, -1f, -0.3f)));
            left.End.rotation = rotation;
            _rig.Gloves[0].localPosition = Vector3.zero;
        }

        private void PoseFreeArms(float moveBlend, float swing)
        {
            for (int side = 0; side < 2; side++)
            {
                float forward = side == 0 ? swing : -swing;
                LimbChain arm = _rig.Arms[side];
                arm.Upper.localRotation = Quaternion.Euler(-forward * 25f, 0f, side == 0 ? -8f : 8f);
                arm.Lower.localRotation = Quaternion.Euler(-12f - Mathf.Max(0f, forward) * 20f * moveBlend, 0f, 0f);
                arm.End.localRotation = Quaternion.identity;
                _rig.Gloves[side].localPosition = new Vector3(0f, -0.045f, 0f);
            }
        }
    }
}
