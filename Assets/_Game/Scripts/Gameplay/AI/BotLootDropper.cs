using System.Collections;
using ExtractionShooter.Core.Combat;
using ExtractionShooter.Core.Inventory;
using ExtractionShooter.Gameplay.Combat;
using ExtractionShooter.Gameplay.Inventory;
using FishNet.Object;
using UnityEngine;

namespace ExtractionShooter.Gameplay.AI
{
    /// <summary>
    /// Server only: when the bot dies, leaves a lootable body with its weapon (with the rounds still loaded),
    /// spare ammo for it and a roll of its loot table, then removes the bot.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BotBrain), typeof(Health))]
    public sealed class BotLootDropper : NetworkBehaviour
    {
        [Tooltip("Networked prefab with an ItemContainer (access: World).")]
        [SerializeField] private ItemContainer _corpsePrefab;

        [Tooltip("Seconds between death and the bot disappearing (the body stays).")]
        [SerializeField, Min(0f)] private float _despawnDelay = 0.5f;

        private BotBrain _brain;
        private Health _health;

        private void Awake()
        {
            _brain = GetComponent<BotBrain>();
            _health = GetComponent<Health>();

            if (_corpsePrefab == null)
                Debug.LogError($"[{nameof(BotLootDropper)}] Corpse prefab is not assigned on {name}.", this);
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            _health.ServerDied += OnServerDied;
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            _health.ServerDied -= OnServerDied;
        }

        private void OnServerDied(DamageInfo damage)
        {
            BotSettings settings = _brain.Settings;
            if (_corpsePrefab != null && settings != null)
            {
                ItemContainer corpse = Instantiate(_corpsePrefab, transform.position, transform.rotation);
                Spawn(corpse.NetworkObject);
                FillCorpse(corpse.ServerInventory, settings);
            }
            StartCoroutine(DespawnAfterDelay());
        }

        private void FillCorpse(SlotInventory body, BotSettings settings)
        {
            var random = new System.Random(unchecked(System.Environment.TickCount * 17 + ObjectId));

            if (settings.Weapon != null)
            {
                ItemStack weapon = ItemFactory.ServerCreate(NetworkManager, settings.Weapon);
                if (!weapon.IsEmpty && settings.Ammo != null && _brain.RoundsLoaded > 0)
                    weapon = weapon.WithLoadedAmmo(settings.Ammo.Id, _brain.RoundsLoaded);
                body.TryAdd(weapon, out _);
            }

            if (settings.Ammo != null)
            {
                int spare = random.Next(settings.SpareRoundsMin, settings.SpareRoundsMax + 1);
                if (spare > 0) body.TryAdd(settings.Ammo.Id, Mathf.Min(spare, settings.Ammo.MaxStack), out _);
            }

            LootFiller.ServerFill(NetworkManager, settings.LootTable, body, random);
        }

        private IEnumerator DespawnAfterDelay()
        {
            yield return new WaitForSeconds(_despawnDelay);
            if (IsSpawned) Despawn(NetworkObject);
        }
    }
}
