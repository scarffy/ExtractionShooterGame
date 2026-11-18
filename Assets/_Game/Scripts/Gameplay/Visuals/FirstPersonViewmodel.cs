using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Visuals;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;
using UnityEngine.Rendering;
using static ExtractionShooter.Gameplay.Visuals.PrimitiveParts;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// The owner's first-person arms and drawn weapon, parented to the main camera (so they follow the view with no lag).
    /// <list type="bullet">
    /// <item>Hip: weapon low and to the right. Aiming down sights: the weapon moves so its line of sight (iron sights or
    /// the fitted optic's reticle) runs through the centre of the screen. Fully aimed through a scope, the viewmodel hides
    /// and the HUD's scope overlay takes over.</item>
    /// <item>Sway against look movement, a walking bob (both subtle while aiming), a kick back on every shot, lowered
    /// while sprinting, reloading or treating wounds, raised after switching weapons.</item>
    /// <item>Muzzle flash on every shot fired. Near walls the weapon pulls back instead of poking through them.</item>
    /// <item>Healing: the weapon is lowered and the left hand brings the medical item up in front of you
    /// (<see cref="UseItemModel"/>, moved by <see cref="UseItemMotion"/>).</item>
    /// </list>
    /// Owner only, presentation only: it reads the networked weapon, aim and stance state and never changes it.
    /// No shadows, so the view model never darkens the world or itself.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerCharacter))]
    public sealed class FirstPersonViewmodel : MonoBehaviour
    {
        private const int WallHitBufferSize = 8;

        [SerializeField] private VisualProfile _profile;

        [Header("Placement (camera space, metres)")]
        [Tooltip("Where the firing hand holds a long gun at the hip.")]
        [SerializeField] private Vector3 _hipGrip = new Vector3(0.14f, -0.2f, 0.36f);

        [Tooltip("Where the firing hand holds a pistol at the hip.")]
        [SerializeField] private Vector3 _pistolHipGrip = new Vector3(0.13f, -0.17f, 0.4f);

        [Tooltip("Shoulders sit a little forward and inward of real ones, so the hands reach the weapon without the upper arms filling the view.")]
        [SerializeField] private Vector3 _leftShoulder = new Vector3(-0.12f, -0.28f, 0f);
        [SerializeField] private Vector3 _rightShoulder = new Vector3(0.2f, -0.3f, -0.12f);
        [SerializeField, Min(0.1f)] private float _upperArmLength = 0.34f;
        [SerializeField, Min(0.1f)] private float _forearmLength = 0.32f;

        [Tooltip("Camera near clip plane while the viewmodel is shown (restored afterwards).")]
        [SerializeField, Range(0.005f, 0.1f)] private float _nearClip = 0.01f;

        [Header("Carries")]
        [SerializeField] private Vector3 _sprintOffset = new Vector3(-0.04f, -0.06f, -0.05f);
        [SerializeField] private Vector3 _sprintEuler = new Vector3(18f, -38f, 14f);
        [SerializeField] private Vector3 _loweredOffset = new Vector3(0f, -0.07f, -0.04f);
        [SerializeField] private Vector3 _loweredEuler = new Vector3(28f, -12f, 8f);

        [Tooltip("Where the left hand holds a medical item while healing (camera space, metres).")]
        [SerializeField] private Vector3 _useItemRest = new Vector3(-0.08f, -0.17f, 0.42f);

        [Tooltip("Seconds to bring a weapon up after switching.")]
        [SerializeField, Min(0.01f)] private float _raiseSeconds = 0.3f;

        [SerializeField, Min(0f)] private float _carrySmoothTime = 0.1f;

        [Header("Sway and bob")]
        [Tooltip("Degrees of sway per degree/second of turning.")]
        [SerializeField, Range(0f, 0.1f)] private float _swayPerDegreePerSecond = 0.015f;
        [SerializeField, Range(0f, 15f)] private float _swayMax = 4f;
        [SerializeField, Min(0f)] private float _swaySmoothTime = 0.07f;

        [Tooltip("Bob size (metres) at walking speed.")]
        [SerializeField, Range(0f, 0.05f)] private float _bobAmount = 0.01f;

        [Tooltip("Metres moved per bob step.")]
        [SerializeField, Min(0.1f)] private float _bobStride = 0.75f;

        [Tooltip("Sway and bob scale when fully aimed.")]
        [SerializeField, Range(0f, 1f)] private float _aimedMotionScale = 0.15f;

        [Header("Shot kick")]
        [Tooltip("Metres the weapon kicks back per unit of kick.")]
        [SerializeField, Range(0f, 0.1f)] private float _kickBack = 0.022f;

        [Tooltip("Degrees the muzzle climbs per unit of kick.")]
        [SerializeField, Range(0f, 15f)] private float _kickPitch = 3f;

        [SerializeField, Min(0.5f)] private float _kickFrequency = 7f;
        [SerializeField, Range(0.1f, 1.5f)] private float _kickDamping = 0.6f;
        [SerializeField, Min(0.01f)] private float _muzzleFlashSeconds = 0.05f;

        [Header("Walls")]
        [Tooltip("How far ahead of the eye walls push the weapon back.")]
        [SerializeField, Min(0.1f)] private float _wallReach = 0.7f;

        [SerializeField, Range(0f, 0.5f)] private float _wallRetract = 0.22f;
        [SerializeField] private LayerMask _wallMask = Physics.DefaultRaycastLayers;

        private readonly RaycastHit[] _wallHits = new RaycastHit[WallHitBufferSize];
        private readonly LimbChain[] _arms = new LimbChain[2];
        private readonly Transform[] _gloves = new Transform[2];

        private PlayerCharacter _character;
        private WeaponAim _aim;
        private HitscanWeapon _weapon;
        private Camera _camera;
        private float _cameraNearClip;

        private Transform _root;
        private Transform _holder;
        private WeaponModel _model;
        private WeaponDefinition _modelWeapon;
        private string _modelKey;
        private float _raisedAt;

        private float _sprint, _sprintVelocity;
        private float _lowered, _loweredVelocity;
        private float _use, _useVelocity;
        private Transform _useItem;
        private UseItemVisual _useItemKind;
        private float _swayYaw, _swayYawVelocity, _swayPitch, _swayPitchVelocity;
        private float _kick, _kickVelocity;
        private float _wall, _wallVelocity;
        private float _phase;
        private Vector3 _lastPosition;
        private Quaternion _lastView;
        private bool _hasLastView;

        private void Awake()
        {
            _character = GetComponent<PlayerCharacter>();
            _aim = GetComponent<WeaponAim>();
            _weapon = GetComponent<HitscanWeapon>();
        }

        private void OnEnable()
        {
            PlayerCharacter.LocalPlayerChanged += OnLocalPlayerChanged;
            if (_weapon != null) _weapon.OwnerShotFired += OnOwnerShotFired;
            OnLocalPlayerChanged(PlayerCharacter.Local);
        }

        private void OnDisable()
        {
            PlayerCharacter.LocalPlayerChanged -= OnLocalPlayerChanged;
            if (_weapon != null) _weapon.OwnerShotFired -= OnOwnerShotFired;
            Teardown();
        }

        private void OnLocalPlayerChanged(PlayerCharacter local)
        {
            if (local != null && local == _character) Setup();
            else Teardown();
        }

        private void Setup()
        {
            if (_root != null) return;
            _camera = Camera.main;
            if (_camera == null || _profile == null)
            {
                if (_profile == null) Debug.LogError($"[{nameof(FirstPersonViewmodel)}] Visual profile is not assigned on {name}.", this);
                return;
            }

            _cameraNearClip = _camera.nearClipPlane;
            _camera.nearClipPlane = Mathf.Min(_cameraNearClip, _nearClip);

            _root = Node(_camera.transform, "Viewmodel", Vector3.zero);
            _holder = Node(_root, "Weapon", Vector3.zero);
            HumanoidRig.BuildArms(_root, _profile, _leftShoulder, _rightShoulder, _upperArmLength, _forearmLength, _arms, _gloves);
            ConfigureRenderers();
            _lastPosition = transform.position;
            _hasLastView = false;
            _root.gameObject.SetActive(false);
        }

        private void Teardown()
        {
            if (_camera != null && _root != null) _camera.nearClipPlane = _cameraNearClip;
            if (_root != null) Destroy(_root.gameObject);
            _root = null;
            _holder = null;
            _model = null;
            _modelWeapon = null;
            _modelKey = null;
            _useItem = null;
            _useItemKind = UseItemVisual.None;
            _camera = null;
        }

        private void OnOwnerShotFired()
        {
            if (_model == null || !_root.gameObject.activeSelf) return;
            _model.Flash(_muzzleFlashSeconds);
            float vertical = _modelWeapon != null ? _modelWeapon.Recoil.VerticalKick : 1f;
            _kick = Mathf.Min(_kick + 0.35f + 0.3f * vertical, 1.5f);
            _kickVelocity += 1.5f;
        }

        private void LateUpdate()
        {
            // The camera may not have existed when this player became the local one.
            if (_root == null && _profile != null && PlayerCharacter.Local == _character) Setup();
            if (_root == null) return;

            PlayerEquipment equipment = _character.Equipment;
            bool hasWeapon = equipment != null &&
                             equipment.TryGetActiveWeapon(out EquipmentSlot slot, out WeaponDefinition weapon, out ItemStack item) &&
                             RefreshModel(slot, weapon, item, equipment.Container != null ? equipment.Container.ItemDatabase : null);
            float aim = _aim != null ? _aim.Aim01 : 0f;
            bool scopedIn = hasWeapon && equipment.TryGetActiveWeaponStats(out WeaponStats stats) && stats.Scoped && aim >= 0.95f;
            bool visible = hasWeapon && _model != null && _character.Health.IsAlive && !scopedIn;
            if (_root.gameObject.activeSelf != visible) _root.gameObject.SetActive(visible);
            if (!visible)
            {
                _hasLastView = false;
                _lastPosition = transform.position;
                return;
            }

            Animate(Time.deltaTime, aim);
        }

        /// <summary>Rebuilds the weapon when the drawn weapon or its attachments change. False if none could be built.</summary>
        private bool RefreshModel(EquipmentSlot slot, WeaponDefinition weapon, in ItemStack item, ItemDatabase database)
        {
            string key = (int)slot + ":" + item.ItemId + ":" + item.Attachments;
            if (key == _modelKey) return _model != null;

            _modelKey = key;
            if (_model != null) Destroy(_model.gameObject);

            var mods = new System.Collections.Generic.List<AttachmentDefinition>(WeaponMods.SlotCount);
            if (database != null && !string.IsNullOrEmpty(item.Attachments))
                foreach (var fitted in WeaponMods.All(item.Attachments))
                    if (database.TryGet(fitted.Value, out ItemDefinition found) && found is AttachmentDefinition mod) mods.Add(mod);

            _model = WeaponModelBuilder.Build(_holder, weapon, mods, _profile);
            _modelWeapon = weapon;
            _raisedAt = Time.time;
            _kick = _kickVelocity = 0f;
            ConfigureRenderers();
            return _model != null;
        }

        private void ConfigureRenderers()
        {
            foreach (Renderer part in _root.GetComponentsInChildren<Renderer>(true))
            {
                part.shadowCastingMode = ShadowCastingMode.Off;
                part.receiveShadows = false;
            }
        }

        private void Animate(float deltaTime, float aim)
        {
            PlayerMotor motor = _character.Motor;
            bool sprinting = motor != null && motor.IsSprinting;
            bool busy = (_character.Reloader != null && _character.Reloader.IsReloading) ||
                        (_character.Injuries != null && _character.Injuries.IsTreating);
            _sprint = ViewmodelMotion.SmoothDamp(_sprint, sprinting ? 1f : 0f, ref _sprintVelocity, _carrySmoothTime * 1.5f, deltaTime);
            _lowered = ViewmodelMotion.SmoothDamp(_lowered, busy ? 1f : 0f, ref _loweredVelocity, _carrySmoothTime * 1.5f, deltaTime);
            float motionScale = Mathf.Lerp(1f, _aimedMotionScale, aim);

            // Sway: lag behind the view's turn rate.
            Quaternion view = _camera.transform.rotation;
            float yawRate = 0f, pitchRate = 0f;
            if (_hasLastView && deltaTime > 0.0001f)
            {
                Vector3 now = view.eulerAngles, before = _lastView.eulerAngles;
                yawRate = ViewmodelMotion.DeltaAngle(before.y, now.y) / deltaTime;
                pitchRate = ViewmodelMotion.DeltaAngle(before.x, now.x) / deltaTime;
            }
            _lastView = view;
            _hasLastView = true;
            float swayMax = _swayMax * motionScale;
            _swayYaw = ViewmodelMotion.SmoothDamp(_swayYaw, ViewmodelMotion.Sway(yawRate, _swayPerDegreePerSecond, swayMax),
                ref _swayYawVelocity, _swaySmoothTime, deltaTime);
            _swayPitch = ViewmodelMotion.SmoothDamp(_swayPitch, ViewmodelMotion.Sway(pitchRate, _swayPerDegreePerSecond, swayMax),
                ref _swayPitchVelocity, _swaySmoothTime, deltaTime);

            // Bob: by distance actually moved.
            Vector3 position = transform.position;
            Vector3 moved = position - _lastPosition;
            _lastPosition = position;
            moved.y = 0f;
            float distance = moved.magnitude;
            if (distance > 2f) distance = 0f; // teleport
            float speed = deltaTime > 0.0001f ? distance / deltaTime : 0f;
            _phase = GaitCycle.Advance(_phase, distance, _bobStride);
            float bob = _bobAmount * Mathf.Clamp01(speed / 4.5f) * motionScale * (1f + 0.8f * _sprint);
            ViewmodelMotion.Bob(_phase, bob, out float bobX, out float bobY);

            _kick = ViewmodelMotion.Spring(_kick, 0f, ref _kickVelocity, _kickFrequency, _kickDamping, deltaTime);
            _wall = ViewmodelMotion.SmoothDamp(_wall, WallProximity(), ref _wallVelocity, 0.06f, deltaTime);

            // Hip and aimed placements of the weapon root (weapon axes = camera axes).
            bool pistol = _model.Hold == HoldStyle.Pistol;
            Vector3 hip = (pistol ? _pistolHipGrip : _hipGrip) - _model.Grip.localPosition;
            Vector3 aimed = new Vector3(0f, 0f, _model.EyeRelief) - _model.Sight.localPosition;
            float aimEase = aim * aim * (3f - 2f * aim);
            Vector3 offset = Vector3.Lerp(hip, aimed, aimEase);
            Quaternion rotation = Quaternion.Slerp(Quaternion.Euler(0f, -1.5f, 0f), Quaternion.identity, aimEase);

            offset += _loweredOffset * _lowered + _sprintOffset * _sprint;
            rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(_loweredEuler.x, _loweredEuler.y, _loweredEuler.z), _lowered) * rotation;
            rotation = Quaternion.Slerp(Quaternion.identity, Quaternion.Euler(_sprintEuler.x, _sprintEuler.y, _sprintEuler.z), _sprint) * rotation;

            float raise = 1f - Mathf.Clamp01((Time.time - _raisedAt) / _raiseSeconds);
            raise *= raise;
            offset += new Vector3(0f, -0.16f, -0.05f) * raise;
            rotation = Quaternion.Euler(35f * raise, 0f, 0f) * rotation;

            offset += new Vector3(0f, -0.02f * _wall, -_wallRetract * _wall);
            rotation = Quaternion.Euler(-20f * _wall, 8f * _wall, 0f) * rotation;

            offset += new Vector3(bobX + _swayYaw * 0.004f, bobY - _swayPitch * 0.003f, -_kickBack * _kick);
            rotation = Quaternion.Euler(-_kickPitch * _kick + _swayPitch, _swayYaw, -_swayYaw * 0.6f) * rotation;

            _holder.localRotation = rotation;
            _holder.localPosition = offset;

            // Hands on the weapon.
            Quaternion grip = _model.Grip.rotation;
            _arms[1].Solve(_model.Grip.position, _root.TransformDirection(new Vector3(0.5f, -1f, -0.4f)));
            _arms[1].End.rotation = grip;
            _arms[0].Solve(_model.Support.position, _root.TransformDirection(new Vector3(pistol ? -0.5f : -0.8f, -1f, -0.3f)));
            _arms[0].End.rotation = grip;

            AnimateUseItem(deltaTime);
        }

        /// <summary>Healing: the left hand lets go of the weapon and holds the medical item up.</summary>
        private void AnimateUseItem(float deltaTime)
        {
            PlayerInjuries injuries = _character.Injuries;
            UseItemVisual kind = injuries != null ? injuries.UseVisual : UseItemVisual.None;
            if (kind != UseItemVisual.None && kind != _useItemKind)
            {
                if (_useItem != null) Destroy(_useItem.gameObject);
                _useItemKind = kind;
                _useItem = UseItemModel.Build(_root, kind, _profile);
                ConfigureRenderers();
            }
            _use = ViewmodelMotion.SmoothDamp(_use, kind != UseItemVisual.None ? 1f : 0f, ref _useVelocity, _carrySmoothTime, deltaTime);
            if (_useItem == null) return;

            bool shown = _use > 0.02f;
            if (_useItem.gameObject.activeSelf != shown) _useItem.gameObject.SetActive(shown);
            if (!shown) return;

            float seconds = kind != UseItemVisual.None ? Time.time - injuries.UseStartedAt : UseItemMotion.RaiseSeconds;
            UsePose pose = UseItemMotion.Evaluate(_useItemKind, seconds, injuries != null ? injuries.TreatmentProgress : 1f);
            // Coming in and going away: slide up from below the view.
            Vector3 rest = _root.TransformPoint(_useItemRest + new Vector3(0f, -0.18f * (1f - _use), 0f));
            UseItemModel.Pose(_useItem, rest, _root.rotation, pose);

            Vector3 hand = Vector3.Lerp(_model.Support.position, _useItem.position, _use);
            _arms[0].Solve(hand, _root.TransformDirection(new Vector3(-0.8f, -1f, -0.3f)));
            _arms[0].End.rotation = Quaternion.Slerp(_model.Grip.rotation, _useItem.rotation, _use);
        }

        /// <summary>0 nothing ahead, 1 a wall right at the eye (within <see cref="_wallReach"/>).</summary>
        private float WallProximity()
        {
            Transform eye = _camera.transform;
            int count = Physics.RaycastNonAlloc(eye.position, eye.forward, _wallHits, _wallReach, _wallMask, QueryTriggerInteraction.Ignore);
            float nearest = _wallReach;
            for (int i = 0; i < count; i++)
            {
                if (_wallHits[i].collider.transform.IsChildOf(transform)) continue;
                if (_wallHits[i].distance < nearest) nearest = _wallHits[i].distance;
            }
            return Mathf.Clamp01((_wallReach - nearest) / _wallReach);
        }
    }
}
