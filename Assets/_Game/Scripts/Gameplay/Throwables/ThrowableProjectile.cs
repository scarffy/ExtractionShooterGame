using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Throwables;
using ExtractionShooter.Gameplay.AI;
using ExtractionShooter.Gameplay.Audio;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Player;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Throwables
{
    /// <summary>
    /// A thrown grenade. The server simulates the physics (clients follow through the NetworkTransform with a
    /// kinematic body) and, when the fuse runs out, applies the effect:
    /// <list type="bullet">
    /// <item>Frag: damage with distance falloff to every player and bot it can see (cover blocks it), split between
    /// body (armor applies) and legs, so it can cause bleeding and fractures. Loud.</item>
    /// <item>Smoke: a cloud registered in <see cref="SmokeVolumes"/> for its lifetime; bots cannot see through it.</item>
    /// <item>Flash: players in range with a line of sight get a white-out on their screen (each owner works out how
    /// hard from where it is looking); bots looking at it are blinded for a few seconds. Loud.</item>
    /// </list>
    /// The blast replicates as state (where and that it went off), so every client, including ones that start
    /// observing late, plays the effect. The object lingers briefly (smoke: for the cloud's life) and despawns.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ThrowableProjectile : NetworkBehaviour
    {
        private const int HitBufferSize = 16;
        private const float SmokeHeight = 1f;
        private const float KillHeight = -50f;

        private static readonly Vector3[] PuffOffsets =
        {
            new Vector3(0f, 0f, 0f), new Vector3(0.45f, -0.1f, 0f), new Vector3(-0.45f, -0.1f, 0f), new Vector3(0f, -0.1f, 0.45f),
            new Vector3(0f, -0.1f, -0.45f), new Vector3(0.3f, 0.3f, 0.3f), new Vector3(-0.3f, 0.3f, -0.3f),
        };

        private static readonly float[] TargetHeights = { 0.3f, 1.2f, 1.65f };

        [Tooltip("Resolves the grenade's definition from its replicated id on clients.")]
        [SerializeField] private ItemDatabase _itemDatabase;

        [Tooltip("The grenade itself; hidden when it goes off.")]
        [SerializeField] private Renderer _body;

        [Tooltip("Sphere shown briefly when a frag or flash goes off.")]
        [SerializeField] private Renderer _blast;

        [Tooltip("Spheres that make up the smoke cloud.")]
        [SerializeField] private Renderer[] _smokePuffs;

        [SerializeField] private Color _fragBlastColor = new Color(1f, 0.6f, 0.2f, 0.85f);
        [SerializeField] private Color _flashBlastColor = new Color(1f, 1f, 0.95f, 0.95f);
        [SerializeField] private Color _smokeColor = new Color(0.78f, 0.78f, 0.76f, 0.8f);

        [Tooltip("Seconds the blast sphere stays visible.")]
        [SerializeField, Min(0.05f)] private float _blastSeconds = 0.25f;

        [Tooltip("Blast sphere diameter at its largest (metres).")]
        [SerializeField, Min(0.1f)] private float _blastSize = 3f;

        [Tooltip("Seconds a frag or flash stays spawned after going off, so every client gets the blast.")]
        [SerializeField, Min(0.1f)] private float _lingerSeconds = 2f;

        [Tooltip("Layers that block blasts and flashes (cover). Characters never block.")]
        [SerializeField] private LayerMask _sightMask = ~(1 << 2);

        private readonly SyncVar<string> _itemId = new SyncVar<string>();
        // Declared before _detonated: SyncTypes are read in order, so the point is known when the blast callback runs.
        private readonly SyncVar<Vector3> _blastPoint = new SyncVar<Vector3>();
        private readonly SyncVar<bool> _detonated = new SyncVar<bool>();

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private Rigidbody _rigidbody;
        private ThrowableDefinition _definition;
        private int _instigator = DamageInfo.NoInstigator;
        private Vector3 _launchVelocity;
        private double _armedAt;
        private double _despawnAt = double.MaxValue;
        private int _smokeId = -1;

        // Client presentation
        private float _effectStartedAt = -1f;
        private Vector3 _effectCenter;
        private Material _blastMaterial;
        private Material _smokeMaterial;
        private AudioSource _audio;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _detonated.OnChange += OnDetonatedChanged;
            SetRendererEnabled(_blast, false);
            if (_smokePuffs != null)
                foreach (Renderer puff in _smokePuffs) SetRendererEnabled(puff, false);

            if (_itemDatabase == null)
                Debug.LogError($"[{nameof(ThrowableProjectile)}] Item database is not assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _detonated.OnChange -= OnDetonatedChanged;
            if (_blastMaterial != null) Destroy(_blastMaterial);
            if (_smokeMaterial != null) Destroy(_smokeMaterial);
        }

        /// <summary>Server, before spawning: what was thrown, by whom (client id) and how fast.</summary>
        public void ServerPrepare(ThrowableDefinition definition, int instigatorClientId, Vector3 velocity)
        {
            _definition = definition;
            _instigator = instigatorClientId;
            _launchVelocity = velocity;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            if (_definition == null)
            {
                Debug.LogError($"[{nameof(ThrowableProjectile)}] Spawned without {nameof(ServerPrepare)}.", this);
                Despawn();
                return;
            }

            // Set here (not before spawning) so the values go out with the spawn message.
            _itemId.Value = _definition.Id;
            _armedAt = Time.timeAsDouble;
            _rigidbody.isKinematic = false;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.linearVelocity = _launchVelocity;
            _rigidbody.angularVelocity = Random.insideUnitSphere * 10f;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            if (_smokeId >= 0) SmokeVolumes.Remove(_smokeId);
            _smokeId = -1;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            // Only the server simulates; clients follow the NetworkTransform.
            if (!IsServerInitialized) _rigidbody.isKinematic = true;
            // Already went off before this client started observing it.
            if (_detonated.Value) OnDetonatedChanged(false, true, false);
        }

        private void Update()
        {
            if (IsServerInitialized) ServerTick();
            if (IsClientInitialized && _effectStartedAt >= 0f) AnimateEffect(Time.time - _effectStartedAt);
        }

        // ---------------- Server ----------------

        private void ServerTick()
        {
            if (_definition == null) return;
            double now = Time.timeAsDouble;

            if (!_detonated.Value && ThrowableMath.FuseExpired(now - _armedAt, _definition.FuseSeconds)) ServerDetonate(now);
            if (now >= _despawnAt || transform.position.y < KillHeight)
            {
                _despawnAt = double.MaxValue;
                Despawn();
            }
        }

        private void ServerDetonate(double now)
        {
            Vector3 point = transform.position;
            // Stays where it went off (kinematic bodies only support speculative continuous collision).
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _rigidbody.isKinematic = true;
            _blastPoint.Value = point;
            _detonated.Value = true;

            switch (_definition.Kind)
            {
                case ThrowableKind.Frag:
                    ServerFragBlast(point);
                    _despawnAt = now + _lingerSeconds;
                    break;
                case ThrowableKind.Flash:
                    ServerFlashBlast(point);
                    _despawnAt = now + _lingerSeconds;
                    break;
                case ThrowableKind.Smoke:
                    _smokeId = ObjectId;
                    SmokeVolumes.Add(_smokeId, point + Vector3.up * SmokeHeight, _definition.Radius, now, _definition.SmokeSeconds,
                        _definition.SmokeGrowSeconds, _definition.SmokeFadeSeconds);
                    _despawnAt = now + _definition.SmokeSeconds;
                    break;
            }

            if (_definition.NoiseRange > 0f) ShotNoise.ServerReport(point, _definition.NoiseRange, _instigator);
        }

        /// <summary>Damage to every living player and bot within the radius that the blast can reach.</summary>
        private void ServerFragBlast(Vector3 point)
        {
            foreach (PlayerCharacter player in PlayerCharacter.ServerActive)
            {
                if (player != null && player.Health.IsAlive) ServerDamage(player.Health, player.transform, point);
            }
            foreach (BotBrain bot in BotBrain.ServerActive)
            {
                if (bot != null && bot.IsAlive) ServerDamage(bot.GetComponent<Health>(), bot.transform, point);
            }
        }

        private void ServerDamage(Health health, Transform target, Vector3 point)
        {
            if (health == null || !TryReach(point, target, out float distance)) return;

            int damage = ThrowableMath.BlastDamage(_definition.Damage, distance, _definition.InnerRadius, _definition.Radius);
            if (damage <= 0) return;

            int legs = Mathf.RoundToInt(damage * _definition.LegShare);
            int body = damage - legs;
            Vector3 chest = target.position + Vector3.up * TargetHeights[1];
            Vector3 direction = chest - point;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.up;

            if (body > 0)
                health.ApplyDamage(new DamageInfo(body, _instigator, chest, direction, HitZone.Body,
                    _definition.Penetration, _definition.ArmorDamagePercent));
            if (legs > 0 && health.IsAlive)
                health.ApplyDamage(new DamageInfo(legs, _instigator, target.position + Vector3.up * TargetHeights[0], direction, HitZone.Legs,
                    _definition.Penetration, _definition.ArmorDamagePercent));
        }

        /// <summary>Players in sight get a white-out (worked out on their own client); bots looking at it are blinded.</summary>
        private void ServerFlashBlast(Vector3 point)
        {
            Vector3 origin = point + Vector3.up * 0.15f;
            foreach (PlayerCharacter player in PlayerCharacter.ServerActive)
            {
                if (player == null || !player.Health.IsAlive || player.Throwables == null) continue;
                Vector3 eye = player.Look.EyePosition;
                if (Vector3.Distance(origin, eye) >= _definition.Radius || !IsVisible(origin, eye, player.transform)) continue;
                player.Throwables.ServerFlash(point, _definition);
            }

            foreach (BotBrain bot in BotBrain.ServerActive)
            {
                if (bot == null || !bot.IsAlive) continue;
                Vector3 eye = bot.transform.position + Vector3.up * TargetHeights[2];
                Vector3 toFlash = origin - eye;
                float distance = toFlash.magnitude;
                if (distance >= _definition.Radius || !IsVisible(origin, eye, bot.transform)) continue;

                float seconds = _definition.FlashSeconds(_definition.FlashIntensity(distance, Vector3.Angle(bot.transform.forward, toFlash)));
                if (seconds > 0f) bot.ServerBlind(seconds);
            }
        }

        /// <summary>
        /// True when the blast reaches any of the target's feet, chest or head; <paramref name="distance"/> is to the
        /// nearest of those points.
        /// </summary>
        private bool TryReach(Vector3 point, Transform target, out float distance)
        {
            Vector3 origin = point + Vector3.up * 0.15f;
            distance = float.MaxValue;
            bool reached = false;
            foreach (float height in TargetHeights)
            {
                Vector3 aim = target.position + Vector3.up * height;
                float d = Vector3.Distance(origin, aim);
                if (d >= _definition.Radius || !IsVisible(origin, aim, target)) continue;
                reached = true;
                if (d < distance) distance = d;
            }
            return reached;
        }

        /// <summary>Nothing solid lies between the two points (the target itself and other characters do not count).</summary>
        private bool IsVisible(Vector3 from, Vector3 to, Transform target)
        {
            Vector3 direction = to - from;
            float length = direction.magnitude;
            if (length < 0.01f) return true;

            int count = Physics.RaycastNonAlloc(from, direction / length, _hitBuffer, length, _sightMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider collider = _hitBuffer[i].collider;
                if (collider == null) continue;
                Transform hit = collider.transform;
                if (hit.IsChildOf(transform) || hit.IsChildOf(target)) continue;
                if (collider.GetComponentInParent<PlayerCharacter>() != null || collider.GetComponentInParent<BotBrain>() != null) continue;
                return false;
            }
            return true;
        }

        // ---------------- Client presentation ----------------

        private void OnDetonatedChanged(bool previous, bool current, bool asServer)
        {
            if (asServer || !current || _effectStartedAt >= 0f) return;

            ThrowableDefinition definition = ResolveDefinition();
            if (definition == null) return;

            Vector3 point = _blastPoint.Value;
            if (point.sqrMagnitude < 1e-6f) point = transform.position;
            _effectStartedAt = Time.time;
            SetRendererEnabled(_body, false);

            switch (definition.Kind)
            {
                case ThrowableKind.Smoke:
                    _effectCenter = point + Vector3.up * SmokeHeight;
                    StartSmoke();
                    PlaySound(ProceduralAudio.SmokePop(), 30f, 0.6f);
                    break;
                case ThrowableKind.Flash:
                    _effectCenter = point;
                    StartBlast(_flashBlastColor);
                    PlaySound(ProceduralAudio.FlashBang(), 200f, 1f);
                    break;
                default:
                    _effectCenter = point;
                    StartBlast(_fragBlastColor);
                    PlaySound(ProceduralAudio.Explosion(), 250f, 1f);
                    break;
            }
        }

        private ThrowableDefinition ResolveDefinition()
        {
            if (_definition != null) return _definition;
            if (_itemDatabase != null && _itemDatabase.TryGet(_itemId.Value, out ItemDefinition found)) _definition = found as ThrowableDefinition;
            return _definition;
        }

        private void StartBlast(Color color)
        {
            if (_blast == null) return;
            _blastMaterial = new Material(_blast.sharedMaterial) { color = color };
            _blast.sharedMaterial = _blastMaterial;
            _blast.transform.position = _effectCenter;
            SetRendererEnabled(_blast, true);
        }

        private void StartSmoke()
        {
            if (_smokePuffs == null || _smokePuffs.Length == 0) return;
            Material source = _smokePuffs[0] != null ? _smokePuffs[0].sharedMaterial : null;
            if (source != null) _smokeMaterial = new Material(source) { color = _smokeColor };
            foreach (Renderer puff in _smokePuffs)
            {
                if (puff == null) continue;
                if (_smokeMaterial != null) puff.sharedMaterial = _smokeMaterial;
                puff.transform.localScale = Vector3.zero;
                SetRendererEnabled(puff, true);
            }
        }

        private void AnimateEffect(float elapsed)
        {
            ThrowableDefinition definition = _definition;
            if (definition == null) return;

            if (definition.Kind != ThrowableKind.Smoke)
            {
                if (_blast == null || !_blast.enabled) return;
                if (elapsed >= _blastSeconds)
                {
                    SetRendererEnabled(_blast, false);
                    return;
                }
                float t = elapsed / _blastSeconds;
                _blast.transform.localScale = Uniform(_blastSize * (0.4f + 0.6f * t));
                if (_blastMaterial != null)
                {
                    Color color = definition.Kind == ThrowableKind.Flash ? _flashBlastColor : _fragBlastColor;
                    color.a *= 1f - t;
                    _blastMaterial.color = color;
                }
                return;
            }

            if (_smokePuffs == null) return;
            float radius = ThrowableMath.SmokeRadius(elapsed, definition.SmokeSeconds, definition.Radius,
                definition.SmokeGrowSeconds, definition.SmokeFadeSeconds);
            for (int i = 0; i < _smokePuffs.Length; i++)
            {
                Renderer puff = _smokePuffs[i];
                if (puff == null) continue;
                Vector3 offset = PuffOffsets[i % PuffOffsets.Length];
                puff.transform.position = _effectCenter + offset * radius;
                puff.transform.localScale = Uniform(radius * 1.25f);
            }
            if (_smokeMaterial != null)
            {
                Color color = _smokeColor;
                color.a *= ThrowableMath.SmokeOpacity(elapsed, definition.SmokeSeconds, definition.SmokeFadeSeconds);
                _smokeMaterial.color = color;
            }
        }

        private void PlaySound(AudioClip clip, float range, float volume)
        {
            if (clip == null) return;
            if (_audio == null)
            {
                var child = new GameObject("Grenade Audio");
                child.transform.SetParent(transform, false);
                _audio = child.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 1f;
                _audio.rolloffMode = AudioRolloffMode.Linear;
                _audio.minDistance = 3f;
                _audio.dopplerLevel = 0f;
            }
            _audio.transform.position = _effectCenter;
            _audio.maxDistance = range;
            _audio.PlayOneShot(clip, volume);
        }

        private static Vector3 Uniform(float scale) => new Vector3(scale, scale, scale);

        private static void SetRendererEnabled(Renderer renderer, bool enabled)
        {
            if (renderer != null) renderer.enabled = enabled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => SmokeVolumes.Clear();
    }
}
