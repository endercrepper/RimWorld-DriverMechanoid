# Driver Automatroid (DMS Add-on)

A small RimWorld mod that adds a new mechanoid — the **Driver** — built on the
**Soldat** chassis from **The Dead Man's Switch** (DMS). The Driver does two things:

1. **Wields basic pistols** — autopistols and revolvers (and the DMS light-sidearm
   tag). It is restricted to sidearms on purpose, via DMS's Fortified weapon filter.
2. **Drives Vehicle Framework vehicles** — it can pilot tanks and crew vehicle
   turrets from SmashPhil's Vehicle Framework. This is the part that needs the
   included C# patch (see below).

The Driver is gestated at the DMS mech gestator and controlled by a mechanitor,
same research tier as the Soldat.

---

## Requirements

| Mod | packageId | Why |
|---|---|---|
| Biotech | `ludeon.rimworld.biotech` | Mechanoids / mechanitor |
| Harmony | `brrainz.harmony` | The C# patch is a Harmony patch |
| Fortified Feature Framework | `AOBA.Framework` | Provides `Fortified.MechWeaponExtension` (the weapon-wield system the Soldat uses) |
| The Dead Man's Switch | `Aoba.DeadManSwitch.Core` | Provides the `BaseMechRace_Soldat` / `BaseAutomatroidKind` / `DMS_LegionMechanoidRecipe` parents the Driver inherits |
| Vehicle Framework | `SmashPhil.VehicleFramework` | What the Driver drives |

Supported RimWorld versions: **1.4 / 1.5 / 1.6** (same as DMS).

---

## Install

1. Drop the `DriverMechanoidMod` folder into your RimWorld `Mods` folder
   (Steam: `steamapps/common/RimWorld/Mods`, or `~/.config/unity3d/Ludeon Studios/RimWorld by Ludeon Studios/Mods`).
2. Enable it in the in-game mod list. It must load **after** DMS, Vehicle Framework
   and Harmony (the `About.xml` already declares `loadAfter`, so a sorted mod list
   will place it correctly).
3. The prebuilt assembly is included at `Assemblies/DMS_DriverMechanoid.dll`
   (compiled for .NET Framework 4.8, which loads on RimWorld 1.4/1.5/1.6). No
   compilation is required to use the mod.

---

## What's in the mod

```
DriverMechanoidMod/
├─ About/
│  ├─ About.xml          Mod metadata + dependencies
│  └─ Preview.png        Mod-list thumbnail
├─ Defs/
│  ├─ DMS_Driver_Mechanoid.xml   ThingDef (race) + PawnKindDef for DMS_Mech_Driver
│  └─ DMS_Driver_Gestation.xml   RecipeDef "DMS_Make_Driver" (mech gestator)
├─ Assemblies/
│  └─ DMS_DriverMechanoid.dll    Prebuilt Harmony patch (net48)
├─ Source/
│  ├─ DriverMechMod.cs           C# source of the patch
│  ├─ DMS_DriverMechanoid.csproj SDK-style project (Krafs.RimWorld.Ref + Lib.Harmony)
│  └─ build.sh                   One-command rebuild script
└─ README.md
```

---

## Design notes

### 1. "Based on the Soldat" — literally

`DMS_Mech_Driver`'s ThingDef inherits DMS's abstract `BaseMechRace_Soldat`, so it
reuses the Soldat's body (`DMS_Humanoid_Soldat`), armor, move speed and battery-
backpack behavior. The PawnKindDef inherits `BaseAutomatroidKind`. The gestation
recipe inherits `DMS_LegionMechanoidRecipe` (same research tier, ingredients and
gestator as `DMS_Make_Soldat`). So the Driver is a true DMS automatroid, not a
look-alike.

### 2. Basic pistols only

The Driver carries DMS's `Fortified.MechWeaponExtension` with `UsableWeaponTags`
restricted to:

- `Autopistol` — vanilla autopistol and most autopistol-class sidearms
- `Revolver` — vanilla revolver and revolver-class sidearms
- `DMS_MechWeaponLight` — DMS's own light sidearm tag

and `UsableTechLevels` of Neolithic / Medieval / Industrial (so flintlocks and
matchlock pistols from weapon mods are allowed too). To broaden or narrow the
allowed weapons, edit the `<UsableWeaponTags>` list in
`Defs/DMS_Driver_Mechanoid.xml`.

The PawnKindDef also lists the same `weaponTags` so that NPC-spawned Drivers
(hostile faction mechs) spawn with an appropriate pistol. Player-gestated Drivers
are equipped by you and filtered by the extension above.

### 3. Driving — and firing — Vehicle Framework vehicles

Vehicle Framework's `VehicleRoleHandler.CanOperateRole(Pawn, HandlingType)` has
**two** gates that, between them, hard-block every colony mechanoid from
operating VF vehicles:

1. `if (pawn.IsPrisoner || pawn.IsColonyMech) return false;` — blocks colony
   mechs from ANY role (driving and turrets). DMS automatroids extend
   `BaseMechanoid`, so every player-owned Driver is a colony mech.
2. `if ((handlingType & HandlingType.Turret) != 0 && pawn.WorkTagIsDisabled(WorkTags.Violent)) return false;`
   — blocks any pawn without the `Violent` work tag from **firing** vehicle
   weapons. DMS automatroids only have `BasicWorker` in `mechEnabledWorkTypes`
   (set on `BaseAutomatroidWalker`), and `BasicWorker` does not carry `Violent`,
   so every Driver has `Violent` in `CombinedDisabledWorkTags`.

Gate (1) blocks driving; gates (1)+(2) together block firing. Neither can be
undone with XML alone, so the mod ships a tiny Harmony patch
(`Assemblies/DMS_DriverMechanoid.dll`) that patches VF's `CanOperateRole` and,
**for `DMS_Mech_Driver` only**, re-runs the remaining guards (ToolUser,
downed/dead/mental-state, prisoner, Manipulation, Consciousness) while dropping
**both** colony-mech-blocking gates. Result:

- **Driving** (Movement role) → ✅ the Driver can pilot any VF vehicle.
- **Firing** (Turret role) → ✅ the Driver can man and fire vehicle turrets
  (e.g. the Marshal's main gun from Vanilla Vehicles Expanded).
- Every other pawn in the game is left on VF's original logic untouched.

> **One pawn, one role.** A VF vehicle assigns each pawn to a single role. A
> tank like the Marshal typically has a separate **driver** (Movement) and
> **gunner** (Turret) role, so a fully-crewed Marshal needs **two** Drivers —
> one driving, one on the gun. (Some vehicles combine Movement+Turret into one
> role; those only need one Driver.)

The vehicle uses **its own** turret weapon when firing — the Driver's equipped
pistol is irrelevant to vehicle combat (the pistol is for when the Driver is
dismounted). The patch is applied via reflection, so it has no compile-time
dependency on `Vehicles.dll` and silently no-ops if Vehicle Framework isn't
loaded (the Driver then simply can't use vehicles, but still works as a pistol
mech).

All of VF's role/assignment gates funnel through that single method
(`VehiclePawn_Handlers`, `RoleHelper`, `RoleAssignment` all call
`VehicleRoleHandler.CanOperateRole`), so the one patch covers driving, turret
manning and role assignment.

### 4. The right-click "Enter vehicle" menu

There's a *third* VF gate, separate from `CanOperateRole`. VF's right-click
context menus (the submenu that lets you pick "Enter vehicle → driver/gunner/…")
are built by classes inheriting `FloatMenuOptionProvider_Vehicle`. That base
class declares:

```csharp
protected override bool MechanoidCanDo => false;
```

The `FloatMenuOptionProvider` base uses `MechanoidCanDo` to filter mechanoid
pawns out of the menu flow **entirely** — so selecting a mechanoid and
right-clicking a vehicle shows nothing, even if the mech can technically operate
a role. This is why a human colonist gets the "Enter vehicle" submenu but a
mechanoid doesn't.

The patch Postfixes the `MechanoidCanDo` property getter on
`FloatMenuOptionProvider_Vehicle` and forces it to return `true`. Re-enabling
the menu for all mechanoids is safe: the menu options themselves still call
`CanOperateRole` (which we only relaxed for the Driver), so non-Driver mechs see
the menu but find no valid slots and get no usable options. The Driver gets the
full role-pick submenu — select it, right-click a vehicle, and choose a slot.

> The patch targets the abstract `FloatMenuOptionProvider_Vehicle` base, so it
> covers **every** vehicle's menu (Marshal, halftrack, boat, aircraft, …) in one
> shot — no per-vehicle config.

### 5. All-Driver vehicle caravans (no human colonist aboard)

A *fourth* gate blocks all-mechanoid vehicle caravans, and it's the one behind
your "caravan disappears after leaving the map" symptom. It lives in vanilla
RimWorld, not VF:

```csharp
// vanilla CaravanUtility.IsOwner(Pawn pawn, Faction caravanFaction)
return pawn.Faction == caravanFaction && pawn.RaceProps.Humanlike;
```

Mechanoids fail the `Humanlike` check, so a Driver is never a caravan "owner."
This breaks all-Driver caravans in two compounding ways:

1. **VF's own owner-fix is a no-op for mechs.** VF patches
   `Caravan.IsOwner(Pawn)` (instance) with an `IsOwnerOfVehicle` postfix meant
   to let vehicle crew count as owners — but that postfix internally calls the
   static `CaravanUtility.IsOwner(p, f)`, which *still* requires Humanlike. So
   VF's fix only ever helped humanlike crew inside vehicles, never mechs.

2. **`CaravanUtility.RandomOwner` crashes.** VF's `RandomVehicleOwner` prefix
   filters a caravan's pawns by `caravan.IsOwner(p)`. With only Drivers aboard
   that filter yields an **empty sequence**, and `RandomElement` throws on the
   caravan's first world-map tick — which destroys the caravan. Hence the
   "disappears" behavior.

The patch Postfixes the static `CaravanUtility.IsOwner(Pawn, Faction)` so the
Driver counts as an owner of **its own faction's** caravans (the faction check
is preserved — a Driver never owns an enemy caravan). This is the single root
fix: once the static returns true for the Driver, the instance method, VF's
postfix, `RandomOwner` and the caravan defeated-check all flow correctly. An
all-Driver vehicle caravan now survives on the world map and travels normally.

> **Scope.** Only `DMS_Mech_Driver` is affected. Every other pawn (human,
> animal, other mechs) stays on vanilla logic. The patch is on a vanilla method,
> so it applies whether or not Vehicle Framework is installed — but it only
> matters when a caravan actually contains a Driver.

### 6. False game-over when camping with no human colonist

A *fifth* gate causes the "caravan lost" / game-over screen when an all-Driver
party (no human colonist anywhere) forms a camp or settlement. It lives in
vanilla `GameEnder`:

- `GameEnder.CheckOrUpdateGameOver` decides game-over by checking for **free
  colonists** (humanlike colonists) on maps (`FreeColonistsSpawnedCount`) and in
  caravans (`IsPlayerControlledWithFreeColonist`).
- `GameEnder` has **no awareness of mechanoids** — colony mechs are not "free
  colonists", so they don't count.

In vanilla this is fine because every caravan/colony must have at least one
human (the mechanitor). But once we let the Driver own a caravan on its own
(patch 5), an all-Driver party trips the check: when the caravan is consumed by
a camp, the camp map has only mechs → `FreeColonistsSpawnedCount` = 0, no
caravans with free colonists → the game-over countdown starts → "caravan lost".

The patch Prefixes `CheckOrUpdateGameOver` and `GameEndTick`: while any Driver is
alive anywhere — on any map, in any caravan, inside any vehicle, or as a world
pawn — it forces `gameEnding = false` and `ticksToGameOver = 0` and skips the
vanilla logic. This prevents the false game-over without affecting any genuine
defeat. If all Drivers (and all humans) are dead, vanilla logic runs normally.

> **Performance.** `GameEndTick` runs every tick, but its prefix has a cheap
> fast path: it only scans for Drivers when `gameEnding` is already true (rare).
> `CheckOrUpdateGameOver` runs on events (pawn deaths, map changes) — not every
> tick — so its scan is not a hot path.

### 7. Camp / ambush map auto-removed (only Drivers aboard a vehicle)

A *sixth* gate destroys the camp/ambush map (and everything on it) immediately
after it's generated. It lives in vanilla `MapPawns.AnyPawnBlockingMapRemoval`:

Temporary maps (camps, ambushes, quest maps, raids) are auto-removed when
`ShouldRemoveMapNow` returns true, which happens when
`!map.mapPawns.AnyPawnBlockingMapRemoval`. The vanilla check iterates
`AllPawnsSpawned` — but **pawns inside a VF vehicle are NOT in
`AllPawnsSpawned`** (they're in the vehicle's handler/ThingOwner). And the
vehicle itself may not satisfy vanilla's pawn-blocking gate (it's not Humanlike,
not a standard colonist). So an all-Driver party inside a vehicle on a freshly
generated camp map can have `AnyPawnBlockingMapRemoval` return false → map
destroyed → vehicle + Drivers lost → "caravan lost".

The patch Postfixes `AnyPawnBlockingMapRemoval`: if vanilla returned false, it
re-scans the map's spawned pawns for any VF vehicle whose crew (`AllPawnsAboard`)
contains a player-faction Driver. If found, it flips the result to true (don't
remove the map). This is conservative — it only ever *prevents* map removal,
never causes it.

> Also fixed: the GameEnder scan now checks vehicle crew on **world pawns** too,
> closing a timing gap during camp formation where the vehicle is briefly a world
> pawn (removed from caravan, not yet spawned on the new map).

---

## Rebuilding the DLL (optional)

The prebuilt `Assemblies/DMS_DriverMechanoid.dll` is all you need. If you want to
rebuild it (e.g. you changed `DriverMechMod.cs`):

```bash
# requires the .NET SDK (https://dot.net) — no RimWorld install needed
cd Source
./build.sh            # or:  ./build.sh Debug
```

`build.sh` restores `Krafs.RimWorld.Ref` + `Lib.Harmony` from NuGet, builds the
net48 assembly and copies it to `../Assemblies/`.

---

## Customizing the look

The Driver reuses the **Soldat** sprite (`Things/Automatroid/soldat`) so the mod
ships with a clean, in-style texture and no missing-mesh errors. To give the Driver
its own art:

1. Add `driver_south.png`, `driver_north.png` and `driver_east.png` to
   `Textures/Things/Automatroid/` (west is auto-mirrored from east).
2. In `Defs/DMS_Driver_Mechanoid.xml`, change both `<texPath>` values from
   `Things/Automatroid/soldat` to `Things/Automatroid/driver`.

---

## Combat Extended compatibility

Yes — the mod is CE-aware. Coverage comes from two sources:

**1. Inherited automatically (no patch needed).** DMS's own CE patch
(`1.6/CE/Patches/Pawns/Automatroid_Medium_Soldat_CE.xml`) targets the *abstract*
`ThingDef[@Name="BaseMechRace_Soldat"]`. Since `DMS_Mech_Driver` inherits that
abstract, all of the following CE work already applies to the Driver:

- Armor re-scale → `ArmorRating_Blunt` 8 / `ArmorRating_Sharp` 6 / `ArmorRating_Heat` 0.1
- CE carry/aim stats → `CarryWeight` 50, `CarryBulk` 20, `AimingAccuracy` 0.75,
  `ShootingAccuracyPawn` 1, `NightVisionEfficiency` 0.5, `MaxHitPoints` 200,
  melee dodge/crit/parry
- `CombatExtended.CompProperties_ArmorDurability` — Durability 790, regen +
  Steel repair, over-heal

**2. Added by this mod** (`Patches/CE/DMS_Driver_CE.xml`, gated on
`CETeam.CombatExtended` via `LoadFolders.xml`, exactly like DMS gates its own CE
folder). DMS's CE patch adds the ammo/loadout extension to PawnKindDefs *by
defName* (`DMS_Mech_Soldat`, `DMS_Mech_Sergeant`, …), so `DMS_Mech_Driver` would
otherwise be skipped. This patch:

- Adds `CombatExtended.LoadoutPropertiesExtension` to `DMS_Mech_Driver` —
  NPC-spawned Drivers now carry 4–6 pistol magazines plus a `DMS_MechWeaponLight`
  / `Autopistol` sidearm with its own ammo.
- Bumps `combatPower` to 80 for threat-point parity with the CE-scaled Soldat.

The pistol weapon filter (`Autopistol` / `Revolver` / `DMS_MechWeaponLight`)
works under CE unchanged, because CE preserves the vanilla `weaponTags` on
patched weapons.

## Compatibility

- Safe to add to an existing save.
- Removing it mid-save is fine as long as no living Driver exists on the map (the
  def + assembly simply won't be referenced anymore).
- The Harmony patch is scoped to `DMS_Mech_Driver` and VF's `CanOperateRole`; it
  does not change behavior for any other mech or pawn.
- CE patch only loads when Combat Extended is active (`LoadFolders.xml`
  `IfModActive` gate); without CE it is inert.

## Credits

- The Dead Man's Switch — by AobaKuma, Bread Mo, Bill Doors, KV4EX, Mortis
- Vehicle Framework — by SmashPhil
- This add-on only adds one new mechanoid variant and a compatibility patch.
