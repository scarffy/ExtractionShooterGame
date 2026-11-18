using System.Collections.Generic;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Visuals
{
    /// <summary>
    /// Builds visual parts from Unity's built-in primitive meshes: a GameObject with a MeshFilter and a MeshRenderer,
    /// and never a collider (so visuals can't block shots, interaction rays or movement). Parts take their parent's
    /// layer. Sizes are in metres: boxes and spheres take their full size, cylinders and capsules a radius and a length
    /// along their local Y (use <see cref="AlongZ"/> / <see cref="AlongX"/> to lay them down).
    /// </summary>
    public static class PrimitiveParts
    {
        /// <summary>Turns a part's local Y onto Z (barrels, scopes).</summary>
        public static readonly Quaternion AlongZ = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>Turns a part's local Y onto X.</summary>
        public static readonly Quaternion AlongX = Quaternion.Euler(0f, 0f, -90f);

        private static readonly Dictionary<PrimitiveType, Mesh> Meshes = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>The built-in mesh of a primitive (read once from a temporary primitive, which is destroyed at once).</summary>
        public static Mesh MeshOf(PrimitiveType type)
        {
            if (Meshes.TryGetValue(type, out Mesh mesh) && mesh != null) return mesh;

            GameObject temporary = GameObject.CreatePrimitive(type);
            mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temporary);
            Meshes[type] = mesh;
            return mesh;
        }

        /// <summary>An empty transform (a bone or socket).</summary>
        public static Transform Node(Transform parent, string name, Vector3 localPosition, Quaternion localRotation)
        {
            var node = new GameObject(name).transform;
            if (parent != null)
            {
                node.gameObject.layer = parent.gameObject.layer;
                node.SetParent(parent, false);
            }
            node.localPosition = localPosition;
            node.localRotation = localRotation;
            return node;
        }

        /// <summary>An empty transform with no rotation.</summary>
        public static Transform Node(Transform parent, string name, Vector3 localPosition) =>
            Node(parent, name, localPosition, Quaternion.identity);

        public static MeshRenderer Box(Transform parent, string name, Vector3 center, Vector3 size, Material material) =>
            Shape(PrimitiveType.Cube, parent, name, center, Quaternion.identity, size, material);

        public static MeshRenderer Box(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material) =>
            Shape(PrimitiveType.Cube, parent, name, center, rotation, size, material);

        public static MeshRenderer Sphere(Transform parent, string name, Vector3 center, Vector3 size, Material material) =>
            Shape(PrimitiveType.Sphere, parent, name, center, Quaternion.identity, size, material);

        public static MeshRenderer Sphere(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation, Material material) =>
            Shape(PrimitiveType.Sphere, parent, name, center, rotation, size, material);

        /// <summary>A cylinder of <paramref name="radius"/> and <paramref name="length"/> along its local Y.</summary>
        public static MeshRenderer Cylinder(Transform parent, string name, Vector3 center, float radius, float length,
            Quaternion rotation, Material material) =>
            Shape(PrimitiveType.Cylinder, parent, name, center, rotation, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material);

        /// <summary>A capsule of <paramref name="radius"/> and total <paramref name="length"/> along its local Y.</summary>
        public static MeshRenderer Capsule(Transform parent, string name, Vector3 center, float radius, float length,
            Quaternion rotation, Material material) =>
            Shape(PrimitiveType.Capsule, parent, name, center, rotation, new Vector3(radius * 2f, length * 0.5f, radius * 2f), material);

        /// <summary>Any primitive with an explicit local scale.</summary>
        public static MeshRenderer Shape(PrimitiveType type, Transform parent, string name, Vector3 center, Quaternion rotation,
            Vector3 scale, Material material)
        {
            Transform part = Node(parent, name, center, rotation);
            part.localScale = scale;
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = MeshOf(type);
            MeshRenderer renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            return renderer;
        }

        /// <summary>Puts a whole hierarchy on one layer.</summary>
        public static void SetLayer(Transform root, int layer)
        {
            if (root == null) return;
            root.gameObject.layer = layer;
            foreach (Transform child in root) SetLayer(child, layer);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Meshes.Clear();
    }
}
