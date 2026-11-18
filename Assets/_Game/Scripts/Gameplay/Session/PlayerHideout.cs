using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Hideout;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Persistence;
using ExtractionShooter.Core.Trading;
using ExtractionShooter.Gameplay.Hideout;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Trading;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// The player's hideout facilities, on the same object as the <see cref="PlayerSession"/>. Only in the hideout:
    /// <list type="bullet">
    /// <item>Stash upgrades: bigger stash levels bought with credits (rows are added; items never move).</item>
    /// <item>Workbench: recipes consume stash items (and credits) and finish after real time, also while offline;
    /// the finished item is collected into the stash.</item>
    /// <item>Gun bench presets: save a weapon's attachments under a name, and buy a preset (owned parts are used,
    /// the rest is bought from the trader) assembled into the stash in one all-or-nothing step.</item>
    /// </list>
    /// Fitting and removing attachments at the bench reuse the session's transfer and detach requests.
    /// The server validates and applies everything; state reaches only the owner and is saved with the profile.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerSession))]
    public sealed class PlayerHideout : NetworkBehaviour
    {
        [Tooltip("Stash levels, workbench recipes and preset limits.")]
        [SerializeField] private HideoutDefinition _hideout;

        private readonly SyncVar<int> _stashTier = new SyncVar<int>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncVar<long> _serverClock = new SyncVar<long>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncList<CraftJob> _crafts = new SyncList<CraftJob>(new SyncTypeSettings(ReadPermission.OwnerOnly));
        private readonly SyncList<WeaponPreset> _presets = new SyncList<WeaponPreset>(new SyncTypeSettings(ReadPermission.OwnerOnly));

        private PlayerSession _session;
        private PlayerTrading _trading;
        private long _clockOffsetTicks;

        /// <summary>Owner side: the stash level, the craft queue or the presets changed.</summary>
        public event Action Changed;

        /// <summary>Owner side: the server's answer to a hideout request (message, whether it went through).</summary>
        public event Action<string, bool> Message;

        public HideoutDefinition Definition => _hideout;

        /// <summary>Stash level; 0 is the free stash. Readable by the owner and the server.</summary>
        public int StashTier => _stashTier.Value;

        /// <summary>Crafts running or waiting to be collected, oldest first.</summary>
        public IReadOnlyList<CraftJob> Crafts => _crafts;

        public IReadOnlyList<WeaponPreset> Presets => _presets;

        public int CraftSlots => _hideout != null ? _hideout.CraftSlots : 1;
        public int FreeCraftSlots => Mathf.Max(0, CraftSlots - _crafts.Count);
        public int MaxPresets => _hideout != null ? _hideout.MaxPresets : 0;

        /// <summary>The server's UTC time as best known here (the owner corrects for its own clock being off).</summary>
        public long UtcNowTicks => DateTime.UtcNow.Ticks + (IsServerInitialized ? 0L : _clockOffsetTicks);

        private long Money => _trading != null ? _trading.Money : 0L;

        private void Awake()
        {
            _session = GetComponent<PlayerSession>();
            _trading = GetComponent<PlayerTrading>();
            _stashTier.OnChange += OnStashTierChanged;
            _serverClock.OnChange += OnServerClockChanged;
            _crafts.OnChange += OnCraftsChanged;
            _presets.OnChange += OnPresetsChanged;
            if (_hideout == null) Debug.LogError($"[{nameof(PlayerHideout)}] No hideout definition assigned on {name}.", this);
        }

        private void OnDestroy()
        {
            _stashTier.OnChange -= OnStashTierChanged;
            _serverClock.OnChange -= OnServerClockChanged;
            _crafts.OnChange -= OnCraftsChanged;
            _presets.OnChange -= OnPresetsChanged;
        }

        // ---------------- Queries (owner and server) ----------------

        /// <summary>The next stash size and its price, or false when the stash is already the biggest.</summary>
        public bool TryGetNextStash(out StashTier next)
        {
            next = default;
            if (_hideout == null || !HideoutUpgrades.TryGetNextPrice(_stashTier.Value, _hideout.StashPrices, out _)) return false;
            next = _hideout.StashTiers[HideoutUpgrades.ClampLevel(_stashTier.Value, _hideout.StashTiers.Count) + 1];
            return true;
        }

        /// <summary>The trader's price for one of an item, or -1 when the trader does not sell it.</summary>
        public long BuyPrice(string itemId)
        {
            TraderDefinition trader = _trading != null ? _trading.Trader : null;
            if (trader == null || string.IsNullOrEmpty(itemId)) return -1;
            foreach (TraderOffer offer in trader.Offers)
                if (offer != null && offer.Item != null && offer.Item.Id == itemId) return TradeMath.BuyPrice(offer.Item.Value, 1, trader.BuyMarkup);
            return -1;
        }

        /// <summary>What buying a preset would use and cost against the stash as this side sees it.</summary>
        public PresetPlan PlanPreset(int index)
        {
            SlotInventory stash = _session != null && _session.Stash != null ? _session.Stash.Layout : null;
            if (stash == null || index < 0 || index >= _presets.Count) return null;
            return PresetAssembly.Plan(_presets[index], stash, BuyPrice);
        }

        // ---------------- Owner requests ----------------

        public void RequestUpgradeStash()
        {
            if (IsOwner) ServerUpgradeStash();
        }

        /// <summary>Starts a craft of the recipe at <paramref name="recipeIndex"/> in the hideout definition.</summary>
        public void RequestCraft(int recipeIndex)
        {
            if (IsOwner) ServerCraft(recipeIndex);
        }

        /// <summary>Moves a finished craft into the stash; -1 collects every finished craft.</summary>
        public void RequestCollect(int jobIndex = -1)
        {
            if (IsOwner) ServerCollect(jobIndex);
        }

        /// <summary>Saves the weapon in a stash or loadout slot (its fitted attachments) as a preset; same name replaces.</summary>
        public void RequestSavePreset(ItemContainer container, int slot, string presetName)
        {
            if (!IsOwner || _session == null) return;
            SessionContainer role = _session.RoleOf(container);
            if (role == SessionContainer.Stash || role == SessionContainer.Loadout) ServerSavePreset(role, slot, presetName ?? string.Empty);
        }

        public void RequestDeletePreset(int index)
        {
            if (IsOwner) ServerDeletePreset(index);
        }

        /// <summary>Builds a preset into the stash, buying whatever is not owned.</summary>
        public void RequestBuyPreset(int index)
        {
            if (IsOwner) ServerBuyPreset(index);
        }

        // ---------------- Server: profile ----------------

        /// <summary>
        /// Server, on sign-in before the containers are restored: sizes the stash for the saved level and restores the
        /// craft queue and presets. <paramref name="profile"/> null (a new player) starts at the defaults.
        /// </summary>
        internal void ServerLoad(PlayerProfile profile)
        {
            ServerApplyStashTier(profile != null ? profile.StashTier : 0);

            _crafts.Clear();
            if (profile != null && _hideout != null)
                foreach (CraftJob job in HideoutCodec.LoadCrafts(profile.Crafts, id => _hideout.TryGetRecipe(id, out _)))
                    _crafts.Add(job);

            _presets.Clear();
            ItemDatabase database = Database;
            if (profile != null && database != null)
                foreach (WeaponPreset preset in HideoutCodec.LoadPresets(profile.Presets, database, database, MaxPresets))
                    _presets.Add(preset);

            _serverClock.Value = DateTime.UtcNow.Ticks;
        }

        /// <summary>Server, when saving: writes the stash level, the craft queue and the presets into the profile.</summary>
        internal void ServerWriteTo(PlayerProfile profile)
        {
            if (profile == null) return;
            profile.StashTier = _stashTier.Value;
            profile.Crafts = new List<StoredCraft>(_crafts.Count);
            foreach (CraftJob job in _crafts) profile.Crafts.Add(HideoutCodec.ToStored(job));
            profile.Presets = new List<StoredPreset>(_presets.Count);
            foreach (WeaponPreset preset in _presets) profile.Presets.Add(HideoutCodec.ToStored(preset));
        }

        /// <summary>Server: sizes the stash for a level (clamped). Growing keeps every item in place; false if it could not be resized.</summary>
        private bool ServerApplyStashTier(int tier)
        {
            if (_hideout == null || _hideout.StashTiers.Count == 0 || _session == null || _session.Stash == null) return false;

            int clamped = HideoutUpgrades.ClampLevel(tier, _hideout.StashTiers.Count);
            StashTier size = _hideout.StashTiers[clamped];
            if (!_session.Stash.ServerResize(size.Cells, size.Width))
            {
                Debug.LogWarning($"[{nameof(PlayerHideout)}] Could not resize the stash of {_session.PlayerName} to level {clamped}.", this);
                return false;
            }
            _stashTier.Value = clamped;
            return true;
        }

        // ---------------- Server: stash upgrade ----------------

        [ServerRpc]
        private void ServerUpgradeStash()
        {
            if (_session.State != SessionState.Hideout || _hideout == null) return;

            switch (HideoutUpgrades.Check(_stashTier.Value, _hideout.StashPrices, Money))
            {
                case UpgradeCheck.MaxLevel:
                    Reply("The stash is already at its biggest.", false);
                    return;
                case UpgradeCheck.NotEnoughCredits:
                    Reply("Not enough credits for the stash upgrade.", false);
                    return;
            }

            TryGetNextStash(out StashTier next);
            if (_trading == null || !_trading.ServerTryCharge(next.Price)) return;
            if (!ServerApplyStashTier(_stashTier.Value + 1))
            {
                _trading.ServerSetMoney(Money + next.Price);
                Reply("The stash could not be upgraded.", false);
                return;
            }

            _session.ServerAfterChange();
            Reply($"Stash upgraded to {next.Cells} cells for {PlayerTrading.FormatCredits(next.Price)}.", true);
        }

        // ---------------- Server: workbench ----------------

        [ServerRpc]
        private void ServerCraft(int recipeIndex)
        {
            SlotInventory stash = _session.ResolveAccessible(SessionContainer.Stash);
            if (stash == null || _hideout == null || !_hideout.TryGetRecipe(recipeIndex, out CraftRecipeDefinition definition)) return;

            CraftRecipe recipe = definition.Recipe;
            switch (Crafting.Check(recipe, stash, Money, FreeCraftSlots))
            {
                case CraftCheck.InvalidRecipe:
                    return;
                case CraftCheck.MissingInputs:
                    Reply($"Missing materials for {definition.DisplayName}.", false);
                    return;
                case CraftCheck.NotEnoughCredits:
                    Reply($"Not enough credits for {definition.DisplayName}.", false);
                    return;
                case CraftCheck.QueueFull:
                    Reply("The workbench is busy: collect or wait for a craft to finish.", false);
                    return;
            }

            if (recipe.CreditCost > 0 && (_trading == null || !_trading.ServerTryCharge(recipe.CreditCost))) return;
            if (!Crafting.TryConsumeInputs(stash, recipe))
            {
                if (recipe.CreditCost > 0) _trading.ServerSetMoney(Money + recipe.CreditCost);
                Reply($"Missing materials for {definition.DisplayName}.", false);
                return;
            }

            long now = DateTime.UtcNow.Ticks;
            _crafts.Add(CraftQueue.Start(recipe.Id, recipe.Seconds, now));
            _serverClock.Value = now;
            _session.ServerAfterChange();
            Reply($"Crafting {definition.DisplayName} ({CraftQueue.FormatDuration(recipe.Seconds)}).", true);
        }

        [ServerRpc]
        private void ServerCollect(int jobIndex)
        {
            SlotInventory stash = _session.ResolveAccessible(SessionContainer.Stash);
            if (stash == null || _hideout == null) return;

            long now = DateTime.UtcNow.Ticks;
            _serverClock.Value = now;
            int collected = 0;
            string lastName = null, blocked = null;
            for (int i = _crafts.Count - 1; i >= 0; i--)
            {
                if (jobIndex >= 0 && i != jobIndex) continue;
                CraftJob job = _crafts[i];
                if (!CraftQueue.IsDone(job, now)) continue;

                if (!_hideout.TryGetRecipe(job.RecipeId, out CraftRecipeDefinition definition))
                {
                    _crafts.RemoveAt(i);
                    continue;
                }

                ItemStack output = ItemFactory.ServerCreate(NetworkManager, definition.Output, definition.OutputQuantity);
                if (output.IsEmpty || !Crafting.TryPlaceOutput(stash, output))
                {
                    ItemFactory.ServerDestroy(NetworkManager, output);
                    blocked = definition.Output != null ? definition.Output.DisplayName : definition.DisplayName;
                    continue;
                }

                _crafts.RemoveAt(i);
                collected++;
                lastName = Describe(definition.Output, output.Quantity);
            }

            if (collected > 0) _session.ServerAfterChange();
            if (blocked != null) Reply($"No room in the stash for {blocked}.", false);
            else if (collected == 1) Reply($"Collected {lastName}.", true);
            else if (collected > 1) Reply($"Collected {collected} crafts.", true);
        }

        // ---------------- Server: gun bench presets ----------------

        [ServerRpc]
        private void ServerSavePreset(SessionContainer role, int slot, string presetName)
        {
            if (role != SessionContainer.Stash && role != SessionContainer.Loadout) return;
            SlotInventory inventory = _session.ResolveAccessible(role);
            ItemDatabase database = Database;
            if (inventory == null || database == null) return;

            int anchor = inventory.AnchorAt(slot);
            if (anchor < 0) return;
            ItemStack weapon = inventory[anchor];
            if (database.MagazineSize(weapon.ItemId, null) <= 0) return;

            var preset = new WeaponPreset(presetName, weapon.ItemId, weapon.Attachments);
            PresetSaveResult result = WeaponPresets.Save(_presets, preset, MaxPresets, database, database);
            switch (result)
            {
                case PresetSaveResult.InvalidName:
                    Reply($"Name the preset with 1–{WeaponPresets.MaxNameLength} letters, digits or spaces.", false);
                    return;
                case PresetSaveResult.InvalidWeapon:
                    Reply("That weapon can't be saved as a preset.", false);
                    return;
                case PresetSaveResult.Full:
                    Reply($"Preset limit reached ({MaxPresets}). Delete one first.", false);
                    return;
                default:
                    WeaponPresets.TryNormalizeName(presetName, out string shown);
                    Reply(result == PresetSaveResult.Replaced ? $"Preset '{shown}' updated." : $"Preset '{shown}' saved.", true);
                    break;
            }
            _session.ServerAfterChange();
        }

        [ServerRpc]
        private void ServerDeletePreset(int index)
        {
            if (_session.State != SessionState.Hideout || index < 0 || index >= _presets.Count) return;
            string presetName = _presets[index].Name;
            _presets.RemoveAt(index);
            _session.ServerAfterChange();
            Reply($"Preset '{presetName}' deleted.", true);
        }

        [ServerRpc]
        private void ServerBuyPreset(int index)
        {
            SlotInventory stash = _session.ResolveAccessible(SessionContainer.Stash);
            ItemDatabase database = Database;
            if (stash == null || database == null || index < 0 || index >= _presets.Count) return;

            WeaponPreset preset = _presets[index];
            PresetPlan plan = PresetAssembly.Plan(preset, stash, BuyPrice);
            if (!plan.CanBuy)
            {
                string missing = database.TryGet(plan.Unavailable, out ItemDefinition item) ? item.DisplayName : "a part";
                Reply($"The trader doesn't sell {missing}.", false);
                return;
            }
            if (plan.Cost > Money)
            {
                Reply($"Not enough credits for '{preset.Name}' ({PlayerTrading.FormatCredits(plan.Cost)}).", false);
                return;
            }

            // Charged first and refunded on failure, so the purchase is all or nothing.
            if (plan.Cost > 0 && (_trading == null || !_trading.ServerTryCharge(plan.Cost))) return;
            if (!PresetAssembly.TryAssemble(stash, preset, plan))
            {
                if (plan.Cost > 0) _trading.ServerSetMoney(Money + plan.Cost);
                Reply("No room in the stash for the weapon.", false);
                return;
            }

            _session.ServerAfterChange();
            Reply(plan.Cost > 0
                ? $"Built '{preset.Name}' for {PlayerTrading.FormatCredits(plan.Cost)}."
                : $"Built '{preset.Name}' from your stash.", true);
        }

        // ---------------- Helpers ----------------

        private ItemDatabase Database => _session != null && _session.Stash != null ? _session.Stash.ItemDatabase : null;

        private void Reply(string message, bool success)
        {
            if (Owner.IsValid) TargetMessage(Owner, message, success);
        }

        [TargetRpc]
        private void TargetMessage(NetworkConnection connection, string message, bool success) => Message?.Invoke(message, success);

        private static string Describe(ItemDefinition item, int count)
        {
            string itemName = item != null ? item.DisplayName : "item";
            return count > 1 ? $"{itemName} x{count}" : itemName;
        }

        private void OnStashTierChanged(int previous, int current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnServerClockChanged(long previous, long current, bool asServer)
        {
            if (!asServer) _clockOffsetTicks = current - DateTime.UtcNow.Ticks;
        }

        private void OnCraftsChanged(SyncListOperation operation, int index, CraftJob previous, CraftJob current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }

        private void OnPresetsChanged(SyncListOperation operation, int index, WeaponPreset previous, WeaponPreset current, bool asServer)
        {
            if (!asServer) Changed?.Invoke();
        }
    }
}
