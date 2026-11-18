using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Visuals;
using UnityEngine;
using static ExtractionShooter.Gameplay.Visuals.PrimitiveParts;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// The item held while healing, built from primitives (no colliders) and posed with <see cref="UseItemMotion"/>:
    /// a bandage roll, a medkit pouch, a splint or a ration can. Used by the first-person view and by other players' bodies.
    /// The model's origin is where the hand holds it.
    /// </summary>
    public static class UseItemModel
    {
        private static readonly Color Gauze = new Color(0.92f, 0.91f, 0.88f);
        private static readonly Color KitRed = new Color(0.62f, 0.12f, 0.1f);
        private static readonly Color Cross = new Color(0.95f, 0.95f, 0.93f);
        private static readonly Color Wood = new Color(0.58f, 0.47f, 0.32f);
        private static readonly Color Strap = new Color(0.2f, 0.2f, 0.18f);
        private static readonly Color Tin = new Color(0.58f, 0.6f, 0.62f);
        private static readonly Color Label = new Color(0.35f, 0.42f, 0.25f);

        /// <summary>Builds the item under <paramref name="parent"/>; null for <see cref="UseItemVisual.None"/>.</summary>
        public static Transform Build(Transform parent, UseItemVisual kind, VisualProfile profile)
        {
            if (kind == UseItemVisual.None || profile == null) return null;

            Transform root = Node(parent, "UseItem_" + kind, Vector3.zero);
            switch (kind)
            {
                case UseItemVisual.Bandage:
                    Cylinder(root, "Roll", new Vector3(0f, 0f, 0.02f), 0.032f, 0.07f, AlongX, profile.Lit(Gauze));
                    Box(root, "Tail", new Vector3(0.02f, -0.035f, 0.035f), new Vector3(0.05f, 0.004f, 0.06f), profile.Lit(Gauze));
                    break;

                case UseItemVisual.Medkit:
                    Box(root, "Pouch", new Vector3(0f, 0.01f, 0.035f), new Vector3(0.12f, 0.08f, 0.04f), profile.Lit(KitRed));
                    Box(root, "CrossA", new Vector3(0f, 0.01f, 0.013f), new Vector3(0.04f, 0.013f, 0.004f), profile.Lit(Cross));
                    Box(root, "CrossB", new Vector3(0f, 0.01f, 0.013f), new Vector3(0.013f, 0.04f, 0.004f), profile.Lit(Cross));
                    Cylinder(root, "Injector", new Vector3(0.075f, 0.025f, 0.03f), 0.007f, 0.065f, Quaternion.identity, profile.Lit(Cross));
                    break;

                case UseItemVisual.Splint:
                    Box(root, "Board", new Vector3(0f, 0.06f, 0.03f), new Vector3(0.035f, 0.26f, 0.012f), profile.Lit(Wood));
                    Box(root, "StrapA", new Vector3(0f, 0.12f, 0.03f), new Vector3(0.045f, 0.02f, 0.02f), profile.Lit(Strap));
                    Box(root, "StrapB", new Vector3(0f, 0.0f, 0.03f), new Vector3(0.045f, 0.02f, 0.02f), profile.Lit(Strap));
                    break;

                case UseItemVisual.Food:
                    Cylinder(root, "Can", new Vector3(0f, 0.02f, 0.03f), 0.035f, 0.09f, Quaternion.identity, profile.Lit(Tin));
                    Cylinder(root, "Label", new Vector3(0f, 0.02f, 0.03f), 0.036f, 0.05f, Quaternion.identity, profile.Lit(Label));
                    break;
            }
            return root;
        }

        /// <summary>Places the item: <paramref name="rest"/> is the hand's resting point and orientation for this view.</summary>
        public static void Pose(Transform item, Vector3 restPosition, Quaternion restRotation, in UsePose pose)
        {
            if (item == null) return;
            item.SetPositionAndRotation(restPosition + restRotation * new Vector3(pose.X, pose.Y, pose.Z),
                restRotation * Quaternion.Euler(pose.Pitch, pose.Yaw, pose.Roll));
        }
    }
}
