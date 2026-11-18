using System;
using System.Collections.Generic;

namespace ExtractionShooter.Core.Hideout
{
    /// <summary>An item id and how many of it (a recipe input or output).</summary>
    public readonly struct ItemAmount
    {
        public ItemAmount(string itemId, int quantity)
        {
            ItemId = itemId;
            Quantity = quantity;
        }

        public string ItemId { get; }
        public int Quantity { get; }

        public bool IsEmpty => string.IsNullOrEmpty(ItemId) || Quantity <= 0;

        public override string ToString() => IsEmpty ? "(none)" : $"{ItemId} x{Quantity}";
    }

    /// <summary>
    /// One workbench recipe as plain data: what it consumes from the stash, what it makes, how long it takes and
    /// what it costs. Built from the gameplay recipe asset; the crafting rules (<see cref="Crafting"/>) only see this.
    /// </summary>
    public sealed class CraftRecipe
    {
        private readonly ItemAmount[] _inputs;

        /// <param name="id">Stable id, saved with queued crafts.</param>
        /// <param name="inputs">Items taken from the stash. The same item may appear more than once; amounts add up.</param>
        public CraftRecipe(string id, IEnumerable<ItemAmount> inputs, ItemAmount output, int seconds, long creditCost)
        {
            Id = id;
            var list = new List<ItemAmount>();
            if (inputs != null)
                foreach (ItemAmount input in inputs)
                    if (!input.IsEmpty) list.Add(input);
            _inputs = list.ToArray();
            Output = output;
            Seconds = Math.Max(0, seconds);
            CreditCost = Math.Max(0L, creditCost);
        }

        public string Id { get; }
        public IReadOnlyList<ItemAmount> Inputs => _inputs;
        public ItemAmount Output { get; }

        /// <summary>Real time from starting the craft to the output being ready.</summary>
        public int Seconds { get; }

        /// <summary>Credits charged when the craft starts (0 for none).</summary>
        public long CreditCost { get; }

        /// <summary>Has an id, an output and at least one input.</summary>
        public bool IsValid => !string.IsNullOrEmpty(Id) && !Output.IsEmpty && _inputs.Length > 0;

        /// <summary>The inputs merged by item id (in first-seen order), so duplicates are checked as one requirement.</summary>
        public IReadOnlyList<ItemAmount> Requirements()
        {
            var order = new List<string>();
            var totals = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (ItemAmount input in _inputs)
            {
                if (!totals.ContainsKey(input.ItemId))
                {
                    order.Add(input.ItemId);
                    totals[input.ItemId] = 0;
                }
                totals[input.ItemId] += input.Quantity;
            }

            var merged = new List<ItemAmount>(order.Count);
            foreach (string id in order) merged.Add(new ItemAmount(id, totals[id]));
            return merged;
        }
    }
}
