using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Small element factory and layout helpers shared by the code-built navigation views
    /// (<see cref="CompassView"/>, <see cref="MapView"/>, <see cref="TacticalPanelView"/>).
    /// </summary>
    internal static class MapUi
    {
        /// <summary>An absolutely positioned element that ignores the pointer.</summary>
        public static VisualElement Box(string name = null)
        {
            var element = new VisualElement { name = name, pickingMode = PickingMode.Ignore };
            element.style.position = Position.Absolute;
            return element;
        }

        /// <summary>A flow-layout (not absolute) container that ignores the pointer.</summary>
        public static VisualElement Group(FlexDirection direction = FlexDirection.Column)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.style.flexDirection = direction;
            return element;
        }

        public static Label Text(string text, float size, Color color, bool bold = true)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = bold ? FontStyle.Bold : FontStyle.Normal;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.marginTop = 0f;
            label.style.marginBottom = 0f;
            label.style.paddingTop = 0f;
            label.style.paddingBottom = 0f;
            return label;
        }

        /// <summary>A label of fixed width whose text is centred on it (used for labels placed by their centre).</summary>
        public static Label CenteredText(string text, float size, Color color, float width, bool bold = true)
        {
            Label label = Text(text, size, color, bold);
            label.style.position = Position.Absolute;
            label.style.width = width;
            label.style.paddingLeft = 0f;
            label.style.paddingRight = 0f;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            return label;
        }

        public static void Place(VisualElement element, float left, float top, float width, float height)
        {
            IStyle style = element.style;
            style.left = left;
            style.top = top;
            style.width = width;
            style.height = height;
        }

        public static void Move(VisualElement element, float left, float top)
        {
            element.style.left = left;
            element.style.top = top;
        }

        public static void Border(VisualElement element, float width, Color color)
        {
            IStyle style = element.style;
            style.borderTopWidth = width;
            style.borderBottomWidth = width;
            style.borderLeftWidth = width;
            style.borderRightWidth = width;
            style.borderTopColor = color;
            style.borderBottomColor = color;
            style.borderLeftColor = color;
            style.borderRightColor = color;
        }

        public static void Radius(VisualElement element, float radius)
        {
            IStyle style = element.style;
            style.borderTopLeftRadius = radius;
            style.borderTopRightRadius = radius;
            style.borderBottomLeftRadius = radius;
            style.borderBottomRightRadius = radius;
        }

        public static void Padding(VisualElement element, float horizontal, float vertical)
        {
            IStyle style = element.style;
            style.paddingLeft = horizontal;
            style.paddingRight = horizontal;
            style.paddingTop = vertical;
            style.paddingBottom = vertical;
        }

        public static void Show(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>Rotates an element about its centre (clockwise in degrees, as on screen).</summary>
        public static void SetRotation(VisualElement element, float degrees) =>
            element.style.rotate = new Rotate(Angle.Degrees(degrees));

        /// <summary>
        /// An element of the given size that draws an arrowhead pointing up (rotate it to face a bearing).
        /// </summary>
        public static VisualElement Arrow(float size, Color fill, Color outline)
        {
            VisualElement arrow = Box("map-arrow");
            arrow.style.width = size;
            arrow.style.height = size;
            arrow.generateVisualContent = context =>
            {
                Painter2D painter = context.painter2D;
                painter.fillColor = fill;
                painter.strokeColor = outline;
                painter.lineWidth = Mathf.Max(1f, size * 0.08f);
                painter.BeginPath();
                painter.MoveTo(new Vector2(size * 0.5f, size * 0.05f));
                painter.LineTo(new Vector2(size * 0.9f, size * 0.92f));
                painter.LineTo(new Vector2(size * 0.5f, size * 0.7f));
                painter.LineTo(new Vector2(size * 0.1f, size * 0.92f));
                painter.ClosePath();
                painter.Fill();
                painter.Stroke();
            };
            return arrow;
        }

        /// <summary>A small square turned 45 degrees: the extraction marker on the compass.</summary>
        public static VisualElement Diamond(float size, Color fill, Color outline)
        {
            VisualElement diamond = Box("map-diamond");
            diamond.style.width = size;
            diamond.style.height = size;
            diamond.style.backgroundColor = fill;
            Border(diamond, 1f, outline);
            SetRotation(diamond, 45f);
            return diamond;
        }
    }
}
