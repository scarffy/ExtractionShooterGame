using System.Collections.Generic;
using ExtractionShooter.Core.Spawning;
using UnityEngine;

namespace ExtractionShooter.Networking
{
    /// <summary>
    /// Hands out spawn poses round robin, or the one farthest from threats. Uses the assigned points,
    /// or this object's children if none are assigned.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SpawnPointProvider : MonoBehaviour, ISpawnPointProvider
    {
        [SerializeField] private Transform[] _spawnPoints;

        private readonly List<Vector3> _positions = new List<Vector3>();
        private int _nextIndex;

        private void Awake()
        {
            if (_spawnPoints != null && _spawnPoints.Length > 0) return;

            _spawnPoints = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
                _spawnPoints[i] = transform.GetChild(i);
        }

        public Pose GetNextSpawnPose()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
            {
                Debug.LogWarning($"[{nameof(SpawnPointProvider)}] No spawn points; using this object's pose.", this);
                return new Pose(transform.position, transform.rotation);
            }

            Transform point = _spawnPoints[_nextIndex];
            _nextIndex = (_nextIndex + 1) % _spawnPoints.Length;
            return new Pose(point.position, point.rotation);
        }

        public Pose GetSafestSpawnPose(IReadOnlyList<Vector3> threats)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0) return GetNextSpawnPose();

            _positions.Clear();
            foreach (Transform point in _spawnPoints)
                _positions.Add(point != null ? point.position : transform.position);

            int index = SpawnSelection.PickSafest(_positions, threats, _nextIndex);
            _nextIndex = (index + 1) % _spawnPoints.Length;
            Transform chosen = _spawnPoints[index];
            return chosen != null ? new Pose(chosen.position, chosen.rotation) : new Pose(transform.position, transform.rotation);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.green;
            foreach (Transform child in transform)
            {
                Gizmos.DrawWireSphere(child.position + Vector3.up, 0.5f);
                Gizmos.DrawRay(child.position + Vector3.up, child.forward);
            }
        }
#endif
    }
}
