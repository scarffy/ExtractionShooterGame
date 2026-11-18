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
    /// The lobby for the local <see cref="PlayerSession"/>: sign in with a player name, then three tabs.
    /// <list type="bullet">
    /// <item>Stash: the loadout (left), its chest rig and backpack (middle) and the stash (right). Same controls as the
    /// raid inventory: click quick-moves (stash ↔ loadout), Alt+click moves one, drag places in a slot,
    /// right-click a bag in the stash opens it.</item>
    /// <item>Trader: the trader's offers (left, click buys) beside the stash (click sells, Alt+click sells one).</item>
    /// <item>Squad: create / join / invite, ready up (<see cref="SquadPanel"/>) beside the stash.</item>
    /// </list>
    /// Credits, the matchmaking status and Find match show in the header. Shown whenever the player is not in a raid.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class StashScreen : MonoBehaviour
    {
        private const string HiddenClass = "hidden";
        private const string LastNameKey = "ExtractionShooter.PlayerName";
        private const string StashCompactClass = "stash-scroll--compact";
        private const string TabActiveClass = "hideout-tab--active";
        private const string MessageFailedClass = "trade-message--failed";
        private const string StashHint = "Click: stash / equip   ·   Alt+click: move one   ·   Drag: place / fit attachment   ·   Right-click: open bag / remove attachment   ·   Safe box and knife are kept on death";
        private const string TraderHint = "Click an offer: buy   ·   Click in the stash: sell   ·   Alt+click: sell one   ·   Right-click a bag: open";
        private const string SquadHint = "Invite by name or share the squad code   ·   Everyone sets Ready, then the leader presses Find match   ·   Solo: just press Find match";

        /// <summary>Which hideout tab is showing.</summary>
        private enum Tab { Stash, Trader, Squad }

        [SerializeField] private ItemDatabase _itemDatabase;
        [SerializeField] private VisualTreeAsset _slotTemplate;

        [Tooltip("Renders the character with the current loadout for the hideout.")]
        [SerializeField] private CharacterPreview _preview;

        private VisualElement _screen;
        private VisualElement _signInPanel;
        private VisualElement _main;
        private VisualElement _bagPanel;
        private TextField _nameField;
        private Button _signInButton;
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
        private Button _squadTab;
        private VisualElement _squadColumn;
        private SquadPanel _squadPanel;
        private RaidSummaryPanel _summaryPanel;
        private VisualElement _characterColumn;
        private VisualElement _equipmentColumn;
        private VisualElement _storageColumn;
        private VisualElement _traderColumn;
        private TraderPanel _traderPanel;
        private Tab _tab = Tab.Stash;

        private VisualElement _stashScroll;
        private VisualElement _previewImage;
        private Label _gearValue;
        private Label _safeBoxTitle;
        private Button _upgradeButton;
        private Button _sortButton;
        private Label _characterName;
        private Label _pocketsTitle;
        private Label _safeBoxName;
        private Label _stashTitle;
        private Label _weight;
        private float _nextTitleRefresh;

        private SlotGridView _loadoutView;
        private SlotGridView _pocketsView;
        private SlotGridView _safeBoxView;
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
            _squadTab = root.Q<Button>("tab-squad");
            _squadColumn = root.Q<VisualElement>("squad-column");
            _characterColumn = root.Q<VisualElement>("hideout-character-column");
            _equipmentColumn = root.Q<VisualElement>("hideout-equipment-column");
            _storageColumn = root.Q<VisualElement>("hideout-storage-column");
            _traderColumn = root.Q<VisualElement>("trader-column");

            _traderPanel = new TraderPanel(root.Q<VisualElement>("trader-grid"), root.Q<VisualElement>("trader-filters"),
                root.Q<Label>("trader-title"), _slotTemplate, _itemDatabase);
            _traderPanel.DetailsChanged += SetDetails;

            _drag = new InventoryDragController(_screen);
            _squadPanel = new SquadPanel(root, () => _drag.Cancel());
            _summaryPanel = new RaidSummaryPanel(root.Q<VisualElement>("raid-summary"));
            _drag.Dropped += OnDropped;
            _drag.CanFit = (held, under) => _itemDatabase != null && _itemDatabase.Fits(held.ItemId, under.ItemId);

            _loadoutView = CreateView(root, "loadout-grid", "hideout-equipment-column", slot => EquipmentRules.DisplayName((EquipmentSlot)slot));
            LoadoutLayout.HostEquipment(_loadoutView, root, "hideout-");
            _pocketsView = CreateView(root, "hideout-pockets-grid", "hideout-pockets-panel");
            _safeBoxView = CreateView(root, "hideout-safebox-grid", "hideout-safebox-panel");
            _previewImage = root.Q<VisualElement>("hideout-character-preview");
            _characterName = root.Q<Label>("hideout-character-name");
            _pocketsTitle = root.Q<Label>("hideout-pockets-title");
            _safeBoxName = root.Q<Label>("hideout-safebox-name");
            _stashTitle = root.Q<Label>("stash-title");
            _sortButton = root.Q<Button>("stash-sort-button");
            _stashScroll = root.Q<VisualElement>("stash-scroll");
            _gearValue = root.Q<Label>("hideout-gear-value");
            _weight = root.Q<Label>("hideout-character-weight");
            _safeBoxTitle = root.Q<Label>("hideout-safebox-title");
            _upgradeButton = root.Q<Button>("safebox-upgrade-button");
            _rigView = CreateView(root, "hideout-rig-grid", "hideout-rig-panel");
            _backpackView = CreateView(root, "hideout-backpack-grid", "hideout-backpack-panel");
            _stashView = CreateView(root, "stash-grid", "stash-panel");
            _bagView = CreateView(root, "hideout-bag-grid", "hideout-bag-panel");

            if (_nameField != null) _nameField.value = PlayerPrefs.GetString(LastNameKey, string.Empty);
            if (_signInButton != null) _signInButton.clicked += SignIn;
            if (_closeBagButton != null) _closeBagButton.clicked += CloseBag;
            if (_stashTab != null) _stashTab.clicked += ShowStashTab;
            if (_traderTab != null) _traderTab.clicked += ShowTraderTab;
            if (_squadTab != null) _squadTab.clicked += ShowSquadTab;
            if (_upgradeButton != null) _upgradeButton.clicked += UpgradeSafeBox;
            if (_sortButton != null) _sortButton.clicked += SortStash;

            PlayerSession.LocalSessionChanged += Bind;
            Bind(PlayerSession.Local);
        }

        /// <summary>Lets the drag controller read the rotate key.</summary>
        private void Update()
        {
            _drag?.Tick();

            // Section fill counts ("Chest rig: 6/12") follow every move without wiring each bag's events.
            if (Time.unscaledTime < _nextTitleRefresh || _session == null || _session.State != SessionState.Hideout) return;
            _nextTitleRefresh = Time.unscaledTime + 0.25f;
            RefreshTitles();
        }

        private void RefreshTitles()
        {
            SetText(_rigTitle, LoadoutLayout.SectionTitle(_session.LoadoutRig, "Chest rig"));
            SetText(_backpackTitle, LoadoutLayout.SectionTitle(_session.LoadoutBackpack, "Backpack"));
            SetText(_pocketsTitle, LoadoutLayout.SectionTitle(_session.LoadoutPockets, "Pockets"));
            SetText(_safeBoxTitle, LoadoutLayout.SectionTitle(_session.SafeBox, "Safe box"));
            SetText(_stashTitle, LoadoutLayout.SectionTitle(_session.Stash, "Stash"));

            // What the loadout will weigh in the raid (same default limits as the player's PlayerEncumbrance).
            if (_itemDatabase != null)
                WeightLabel.Show(_weight, GearWeight.Of(_session.Loadout, _itemDatabase) + GearWeight.Of(_session.LoadoutRig, _itemDatabase) +
                                          GearWeight.Of(_session.LoadoutBackpack, _itemDatabase) +
                                          GearWeight.Of(_session.LoadoutPockets, _itemDatabase), Encumbrance.Default);
        }

        private void SortStash()
        {
            _drag.Cancel();
            if (_session != null) _session.RequestSortStash();
        }

        private void OnDisable()
        {
            PlayerSession.LocalSessionChanged -= Bind;
            if (_signInButton != null) _signInButton.clicked -= SignIn;
            if (_closeBagButton != null) _closeBagButton.clicked -= CloseBag;
            if (_stashTab != null) _stashTab.clicked -= ShowStashTab;
            if (_traderTab != null) _traderTab.clicked -= ShowTraderTab;
            if (_squadTab != null) _squadTab.clicked -= ShowSquadTab;
            if (_upgradeButton != null) _upgradeButton.clicked -= UpgradeSafeBox;
            if (_sortButton != null) _sortButton.clicked -= SortStash;
            if (_traderPanel != null) _traderPanel.DetailsChanged -= SetDetails;
            Bind(null);
            _squadPanel?.Dispose();
            _squadPanel = null;
            _summaryPanel?.Dispose();
            _summaryPanel = null;
        }

        private SlotGridView CreateView(VisualElement root, string gridName, string panelName, System.Func<int, string> caption = null)
        {
            VisualElement grid = root.Q<VisualElement>(gridName) ?? new VisualElement();
            var view = new SlotGridView(grid, _slotTemplate, _itemDatabase, caption);
            view.SlotClicked += OnSlotClicked;
            view.SlotHovered += ShowDetails;
            view.SlotPressed += _drag.Press;
            view.SlotSecondaryClicked += OnSecondaryClicked;

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
                _session.RaidSummaryReceived -= OnRaidSummary;
                Unwatch(_session.Loadout);
                Unwatch(_session.LoadoutPockets);
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
                _session.RaidSummaryReceived += OnRaidSummary;
                Watch(_session.Loadout);
                Watch(_session.LoadoutPockets);
                if (_session.Trading != null)
                {
                    _session.Trading.MoneyChanged += RefreshCredits;
                    _session.Trading.TradeCompleted += OnTradeCompleted;
                }
            }
            SetText(_tradeMessage, string.Empty);
            _squadPanel?.Bind(_session != null ? _session.GetComponent<PlayerSquad>() : null);
            Refresh();
        }

        private void OnRaidSummary(ExtractionShooter.Core.Raid.RaidSummary summary)
        {
            _drag.Cancel();
            _summaryPanel?.Show(summary);
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
            _pocketsView.Bind(_session.LoadoutPockets);
            _safeBoxView.Bind(_session.SafeBox);
            RefreshPreview();

            ItemContainer bag = _session.OpenBag;
            _bagView.Bind(bag);
            SetVisible(_bagPanel, bag != null);
            // Shorter stash while a bag is open, so both fit on screen.
            if (_stashScroll != null) _stashScroll.EnableInClassList(StashCompactClass, bag != null);
            if (bag != null) SetText(_bagTitle, bag.DisplayName);

            bool trader = IsTrading;
            bool squad = _tab == Tab.Squad && _squadColumn != null;
            bool stash = !trader && !squad;
            SetVisible(_characterColumn, stash);
            SetVisible(_equipmentColumn, stash);
            SetVisible(_storageColumn, stash);
            SetText(_characterName, _session.PlayerName);
            RefreshTitles();
            SetVisible(_traderColumn, trader);
            SetVisible(_squadColumn, squad);
            SetVisible(_traderTab, _session.Trading != null);
            if (_stashTab != null) _stashTab.EnableInClassList(TabActiveClass, stash);
            if (_traderTab != null) _traderTab.EnableInClassList(TabActiveClass, trader);
            if (_squadTab != null) _squadTab.EnableInClassList(TabActiveClass, squad);
            SetText(_hint, trader ? TraderHint : squad ? SquadHint : StashHint);
            if (trader) _traderPanel.Bind(_session.Trading);
            RefreshCredits();
        }

        private void RefreshCredits()
        {
            PlayerTrading trading = _session != null ? _session.Trading : null;
            SetText(_credits, trading != null ? PlayerTrading.FormatCredits(trading.Money) : string.Empty);
            if (trading != null && _tab == Tab.Trader) _traderPanel.Refresh();
            RefreshSafeBox();
        }

        /// <summary>Character render and the value of everything being deployed with.</summary>
        private void RefreshPreview()
        {
            if (_preview != null)
            {
                _preview.Show(_session != null ? _session.Loadout : null);
                if (_previewImage != null && _preview.Texture != null)
                    _previewImage.style.backgroundImage = Background.FromRenderTexture(_preview.Texture);
            }

            if (_session == null || _itemDatabase == null) return;
            long value = GearValue.Of(_session.Loadout, _itemDatabase) + GearValue.Of(_session.LoadoutRig, _itemDatabase) +
                         GearValue.Of(_session.LoadoutBackpack, _itemDatabase) + GearValue.Of(_session.LoadoutPockets, _itemDatabase);
            SetText(_gearValue, $"Gear value  {PlayerTrading.FormatCredits(value)}");
        }

        private void RefreshSafeBox()
        {
            ItemContainer safeBox = _session != null ? _session.SafeBox : null;
            SetText(_safeBoxName, safeBox != null ? safeBox.DisplayName : "Safe box");
            if (_upgradeButton == null) return;

            PlayerTrading trading = _session != null ? _session.Trading : null;
            bool hasNext = trading != null && trading.TryGetNextSafeBox(out var next);
            SetVisible(_upgradeButton, hasNext);
            if (!hasNext) return;
            trading.TryGetNextSafeBox(out next);
            _upgradeButton.text = $"{next.Width}x{next.Height}  ·  {PlayerTrading.FormatCredits(next.Price)}";
            _upgradeButton.SetEnabled(trading.Money >= next.Price);
        }

        private void UpgradeSafeBox()
        {
            _drag.Cancel();
            if (_session != null && _session.Trading != null) _session.Trading.RequestUpgradeSafeBox();
        }

        private void Watch(ItemContainer container)
        {
            if (container != null) container.Changed += Refresh;
        }

        private void Unwatch(ItemContainer container)
        {
            if (container != null) container.Changed -= Refresh;
        }

        private void ShowStashTab() => SetTab(Tab.Stash);

        private void ShowTraderTab() => SetTab(Tab.Trader);

        private void ShowSquadTab() => SetTab(Tab.Squad);

        private void SetTab(Tab tab)
        {
            _drag.Cancel();
            _tab = tab;
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
            SetText(title, LoadoutLayout.SectionTitle(storage, fallbackTitle));
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

        /// <summary>Right click: open a bag from the stash, or take the last attachment off a weapon.</summary>
        private void OnSecondaryClicked(ItemContainer source, int slot)
        {
            if (_session == null) return;
            ItemStack stack = source.GetSlot(slot);
            if (source == _stashView.Container && stack.HasContents) _session.RequestOpenBag(source, slot);
            else if (InventoryScreen.TryGetLastAttachment(stack, out AttachmentSlot fitted)) _session.RequestDetach(source, slot, fitted);
        }

        private void OnDropped(ItemContainer source, int sourceSlot, ItemContainer target, int targetSlot, bool rotated)
        {
            if (_session != null) _session.RequestTransfer(source, sourceSlot, target, targetSlot, 0, rotated);
        }

        private bool IsTrading => _tab == Tab.Trader && _session != null && _session.Trading != null;

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
