using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Player;
using ExtractionShooter.Gameplay.Raid;
using ExtractionShooter.Gameplay.Session;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hud
{
    /// <summary>
    /// Centre extraction prompt: while extracting, a countdown ring with the seconds left, the point's name and either
    /// "Stay in the zone" or the fee that will be charged; in a closed or unaffordable point, why it will not let the
    /// player out; after stepping out mid-countdown, "Extraction cancelled" for a moment. The text comes from
    /// <see cref="ExtractionPromptTracker"/>; the server decides the extraction itself.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ExtractHudView : MonoBehaviour
    {
        private const string HiddenClass = "hidden";
        private const string ExtractingClass = "extract-hud--extracting";
        private const string BlockedClass = "extract-hud--blocked";
        private const string CancelledClass = "extract-hud--cancelled";
        private const float RingWidth = 6f;

        [SerializeField] private Color _ringTrackColor = new Color(1f, 1f, 1f, 0.15f);
        [SerializeField] private Color _ringFillColor = new Color(0.36f, 0.92f, 0.46f, 1f);
        [SerializeField] private Color _ringPaidColor = new Color(1f, 0.74f, 0.28f, 1f);

        private readonly ExtractionPromptTracker _tracker = new ExtractionPromptTracker();

        private VisualElement _root;
        private VisualElement _ring;
        private Label _countdown;
        private Label _title;
        private Label _detail;
        private ExtractionPrompt _shown;
        private bool _paid;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _root = root.Q<VisualElement>("extract-hud");
            _ring = root.Q<VisualElement>("extract-ring");
            _countdown = root.Q<Label>("extract-countdown");
            _title = root.Q<Label>("extract-title");
            _detail = root.Q<Label>("extract-detail");
            if (_root == null || _ring == null || _countdown == null || _title == null || _detail == null)
            {
                Debug.LogError($"[{nameof(ExtractHudView)}] UXML is missing the extract-hud elements.", this);
                enabled = false;
                return;
            }

            _ring.generateVisualContent = DrawRing;
            _tracker.Reset();
            Show(ExtractionPrompt.None, false);
        }

        private void OnDisable()
        {
            if (_ring != null) _ring.generateVisualContent = null;
        }

        private void Update()
        {
            PlayerCharacter player = PlayerCharacter.Local;
            PlayerRaid raid = player != null ? player.Raid : null;
            bool alive = raid != null && player.Health != null && player.Health.IsAlive;

            string zoneName = null;
            ExtractionStatus status = default;
            bool paid = false;
            if (alive)
            {
                ExtractionZone zone = ExtractionZone.Find(player.transform.position);
                if (zone != null)
                {
                    zoneName = zone.DisplayName;
                    status = zone.StatusFor(raid.SecondsLeft, LocalCredits());
                    paid = status.IsPaid;
                }
            }

            ExtractionPrompt prompt = _tracker.Update(Time.unscaledTimeAsDouble, alive, zoneName, status,
                alive ? raid.ZoneName : null, alive ? raid.ExtractProgress : 0f, alive ? raid.ExtractSeconds : 0f);
            Show(prompt, paid);
        }

        private void Show(ExtractionPrompt prompt, bool paid)
        {
            bool changed = prompt.Kind != _shown.Kind || prompt.Title != _shown.Title || prompt.Detail != _shown.Detail ||
                           prompt.Countdown != _shown.Countdown || paid != _paid;
            bool ringChanged = !Mathf.Approximately(prompt.Progress, _shown.Progress) || paid != _paid;
            _shown = prompt;
            _paid = paid;
            if (ringChanged) _ring.MarkDirtyRepaint();
            if (!changed) return;

            _root.EnableInClassList(HiddenClass, !prompt.IsVisible);
            _root.EnableInClassList(ExtractingClass, prompt.Kind == ExtractionPromptKind.Extracting);
            _root.EnableInClassList(BlockedClass, prompt.Kind == ExtractionPromptKind.Blocked);
            _root.EnableInClassList(CancelledClass, prompt.Kind == ExtractionPromptKind.Cancelled);
            _ring.EnableInClassList(HiddenClass, prompt.Kind != ExtractionPromptKind.Extracting);
            _countdown.text = prompt.Countdown;
            _title.text = prompt.Title;
            _detail.text = prompt.Detail;
        }

        private void DrawRing(MeshGenerationContext context)
        {
            Rect rect = _ring.contentRect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f - RingWidth * 0.5f;
            if (radius <= 0f) return;
            Vector2 centre = rect.center;

            Painter2D painter = context.painter2D;
            painter.lineWidth = RingWidth;
            painter.lineCap = LineCap.Round;

            painter.strokeColor = _ringTrackColor;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();

            float progress = _shown.Progress;
            if (progress <= 0f) return;
            painter.strokeColor = _paid ? _ringPaidColor : _ringFillColor;
            painter.BeginPath();
            painter.Arc(centre, radius, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * progress));
            painter.Stroke();
        }

        /// <summary>The local player's credits (paid exits); 0 before the session is known.</summary>
        private static long LocalCredits()
        {
            PlayerSession session = PlayerSession.Local;
            return session != null && session.Trading != null ? session.Trading.Money : 0L;
        }
    }
}
