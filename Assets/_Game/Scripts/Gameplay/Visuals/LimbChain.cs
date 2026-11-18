using ExtractionShooter.Core.Visuals;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Two bones of a limb (upper arm + forearm, thigh + shin) whose meshes hang along their local -Y, posed by
    /// <see cref="LimbIk"/> in world space so the end of the limb reaches a target. The joint bends toward the pole
    /// (elbows out and down, knees forward). The bone after the lower one (hand, foot) follows as its child.
    /// </summary>
    public readonly struct LimbChain
    {
        public LimbChain(Transform upper, Transform lower, Transform end, float upperLength, float lowerLength)
        {
            Upper = upper;
            Lower = lower;
            End = end;
            UpperLength = upperLength;
            LowerLength = lowerLength;
        }

        public Transform Upper { get; }
        public Transform Lower { get; }
        public Transform End { get; }
        public float UpperLength { get; }
        public float LowerLength { get; }

        public bool IsValid => Upper != null && Lower != null;

        /// <summary>Reaches for <paramref name="target"/> (world), bending toward <paramref name="pole"/> (world direction).</summary>
        public void Solve(Vector3 target, Vector3 pole)
        {
            if (!IsValid) return;

            Vector3 root = Upper.position;
            Vector3 toTarget = target - root;
            float distance = toTarget.magnitude;
            Vector3 direction = distance > 0.00001f ? toTarget / distance : Vector3.down;

            LimbIk.Solution solution = LimbIk.Solve(UpperLength, LowerLength, distance);
            Vector3 bend = Vector3.ProjectOnPlane(pole, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(Vector3.forward, direction);
            if (bend.sqrMagnitude < 0.000001f) bend = Vector3.ProjectOnPlane(Vector3.right, direction);
            bend.Normalize();

            Vector3 joint = root + (direction * solution.RootCos + bend * solution.RootSin) * UpperLength;
            Vector3 end = root + direction * solution.Reach;
            Upper.rotation = SegmentRotation(joint - root, bend);
            Lower.rotation = SegmentRotation(end - joint, bend);
        }

        /// <summary>A rotation that points a bone's -Y along <paramref name="direction"/>, its +Z toward <paramref name="front"/>.</summary>
        public static Quaternion SegmentRotation(Vector3 direction, Vector3 front)
        {
            if (direction.sqrMagnitude < 0.0000001f) return Quaternion.identity;
            Vector3 down = direction.normalized;
            Vector3 forward = Vector3.ProjectOnPlane(front, down);
            if (forward.sqrMagnitude < 0.000001f) forward = Vector3.ProjectOnPlane(Vector3.forward, down);
            if (forward.sqrMagnitude < 0.000001f) forward = Vector3.ProjectOnPlane(Vector3.right, down);
            return Quaternion.LookRotation(forward.normalized, -down);
        }
    }
}
