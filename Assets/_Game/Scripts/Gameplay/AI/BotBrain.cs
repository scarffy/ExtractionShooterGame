using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Weapons;
using FishNet.Object;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

namespace ExtractionShooter.Gameplay.AI
{
    /// <summary>
    /// Server-side bot: patrols the NavMesh, notices players it can see (field of view, range, line of sight)
    /// or hear (close by, or shooting it), reacts after a delay, then shoots in bursts with its weapon's
    /// magazine, fire rate and reload, using the same ballistics as players. When it loses sight it searches
    /// the last known position, then goes back to patrolling. Movement replicates through the NetworkTransform;
    /// clients only see tracers.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Health), typeof(NavMeshAgent))]
    public sealed class BotBrain : NetworkBehaviour, IShotSource
    {
        private const int HitBufferSize = 16;
        private const float FireAngle = 12f;
        private const float AwareMemorySeconds = 2f;

        private enum State { Patrol, Engage, Search }

        [SerializeField] private BotSettings _settings;

        [Tooltip("Eye point for sight checks and shots.")]
        [SerializeField] private Transform _eye;

        private static readonly List<BotBrain> ServerActiveList = new List<BotBrain>();

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];

        private NavMeshAgent _agent;
        private Health _health;
        private Vector3 _home;

        private State _state;
        private PlayerCharacter _target;
        private bool _targetVisible;
        private Vector3 _lastSeenPosition;
        private float _lastSeenTime;
        private float _searchUntil;
        private float _nextThinkAt;

        private float _canShootAt;
        private float _engagedSince;
        private float _nextShotAt;
        private float _reloadDoneAt;
        private int _burstLeft;
        private int _rounds;

        /// <summary>Client side. Raised for every confirmed projectile (origin, end point).</summary>
        public event Action<Vector3, Vector3> ShotConfirmed;

        /// <summary>Server: every spawned bot (alive or dead).</summary>
        public static IReadOnlyList<BotBrain> ServerActive => ServerActiveList;

        public BotSettings Settings => _settings;

        public bool IsAlive => _health.IsAlive;

        /// <summary>Server: rounds left in the magazine (dropped with the weapon).</summary>
        public int RoundsLoaded => _rounds;

        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _health = GetComponent<Health>();
            _agent.enabled = false; // only the server drives the agent

            if (_settings == null || _settings.Weapon == null || _settings.Ammo == null)
                Debug.LogError($"[{nameof(BotBrain)}] Settings with a weapon and ammo must be assigned on {name}.", this);
            if (_eye == null)
                Debug.LogError($"[{nameof(BotBrain)}] Eye is not assigned on {name}.", this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            ServerActiveList.Add(this);
            _health.ServerDamaged += OnServerDamaged;

            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                transform.position = hit.position;

            _home = transform.position;
            _agent.enabled = true;
            _agent.angularSpeed = _settings.TurnSpeed;
            _rounds = _settings.Weapon.MagazineSize;
            _burstLeft = Random.Range(_settings.BurstMin, _settings.BurstMax + 1);
            EnterPatrol();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            ServerActiveList.Remove(this);
            _health.ServerDamaged -= OnServerDamaged;
            if (_agent != null) _agent.enabled = false;
        }

        private void Update()
        {
            if (!IsServerInitialized || _settings == null || _eye == null) return;

            if (!_health.IsAlive)
            {
                if (_agent.enabled) _agent.enabled = false;
                return;
            }
            if (!_agent.isOnNavMesh) return;

            float now = Time.time;
            if (now >= _nextThinkAt)
            {
                _nextThinkAt = now + _settings.ThinkInterval;
                Sense(now);
            }

            switch (_state)
            {
                case State.Patrol: UpdatePatrol(); break;
                case State.Engage: UpdateEngage(now); break;
                case State.Search: UpdateSearch(now); break;
            }
        }

        // ---------------- Senses ----------------

        private void Sense(float now)
        {
            PlayerCharacter best = null;
            float bestDistance = float.MaxValue;
            Vector3 eye = _eye.position;

            foreach (PlayerCharacter player in PlayerCharacter.ServerActive)
            {
                if (player == null || !player.Health.IsAlive) continue;

                Vector3 aim = AimPoint(player, _settings.ChestHeight);
                Vector3 toTarget = aim - eye;
                float distance = toTarget.magnitude;
                if (distance > _settings.SightRange || distance < 0.01f) continue;

                bool alreadyAware = player == _target && now - _lastSeenTime < AwareMemorySeconds;
                if (!alreadyAware && IsInsertionProtected(player)) continue;
                bool inView = Vector3.Angle(transform.forward, toTarget) <= _settings.FieldOfView * 0.5f;
                if (!alreadyAware && !inView && distance > _settings.HearingRange) continue;
                if (!HasLineOfSight(player, eye, toTarget / distance, distance)) continue;

                if (distance < bestDistance)
                {
                    best = player;
                    bestDistance = distance;
                }
            }

            if (best != null)
            {
                if (best != _target || !_targetVisible)
                {
                    _canShootAt = now + _settings.ReactionSeconds;
                    _engagedSince = _canShootAt;
                }
                _target = best;
                _targetVisible = true;
                _lastSeenPosition = best.transform.position;
                _lastSeenTime = now;
                _state = State.Engage;
                return;
            }

            _targetVisible = false;
            if (_state == State.Engage && now - _lastSeenTime > _settings.LoseSightSeconds)
                EnterSearch(now);
        }

        private bool IsInsertionProtected(PlayerCharacter player) =>
            player.Raid != null && player.Raid.ServerSecondsInRaid < _settings.InsertionGraceSeconds;

        private bool HasLineOfSight(PlayerCharacter target, Vector3 origin, Vector3 direction, float distance)
        {
            int count = Physics.RaycastNonAlloc(origin, direction, _hitBuffer, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float closest = float.MaxValue;
            Transform blocker = null;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform) || hit.distance >= closest) continue;
                closest = hit.distance;
                blocker = hit.collider.transform;
            }
            return blocker == null || blocker.IsChildOf(target.transform);
        }

        private void OnServerDamaged(DamageInfo damage)
        {
            if (!_health.IsAlive || damage.InstigatorClientId == DamageInfo.NoInstigator) return;

            foreach (PlayerCharacter player in PlayerCharacter.ServerActive)
            {
                if (player == null || player.OwnerId != damage.InstigatorClientId || !player.Health.IsAlive) continue;

                // Shot by someone: turn towards them and fight (they are found by sight once facing them).
                float now = Time.time;
                if (_target != player || !_targetVisible)
                {
                    _canShootAt = Mathf.Max(_canShootAt, now + _settings.ReactionSeconds);
                    _engagedSince = _canShootAt;
                }
                _target = player;
                _lastSeenPosition = player.transform.position;
                _lastSeenTime = now;
                _state = State.Engage;
                return;
            }
        }

        // ---------------- States ----------------

        private void EnterPatrol()
        {
            _state = State.Patrol;
            _target = null;
            _targetVisible = false;
            _agent.updateRotation = true;
            _agent.speed = _settings.PatrolSpeed;
            SetRandomPatrolDestination();
        }

        private void EnterSearch(float now)
        {
            _state = State.Search;
            _searchUntil = now + _settings.SearchSeconds;
            _agent.updateRotation = true;
            _agent.isStopped = false;
            _agent.speed = _settings.ChaseSpeed;
            _agent.SetDestination(_lastSeenPosition);
        }

        private void UpdatePatrol()
        {
            if (!_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.3f)
                SetRandomPatrolDestination();
        }

        private void UpdateEngage(float now)
        {
            _agent.updateRotation = false;
            Vector3 lookAt = _targetVisible && _target != null ? _target.transform.position : _lastSeenPosition;
            TurnTowards(lookAt);

            float distance = Vector3.Distance(transform.position, _lastSeenPosition);
            bool holdPosition = _targetVisible && distance <= _settings.EngageDistance;
            _agent.isStopped = holdPosition;
            if (!holdPosition)
            {
                _agent.speed = _settings.ChaseSpeed;
                _agent.SetDestination(_lastSeenPosition);
            }

            if (_targetVisible && _target != null) TryShoot(now);
        }

        private void UpdateSearch(float now)
        {
            if (now >= _searchUntil && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance + 0.5f)
                EnterPatrol();
        }

        private void SetRandomPatrolDestination()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Vector2 offset = Random.insideUnitCircle * _settings.PatrolRadius;
                Vector3 candidate = _home + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(hit.position);
                    return;
                }
            }
        }

        private void TurnTowards(Vector3 worldPosition)
        {
            Vector3 flat = worldPosition - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;

            Quaternion goal = Quaternion.LookRotation(flat);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, goal, _settings.TurnSpeed * Time.deltaTime);
        }

        // ---------------- Shooting ----------------

        private void TryShoot(float now)
        {
            WeaponDefinition weapon = _settings.Weapon;
            if (now < _reloadDoneAt || now < _canShootAt || now < _nextShotAt) return;

            if (_rounds <= 0)
            {
                _reloadDoneAt = now + weapon.ReloadSeconds;
                _rounds = weapon.MagazineSize;
                return;
            }

            Vector3 eye = _eye.position;
            bool headshot = Random.value < _settings.HeadshotChance;
            Vector3 aim = AimPoint(_target, headshot ? _settings.HeadHeight : _settings.ChestHeight);
            Vector3 direction = (aim - eye).normalized;
            Vector3 flatForward = transform.forward;
            if (Vector3.Angle(flatForward, new Vector3(direction.x, 0f, direction.z)) > FireAngle) return; // still turning

            AmmoDefinition ammo = _settings.Ammo;
            // Aim settles while the bot keeps a target: the first shots go wide, giving players time to react.
            float settle = Mathf.Clamp01((now - _engagedSince) / _settings.AimSettleSeconds);
            float aimError = _settings.AimError * Mathf.Lerp(_settings.FirstShotErrorMultiplier, 1f, settle);
            Vector3 aimed = ApplyAimError(direction, aimError, _settings.VerticalErrorScale);
            float spread = weapon.Spread + ammo.ExtraSpread;
            var ends = new Vector3[ammo.Projectiles];
            for (int i = 0; i < ends.Length; i++)
                ends[i] = FireProjectile(weapon, ammo, eye, HitscanQuery.ApplySpread(aimed, spread));
            BroadcastShot(eye, ends);

            _rounds--;
            if (--_burstLeft > 0)
            {
                _nextShotAt = now + weapon.SecondsBetweenShots;
            }
            else
            {
                _burstLeft = Random.Range(_settings.BurstMin, _settings.BurstMax + 1);
                _nextShotAt = now + Mathf.Max(weapon.SecondsBetweenShots, Random.Range(_settings.BurstPauseMin, _settings.BurstPauseMax));
            }
        }

        private Vector3 FireProjectile(WeaponDefinition weapon, AmmoDefinition ammo, Vector3 origin, Vector3 direction)
        {
            if (!HitscanQuery.TryGetFirstHit(origin, direction, weapon.Range, weapon.HitMask, transform, _hitBuffer,
                    out RaycastHit hit, out Hitbox hitbox))
                return origin + direction * weapon.Range;

            IDamageable target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null)
            {
                HitZone zone = hitbox != null ? hitbox.Zone : HitZone.Body;
                float multiplier = hitbox != null ? hitbox.DamageMultiplier : 1f;
                int damage = Mathf.Max(1, Mathf.RoundToInt(ammo.Damage * multiplier));
                target.ApplyDamage(new DamageInfo(damage, DamageInfo.NoInstigator, hit.point, direction, zone,
                    ammo.Penetration, ammo.ArmorDamagePercent));
            }
            return hit.point;
        }

        [ObserversRpc]
        private void BroadcastShot(Vector3 origin, Vector3[] ends)
        {
            if (ends == null) return;
            foreach (Vector3 end in ends)
                ShotConfirmed?.Invoke(origin, end);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ServerActiveList.Clear();

        /// <summary>
        /// Human-like aim error: a random offset inside an ellipse of <paramref name="maxDegrees"/> horizontally,
        /// squashed vertically so misses go past the target's sides rather than into the sky or the floor.
        /// </summary>
        private static Vector3 ApplyAimError(Vector3 direction, float maxDegrees, float verticalScale)
        {
            if (maxDegrees <= 0f) return direction;

            Vector2 offset = Random.insideUnitCircle * maxDegrees;
            Vector3 right = Vector3.Cross(Vector3.up, direction);
            Quaternion pitch = right.sqrMagnitude > 0.0001f
                ? Quaternion.AngleAxis(-offset.y * verticalScale, right.normalized)
                : Quaternion.identity;
            return (Quaternion.AngleAxis(offset.x, Vector3.up) * pitch * direction).normalized;
        }

        private static Vector3 AimPoint(PlayerCharacter player, float height) =>
            player.transform.position + Vector3.up * height;
    }
}
