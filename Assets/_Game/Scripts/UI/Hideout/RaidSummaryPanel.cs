using System;
using ExtractionShooter.Core.Raid;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The end-of-raid report shown over the hideout: result, time, kills and what the gear was worth going in and coming out.
    /// Stays up until the player presses Continue.
    /// </summary>
    public sealed class RaidSummaryPanel : IDisposable
    {
        private const string Hidden = "hidden";

        private readonly VisualElement _root;
        private readonly Label _caption;
        private readonly Label _result;
        private readonly Label _time;
        private readonly Label _playerKills;
        private readonly Label _botKills;
        private readonly Label _deployed;
        private readonly Label _extracted;
        private readonly Label _fee;
        private readonly VisualElement _feeRow;
        private readonly Label _profit;
        private readonly Button _close;

        public bool IsOpen => _root != null && !_root.ClassListContains(Hidden);

        public RaidSummaryPanel(VisualElement root)
        {
            _root = root;
            if (_root == null) return;
            _caption = root.Q<Label>("raid-summary-caption");
            _result = root.Q<Label>("raid-summary-result");
            _time = root.Q<Label>("raid-summary-time");
            _playerKills = root.Q<Label>("raid-summary-player-kills");
            _botKills = root.Q<Label>("raid-summary-bot-kills");
            _deployed = root.Q<Label>("raid-summary-deployed");
            _extracted = root.Q<Label>("raid-summary-extracted");
            _profit = root.Q<Label>("raid-summary-profit");
            _fee = root.Q<Label>("raid-summary-fee");
            _feeRow = root.Q<VisualElement>("raid-summary-fee-row");
            _close = root.Q<Button>("raid-summary-close");
            if (_close != null) _close.clicked += Close;
            _root.AddToClassList(Hidden);
        }

        public void Show(RaidSummary summary)
        {
            if (_root == null) return;

            bool success = summary.Result == RaidResult.Extracted;
            Set(_caption, $"RAID SUMMARY  ·  {summary.Difficulty.DisplayName().ToUpperInvariant()}");
            Set(_result, summary.ResultText);
            _result?.EnableInClassList("raid-summary__result--success", success);
            _result?.EnableInClassList("raid-summary__result--failed", !success);
            Set(_time, $"Time in raid  {summary.TimeText}");
            Set(_playerKills, summary.PlayerKills.ToString());
            Set(_botKills, summary.BotKills.ToString());
            Set(_deployed, Credits(summary.DeployedValue));
            Set(_extracted, success ? Credits(summary.ExtractedValue) : "Lost");

            Set(_fee, "−" + Credits(summary.ExitFee));
            _feeRow?.EnableInClassList(Hidden, summary.ExitFee <= 0);

            long profit = summary.Profit;
            Set(_profit, (profit > 0 ? "+" : profit < 0 ? "−" : string.Empty) + Credits(Math.Abs(profit)));
            _profit?.EnableInClassList("raid-summary__value--gain", profit > 0);
            _profit?.EnableInClassList("raid-summary__value--loss", profit < 0);

            _root.RemoveFromClassList(Hidden);
            _root.BringToFront();
        }

        public void Close() => _root?.AddToClassList(Hidden);

        public void Dispose()
        {
            if (_close != null) _close.clicked -= Close;
        }

        private static string Credits(long value) => $"{value:N0} cr";

        private static void Set(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
