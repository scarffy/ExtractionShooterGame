using ExtractionShooter.Core.Input;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Input;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Session;
using ExtractionShooter.UI.Inventory;
using UnityEngine;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The lobby for the local <see cref="PlayerSession"/>: sign in with a player name, then two tabs.
    /// <list type="bullet">
    /// <item>Stash: the loadout (left), its chest rig and backpack (middle) and the stash (right). Same controls as the
    /// raid inventory: click quick-moves (stash ↔ loadout), Alt+click moves one, drag places in a slot,
    /// right-click a bag in the stash opens it.</item>
    /// <item>Trader: the trader's offers (left, click buys) beside the stash (click sells, Alt+click sells one).</item>
    /// </list>
    /// Credits show in the header. Shown whenever the player is not in a raid.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class StashScreen : MonoBehaviour
    {
        private const string HiddenClass = "hidden";
        private const string LastNameKey = "ExtractionShooter.PlayerName";
        private const string TabActiveClass = "hideout-tab--active";
        private const string MessageFailedClass = "trade-message--failed";
        private const string StashHint = "Click: stash / equip   ·   Alt+click: move one   ·   Drag: place in a slot   ·   Right-click a bag in the stash: open   ·   What you deploy with is at risk";
        private const string TraderHint = "Click an offer: buy   ·   Click in the stash: sell   ·   Alt+click: sell one   ·   Right-click a bag: open";

        [SerializeField] private ItemDatabase _itemDatabase;
        [SerializeField] private VisualTreeAsset _slotTemplate;

        private VisualElement _screen;
        private VisualElement _signInPanel;
        private VisualElement _main;
        private VisualElement _bagPanel;
        private TextField _nameField;
        private Button _signInButton;
        private Button _deployButton;
        private Button _closeBagButton;
        private Label _signInError;
        private Label _title;
        private Label _rigTitle;
        private Label _backpackTitle;
        private Label _rigEmpty;
        private Label _backpackEmpty;
        private Label _bagTitle;
        private Label _details;
        private Label _hint;
        private Label _credits;
        private Label _tradeMessage;
        private Button _stashTab;
        private Button _traderTab;
        private VisualElement _gearColumn;
        private VisualElement _middleColumn;
        private VisualElement _traderColumn;
        private TraderPanel _traderPanel;
        private bool _traderMode;

        private SlotGridView _loadoutView;
        private SlotGridView _rigView;
        private SlotGridView _backpackView;
        private SlotGridView _stashView;
        private SlotGridView _bagView;
        private InventoryDragController _drag;

        private PlayerSession _session;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _screen = root.Q<VisualElement>("hideout-screen");
            if (_screen == null)
            {
                Debug.LogError($"[{nameof(StashScreen)}] UXML is missing 'hideout-screen'.", this);
                enabled = false;
                return;
            }

            _signInPanel = root.Q<VisualElement>("sign-in-panel");
            _main = root.Q<VisualElement>("hideout-main");
            _bagPanel = root.Q<VisualElement>("hideout-bag-panel");
            _nameField = root.Q<TextField>("name-field");
            _signInButton = root.Q<Button>("sign-in-button");
            _deployButton = root.Q<Button>("deploy-button");
            _closeBagButton = root.Q<Button>("close-bag-button");
            _signInError = root.Q<Label>("sign-in-error");
            _title = root.Q<Label>("hideout-title");
            _rigTitle = root.Q<Label>("hideout-rig-title");
            _backpackTitle = root.Q<Label>("hideout-backpack-title");
            _rigEmpty = root.Q<Label>("hideout-rig-empty");
            _backpackEmpty = root.Q<Label>("hideout-backpack-empty");
            _bagTitle = root.Q<Label>("hideout-bag-title");
            _details = root.Q<Label>("hideout-details");
            _hint = root.Q<Label>("hideout-hint");
            _credits = root.Q<Label>("hideout-credits");
            _tradeMessage = root.Q<Label>("trade-message");
            _stashTab = root.Q<Button>("tab-stash");
            _traderTab = root.Q<Button>("tab-trader");
            _gearColumn = root.Q<VisualElement>("hideout-gear-column");
            _middleColumn = root.Q<VisualElement>("hideout-middle-column");
            _traderColumn = root.Q<VisualElement>("trader-column");

            _traderPanel = new TraderPanel(root.Q<VisualElement>("trader-grid"), root.Q<VisualElement>("trader-filters"),
                root.Q<Label>("trader-title"), _slotTemplate, _itemDatabase);
            _traderPanel.DetailsChanged += SetDetails;

            _drag = new InventoryDragController(_screen);
            _drag.Dropped += OnDropped;

            _loadoutView = CreateView(root, "loadout-grid", "loadout-panel", slot => EquipmentRules.DisplayName((EquipmentSlot)slot));
            _rigView = CreateView(root, "hideout-rig-grid", "hideout-rig-panel");
            _backpackView = CreateView(root, "hideout-backpack-grid", "hideout-backpack-panel");
            _stashView = CreateView(root, "stash-grid", "stash-panel");
            _bagView = CreateView(root, "hideout-bag-grid", "hideout-bag-panel");
            _stashView.SlotSecondaryClicked += OnStashSecondaryClicked;

            if (_nameField != null) _nameField.value = PlayerPrefs.GetString(LastNameKey, string.Empty);
            if (_signInButton != null) _signInButton.clicked += SignIn;
            if (_deployButton != null) _deployButton.clicked += Deploy;
            if (_closeBagButton != null) _closeBagButton.clicked += CloseBag;
            if (_stashTab != null) _stashTab.clicked += ShowStashTab;
            if (_traderTab != null) _traderTab.clicked += ShowTraderTab;

            PlayerSession.LocalSessionChanged += Bind;
            Bind(PlayerSession.Local);
        }

        private void OnDisable()
        {
            PlayerSession.LocalSessionChanged -= Bind;
            if (_signInButton != null) _signInButton.clicked -= SignIn;
            if (_deployButton != null) _deployButton.clicked -= Deploy;
            if (_closeBagButton != null) _closeBagButton.clicked -= CloseBag;
            if (_stashTab != null) _stashTab.clicked -= ShowStashTab;
            if (_traderTab != null) _traderTab.clicked -= ShowTraderTab;
            if (_traderPanel != null) _traderPanel.DetailsChanged -= SetDetails;
            Bind(null);
        }

        private SlotGridView CreateView(VisualElement root, string gridName, string panelName, System.Func<int, string> caption = null)
        {
            VisualElement grid = root.Q<VisualElement>(gridName) ?? new VisualElement();
            var view = new SlotGridView(grid, _slotTemplate, _itemDatabase, caption);
            view.SlotClicked += OnSlotClicked;
            view.SlotHovered += ShowDetails;
            view.SlotPressed += _drag.Press;

            VisualElement panel = root.Q<VisualElement>(panelName);
            if (panel != null) panel.userData = view;
            return view;
        }

        // ---------------- Binding ----------------

        private void Bind(PlayerSession session)
        {
            if (_session != null)
            {
                _session.Changed -= Refresh;
                _session.SignInFailed -= OnSignInFailed;
                if (_session.Loadout != null) _session.Loadout.Changed -= Refresh;
                if (_session.Trading != null)
                {
                    _session.Trading.MoneyChanged -= RefreshCredits;
                    _session.Trading.TradeCompleted -= OnTradeCompleted;
                }
            }

            _session = session;

            if (_session != null)
            {
                _session.Changed += Refresh;
                _session.SignInFailed += OnSignInFailed;
                if (_session.Loadout != null) _session.Loadout.Changed += Refresh;
                if (_session.Trading != null)
                {
                    _session.Trading.MoneyChanged += RefreshCredits;
                    _session.Trading.TradeCompleted += OnTradeCompleted;
                }
            }
            SetText(_tradeMessage, string.Empty);
            Refresh();
        }

        private void Refresh()
        {
            bool visible = _session != null && _session.State != SessionState.InRaid;
            SetVisible(_screen, visible);
            if (!visible)
            {
                _drag.Cancel();
                return;
            }

            CursorController.SetLocked(false);

            bool signedIn = _session.State == SessionState.Hideout;
            SetVisible(_signInPanel, !signedIn);
            SetVisible(_main, signedIn);
            if (!signedIn) return;

            SetText(_title, $"{_session.PlayerName}  ·  Hideout");
            _loadoutView.Bind(_session.Loadout);
            _stashView.Bind(_session.Stash);
            BindStorage(_rigView, _rigTitle, _rigEmpty, "Chest rig", _session.LoadoutRig);
            BindStorage(_backpackView, _backpackTitle, _backpackEmpty, "Backpack", _session.LoadoutBackpack);

            ItemContainer bag = _session.OpenBag;
            _bagView.Bind(bag);
            SetVisible(_bagPanel, bag != null);
            if (bag != null) SetText(_bagTitle, bag.DisplayName);

            bool trader = _traderMode && _session.Trading != null;
            SetVisible(_gearColumn, !trader);
            SetVisible(_middleColumn, !trader);
            SetVisible(_traderColumn, trader);
            SetVisible(_traderTab, _session.Trading != null);
            if (_stashTab != null) _stashTab.EnableInClassList(TabActiveClass, !trader);
            if (_traderTab != null) _traderTab.EnableInClassList(TabActiveClass, trader);
            SetText(_hint, trader ? TraderHint : StashHint);
            if (trader) _traderPanel.Bind(_session.Trading);
            RefreshCredits();
        }

        private void RefreshCredits()
        {
            PlayerTrading trading = _session != null ? _session.Trading : null;
            SetText(_credits, trading != null ? PlayerTrading.FormatCredits(trading.Money) : string.Empty);
            if (trading != null && _traderMode) _traderPanel.Refresh();
        }

        private void ShowStashTab() => SetTraderMode(false);

        private void ShowTraderTab() => SetTraderMode(true);

        private void SetTraderMode(bool traderMode)
        {
            _drag.Cancel();
            _traderMode = traderMode;
            SetText(_tradeMessage, string.Empty);
            SetDetails(string.Empty);
            Refresh();
        }

        private void OnTradeCompleted(string message, bool success)
        {
            // The hovered item may be gone (sold): its details would be stale.
            if (success) SetDetails(string.Empty);
            SetText(_tradeMessage, message);
            if (_tradeMessage != null) _tradeMessage.EnableInClassList(MessageFailedClass, !success);
        }

        private static void BindStorage(SlotGridView view, Label title, Label emptyLabel, string fallbackTitle, ItemContainer storage)
        {
            view.Bind(storage);
            SetText(title, storage != null ? $"{storage.DisplayName}  ·  {storage.SlotCount}" : fallbackTitle);
            SetVisible(emptyLabel, storage == null);
        }

        // ---------------- Actions ----------------

        private void SignIn()
        {
            if (_session == null || _nameField == null) return;

            string playerName = _nameField.value != null ? _nameField.value.Trim() : string.Empty;
            PlayerPrefs.SetString(LastNameKey, playerName);
            SetText(_signInError, string.Empty);
            _session.RequestSignIn(playerName);
        }

        private void OnSignInFailed(string reason) => SetText(_signInError, reason);

        private void Deploy()
        {
            _drag.Cancel();
            if (_session != null) _session.RequestDeploy();
        }

        private void CloseBag()
        {
            if (_session != null) _session.RequestCloseBag();
        }

        private void OnSlotClicked(ItemContainer source, int slot, bool single)
        {
            if (_drag.IsClickFromDrop || _session == null) return;
            // The click's own modifier, or the keyboard state (runtime UI events may not report modifiers).
            bool moveOne = single || ModifierKeys.SingleItemHeld;

            if (IsTrading && (source == _session.Stash || source == _session.OpenBag))
                _session.Trading.RequestSell(source, slot, moveOne ? 1 : 0);
            else
                _session.RequestQuickMove(source, slot, moveOne ? 1 : 0);
        }

        private void OnStashSecondaryClicked(ItemContainer source, int slot)
        {
            if (_session != null && source.GetSlot(slot).HasContents) _session.RequestOpenBag(source, slot);
        }

        private void OnDropped(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot)
        {
            if (_session != null) _session.RequestTransfer(source, sourceSlot, target, targetSlot);
        }

        private bool IsTrading => _traderMode && _session != null && _session.Trading != null;

        private void ShowDetails(ItemStack stack)
        {
            if (stack.IsEmpty || _itemDatabase == null || !_itemDatabase.TryGet(stack.ItemId, out ItemDefinition item))
            {
                SetDetails(string.Empty);
                return;
            }

            string text = ItemText.Details(item, stack, _itemDatabase);
            if (IsTrading)
            {
                long whole = _session.Trading.QuoteSell(stack, 0, _itemDatabase);
                text += stack.Quantity > 1
                    ? $"   ·   Sells for {PlayerTrading.FormatCredits(whole)} ({PlayerTrading.FormatCredits(_session.Trading.QuoteSell(stack, 1, _itemDatabase))} each)"
                    : $"   ·   Sells for {PlayerTrading.FormatCredits(whole)}";
            }
            SetDetails(text);
        }

        private void SetDetails(string text) => SetText(_details, text);

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
