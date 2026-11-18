using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Squad;
using ExtractionShooter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Shared plumbing for HUD markers that follow points in the world (<see cref="TeammateTagsView"/>,
    /// <see cref="PingMarkersView"/>): when to draw them, a full-screen layer behind the rest of the HUD, and
    /// world-to-panel projection through <see cref="ScreenMarkerMath"/>.
    /// </summary>
    internal static class WorldMarkers
    {
        /// <summary>World markers show while the local player is alive, in a raid and in control (no menu or map open).</summary>
        public static bool HudVisible(PlayerCharacter player) =>
            player != null && player.Raid != null && player.Health.IsAlive && CursorController.IsLocked;

        /// <summary>A layer covering the whole panel, inserted behind everything else on the document so menus and the map cover it.</summary>
        public static VisualElement CreateLayer(VisualElement documentRoot, string name)
        {
            VisualElement layer = MapUi.Box(name);
            layer.style.left = 0f;
            layer.style.top = 0f;
            layer.style.right = 0f;
            layer.style.bottom = 0f;
            documentRoot.Insert(0, layer);
            return layer;
        }

        /// <summary>The layer's size in panel pixels; false before its first layout.</summary>
        public static bool TryGetSize(VisualElement layer, out float width, out float height)
        {
            Rect rect = layer.layout;
            width = rect.width;
            height = rect.height;
            return !float.IsNaN(width) && !float.IsNaN(height) && width > 1f && height > 1f;
        }

        /// <summary>Where a world point lands on a panel of the given size, pinned inside the margins when off-screen.</summary>
        public static ScreenMarker Project(Camera camera, Vector3 world, float width, float height, float marginX, float marginY)
        {
            Vector3 viewport = camera.WorldToViewportPoint(world);
            return ScreenMarkerMath.Place(viewport.x, viewport.y, viewport.z, width, height, marginX, marginY);
        }

        /// <summary>The game camera, re-fetched when it changes (scene loads).</summary>
        public static Camera ResolveCamera(Camera cached) => cached != null && cached.enabled ? cached : Camera.main;

        /// <summary>Uniform scale about the element's centre.</summary>
        public static void SetScale(VisualElement element, float scale) =>
            element.style.scale = new Scale(new Vector2(scale, scale));
    }
}
