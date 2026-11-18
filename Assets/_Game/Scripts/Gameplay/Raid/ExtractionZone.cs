using System.Collections.Generic;
using ExtractionShooter.Core.Raid;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Raid
{
    /// <summary>
    /// An area where players extract by staying inside for a few seconds (see <see cref="PlayerRaid"/>).
    /// The area is a box (<see cref="_size"/>) around this transform; it has no collider, so it never
    /// blocks bullets or interaction rays. Its <see cref="Rule"/> may close it until late in the raid or charge
    /// a credit fee (Delta Force style); the server enforces it, the HUD shows it through <see cref="StatusFor"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ExtractionZone : MonoBehaviour
    {
        private static readonly List<ExtractionZone> ActiveZones = new List<ExtractionZone>();

        [SerializeField] private string _displayName = "Extraction";

        [Tooltip("Box size in local space, centred on this transform.")]
        [SerializeField] private Vector3 _size = new Vector3(6f, 4f, 6f);

        [Tooltip("Always open, paid (credits charged from the profile on extraction) or late-raid (opens near the end).")]
        [SerializeField] private ExtractionRuleKind _rule = ExtractionRuleKind.AlwaysOpen;

        [Tooltip("Paid: credits charged when the extraction completes.")]
        [SerializeField, Min(0)] private int _cost = 2000;

        [Tooltip("Late-raid: opens once the raid clock shows this many seconds or fewer.")]
        [SerializeField, Min(0)] private int _opensWithSecondsLeft = 480;

        public string DisplayName => _displayName;

        /// <summary>The condition for extracting here.</summary>
        public ExtractionRule Rule => new ExtractionRule(_rule, _cost, _opensWithSecondsLeft);

        /// <summary>This point's state for a player with this much raid time left and these credits.</summary>
        public ExtractionStatus StatusFor(int secondsLeft, long credits) => Rule.Evaluate(secondsLeft, credits);

        /// <summary>Box size in local space (the map draws it).</summary>
        public Vector3 Size => _size;

        /// <summary>Every enabled zone in the loaded scenes (on server and clients alike; zones are scene objects).</summary>
        public static IReadOnlyList<ExtractionZone> All => ActiveZones;

        /// <summary>The zone containing a world position, or null.</summary>
        public static ExtractionZone Find(Vector3 worldPosition)
        {
            foreach (ExtractionZone zone in ActiveZones)
                if (zone.Contains(worldPosition)) return zone;
            return null;
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = _size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        private void OnEnable() => ActiveZones.Add(this);

        private void OnDisable() => ActiveZones.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.4f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, _size);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => ActiveZones.Clear();
    }
}
