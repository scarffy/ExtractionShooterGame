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
- **No editor generators** for scenes or prefabs: scenes, prefabs and data assets are written as Unity YAML by an
  offline tool outside the project, never by editor menu scripts.

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
Server-authoritative, grid-based (Tetris-style). Clients only send requests; the server validates every one.

**Equipment (8 slots):** Helmet · Body Armor · Chest Rig · Backpack · Primary 1 · Primary 2 · Pistol · Melee.
**Storage:** pockets (5 cells, always there, lost on death), the chest rig and backpack grids, and the **safe box**
(2x1, upgradeable to 3x3 at the trader) which is kept on death. The knife in the melee slot is never lost.
Items have footprints (a rifle is 4x2) and rotate with R while dragging. Bags carry their own networked grid and
cannot go inside bags.

**Weapons:** 14 weapons over 11 calibers, 32 ammo types (damage, penetration, armor damage, pellets, muzzle
velocity). 1/2/3 select Primary 1/Primary 2/Pistol. R reloads from rig → pockets → backpack (`ReloadPlanner`).
**Attachments:** 8 mods in 4 slots (optic, muzzle, grip, magazine): Red Dot, Holographic, 4x Scope, Suppressor,
Compensator, Vertical Grip, Angled Grip, Extended Magazine. Drag a mod onto a weapon to fit it (a replaced mod comes
back), right-click the weapon to remove the last one. Mods change spread, hip spread, aim time, zoom/scope,
magazine size, recoil and how far shots are heard (`WeaponStats`, `WeaponModding`).

**Armor:** head/body/legs hitboxes; helmet and chest pad, class 1–6, penetration vs class and wear (`ArmorMath`).

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
1. **Sign in** with a player name; the profile is saved on the server
   (`persistentDataPath/Profiles/<name>.json`). New profiles get the starter kit and a knife, and no credits.
2. **Hideout:** loadout, pockets, safe box, rig/backpack, a scrolling stash, character preview and gear value.
   **Trader** tab: buys at 60% and sells at 125% of item value (unlimited stock), safe box upgrades.
   **Squad** tab: create a squad (up to 4), join by code or invite by name, set Ready.
3. **Find match:** the squad leader queues the whole (ready) squad. Matchmaking fills a raid (up to 12 players)
   first-in first-out without splitting squads and launches after a 15 s countdown, or at once when full.
   Raids run one at a time on this map; a squad may late-join a raid in its first 2 minutes, otherwise it waits for
   the next one. Squadmates spawn together; squads spawn far apart. Bots and loot crates reset for each raid.
4. **Extract:** hold position in a green extraction zone for 8 s. Everything you carry comes back.
5. **Die:** gear stays on your body (except the safe box and knife). If you're broke and have no weapon, you get the
   starter kit. Leaving mid-raid loses the gear too.

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

## Combat
- **Aiming:** hip fire has a wide cone that grows when moving; RMB raises the sights (zoom, tighter cone, slower
  movement). Scoped optics show a scope overlay. The crosshair shows where shots can land.
- **Recoil:** every weapon has a learnable pattern (climb + sway + small jitter), reduced by aiming, crouching and
  attachments; the view recovers after you stop firing (`RecoilPattern`).
- **Bullet drop:** shots follow a ballistic arc from the muzzle velocity, traced in segments (`Ballistics`,
  `BallisticTrace`): ~6 cm at 100 m for an AWM, ~31 cm for an SMG.
- **Movement:** sprint (stamina), crouch (smaller hitboxes, steadier aim), jump costs stamina.
- **Knife:** V, server-validated melee.
- **Injuries:** hits can cause bleeding; leg hits can fracture (slower movement). Bandage/Medkit stop bleeding,
  Splint fixes a fracture; treatment takes time and can't be combined with shooting.
- **Grenades:** G throws, B cycles frag / smoke / flash. Frag: radius damage blocked by cover (can bleed/fracture).
  Smoke: 20 s cloud that bots can't see through. Flash: whites out players looking at it and blinds bots.
- **Squads:** no friendly fire by default (`SquadLobby._friendlyFire`).
- **Sound:** synthesized gunshots per weapon class, footsteps, reloads, knife, explosions; suppressors are quieter.

## Bots
Four loadouts (MP5 with holo, Glock, AKM with red dot/grip/compensator, M870). They patrol, react to sight and to
gunshots within 40 m, take cover when reloading or badly hurt, flank, can't see through smoke, get flashed, and drop
their modded weapon and loot.

## Navigation
Compass strip with bearing and extraction markers; **M** opens the map (cover, extractions with distance, squadmates,
your facing) with the tactical panel (raid time, extraction status, carried value, health and injuries, squad).

## Controls
WASD move · mouse look · Space jump · Shift sprint · C crouch · LMB fire · RMB aim · R reload · V knife ·
G throw grenade · B next grenade · E interact · 1/2/3 weapons · Tab inventory · M map · Esc menu.
Gamepad: sticks, A jump, RT fire, LT aim, B reload, X interact, Y cycle weapon, RB knife, LB throw, d-pad left
next grenade, L3 sprint, R3 crouch, Select inventory, Start menu.
Inventory: click quick-moves, Alt+click moves one, drag places (R rotates), right-click opens a bag / uses a medical
item / removes the last attachment.

## Dedicated server
Build with the Dedicated Server target. The server starts automatically; optional `-port 7770`.
