using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace DMS_DriverMechanoid
{
    public class DriverMechMod : Mod
    {
        // All mechanoid defNames this mod treats as "driver" variants.
        public static readonly HashSet<string> DriverDefNames = new HashSet<string>
        {
            "DMS_Mech_Driver",
            "DMS_Mech_AdvDriver",
            "DMS_Mech_MechPilot",
        };

        public const string HarmonyId = "yourname.dms.drivermech";

        public DriverMechMod(ModContentPack content) : base(content)
        {
            try
            {
                var harmony = new Harmony(HarmonyId);
                PatchVehicleFramework(harmony);
                PatchCaravanOwnership(harmony);
                PatchGameEnder(harmony);
                PatchMapRemoval(harmony);
                PatchGravshipPilot(harmony);
                PatchExosuitFramework(harmony);
                PatchPilotingAbility(harmony);
                PatchMechControlRange(harmony);
                Log.Message($"[DMS Driver Mechanoid] Initialized. Driver variants: {string.Join(", ", DriverDefNames)}");
            }
            catch (Exception e)
            {
                Log.Error($"[DMS Driver Mechanoid] Failed to initialize: {e}");
            }
        }

        // ============================================================
        //  Patch orchestrators
        // ============================================================

        public static void PatchVehicleFramework(Harmony harmony)
        {
            // 1. CanOperateRole — lets Drivers operate VF vehicle roles (drive + turret).
            Type vfHandlerType = AccessTools.TypeByName("Vehicles.VehicleRoleHandler");
            if (vfHandlerType == null) return;
            var prefix = new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.CanOperateRole_Prefix));
            int patched = 0;
            foreach (MethodInfo m in vfHandlerType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                         .Where(m => m.Name == "CanOperateRole" && m.GetParameters().Length == 2))
            {
                harmony.Patch(m, prefix: prefix);
                patched++;
            }
            if (patched > 0) Log.Message($"[DMS Driver Mechanoid] Patched {patched} CanOperateRole overload(s).");

            // 2. MechanoidCanDo — enables the right-click "Enter vehicle" menu for mechs.
            Type providerType = AccessTools.TypeByName("Vehicles.FloatMenuOptionProvider_Vehicle");
            if (providerType != null)
            {
                MethodInfo getter = AccessTools.Property(providerType, "MechanoidCanDo")?.GetGetMethod(nonPublic: true);
                if (getter != null)
                {
                    harmony.Patch(getter, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.MechanoidCanDo_Postfix)));
                    Log.Message("[DMS Driver Mechanoid] Patched MechanoidCanDo.");
                }
            }
        }

        public static void PatchCaravanOwnership(Harmony harmony)
        {
            MethodInfo isOwner = AccessTools.Method(typeof(CaravanUtility), "IsOwner", new[] { typeof(Pawn), typeof(Faction) });
            if (isOwner != null)
            {
                harmony.Patch(isOwner, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.CaravanIsOwner_Postfix)));
                Log.Message("[DMS Driver Mechanoid] Patched CaravanUtility.IsOwner.");
            }
        }

        public static void PatchGameEnder(Harmony harmony)
        {
            MethodInfo checkOrUpdate = AccessTools.Method(typeof(GameEnder), "CheckOrUpdateGameOver");
            MethodInfo gameEndTick = AccessTools.Method(typeof(GameEnder), "GameEndTick");
            int patched = 0;
            if (checkOrUpdate != null) { harmony.Patch(checkOrUpdate, prefix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.CheckOrUpdateGameOver_Prefix))); patched++; }
            if (gameEndTick != null) { harmony.Patch(gameEndTick, prefix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.GameEndTick_Prefix))); patched++; }
            if (patched > 0) Log.Message($"[DMS Driver Mechanoid] Patched {patched} GameEnder method(s).");
        }

        public static void PatchMapRemoval(Harmony harmony)
        {
            MethodInfo getter = AccessTools.Property(typeof(MapPawns), "AnyPawnBlockingMapRemoval")?.GetGetMethod(nonPublic: true);
            if (getter != null)
            {
                harmony.Patch(getter, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.AnyPawnBlockingMapRemoval_Postfix)));
                Log.Message("[DMS Driver Mechanoid] Patched AnyPawnBlockingMapRemoval.");
            }
        }

        public static void PatchGravshipPilot(Harmony harmony)
        {
            try
            {
                Type ritualBehaviorType = AccessTools.TypeByName("RimWorld.RitualBehaviorWorker_GravshipLaunch");
                if (ritualBehaviorType == null) return;

                MethodInfo pawnCanFillRole = AccessTools.Method(ritualBehaviorType, "PawnCanFillRole");
                if (pawnCanFillRole != null)
                {
                    harmony.Patch(pawnCanFillRole, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.GravshipPawnCanFillRole_Postfix)));
                    Log.Message("[DMS Driver Mechanoid] Patched PawnCanFillRole.");
                }

                MethodInfo appliesToPawn = AccessTools.Method(typeof(RitualRoleColonist), "AppliesToPawn");
                if (appliesToPawn != null)
                {
                    harmony.Patch(appliesToPawn, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.RitualRoleAppliesToPawn_Postfix)));
                    Log.Message("[DMS Driver Mechanoid] Patched AppliesToPawn.");
                }

                MethodInfo allCandidates = AccessTools.Property(typeof(RitualRoleAssignments), "AllCandidatePawns")?.GetGetMethod(nonPublic: true);
                if (allCandidates != null)
                {
                    harmony.Patch(allCandidates, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.RitualAllCandidatePawns_Postfix)));
                    Log.Message("[DMS Driver Mechanoid] Patched AllCandidatePawns.");
                }
            }
            catch (Exception e) { Log.Warning($"[DMS Driver Mechanoid] Gravship patch skipped: {e.Message}"); }
        }

        public static void PatchExosuitFramework(Harmony harmony)
        {
            try
            {
                Type maintenanceBay = AccessTools.TypeByName("Exosuit.Building_MaintenanceBay");
                Type ejectorBay = AccessTools.TypeByName("Exosuit.Building_EjectorBay");
                if (maintenanceBay == null && ejectorBay == null) return;

                var canAcceptPostfix = new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.ExosuitCanAcceptPawn_Postfix));
                int patched = 0;
                foreach (Type bayType in new[] { maintenanceBay, ejectorBay })
                {
                    if (bayType == null) continue;
                    MethodInfo m = AccessTools.Method(bayType, "CanAcceptPawn", new[] { typeof(Pawn) });
                    if (m != null) { harmony.Patch(m, postfix: canAcceptPostfix); patched++; }
                }
                if (patched > 0) Log.Message($"[DMS Driver Mechanoid] Patched {patched} Exosuit CanAcceptPawn method(s).");

                // AssigningCandidates — inject Mech Pilot into the "set owner" gizmo list.
                Type compType = AccessTools.TypeByName("Exosuit.CompAssignableToPawn_Parking");
                if (compType != null)
                {
                    MethodInfo getter = AccessTools.Property(compType, "AssigningCandidates")?.GetGetMethod(nonPublic: true);
                    if (getter != null)
                    {
                        harmony.Patch(getter, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.ExosuitAssigningCandidates_Postfix)));
                        Log.Message("[DMS Driver Mechanoid] Patched AssigningCandidates.");
                    }
                }

                // HasPartsToWear + Wear — let the Mech Pilot equip exosuit apparel.
                MethodInfo hasParts = AccessTools.Method(typeof(ApparelUtility), "HasPartsToWear", new[] { typeof(Pawn), typeof(ThingDef) });
                if (hasParts != null)
                {
                    harmony.Patch(hasParts, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.HasPartsToWear_Postfix)));
                }

                MethodInfo wear = AccessTools.Method(typeof(Pawn_ApparelTracker), "Wear", new[] { typeof(Apparel), typeof(bool), typeof(bool) });
                if (wear != null)
                {
                    harmony.Patch(wear,
                        prefix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.ApparelWear_Prefix)),
                        postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.ApparelWear_Postfix)));
                    Log.Message("[DMS Driver Mechanoid] Patched Wear (prefix + postfix).");
                }

                // Prevent the Mech Pilot from REMOVING exosuit apparel to inventory.
                // The exosuit should only be removable via the EjectorBay, not stripped
                // or manually dropped. Patch TryDrop and Remove.
                MethodInfo tryDrop = AccessTools.Method(typeof(Pawn_ApparelTracker), "TryDrop",
                    new[] { typeof(Apparel), typeof(Thing), typeof(bool).MakeByRefType() });
                if (tryDrop != null)
                {
                    harmony.Patch(tryDrop, prefix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.ApparelTryDrop_Remove_Prefix)));
                }
                MethodInfo remove = AccessTools.Method(typeof(Pawn_ApparelTracker), "Remove",
                    new[] { typeof(Apparel) });
                if (remove != null)
                {
                    harmony.Patch(remove, prefix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.ApparelTryDrop_Remove_Prefix)));
                }
                Log.Message("[DMS Driver Mechanoid] Patched TryDrop + Remove (block exosuit removal from Mech Pilot).");
            }
            catch (Exception e) { Log.Warning($"[DMS Driver Mechanoid] Exosuit patch skipped: {e.Message}"); }
        }

        public static void PatchPilotingAbility(Harmony harmony)
        {
            // PilotingAbility is now handled entirely via XML statBases with
            // compensated values (0.5 for 10%, 3.5 for 70%). The stat has a
            // consistent 0.2x multiplier for mechs (skill + health), so:
            //   0.5 * 0.2 = 0.1  (10%)
            //   3.5 * 0.2 = 0.7  (70%)
            // No C# stat patching needed.
            Log.Message("[DMS Driver Mechanoid] PilotingAbility handled via XML statBases (0.5 / 3.5).");
        }

        public static void PatchMechControlRange(Harmony harmony)
        {
            MethodInfo canDraft = AccessTools.Method(typeof(MechanitorUtility), "CanDraftMech", new[] { typeof(Pawn) });
            if (canDraft != null)
            {
                harmony.Patch(canDraft, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.CanDraftMech_Postfix)));
                Log.Message("[DMS Driver Mechanoid] Patched CanDraftMech.");
            }
            MethodInfo inRange = AccessTools.Method(typeof(MechanitorUtility), "InMechanitorCommandRange", new[] { typeof(Pawn), typeof(LocalTargetInfo) });
            if (inRange != null)
            {
                harmony.Patch(inRange, postfix: new HarmonyMethod(typeof(DriverPatches), nameof(DriverPatches.InMechanitorCommandRange_Postfix)));
                Log.Message("[DMS Driver Mechanoid] Patched InMechanitorCommandRange.");
            }
        }
    }

    /// <summary>
    ///   All Harmony postfix/prefix methods.
    /// </summary>
    public static class DriverPatches
    {
        // ---- helpers ----

        public static bool IsDriverPawn(Pawn pawn)
        {
            return pawn != null && pawn.def != null && DriverMechMod.DriverDefNames.Contains(pawn.def.defName);
        }

        public static bool IsAlivePlayerDriver(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Destroyed && pawn.def != null
                && DriverMechMod.DriverDefNames.Contains(pawn.def.defName) && pawn.Faction == Faction.OfPlayer;
        }

        // ---- VF: CanOperateRole ----

        public static bool CanOperateRole_Prefix(object[] __args, ref bool __result)
        {
            Pawn pawn = __args.Length > 0 ? __args[0] as Pawn : null;
            if (!IsDriverPawn(pawn)) return true;

            int handlingType = __args.Length > 1 && __args[1] != null ? Convert.ToInt32(__args[1]) : 0;
            if (handlingType == 0) { __result = true; return false; }

            // Skip IsColonyMech + Violent gates; keep the rest.
            if (!pawn.RaceProps.ToolUser) { __result = false; return false; }
            if (pawn.Downed || pawn.Dead || pawn.InMentalState) { __result = false; return false; }
            if (pawn.IsPrisoner) { __result = false; return false; }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation)) { __result = false; return false; }
            if (!pawn.health.capacities.CapableOf(PawnCapacityDefOf.Consciousness)) { __result = false; return false; }

            __result = true;
            return false;
        }

        public static void MechanoidCanDo_Postfix(ref bool __result) { __result = true; }

        // ---- Caravan ownership ----

        public static void CaravanIsOwner_Postfix(Pawn pawn, Faction caravanFaction, ref bool __result)
        {
            if (__result) return;
            if (!IsDriverPawn(pawn)) return;
            if (pawn.Faction == caravanFaction) __result = true;
        }

        // ---- GameEnder ----

        public static bool CheckOrUpdateGameOver_Prefix(GameEnder __instance)
        {
            if (AnyDriverAliveAnywhere()) { ResetGameEnding(__instance); return false; }
            return true;
        }

        public static bool GameEndTick_Prefix(GameEnder __instance)
        {
            if (!IsGameEnding(__instance)) return true;
            if (AnyDriverAliveAnywhere()) { ResetGameEnding(__instance); return false; }
            return true;
        }

        private static void ResetGameEnding(GameEnder instance)
        {
            try
            {
                FieldInfo f1 = AccessTools.Field(typeof(GameEnder), "gameEnding");
                if (f1 != null) f1.SetValue(instance, false);
                FieldInfo f2 = AccessTools.Field(typeof(GameEnder), "ticksToGameOver");
                if (f2 != null) f2.SetValue(instance, 0);
            }
            catch { }
        }

        private static bool IsGameEnding(GameEnder instance)
        {
            try { return AccessTools.Field(typeof(GameEnder), "gameEnding")?.GetValue(instance) is bool b && b; }
            catch { return false; }
        }

        private static bool AnyDriverAliveAnywhere()
        {
            foreach (Map map in Find.Maps)
            {
                if (map?.mapPawns == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (IsAlivePlayerDriver(pawn)) return true;
                    if (VehicleCrewHasDriver(pawn)) return true;
                }
            }
            foreach (Caravan caravan in Find.WorldObjects.Caravans)
            {
                if (caravan == null || caravan.Faction != Faction.OfPlayer) continue;
                foreach (Pawn pawn in caravan.PawnsListForReading)
                {
                    if (IsAlivePlayerDriver(pawn)) return true;
                    if (VehicleCrewHasDriver(pawn)) return true;
                }
            }
            foreach (Pawn pawn in Find.WorldPawns.AllPawnsAlive)
            {
                if (IsAlivePlayerDriver(pawn)) return true;
                if (VehicleCrewHasDriver(pawn)) return true;
            }
            return false;
        }

        private static bool VehicleCrewHasDriver(Pawn pawn)
        {
            if (pawn == null) return false;
            Type t = pawn.GetType();
            if (t == null || t.FullName == null || !t.FullName.Contains("VehiclePawn")) return false;
            try
            {
                PropertyInfo prop = AccessTools.Property(t, "AllPawnsAboard") ?? AccessTools.Property(t, "AllCapablePawns");
                if (prop == null) return false;
                object val = prop.GetValue(pawn, null);
                if (val is List<Pawn> list) { foreach (Pawn crew in list) if (IsAlivePlayerDriver(crew)) return true; }
                else if (val is System.Collections.IEnumerable en) { foreach (object o in en) if (o is Pawn crew && IsAlivePlayerDriver(crew)) return true; }
            }
            catch { }
            return false;
        }

        // ---- MapPawns.AnyPawnBlockingMapRemoval ----

        public static void AnyPawnBlockingMapRemoval_Postfix(MapPawns __instance, ref bool __result)
        {
            if (__result) return;
            if (__instance == null) return;
            try
            {
                IReadOnlyList<Pawn> spawned = __instance.AllPawnsSpawned;
                if (spawned == null) return;
                foreach (Pawn pawn in spawned) { if (VehicleCrewHasDriver(pawn)) { __result = true; return; } }
            }
            catch { }
        }

        // ---- Gravship ritual ----

        public static void GravshipPawnCanFillRole_Postfix(Pawn pawn, ref bool __result)
        {
            if (__result) return;
            if (pawn == null || pawn.def == null) return;
            if (pawn.def.defName != "DMS_Mech_AdvDriver" && pawn.def.defName != "DMS_Mech_Driver") return;
            if (pawn.Downed || pawn.Dead || pawn.InMentalState) return;
            if (pawn.Faction != Faction.OfPlayer) return;
            __result = true;
        }

        public static void RitualRoleAppliesToPawn_Postfix(Pawn p, ref bool __result)
        {
            if (__result) return;
            if (p == null || p.def == null) return;
            if (p.def.defName != "DMS_Mech_AdvDriver" && p.def.defName != "DMS_Mech_Driver") return;
            if (p.Downed || p.Dead || p.InMentalState) return;
            if (p.Faction != Faction.OfPlayer) return;
            __result = true;
        }

        public static void RitualAllCandidatePawns_Postfix(RitualRoleAssignments __instance, ref List<Pawn> __result)
        {
            if (__result == null) return;
            try
            {
                FieldInfo targetField = AccessTools.Field(typeof(RitualRoleAssignments), "ritualTarget");
                if (targetField == null) return;
                TargetInfo target = (TargetInfo)targetField.GetValue(__instance);
                Map map = target.Map;
                if (map == null || map.mapPawns == null) return;

                HashSet<int> existing = new HashSet<int>();
                foreach (Pawn p in __result) { if (p != null) existing.Add(p.thingIDNumber); }

                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.def == null) continue;
                    if (pawn.def.defName != "DMS_Mech_AdvDriver" && pawn.def.defName != "DMS_Mech_Driver") continue;
                    if (pawn.Faction != Faction.OfPlayer) continue;
                    if (pawn.Downed || pawn.Dead) continue;
                    if (existing.Contains(pawn.thingIDNumber)) continue;
                    __result.Add(pawn);
                    existing.Add(pawn.thingIDNumber);
                }
            }
            catch { }
        }

        // ---- Exosuit Framework ----

        public static void ExosuitCanAcceptPawn_Postfix(Pawn pawn, ref AcceptanceReport __result)
        {
            if (__result.Accepted) return;
            if (pawn == null || pawn.def == null || pawn.def.defName != "DMS_Mech_MechPilot") return;
            if (pawn.Downed || pawn.Dead || pawn.InMentalState) return;
            if (pawn.Faction != Faction.OfPlayer) return;
            __result = AcceptanceReport.WasAccepted;
        }

        public static void ExosuitAssigningCandidates_Postfix(ThingComp __instance, ref IEnumerable<Pawn> __result)
        {
            if (__instance == null || __result == null) return;
            try
            {
                Map map = __instance.parent?.Map;
                if (map == null || map.mapPawns == null) return;
                HashSet<int> existing = new HashSet<int>();
                foreach (Pawn p in __result) { if (p != null) existing.Add(p.thingIDNumber); }
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.def == null) continue;
                    if (pawn.def.defName != "DMS_Mech_MechPilot") continue;
                    if (pawn.Faction != Faction.OfPlayer) continue;
                    if (pawn.Downed || pawn.Dead) continue;
                    if (existing.Contains(pawn.thingIDNumber)) continue;
                    List<Pawn> list = __result as List<Pawn> ?? new List<Pawn>(__result);
                    list.Add(pawn);
                    existing.Add(pawn.thingIDNumber);
                    __result = list;
                }
            }
            catch { }
        }

        public static void HasPartsToWear_Postfix(Pawn pawn, ref bool __result)
        {
            if (__result) return;
            if (pawn == null || pawn.def == null || pawn.def.defName != "DMS_Mech_MechPilot") return;
            __result = true;
        }

        public static void ApparelWear_Postfix(Pawn_ApparelTracker __instance, Apparel newApparel)
        {
            if (__instance == null || newApparel == null) return;
            Pawn pawn = __instance.pawn;
            if (pawn == null || pawn.def == null || pawn.def.defName != "DMS_Mech_MechPilot") return;
            if (__instance.WornApparel.Contains(newApparel)) return;
            try { __instance.WornApparel.Add(newApparel); newApparel.Notify_Equipped(pawn); }
            catch { }
        }

        public static bool ApparelWear_Prefix(Pawn_ApparelTracker __instance, Apparel newApparel)
        {
            if (__instance == null || newApparel == null) return true;
            Pawn pawn = __instance.pawn;
            if (pawn == null || pawn.def == null || pawn.def.defName != "DMS_Mech_MechPilot") return true;
            Type exosuitCoreType = AccessTools.TypeByName("Exosuit.Exosuit_Core");
            if (exosuitCoreType == null) return true;
            if (exosuitCoreType.IsInstanceOfType(newApparel)) return true;
            Type compSuitModuleType = AccessTools.TypeByName("Exosuit.CompSuitModule");
            if (compSuitModuleType != null)
            {
                try
                {
                    MethodInfo tryGetComp = AccessTools.Method(typeof(ThingCompUtility), "TryGetComp", new[] { typeof(ThingWithComps), compSuitModuleType });
                    if (tryGetComp != null && tryGetComp.Invoke(null, new object[] { newApparel }) != null) return true;
                }
                catch { }
            }
            Type noGenderExtType = AccessTools.TypeByName("Exosuit.NoGenederApparelExt");
            if (noGenderExtType != null)
            {
                try
                {
                    var ext = newApparel.def.modExtensions?.Find(e => e != null && noGenderExtType.IsInstanceOfType(e));
                    if (ext != null) return true;
                }
                catch { }
            }
            return false;
        }

        // ---- Prevent exosuit removal from Mech Pilot ----

        /// <summary>
        ///   Prefix for Pawn_ApparelTracker.TryDrop and Remove.
        ///   Blocks removal of exosuit apparel from the Mech Pilot — the exosuit
        ///   should only be removable via the EjectorBay, not stripped or dropped.
        /// </summary>
        public static bool ApparelTryDrop_Remove_Prefix(Pawn_ApparelTracker __instance, Apparel ap)
        {
            if (__instance == null || ap == null) return true;
            Pawn pawn = __instance.pawn;
            if (pawn == null || pawn.def == null || pawn.def.defName != "DMS_Mech_MechPilot") return true;

            Type exosuitCoreType = AccessTools.TypeByName("Exosuit.Exosuit_Core");
            if (exosuitCoreType == null) return true;
            if (exosuitCoreType.IsInstanceOfType(ap)) return false;

            Type compSuitModuleType = AccessTools.TypeByName("Exosuit.CompSuitModule");
            if (compSuitModuleType != null)
            {
                try
                {
                    MethodInfo tryGetComp = AccessTools.Method(typeof(ThingCompUtility), "TryGetComp",
                        new[] { typeof(ThingWithComps), compSuitModuleType });
                    if (tryGetComp != null && tryGetComp.Invoke(null, new object[] { ap }) != null) return false;
                }
                catch { }
            }

            Type noGenderExtType = AccessTools.TypeByName("Exosuit.NoGenederApparelExt");
            if (noGenderExtType != null)
            {
                try
                {
                    var ext = ap.def.modExtensions?.Find(e => e != null && noGenderExtType.IsInstanceOfType(e));
                    if (ext != null) return false;
                }
                catch { }
            }
            return true;
        }

        // ---- Mech control range ----

        public static void CanDraftMech_Postfix(Pawn mech, ref AcceptanceReport __result)
        {
            if (__result.Accepted) return;
            if (!IsDriverPawn(mech)) return;
            if (mech.Downed || mech.Dead || mech.Destroyed) return;
            __result = AcceptanceReport.WasAccepted;
        }

        public static void InMechanitorCommandRange_Postfix(Pawn mech, ref bool __result)
        {
            if (__result) return;
            if (!IsDriverPawn(mech)) return;
            __result = true;
        }
    }
}
