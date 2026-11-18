using System;
using System.Collections.Generic;
using ExtractionShooter.Core.Hideout;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Hideout;
using ExtractionShooter.Gameplay.Inventory;
using ExtractionShooter.Gameplay.Session;
using ExtractionShooter.UI.Inventory;
using UnityEngine.UIElements;

namespace ExtractionShooter.UI.Hideout
{
    /// <summary>
    /// The hideout's Workbench tab, beside the stash, in two modes:
    /// <list type="bullet">
    /// <item>Crafting: every recipe with its inputs (have / need from the stash), output, time and cost, and a Craft
    /// button; the craft queue with progress, countdown and Collect.</item>
    /// <item>Gun bench (<see cref="GunBenchPanel"/>): edit a weapon's attachments and save or buy presets.</item>
    /// </list>
    /// Every action is a request to the local <see cref="PlayerHideout"/>; the answer comes back through its synced
    /// state and messages. Plain C#: owned by <see cref="StashScreen"/>.
    /// </summary>
    public sealed class WorkbenchPanel : IDisposable
    {
        private const string HiddenClass = "hidden";
        private const string ModeActiveClass = "trader-filter--active";
        private const string RecipeClass = "workbench-recipe";
        private const string RecipeReadyClass = "workbench-recipe--ready";
        private const string ChipClass = "workbench-chip";
        private const string ChipMissingClass = "workbench-chip--missing";
        private const string JobClass = "workbench-job";
        private const string JobDoneClass = "workbench-job--done";

        /// <summary>What the tab shows.</summary>
        private enum Mode { Crafting, GunBench }

        private readonly Action _beforeAction;
        private readonly ItemDatabase _itemDatabase;
        private readonly VisualElement _craftingBody;
        private readonly VisualElement _gunBenchBody;
        private readonly ScrollView _recipeList;
        private readonly VisualElement _queue;
        private readonly Label _queueTitle;
        private readonly Button _collectAllButton;
        private readonly Button _craftingModeButton;
        private readonly Button _gunBenchModeButton;
        private readonly GunBenchPanel _gunBench;
        private readonly List<(Label time, VisualElement fill, Button collect, int index)> _jobViews =
            new List<(Label, VisualElement, Button, int)>();

        private PlayerSession _session;
        private PlayerHideout _hideout;
        private ItemContainer _watchedStash;
        private Mode _mode = Mode.Crafting;
        private bool _visible;
        private int _readyJobs = -1;

        /// <summary>Hovered recipe or bench item details, or empty when the pointer leaves.</summary>
        public event Action<string> DetailsChanged;

        /// <summary>Switched between crafting and the gun bench.</summary>
        public event Action ModeChanged;

        /// <param name="beforeAction">Called before any request is sent (the hideout cancels a drag in progress).</param>
        public WorkbenchPanel(VisualElement root, ItemDatabase itemDatabase, Action beforeAction = null)
        {
            _beforeAction = beforeAction;
            _itemDatabase = itemDatabase;
            _craftingBody = root.Q<VisualElement>("workbench-crafting");
            _gunBenchBody = root.Q<VisualElement>("gunbench");
            _recipeList = root.Q<ScrollView>("workbench-recipes");
            _queue = root.Q<VisualElement>("workbench-queue");
            _queueTitle = root.Q<Label>("workbench-queue-title");
            _collectAllButton = root.Q<Button>("workbench-collect-all");
            _craftingModeButton = root.Q<Button>("workbench-mode-crafting");
            _gunBenchModeButton = root.Q<Button>("workbench-mode-gunbench");
            _gunBench = new GunBenchPanel(root, itemDatabase, beforeAction);
            _gunBench.DetailsChanged += OnDetails;

            if (_collectAllButton != null) _collectAllButton.clicked += CollectAll;
            if (_craftingModeButton != null) _craftingModeButton.clicked += ShowCrafting;
            if (_gunBenchModeButton != null) _gunBenchModeButton.clicked += ShowGunBench;
            ApplyMode();
        }

        /// <summary>True while the gun bench is showing (stash clicks select weapons and fit attachments).</summary>
        public bool IsGunBench => _visible && _mode == Mode.GunBench;

        /// <summary>The gun bench is the chosen mode (whether or not the tab shows).</summary>
        public bool IsGunBenchMode => _mode == Mode.GunBench;

        public void Bind(PlayerSession session)
        {
            if (_hideout != null) _hideout.Changed -= Refresh;
            if (_session != null && _session.Trading != null) _session.Trading.MoneyChanged -= Refresh;
            Watch(null);

            _session = session;
            _hideout = session != null ? session.Hideout : null;

            if (_hideout != null) _hideout.Changed += Refresh;
            if (_session != null && _session.Trading != null) _session.Trading.MoneyChanged += Refresh;
            Watch(session != null ? session.Stash : null);
            _gunBench.Bind(session);
            Refresh();
        }

        /// <summary>Shown with the Workbench tab; hidden panels skip their rebuilds.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            _gunBench.SetVisible(visible && _mode == Mode.GunBench);
            if (visible) Refresh();
        }

        /// <summary>Rebuilds the recipes and the queue (inputs owned, affordability, queue contents) while crafting shows.</summary>
        public void Refresh()
        {
            // The gun bench follows its own events while it shows.
            if (!_visible || _mode != Mode.Crafting) return;
            RebuildRecipes();
            RebuildQueue();
        }

        /// <summary>Called a few times a second: countdowns and progress bars, and a rebuild when a craft finishes.</summary>
        public void Tick()
        {
            if (!_visible || _mode != Mode.Crafting || _hideout == null) return;

            long now = _hideout.UtcNowTicks;
            int ready = 0;
            foreach ((Label time, VisualElement fill, Button collect, int index) in _jobViews)
            {
                if (index >= _hideout.Crafts.Count) continue;
                CraftJob job = _hideout.Crafts[index];
                bool done = CraftQueue.IsDone(job, now);
                if (done) ready++;
                SetText(time, done ? "Ready" : CraftQueue.FormatDuration(CraftQueue.RemainingSeconds(job, now)));
                if (fill != null) fill.style.width = Length.Percent(CraftQueue.Progress(job, now) * 100f);
                collect?.SetEnabled(done);
            }
            if (ready != _readyJobs) RebuildQueue();
        }

        /// <summary>
        /// A click on a stash item while the gun bench shows: selects a weapon or fits an attachment to the selected
        /// weapon. Returns false when the click should do its usual thing.
        /// </summary>
        public bool HandleStashClick(ItemContainer source, int slot) => IsGunBench && _gunBench.HandleClick(source, slot);

        public void Dispose()
        {
            Bind(null);
            if (_collectAllButton != null) _collectAllButton.clicked -= CollectAll;
            if (_craftingModeButton != null) _craftingModeButton.clicked -= ShowCrafting;
            if (_gunBenchModeButton != null) _gunBenchModeButton.clicked -= ShowGunBench;
            _gunBench.DetailsChanged -= OnDetails;
            _gunBench.Dispose();
        }

        // ---------------- Modes ----------------

        private void ShowCrafting() => SetMode(Mode.Crafting);

        private void ShowGunBench() => SetMode(Mode.GunBench);

        private void SetMode(Mode mode)
        {
            _beforeAction?.Invoke();
            _mode = mode;
            DetailsChanged?.Invoke(string.Empty);
            ApplyMode();
            Refresh();
            ModeChanged?.Invoke();
        }

        private void ApplyMode()
        {
            SetVisible(_craftingBody, _mode == Mode.Crafting);
            SetVisible(_gunBenchBody, _mode == Mode.GunBench);
            _craftingModeButton?.EnableInClassList(ModeActiveClass, _mode == Mode.Crafting);
            _gunBenchModeButton?.EnableInClassList(ModeActiveClass, _mode == Mode.GunBench);
            _gunBench.SetVisible(_visible && _mode == Mode.GunBench);
        }

        // ---------------- Recipes ----------------

        private void RebuildRecipes()
        {
            if (_recipeList == null) return;
            _recipeList.Clear();

            HideoutDefinition definition = _hideout != null ? _hideout.Definition : null;
            if (definition == null)
            {
                _recipeList.Add(new Label("No workbench here.") { pickingMode = PickingMode.Ignore });
                return;
            }

            SlotInventory stash = _session != null && _session.Stash != null ? _session.Stash.Layout : null;
            long credits = _session != null && _session.Trading != null ? _session.Trading.Money : 0;
            for (int i = 0; i < definition.Recipes.Count; i++)
            {
                CraftRecipeDefinition recipe = definition.Recipes[i];
                if (recipe == null || !recipe.Recipe.IsValid) continue;
                _recipeList.Add(CreateRecipeRow(recipe, i, stash, credits));
            }
        }

        private VisualElement CreateRecipeRow(CraftRecipeDefinition recipe, int index, SlotInventory stash, long credits)
        {
            var row = new VisualElement();
            row.AddToClassList(RecipeClass);
            if (recipe.Output != null) row.AddToClassList("slot--" + recipe.Output.Category.ToString().ToLowerInvariant());

            var info = new VisualElement();
            info.AddToClassList("workbench-recipe__info");
            row.Add(info);

            var title = new Label(recipe.OutputQuantity > 1 ? $"{recipe.DisplayName}  x{recipe.OutputQuantity}" : recipe.DisplayName);
            title.AddToClassList("workbench-recipe__name");
            info.Add(title);

            var chips = new VisualElement();
            chips.AddToClassList("workbench-chips");
            foreach (RecipeInput input in recipe.Inputs)
            {
                if (input == null || input.Item == null) continue;
                int have = Crafting.CountAvailable(stash, input.Item.Id);
                var chip = new Label($"{input.Item.DisplayName}  {have}/{input.Quantity}") { pickingMode = PickingMode.Ignore };
                chip.AddToClassList(ChipClass);
                chip.EnableInClassList(ChipMissingClass, have < input.Quantity);
                chips.Add(chip);
            }
            info.Add(chips);

            string cost = recipe.CreditCost > 0 ? $"   ·   {PlayerTrading.FormatCredits(recipe.CreditCost)}" : string.Empty;
            var meta = new Label($"{CraftQueue.FormatDuration(recipe.CraftSeconds)}{cost}") { pickingMode = PickingMode.Ignore };
            meta.AddToClassList("workbench-recipe__meta");
            info.Add(meta);

            CraftCheck check = Crafting.Check(recipe.Recipe, stash, credits, _hideout.FreeCraftSlots);
            var craft = new Button(() => Craft(index)) { text = check == CraftCheck.QueueFull ? "Busy" : "Craft" };
            craft.AddToClassList("panel-button");
            craft.AddToClassList("workbench-recipe__button");
            craft.SetEnabled(check == CraftCheck.Ok);
            row.Add(craft);
            row.EnableInClassList(RecipeReadyClass, check == CraftCheck.Ok);

            row.RegisterCallback<PointerEnterEvent>(_ => DetailsChanged?.Invoke(RecipeDetails(recipe, check)));
            row.RegisterCallback<PointerLeaveEvent>(_ => DetailsChanged?.Invoke(string.Empty));
            return row;
        }

        private string RecipeDetails(CraftRecipeDefinition recipe, CraftCheck check)
        {
            string details = recipe.Output != null && _itemDatabase != null
                ? ItemText.Details(recipe.Output, new ItemStack(recipe.Output.Id, recipe.OutputQuantity), _itemDatabase)
                : recipe.DisplayName;
            switch (check)
            {
                case CraftCheck.MissingInputs: return details + "   ·   Missing materials in the stash";
                case CraftCheck.NotEnoughCredits: return details + "   ·   Not enough credits";
                case CraftCheck.QueueFull: return details + "   ·   Workbench busy";
                default: return details;
            }
        }

        private void Craft(int index)
        {
            _beforeAction?.Invoke();
            _hideout?.RequestCraft(index);
        }

        // ---------------- Queue ----------------

        private void RebuildQueue()
        {
            _jobViews.Clear();
            if (_queue == null) return;
            _queue.Clear();
            if (_hideout == null) return;

            HideoutDefinition definition = _hideout.Definition;
            long now = _hideout.UtcNowTicks;
            int ready = 0;
            IReadOnlyList<CraftJob> crafts = _hideout.Crafts;
            for (int i = 0; i < crafts.Count; i++)
            {
                CraftJob job = crafts[i];
                bool done = CraftQueue.IsDone(job, now);
                if (done) ready++;
                CraftRecipeDefinition recipe = null;
                definition?.TryGetRecipe(job.RecipeId, out recipe);
                _queue.Add(CreateJobRow(recipe, job, i, done, now));
            }
            for (int i = crafts.Count; i < _hideout.CraftSlots; i++)
            {
                var empty = new Label("Empty slot") { pickingMode = PickingMode.Ignore };
                empty.AddToClassList(JobClass);
                empty.AddToClassList("workbench-job--empty");
                _queue.Add(empty);
            }

            _readyJobs = ready;
            SetText(_queueTitle, $"Queue  {crafts.Count}/{_hideout.CraftSlots}");
            SetVisible(_collectAllButton, ready > 1);
        }

        private VisualElement CreateJobRow(CraftRecipeDefinition recipe, CraftJob job, int index, bool done, long now)
        {
            var row = new VisualElement();
            row.AddToClassList(JobClass);
            row.EnableInClassList(JobDoneClass, done);

            var header = new VisualElement();
            header.AddToClassList("workbench-job__header");
            string itemName = recipe == null ? job.RecipeId : recipe.OutputQuantity > 1 ? $"{recipe.DisplayName} x{recipe.OutputQuantity}" : recipe.DisplayName;
            var name = new Label(itemName) { pickingMode = PickingMode.Ignore };
            name.AddToClassList("workbench-job__name");
            header.Add(name);
            var time = new Label(done ? "Ready" : CraftQueue.FormatDuration(CraftQueue.RemainingSeconds(job, now))) { pickingMode = PickingMode.Ignore };
            time.AddToClassList("workbench-job__time");
            header.Add(time);
            row.Add(header);

            var bar = new VisualElement { pickingMode = PickingMode.Ignore };
            bar.AddToClassList("workbench-progress");
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList("workbench-progress__fill");
            fill.style.width = Length.Percent(CraftQueue.Progress(job, now) * 100f);
            bar.Add(fill);
            row.Add(bar);

            var collect = new Button(() => Collect(index)) { text = "Collect" };
            collect.AddToClassList("panel-button");
            collect.AddToClassList("workbench-job__button");
            collect.SetEnabled(done);
            row.Add(collect);

            _jobViews.Add((time, fill, collect, index));
            return row;
        }

        private void Collect(int index)
        {
            _beforeAction?.Invoke();
            _hideout?.RequestCollect(index);
        }

        private void CollectAll()
        {
            _beforeAction?.Invoke();
            _hideout?.RequestCollect();
        }

        // ---------------- Helpers ----------------

        private void Watch(ItemContainer stash)
        {
            if (_watchedStash != null) _watchedStash.Changed -= Refresh;
            _watchedStash = stash;
            if (_watchedStash != null) _watchedStash.Changed += Refresh;
        }

        private void OnDetails(string text) => DetailsChanged?.Invoke(text);

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
