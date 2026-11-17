using System;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Core.Trading;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Trading;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

namespace ExtractionShooter.Gameplay.Session
{
    /// <summary>
    /// The player's credits and their dealings with the lobby trader, on the same object as the
    /// <see cref="PlayerSession"/>. Only in the hideout: buy an offer into the stash, or sell an item
    /// from the stash (or a bag opened in it). The server prices and validates everything; credits are
    /// saved with the profile. New players start with none: credits come from selling loot.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerSession))]
    public sealed class PlayerTrading : NetworkBehaviour
    {
        [SerializeField] private TraderDefinition _trader;

        private readonly SyncVar<long> _money = new SyncVar<long>(new SyncTypeSettings(ReadPermission.OwnerOnly));

        private PlayerSession _session;

        /// <summary>Owner side: credits changed.</summary>
        public event Action MoneyChanged;

        /// <summary>Owner side: the server finished a trade (message for the player, whether it went through).</summary>
        public event Action<string, bool> TradeCompleted;

        public TraderDefinition Trader => _trader;

        /// <summary>Credits. Readable by the owner and the server.</summary>
        public long Money => _money.Value;

        private void Awake()
        {
            _session = GetComponent<PlayerSession>();
            _money.OnChange += OnMoneyChanged;
            if (_trader == null) Debug.LogError($"[{nameof(PlayerTrading)}] No trader assigned on {name}.", this);
        }

        private void OnDestroy() => _money.OnChange -= OnMoneyChanged;

        // ---------------- Owner requests ----------------

        /// <summary>Buys one pack of a trader offer into the stash.</summary>
        public void RequestBuy(int offerIndex)
        {
            if (IsOwner) ServerBuy(offerIndex);
        }

        /// <summary>Buys the next safe box size, if there is one.</summary>
        public void RequestUpgradeSafeBox()
        {
            if (IsOwner) ServerUpgradeSafeBox();
        }

        /// <summary>The next safe box size and its price, or false when the box is already the biggest.</summary>
        public bool TryGetNextSafeBox(out SafeBoxTier next)
        {
            next = default;
            if (_trader == null || _session == null) return false;
            int index = _session.SafeBoxTier + 1;
            if (index >= _trader.SafeBoxTiers.Count) return false;
            next = _trader.SafeBoxTiers[index];
            return true;
        }

        /// <summary>Sells from the stash or a bag opened in it. <paramref name="quantity"/> 0 sells the whole stack.</summary>
        public void RequestSell(ItemContainer source, int slot, int quantity = 0)
        {
            if (!IsOwner || _session == null) return;
            SessionContainer role = _session.RoleOf(source);
            if (role == SessionContainer.Stash || role == SessionContainer.OpenBag) ServerSell(role, slot, quantity);
        }

        /// <summary>What the trader would pay for a stack right now (for display; the server prices the real sale).</summary>
        public long QuoteSell(in ItemStack stack, int quantity, ItemDatabase database) =>
            _trader != null ? _trader.SellPrice(stack, quantity <= 0 ? stack.Quantity : quantity, database) : 0;

        // ---------------- Server ----------------

        /// <summary>Server: set from the profile on sign-in.</summary>
        internal void ServerSetMoney(long money) => _money.Value = Math.Max(0L, money);

        [ServerRpc]
        private void ServerBuy(int offerIndex)
        {
            SlotInventory stash = _session.ResolveAccessible(SessionContainer.Stash);
            if (stash == null || _trader == null || !_trader.TryGetOffer(offerIndex, out TraderOffer offer)) return;

            ItemDefinition item = offer.Item;
            long price = _trader.BuyPrice(offer);
            if (price > _money.Value)
            {
                Reply($"Not enough credits for {item.DisplayName}.", false);
                return;
            }
            if (stash.SpaceFor(new ItemStack(item.Id, 1)) < offer.Quantity)
            {
                Reply("No room in the stash.", false);
                return;
            }

            ItemStack stack = ItemFactory.ServerCreate(NetworkManager, item, offer.Quantity);
            if (stack.IsEmpty)
            {
                Reply($"{item.DisplayName} can't be bought right now.", false);
                return;
            }

            stash.TryAdd(stack, out int added);
            if (added <= 0)
            {
                ItemFactory.ServerDestroy(NetworkManager, stack);
                Reply("No room in the stash.", false);
                return;
            }

            long charged = added == stack.Quantity ? price : TradeMath.BuyPrice(item.Value, added, _trader.BuyMarkup);
            _money.Value -= charged;
            _session.ServerAfterChange();
            Reply($"Bought {Describe(item, added)} for {FormatCredits(charged)}.", true);
        }

        [ServerRpc]
        private void ServerUpgradeSafeBox()
        {
            if (_session.State != SessionState.Hideout || !TryGetNextSafeBox(out SafeBoxTier next)) return;
            if (next.Price > _money.Value)
            {
                Reply($"Not enough credits for the {next.Name}.", false);
                return;
            }
            if (!_session.ServerApplySafeBoxTier(_session.SafeBoxTier + 1))
            {
                Reply("The safe box could not be upgraded.", false);
                return;
            }

            _money.Value -= next.Price;
            _session.ServerAfterChange();
            Reply($"Upgraded to the {next.Name} ({next.Width}x{next.Height}) for {FormatCredits(next.Price)}.", true);
        }

        [ServerRpc]
        private void ServerSell(SessionContainer role, int slot, int quantity)
        {
            SlotInventory from = _session.ResolveAccessible(role);
            if (from == null || _trader == null || !from.IsValidSlot(slot) || from[slot].IsEmpty) return;

            ItemStack stack = from[slot];
            ItemDatabase database = _session.Stash.ItemDatabase;
            if (database == null || !database.TryGet(stack.ItemId, out ItemDefinition item)) return;

            if (ItemContainer.TryResolveContents(NetworkManager, stack, out ItemContainer storage) &&
                storage.ServerInventory != null && !storage.ServerInventory.IsEmpty)
            {
                Reply($"Empty the {item.DisplayName} before selling it.", false);
                return;
            }

            int count = quantity <= 0 ? stack.Quantity : Math.Min(quantity, stack.Quantity);
            long price = _trader.SellPrice(stack, count, database);
            if (price <= 0)
            {
                Reply($"The trader won't pay anything for {item.DisplayName}.", false);
                return;
            }

            ItemStack sold = from.TakeFromSlot(slot, count);
            ItemFactory.ServerDestroy(NetworkManager, sold);
            _money.Value += price;
            _session.ServerAfterChange();
            Reply($"Sold {Describe(item, count)} for {FormatCredits(price)}.", true);
        }

        private void Reply(string message, bool success)
        {
            if (Owner.IsValid) TargetTradeCompleted(Owner, message, success);
        }

        [TargetRpc]
        private void TargetTradeCompleted(NetworkConnection connection, string message, bool success) =>
            TradeCompleted?.Invoke(message, success);

        private void OnMoneyChanged(long previous, long current, bool asServer)
        {
            if (!asServer) MoneyChanged?.Invoke();
        }

        private static string Describe(ItemDefinition item, int count) =>
            count > 1 ? $"{item.DisplayName} x{count}" : item.DisplayName;

        /// <summary>"12,450 cr".</summary>
        public static string FormatCredits(long credits) => $"{credits:N0} cr";
    }
}
