# Extraction Shooter

Unity 6.3 (URP) + FishNet 4.7.4. Server authoritative.

## Project layout
```
Assets/_Game/
  Data/          ScriptableObject assets (movement, weapons, items, loot tables)
  Materials/
  Prefabs/
  Scenes/
  UI/            UXML, USS, panel settings and theme
  Scripts/
    Core/        ExtractionShooter.Core        - engine-level contracts, no networking
    Gameplay/    ExtractionShooter.Gameplay    - player, combat, weapons (FishNet)
    Networking/  ExtractionShooter.Networking  - spawning, server bootstrap
    UI/          ExtractionShooter.UI          - HUD and menus
```
Third-party assets stay outside `_Game` (e.g. `Assets/ThirdParty/Synty`).

### Assembly dependencies (one direction only)
```
UI ─┐
    ├─► Gameplay ─► Core
Networking ─┘
```
Gameplay never references Networking or UI. Cross-module communication goes through
interfaces in Core (`IDamageable`, `ISpawnPointProvider`) and C# events.

## Conventions
- **Namespaces** mirror folders: `ExtractionShooter.<Assembly>.<Folder>`.
- **Naming:** PascalCase types/methods/properties/events/constants, `_camelCase` private
  fields (including `[SerializeField]`), `camelCase` locals/parameters, `I` prefix on interfaces.
  Enforced by `.editorconfig` at the project root.
- **Components:** `sealed` unless designed for inheritance, `[DisallowMultipleComponent]`,
  `[RequireComponent]` for hard dependencies, serialized references validated in `Awake`.
- **Config** lives in ScriptableObjects under `Data/`, not hard-coded values.
- **Networking:** clients request, the server validates and decides. Server-only methods are
  prefixed `Server` (`ServerTeleport`, `ServerRestore`) and server-only events `Server*`.
  RPC methods are named for intent (`RequestShot`, `SubmitInput`, `BroadcastShot`).
- **No editor generators** for scenes or prefabs. Scenes and prefabs are built by hand.

## Player prefab
| Component | Role |
|---|---|
| `PlayerCharacter` | Root facade, owner-only setup, `PlayerCharacter.Local` |
| `PlayerMotor` | Owner sends input per tick, server moves the CharacterController |
| `PlayerLook` | Owner camera, yaw/pitch, eye position |
| `PlayerVisuals` | Hides own body and dead bodies |
| `Health` | Server-owned HP (`IDamageable`); applies `IDamageModifier`s (armor), reports death |
| `HitscanWeapon` / `HitscanWeaponView` | Fires the active equipped weapon (server-validated) / client tracers |
| `ItemContainer` (Equipment) + `PlayerEquipment` | 7 typed gear slots, weapon selection, armor, loadout |
| `PlayerInventory` | Validated item requests and container access |
| `PlayerInteractor` | Finds what the crosshair is on; E to use it |
| `DeathLootDropper` | Moves all gear into a lootable body on death |
| `Hitbox_Head`, `Hitbox_Body` (children) | Trigger hitboxes: head x2 damage + helmet, body + chest pad |
| `InputSystemPlayerInput` | Keyboard/mouse + gamepad (`IPlayerInput`) |
| `NetworkObject`, `NetworkTransform` | FishNet; NetworkTransform is **server authoritative** |

## Inventory & equipment
Server-authoritative and slot-based. Clients only send requests; the server validates every one.

**Equipment (7 slots, type-checked on the server):** Helmet · Body Armor · Chest Rig · Backpack ·
Primary 1 · Primary 2 · Pistol. Pistols only go in the Pistol slot; primaries only in Primary 1/2.

**Storage** comes only from the equipped chest rig and backpack (no pockets). Each bag item owns a
networked storage container that travels with it, so a full backpack stays full when dropped, stored in
a crate or left on a body. Bags cannot go inside bags.

**Weapons:** the active equipped weapon fires. 1/2/3 select Primary 1/Primary 2/Pistol (gamepad Y cycles).
14 weapons over 11 calibers (Data/Weapons): Glock 17, M1911A1, Desert Eagle, MP5, MP7A1, UMP45, M4A1,
AK-74M, AKM, SCAR-H, SR-25, SVD, AWM, M870. The weapon sets caliber, magazine, fire mode (semi/auto),
fire rate, spread, range and reload time.

**Ammo:** 32 types, 2–3 per caliber (Data/Ammo). The round sets damage, penetration, armor damage % and
pellets (buckshot fires 9). Each weapon remembers which ammo is loaded and how many rounds
(`ItemStack.AmmoId/AmmoCount`), so loaded rounds travel with the gun. R reloads from the rig, then the
backpack: tops up the loaded type, otherwise switches to the first compatible type and puts the old
rounds back (`ReloadPlanner`, unit-tested). Pulling the trigger on an empty gun starts a reload.
Switching weapon, moving the gun or dying cancels a reload. Loot weapons spawn empty.

**Armor:** hits land on head or body hitboxes (head x2 damage). Helmet protects the head, chest pad the
body. Armor is class 1–6 and protects with class x 10, falling to half as it wears. Penetration equal to
protection lets 50% through, +10 lets all through, -10 is stopped (blunt damage only). Every hit wears the
armor by damage x armor damage % (`ArmorMath`, unit-tested). Broken armor does nothing.

**Death:** all equipment moves into a lootable body (bags keep contents, armor keeps durability, guns keep
their rounds). The body despawns after 5 minutes.

| Piece | Where | Role |
|---|---|---|
| `SlotInventory`, `ItemStack`, `ISlotFilter` | Core/Inventory | Pure C# rules incl. per-slot filters and stateful items (durability, bag contents). Unit-tested. |
| `ArmorMath`, `HitZone`, `IDamageModifier` | Core/Combat | Penetration vs armor class and wear (unit-tested); the damage-modifier hook |
| `ReloadPlanner` | Core/Inventory | What a reload takes, keeps and puts back (unit-tested) |
| `ItemDefinition` + `WeaponDefinition`, `AmmoDefinition`, `ArmorDefinition`, `ContainerItemDefinition` | Data/* | Typed item data |
| `EquipmentRules`, `SlotFilters` | Gameplay/Inventory | Which item fits which slot; no bags in bags |
| `ItemFactory` | Gameplay/Inventory | Creates items with state (full durability, spawned bag storage) and destroys it |
| `ItemContainer` | Gameplay/Inventory | Networked slots (`SyncList`): equipment, bag storage, crates, bodies |
| `PlayerEquipment` | Gameplay/Inventory | Equipment, weapon selection, loaded ammo and the reload transaction, armor (`IDamageModifier`), loadout |
| `PlayerInventory` | Gameplay/Inventory | Access rules + requests: open/close, open a bag in a container, transfer, quick move |
| `DeathLootDropper`, `LootSpawner`, `WorldLootSpawner` | Gameplay / Networking | Body on death; crate loot; crates spawned at server start |
| `Hitbox`, `HitscanWeapon`, `WeaponReloader` | Gameplay | Head/body zones; fires the drawn weapon (spends rounds, spread, pellets); timed reload |
| `InventoryScreen`, `EquipmentHudView`, `InteractionPromptView`, `ItemText` | UI/Inventory | UI Toolkit screen, ammo/weapon/armor HUD, "[E] Search" prompt, shared stat text |

Item ids are stable strings saved in stashes and sent over the network: never rename a shipped id.
Known limitation: container contents replicate to all clients (a modified client could read other bags).

## Raid loop
1. **Sign in** with a player name (3–16 letters, digits, `_` or `-`). The name is the profile: it is saved on the
   server as `persistentDataPath/Profiles/<name>.json` (macOS editor: `~/Library/Application Support/<company>/<product>/Profiles`).
   A new profile starts with the `DefaultLoadout` starter kit. One session per name at a time.
2. **Hideout:** stash (60 slots) on the right, loadout + its rig and backpack on the left. Click moves stash ↔ loadout,
   Alt+click moves one, drag places in a slot, right-click a bag in the stash opens it. Every change saves immediately.
3. **Deploy:** the loadout goes onto a new character at a spawn point; empty magazines are loaded from carried ammo.
   The profile is saved without it, so the gear is at risk.
4. **Extract:** stand in a green extraction zone (North Gate, South Gate) for 8 s; leaving resets it. Everything you
   carry comes back as your loadout. The raid lasts 20 minutes; whoever has not extracted is killed (MIA).
5. **Die:** gear stays on your body; you return to the hideout after 4 s. If your loadout is empty you get the starter kit.
   Leaving mid-raid (disconnect) loses the gear too.

| Piece | Where | Role |
|---|---|---|
| `PlayerProfile`, `ProfileNames`, `ProfileCodec` | Core/Persistence | Save format (flat, JsonUtility-friendly), name rules, load validation (unit-tested) |
| `ProfileStore`, `ProfileSnapshot` | Gameplay/Session | JSON files with safe replace; containers ↔ profile (bags get fresh storage on load) |
| `PlayerSession` | Gameplay/Session | Per-client stash + loadout, sign in, deploy / end raid, hideout moves (server-validated) |
| `PlayerRaid`, `ExtractionZone` | Gameplay/Raid | Raid clock, MIA, hold-to-extract; zones are boxes without colliders |
| `RaidDirector` | Networking | Spawns sessions, characters on deploy, ends raids on extract/death |
| `LoadoutBuilder` | Gameplay/Inventory | Fills an equipment container and its bags; moves gear slot for slot |
| `OutOfBoundsKiller` + boundary walls | Networking / scene | Nobody walks off the map; anything below -20 m dies |
| `StashScreen`, `RaidHudView` | UI | Sign-in + hideout screen; raid timer and extraction progress |

## Controls
WASD / left stick move, mouse / right stick look, Space / A jump, LMB / RT fire, R / B reload,
E / X interact, Tab / Select inventory, 1/2/3 / Y weapons, Esc / Start toggles the menu (debug HUD).
Inventory: left-click quick-moves (equip, store in rig/backpack, or move to the loot side);
right-click a bag in a container to open it; "Take all" empties what you're looting.

## Dedicated server
Build with the Dedicated Server target. The server starts automatically; optional `-port 7770`.
