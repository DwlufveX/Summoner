using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using VEF.Abilities;
using VanillaPsycastsExpanded;

namespace RimMod.Summoner
{
    public sealed class SummonerMod : Mod
    {
        public SummonerMod(ModContentPack content) : base(content)
        {
            new Harmony("rimmod.summoner").PatchAll();
        }
    }

    [HarmonyPatch(typeof(CompAbilities), nameof(CompAbilities.GiveAbility))]
    public static class LearnedContract
    {
        public static void Postfix(CompAbilities __instance, VEF.Abilities.AbilityDef abilityDef)
        {
            if (abilityDef.defName == "RMS_Contract" && Current.ProgramState == ProgramState.Playing)
                Soul.Game?.BeginSearch(__instance.parent as Pawn);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class DeathPatch
    {
        public static void Postfix(Pawn __instance)
        {
            if (__instance.Dead) Soul.Game?.NotifyDeath(__instance);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
    public static class ShareDamagePatch
    {
        [ThreadStatic] private static bool sharing;
        public static void Postfix(Pawn __instance, ref DamageInfo dinfo, bool absorbed)
        {
            if (sharing || absorbed || dinfo.Amount <= 0 || !dinfo.Def.harmsHealth || !dinfo.Def.ExternalViolenceFor(__instance)) return;
            var r = Soul.Game?.Active(__instance);
            var wolf = r?.beast;
            if (r?.linkEnabled != true || wolf == null || wolf.Dead || wolf.Destroyed || !Soul.Together(__instance, wolf, 30) || !Soul.Knows(__instance, "RMS_Link")) return;
            float share = dinfo.Amount * .2f;
            dinfo.SetAmount(dinfo.Amount - share);
            try
            {
                sharing = true;
                // Soul wounds bypass ordinary armour; no copied owner body-part reference.
                var result = wolf.TakeDamage(new DamageInfo(DamageDefOf.Cut, share, 999f, instigator: dinfo.Instigator));
                Soul.Wolf(wolf)?.SharedDamage(result.totalDamageDealt);
            }
            finally { sharing = false; }
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class CombatPatch
    {
        public struct Before { public bool hostile, alive; }
        public static void Prefix(Thing __instance, DamageInfo dinfo, out Before __state)
        {
            __state = new Before { hostile = dinfo.Instigator is Pawn a && __instance.HostileTo(a), alive = __instance is Pawn p && !p.Dead };
        }
        public static void Postfix(Thing __instance, DamageInfo dinfo, DamageWorker.DamageResult __result, Before __state)
        {
            if (__result.totalDamageDealt <= 0) return;
            var attacker = dinfo.Instigator as Pawn;
            var wolf = Soul.Wolf(attacker);
            if (wolf?.Bound == true && __state.hostile && __state.alive && __instance is Pawn target)
            {
                wolf.lastCombatTick = Soul.Now;
                // Tiny creatures give proportionally less experience.
                float factor = Math.Min(1, target.kindDef.combatPower / 75f);
                wolf.GainXP((__result.totalDamageDealt * .5f + (target.Dead ? 20 : 0)) * factor);
            }
            if (!(__instance is Pawn owner) || attacker == null || !attacker.HostileTo(owner)) return;
            var c = Soul.Wolf(Soul.Game?.Active(owner)?.beast);
            if (c == null || !Soul.Together(owner, c.Pawn, 30)) return;
            c.TryGuard();
            if (Soul.Now - c.lastProtectTick >= 600 && c.Attack(attacker, false))
            { c.lastProtectTick = Soul.Now; c.GainBond(.2f); }
        }
    }

    [HarmonyPatch(typeof(TendUtility), nameof(TendUtility.DoTend))]
    public static class TendPatch
    {
        public static void Prefix(Pawn patient, out bool __state) => __state = patient.health.HasHediffsNeedingTend();
        public static void Postfix(Pawn doctor, Pawn patient, bool __state)
        {
            var c = Soul.Wolf(patient);
            if (__state && c?.Bound == true && doctor == c.owner && Soul.Now - c.lastTendTick >= 2500)
            { c.lastTendTick = Soul.Now; c.GainBond(.2f); }
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Sterile))]
    public static class NoBreeding
    {
        public static void Postfix(Pawn __instance, ref bool __result) { if (Soul.Protected(__instance)) __result = true; }
    }

    [HarmonyPatch(typeof(TrainableUtility), nameof(TrainableUtility.TamenessCanDecay), new[] { typeof(Pawn) })]
    public static class ContractTameness
    {
        public static void Postfix(Pawn pawn, ref bool __result)
        { if (Soul.Protected(pawn)) __result = false; }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.IsPlayerControlled), MethodType.Getter)]
    public static class DirectContractControl
    {
        public static void Postfix(Pawn __instance, ref bool __result)
        { if (Soul.Wolf(__instance)?.Bound == true) __result = true; }
    }

    [HarmonyPatch(typeof(PsycasterPathDef), nameof(PsycasterPathDef.CanPawnUnlock))]
    public static class PlayerOnlyPath
    {
        public static void Postfix(PsycasterPathDef __instance, Pawn pawn, ref bool __result)
        { if (__instance.defName == "RMS_Summoner" && pawn.Faction != Faction.OfPlayer) __result = false; }
    }

    [HarmonyPatch(typeof(TradeUtility), nameof(TradeUtility.PlayerSellableNow))]
    public static class NoTrade
    {
        public static bool Prefix(Thing t, ref bool __result)
        {
            if (!(t is Pawn p) || !Soul.Protected(p)) return true;
            __result = false; return false;
        }
    }

    [HarmonyPatch(typeof(TameUtility), nameof(TameUtility.CanTame))]
    public static class NoTameDesignation
    {
        public static void Postfix(Pawn pawn, ref bool __result) { if (Soul.Protected(pawn)) __result = false; }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.KindLabel), MethodType.Getter)]
    public static class EvolvedLabel
    {
        public static void Postfix(Pawn __instance, ref string __result)
        { if (Soul.Wolf(__instance)?.Evolved == true) __result = "巨牙灵狼"; }
    }

    [HarmonyPatch]
    public static class NoTaming
    {
        public static IEnumerable<MethodBase> TargetMethods() => typeof(InteractionWorker_RecruitAttempt).GetMethods().Where(m => m.Name == "DoRecruit" && m.GetParameters().Length == 3);
        public static bool Prefix(Pawn recruitee) => !Soul.Protected(recruitee);
    }

    // Also reject vanilla taming's longer overload, including its out parameters.
    [HarmonyPatch]
    public static class NoTamingDetailed
    {
        public static MethodBase TargetMethod() => typeof(InteractionWorker_RecruitAttempt).GetMethods().Single(m => m.Name == "DoRecruit" && m.GetParameters().Length == 6);
        public static bool Prefix(Pawn recruitee, ref string letterLabel, ref string letter)
        {
            if (!Soul.Protected(recruitee)) return true;
            letterLabel = null; letter = null; return false;
        }
    }

    public sealed class ThoughtWorker_Companion : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var r = Soul.Game?.Active(p);
            if (r?.beast == null || r.beast.Dead) return ThoughtState.Inactive;
            return ThoughtState.ActiveAtStage(r.beast.health.hediffSet.hediffs.Any(h => h is Hediff_Injury) ? 1 : 0);
        }
    }

    public sealed class ThoughtWorker_Trauma : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p) => Soul.Has(p, "RMS_Trauma");
    }

    // The progression component runs through the game component so off-screen maps are covered.
    [HarmonyPatch(typeof(SummonerGame), nameof(SummonerGame.GameComponentTick))]
    public static class GrowthTick
    {
        public static void Postfix(SummonerGame __instance)
        {
            if (Soul.Now % 2500 != 0) return;
            foreach (var r in __instance.records)
                if (r.state == ContractState.Active) Soul.Wolf(r.beast)?.TickBond();
        }
    }
}
