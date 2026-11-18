using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Matchmaking;
using ExtractionShooter.Core.Raid;
using ExtractionShooter.Gameplay.Session;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The hideout's squad tab and the header's matchmaking controls for the local <see cref="PlayerSquad"/>.
    /// <list type="bullet">
    /// <item>Squad tab: the squad code, members with leader and ready state, Ready, Create and Leave, invite by name,
    /// join by code, a pending invite (Accept / Decline) and the matchmaking status.</item>
    /// <item>Header (every tab): the difficulty picker (the map's two difficulties; the leader picks), one status line and the
    /// Find match / Cancel button; the Squad tab is highlighted while an invite is pending.</item>
    /// </list>
    /// Every action is a request; the server's answer comes back through the squad's SyncVars and messages.
    /// Plain C#: owned by <see cref="StashScreen"/>.
    /// </summary>
    public sealed class SquadPanel : IDisposable
    {
        private const string HiddenClass = "hidden";
        private const string TabAlertClass = "hideout-tab--alert";
        private const string CancelClass = "deploy-button--cancel";
        private const string ActiveClass = "squad-button--active";
        private const string MemberClass = "squad-member";
        private const string MemberSelfClass = "squad-member--self";
        private const string MemberNameClass = "squad-member__name";
        private const string TagClass = "squad-member__tag";
        private const string LeaderTagClass = "squad-member__tag--leader";
        private const string ReadyTagClass = "squad-member__tag--ready";

        private readonly Action _beforeAction;
        private readonly Label _code;
        private readonly Label _empty;
        private readonly VisualElement _members;
        private readonly Button _readyButton;
        private readonly Button _createButton;
        private readonly Button _leaveButton;
        private readonly VisualElement _invitePanel;
        private readonly Label _inviteLabel;
        private readonly Button _acceptButton;
        private readonly Button _declineButton;
        private readonly TextField _inviteField;
        private readonly Button _inviteButton;
        private readonly TextField _codeField;
        private readonly Button _joinButton;
        private readonly Label _queueDetail;
        private readonly Label _message;
        private readonly Label _headerStatus;
        private readonly Button _findMatchButton;
        private readonly Button _tab;
        private readonly List<PlayerSquad> _squadmates = new List<PlayerSquad>();
        private readonly DifficultyPicker _difficulty;

        private PlayerSquad _squad;
        private string _lastMessage = string.Empty;
        private bool _wasSearching;
        // A pick sent to the server but not echoed back yet: shown and used for Find match until the server agrees.
        private RaidDifficulty? _pendingDifficulty;
        private const string SearchingNotice = "Searching for a";

        /// <param name="beforeAction">Called before any request is sent (the hideout cancels a drag in progress).</param>
        public SquadPanel(VisualElement root, Action beforeAction = null)
        {
            _beforeAction = beforeAction;
            _code = root.Q<Label>("squad-code");
            _empty = root.Q<Label>("squad-empty");
            _members = root.Q<VisualElement>("squad-members");
            _readyButton = root.Q<Button>("squad-ready-button");
            _createButton = root.Q<Button>("squad-create-button");
            _leaveButton = root.Q<Button>("squad-leave-button");
            _invitePanel = root.Q<VisualElement>("squad-invite-panel");
            _inviteLabel = root.Q<Label>("squad-invite-label");
            _acceptButton = root.Q<Button>("squad-accept-button");
            _declineButton = root.Q<Button>("squad-decline-button");
            _inviteField = root.Q<TextField>("squad-invite-field");
            _inviteButton = root.Q<Button>("squad-invite-button");
            _codeField = root.Q<TextField>("squad-code-field");
            _joinButton = root.Q<Button>("squad-join-button");
            _queueDetail = root.Q<Label>("squad-queue-detail");
            _message = root.Q<Label>("squad-message");
            _headerStatus = root.Q<Label>("queue-status");
            _findMatchButton = root.Q<Button>("find-match-button");
            _tab = root.Q<Button>("tab-squad");
            _difficulty = new DifficultyPicker(root.Q<VisualElement>("difficulty-picker"));
            _difficulty.Picked += PickDifficulty;

            if (_readyButton != null) _readyButton.clicked += ToggleReady;
            if (_createButton != null) _createButton.clicked += Create;
            if (_leaveButton != null) _leaveButton.clicked += Leave;
            if (_acceptButton != null) _acceptButton.clicked += AcceptInvite;
            if (_declineButton != null) _declineButton.clicked += DeclineInvite;
            if (_inviteButton != null) _inviteButton.clicked += Invite;
            if (_joinButton != null) _joinButton.clicked += Join;
            if (_findMatchButton != null) _findMatchButton.clicked += FindOrCancel;
            PlayerSquad.AnyChanged += Refresh;
        }

        public void Dispose()
        {
            Bind(null);
            PlayerSquad.AnyChanged -= Refresh;
            _difficulty.Picked -= PickDifficulty;
            _difficulty.Dispose();
            if (_readyButton != null) _readyButton.clicked -= ToggleReady;
            if (_createButton != null) _createButton.clicked -= Create;
            if (_leaveButton != null) _leaveButton.clicked -= Leave;
            if (_acceptButton != null) _acceptButton.clicked -= AcceptInvite;
            if (_declineButton != null) _declineButton.clicked -= DeclineInvite;
            if (_inviteButton != null) _inviteButton.clicked -= Invite;
            if (_joinButton != null) _joinButton.clicked -= Join;
            if (_findMatchButton != null) _findMatchButton.clicked -= FindOrCancel;
        }

        public void Bind(PlayerSquad squad)
        {
            if (_squad == squad)
            {
                Refresh();
                return;
            }

            if (_squad != null)
            {
                _squad.Changed -= Refresh;
                _squad.MessageReceived -= OnMessage;
            }
            _squad = squad;
            _lastMessage = string.Empty;
            _pendingDifficulty = null;
            if (_squad != null)
            {
                _squad.Changed += Refresh;
                _squad.MessageReceived += OnMessage;
            }
            Refresh();
        }

        public void Refresh()
        {
            PlayerSquad squad = _squad;
            bool inSquad = squad != null && squad.InSquad;

            SetText(_code, inSquad ? $"Code  {squad.SquadCode}" : string.Empty);
            SetVisible(_empty, !inSquad);
            RefreshMembers(inSquad ? squad.SquadId : SquadRules.NoSquad);

            SetVisible(_readyButton, inSquad);
            SetVisible(_createButton, !inSquad);
            SetVisible(_leaveButton, inSquad);
            if (_readyButton != null && squad != null)
            {
                _readyButton.text = squad.IsReady ? "Ready" : "Set ready";
                _readyButton.EnableInClassList(ActiveClass, squad.IsReady);
            }

            bool invited = squad != null && squad.HasInvite;
            SetVisible(_invitePanel, invited);
            if (invited) SetText(_inviteLabel, $"{squad.InviteFrom} invited you to their squad.");
            if (_tab != null)
            {
                _tab.EnableInClassList(TabAlertClass, invited);
                _tab.text = inSquad ? $"Squad · {_squadmates.Count}" : "Squad";
            }

            RefreshMatchmaking(squad, inSquad);
            SetText(_message, _lastMessage);
        }

        private void RefreshMatchmaking(PlayerSquad squad, bool inSquad)
        {
            bool searching = squad != null && squad.IsSearching;
            // The search ended (launched into a raid, or stopped): its "Searching…" notice is stale now.
            if (_wasSearching && !searching && _lastMessage.StartsWith(SearchingNotice, StringComparison.Ordinal)) _lastMessage = string.Empty;
            _wasSearching = searching;
            bool canStart = squad != null && (!inSquad || squad.IsLeader);

            MapDifficulties map = SquadLobby.Instance != null ? SquadLobby.Instance.Difficulties : MapDifficulties.EasyNormal;
            RaidDifficulty difficulty = SelectedDifficulty(map);
            _difficulty.Show(map, difficulty, canStart && !searching);

            string status = searching ? $"{difficulty.DisplayName()}  ·  {SquadRules.DescribeQueue(squad.Queue)}" : string.Empty;
            SetText(_queueDetail, searching ? status : canStart ? "Not searching." : "Waiting for the squad leader to start the search.");
            SetText(_headerStatus, searching ? status : !string.IsNullOrEmpty(_lastMessage) ? _lastMessage
                : inSquad && !canStart ? "Get ready — the leader starts the search." : string.Empty);

            if (_findMatchButton == null) return;
            _findMatchButton.text = searching ? "Cancel search" : "Find match";
            _findMatchButton.EnableInClassList(CancelClass, searching);
            _findMatchButton.SetEnabled(searching || canStart);
        }

        private void RefreshMembers(int squadId)
        {
            PlayerSquad.GetSquad(squadId, _squadmates);
            if (_members == null) return;

            _members.Clear();
            foreach (PlayerSquad member in _squadmates)
            {
                var row = new VisualElement();
                row.AddToClassList(MemberClass);
                row.EnableInClassList(MemberSelfClass, member == _squad);

                string name = member.Session != null && !string.IsNullOrEmpty(member.Session.PlayerName) ? member.Session.PlayerName : "…";
                var nameLabel = new Label(member == _squad ? $"{name}  (you)" : name);
                nameLabel.AddToClassList(MemberNameClass);
                row.Add(nameLabel);

                if (member.IsLeader) row.Add(Tag("Leader", LeaderTagClass));
                row.Add(Tag(member.IsReady ? "Ready" : "Not ready", member.IsReady ? ReadyTagClass : null));
                _members.Add(row);
            }
        }

        private static Label Tag(string text, string extraClass)
        {
            var tag = new Label(text);
            tag.AddToClassList(TagClass);
            if (extraClass != null) tag.AddToClassList(extraClass);
            return tag;
        }

        private void OnMessage(string message)
        {
            _lastMessage = message ?? string.Empty;
            Refresh();
        }

        // ---------------- Actions ----------------

        private void ToggleReady()
        {
            if (Begin()) _squad.RequestSetReady(!_squad.IsReady);
        }

        private void Create()
        {
            if (Begin()) _squad.RequestCreate();
        }

        private void Leave()
        {
            if (Begin()) _squad.RequestLeave();
        }

        private void AcceptInvite()
        {
            if (Begin()) _squad.RequestAcceptInvite();
        }

        private void DeclineInvite()
        {
            if (Begin()) _squad.RequestDeclineInvite();
        }

        private void Invite()
        {
            string playerName = TakeText(_inviteField);
            if (playerName.Length > 0 && Begin()) _squad.RequestInvite(playerName);
        }

        private void Join()
        {
            string code = TakeText(_codeField);
            if (code.Length > 0 && Begin()) _squad.RequestJoin(code);
        }

        private void FindOrCancel()
        {
            if (!Begin()) return;
            if (_squad.IsSearching) _squad.RequestCancelMatch();
            else _squad.RequestFindMatch(SelectedDifficulty(SquadLobby.Instance != null ? SquadLobby.Instance.Difficulties : MapDifficulties.EasyNormal));
        }

        private void PickDifficulty(RaidDifficulty difficulty)
        {
            if (!Begin()) return;
            _pendingDifficulty = difficulty;
            _squad.RequestSetDifficulty(difficulty);
            Refresh();
        }

        /// <summary>The pick to show and search with: a pick not yet confirmed by the server, else the server's.</summary>
        private RaidDifficulty SelectedDifficulty(MapDifficulties map)
        {
            if (_squad == null) return RaidDifficulty.Normal;
            if (_pendingDifficulty.HasValue && _pendingDifficulty.Value == _squad.Difficulty) _pendingDifficulty = null;
            // Only the leader (or a solo player) has a pick of their own; squadmates always show the leader's.
            bool ownPick = !_squad.InSquad || _squad.IsLeader;
            return map.Clamp(ownPick && _pendingDifficulty.HasValue ? _pendingDifficulty.Value : _squad.Difficulty);
        }

        /// <summary>Clears the last message and runs the pre-action hook. False when there is no squad state to act on.</summary>
        private bool Begin()
        {
            if (_squad == null) return false;
            _beforeAction?.Invoke();
            _lastMessage = string.Empty;
            return true;
        }

        private static string TakeText(TextField field)
        {
            if (field == null) return string.Empty;
            string text = field.value != null ? field.value.Trim() : string.Empty;
            field.value = string.Empty;
            return text;
        }

        private static void SetVisible(VisualElement element, bool visible)
        {
            if (element != null) element.EnableInClassList(HiddenClass, !visible);
        }

        private static void SetText(Label label, string text)
        {
            if (label != null) label.text = text;
        }
    }
}
