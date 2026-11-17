using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Weapons;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Audio
{
    /// <summary>
    /// Client-side 3D sound for a character (player or bot): gunshots (by caliber class, heard far away), footsteps
    /// (louder when sprinting, near silent when crouched), knife swings and hits, reloads and being hurt.
    /// Everything is presentation only, driven by events and movement that already replicate.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAudio : MonoBehaviour
    {
        [Tooltip("Metres walked per footstep.")]
        [SerializeField, Min(0.3f)] private float _stepLength = 1.7f;

        [Tooltip("How far footsteps carry at walking speed (metres).")]
        [SerializeField, Min(1f)] private float _footstepRange = 22f;

        [SerializeField, Range(0f, 1f)] private float _footstepVolume = 0.45f;
        [SerializeField, Range(0f, 1f)] private float _gunVolume = 0.9f;

        private AudioSource _gunSource;
        private AudioSource _bodySource;
        private IShotSource _shots;
        private MeleeAttack _melee;
        private WeaponReloader _reloader;
        private Health _health;
        private PlayerMotor _motor;

        private Vector3 _lastPosition;
        private float _distanceSinceStep;
        private int _stepVariant;
        private int _lastShotFrame = -1;

        private void Awake()
        {
            _gunSource = CreateSource("Gun Audio", 2f, 220f);
            _bodySource = CreateSource("Body Audio", 1f, _footstepRange);
            _shots = GetComponent<IShotSource>();
            _melee = GetComponent<MeleeAttack>();
            _reloader = GetComponent<WeaponReloader>();
            _health = GetComponent<Health>();
            _motor = GetComponent<PlayerMotor>();
            _lastPosition = transform.position;
        }

        private void OnEnable()
        {
            if (_shots != null) _shots.ShotConfirmed += OnShot;
            if (_melee != null) _melee.Swung += OnSwung;
            if (_reloader != null) _reloader.ReloadStateChanged += OnReloadChanged;
            if (_health != null) _health.Changed += OnHealthChanged;
        }

        private void OnDisable()
        {
            if (_shots != null) _shots.ShotConfirmed -= OnShot;
            if (_melee != null) _melee.Swung -= OnSwung;
            if (_reloader != null) _reloader.ReloadStateChanged -= OnReloadChanged;
            if (_health != null) _health.Changed -= OnHealthChanged;
        }

        private void Update()
        {
            Vector3 position = transform.position;
            Vector3 delta = position - _lastPosition;
            _lastPosition = position;
            if (Time.deltaTime <= 0f || (_health != null && !_health.IsAlive)) return;

            float vertical = Mathf.Abs(delta.y) / Time.deltaTime;
            delta.y = 0f;
            float moved = delta.magnitude;
            if (moved > 5f || vertical > 2f) return; // teleport, or in the air

            _distanceSinceStep += moved;
            float stride = _motor != null && _motor.IsSprinting ? _stepLength * 1.35f : _stepLength;
            if (_distanceSinceStep < stride) return;

            _distanceSinceStep = 0f;
            float loudness = _motor != null && _motor.IsCrouched ? 0.25f : _motor != null && _motor.IsSprinting ? 1.4f : 1f;
            _bodySource.maxDistance = _footstepRange * Mathf.Max(0.4f, loudness);
            _bodySource.pitch = Random.Range(0.92f, 1.08f);
            _bodySource.PlayOneShot(ProceduralAudio.Footstep(_stepVariant), _footstepVolume * Mathf.Min(1f, loudness));
            _stepVariant = (_stepVariant + 1) % 4;
        }

        private void OnShot(Vector3 origin, Vector3 end)
        {
            // Shotguns report once per shell, not once per pellet.
            if (Time.frameCount == _lastShotFrame) return;
            _lastShotFrame = Time.frameCount;

            GunSound sound = ProceduralAudio.ClassOf(_shots.FiringWeapon);
            float muffle = Mathf.Clamp(_shots.FiringSoundMultiplier, 0.1f, 2f);
            _gunSource.maxDistance = ProceduralAudio.GunRange(sound) * muffle;
            // Suppressed shots are quieter and a little higher pitched.
            _gunSource.pitch = Random.Range(0.95f, 1.05f) * (muffle < 1f ? 1.25f : 1f);
            _gunSource.PlayOneShot(ProceduralAudio.Gunshot(sound), _gunVolume * Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(muffle)));
        }

        private void OnSwung(Vector3 origin, Vector3 direction, bool hit)
        {
            _bodySource.maxDistance = 15f;
            _bodySource.pitch = 1f;
            _bodySource.PlayOneShot(hit ? ProceduralAudio.KnifeHit() : ProceduralAudio.KnifeSwing(), 0.7f);
        }

        private void OnReloadChanged()
        {
            if (_reloader == null || !_reloader.IsReloading) return;
            _bodySource.maxDistance = 18f;
            _bodySource.pitch = 1f;
            _bodySource.PlayOneShot(ProceduralAudio.ReloadClick(), 0.6f);
        }

        private void OnHealthChanged(int previous, int current)
        {
            if (current < previous && current > 0) _bodySource.PlayOneShot(ProceduralAudio.Hurt(), 0.5f);
        }

        private AudioSource CreateSource(string name, float minDistance, float maxDistance)
        {
            var child = new GameObject(name);
            child.transform.SetParent(transform, false);
            child.transform.localPosition = new Vector3(0f, 1.4f, 0f);
            AudioSource source = child.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.dopplerLevel = 0f;
            return source;
        }
    }
}
