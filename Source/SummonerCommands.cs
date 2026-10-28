using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimMod.Summoner
{
    public static class SummonerCommands
    {
        public static Texture2D Icon(string name) => ContentFinder<Texture2D>.Get("UI/Summoner/" + name);
        public static void ShowResult(bool ok, string error, Pawn target)
        {
            if (!ok) Messages.Message(error ?? "RMS_BeastUnavailable".Translate().ToString(), target, MessageTypeDefOf.RejectInput, false);
        }

        public static IEnumerable<Gizmo> ForBeast(Pawn pawn)
        {
            if (!SummonerGameplay.IsSpiritBeast(pawn) || !pawn.Spawned || pawn.Dead) yield break;
            var record = pawn.GetComp<CompSummonerBeastLink>().RecordOrNull();
            if (record == null)
            {
                var candidates = SummonerArchive.Current.Summoners
                    .Where(s => SummonerGameplay.UsableMaster(s.master) && s.master.Map == pawn.Map).ToList();
                var contract = new Command_Action
                {
                    defaultLabel = "RMS_Contract".Translate(), defaultDesc = "RMS_ContractDesc".Translate(), icon = Icon("Contract"),
                    action = () => Find.WindowStack.Add(new FloatMenu(candidates
                        .Select(s => new FloatMenuOption(s.master.LabelShort, () => s.master.jobs.TryTakeOrderedJob(
                            JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RMS_ContractBeast"), pawn)))).ToList()))
                };
                if (candidates.Count == 0) contract.Disable("RMS_MasterUnavailable".Translate());
                else if (pawn.Downed || (pawn.Faction != null && pawn.Faction != Faction.OfPlayer)) contract.Disable("RMS_CannotContract".Translate());
                yield return contract;
                yield break;
            }
            if (pawn.Faction != Faction.OfPlayer) yield break;
            SummonerGameplay.EnsureControl(pawn);
            var draft = new Command_Toggle
            {
                defaultLabel = (pawn.Drafted ? "CommandUndraftLabel" : "CommandDraftLabel").Translate(),
                defaultDesc = "CommandToggleDraftDesc".Translate(), icon = TexCommand.Draft,
                isActive = () => pawn.Drafted, toggleAction = () => pawn.drafter.Drafted = !pawn.Drafted
            };
            if (pawn.Downed) draft.Disable("RMS_BeastUnavailable".Translate());
            yield return draft;
            if (pawn.Drafted)
            {
                yield return new Command_Target
                {
                    defaultLabel = "RMS_Move".Translate(), defaultDesc = "RMS_MoveDesc".Translate(), icon = Icon("Call"),
                    targetingParams = TargetingParameters.ForCell(), action = t =>
                    {
                        if (t.Cell.Standable(pawn.Map) && pawn.CanReach(t.Cell, PathEndMode.OnCell, Danger.Deadly))
                            pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, t.Cell));
                    }
                };
                yield return new Command_Target
                {
                    defaultLabel = "RMS_Attack".Translate(), defaultDesc = "RMS_AttackDesc".Translate(), icon = TexCommand.Attack,
                    targetingParams = TargetingParameters.ForAttackAny(), action = t =>
                    {
                        if (!t.HasThing || t.Thing.Destroyed || t.Thing == pawn) return;
                        bool ranged = pawn.def.GetModExtension<SummonerBeastDef>().ranged;
                        var job = JobMaker.MakeJob(ranged ? JobDefOf.AttackStatic : JobDefOf.AttackMelee, t.Thing);
                        job.maxNumStaticAttacks = ranged ? 10 : 0;
                        pawn.jobs.TryTakeOrderedJob(job);
                    }
                };
            }
            var owner = SummonerArchive.Current.FindSummoner(record.ownerId);
            var recall = new Command_Action
            {
                defaultLabel = "RMS_Recall".Translate(), defaultDesc = "RMS_RecallDesc".Translate(), icon = Icon("Call"),
                action = () => { bool ok = SummonerGameplay.TryRecall(owner.master, record.id, out var error); ShowResult(ok, error, owner.master); }
            };
            if (owner == null || !SummonerGameplay.UsableMaster(owner.master) || owner.master.Map != pawn.Map)
                recall.Disable("RMS_MasterUnavailable".Translate());
            yield return recall;
        }
    }

    [HarmonyPatch(typeof(Pawn), "GetGizmos")]
    internal static class SummonerPawnGizmos
    {
        private static IEnumerable<Gizmo> Postfix(IEnumerable<Gizmo> __result, Pawn __instance)
        {
            foreach (var item in __result) yield return item;
            foreach (var item in SummonerCommands.ForBeast(__instance)) yield return item;
            var owner = SummonerArchive.Current?.FindSummoner(__instance);
            if (owner != null && __instance.Faction == Faction.OfPlayer)
                yield return new Command_Action { defaultLabel = "RMS_Roster".Translate(), defaultDesc = "RMS_RosterDesc".Translate(),
                    icon = SummonerCommands.Icon("Contract"), action = () => Find.WindowStack.Add(new Dialog_SummonerRoster(__instance)) };
        }
    }

    // Keep animal/colonist classification intact; only grant orders to actual contracted spirit beasts.
    [HarmonyPatch(typeof(Pawn), "get_CanTakeOrder")]
    internal static class SummonerPawnOrders
    {
        private static void Postfix(Pawn __instance, ref bool __result)
        {
            if (!__result && __instance.Faction == Faction.OfPlayer && SummonerGameplay.IsSpiritBeast(__instance)
                && __instance.GetComp<CompSummonerBeastLink>().RecordOrNull() != null) __result = true;
        }
    }

    public sealed class Dialog_SummonerRoster : Window
    {
        private readonly Pawn master;
        private Vector2 scroll;
        public override Vector2 InitialSize => new Vector2(740, 560);
        public Dialog_SummonerRoster(Pawn master)
        {
            this.master = master;
            doCloseX = true; doCloseButton = true; absorbInputAroundWindow = true;
        }
        public override void DoWindowContents(Rect rect)
        {
            var owner = SummonerArchive.Current?.FindSummoner(master);
            if (owner == null) { Close(); return; }
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0, 0, rect.width, 34), "RMS_RosterTitle".Translate(master.LabelShort));
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0, 40, rect.width, 30), "RMS_RosterCapacity".Translate(
                SummonerArchive.Current.FieldLoad(owner.id), owner.fieldCapacity, owner.slots.Count, owner.contractSlotCapacity));
            Widgets.Label(new Rect(0, 72, rect.width, 44), "RMS_RosterHint".Translate());
            var area = new Rect(0, 124, rect.width, rect.height - 174);
            var view = new Rect(0, 0, area.width - 20, owner.slots.Sum(s => 38 + (1 + s.reserveBeastIds.Count) * 64));
            Widgets.BeginScrollView(area, ref scroll, view);
            float y = 0;
            foreach (var slot in owner.slots)
            {
                Widgets.Label(new Rect(0, y, view.width, 30), "RMS_SlotTitle".Translate(owner.slots.IndexOf(slot) + 1,
                    string.IsNullOrEmpty(slot.bloodlineId) ? "RMS_EmptySlot".Translate().ToString() : "RMS_Druid".Translate().ToString()));
                y += 38;
                DrawBeast(slot.primaryBeastId, true, view.width, ref y);
                foreach (string id in slot.reserveBeastIds.ToList()) DrawBeast(id, false, view.width, ref y);
            }
            Widgets.EndScrollView();
        }
        private void DrawBeast(string id, bool primary, float width, ref float y)
        {
            var beast = SummonerArchive.Current.FindBeast(id);
            if (beast == null) { Widgets.Label(new Rect(8, y, width, 28), "RMS_NoPrimary".Translate()); y += 64; return; }
            string status = beast.dead || beast.pawn == null || beast.pawn.Dead ? "RMS_Dead"
                : beast.sleepEndTick > Find.TickManager.TicksGame ? "RMS_Sleeping"
                : beast.presence == BeastPresence.OnField ? "RMS_OnField" : "RMS_InRealm";
            Widgets.Label(new Rect(8, y, width - 240, 28), (primary ? "RMS_Primary" : "RMS_Reserve").Translate()
                + " · " + (beast.pawn?.LabelShort ?? "RMS_BeastUnavailable".Translate().ToString()));
            Widgets.Label(new Rect(8, y + 28, width - 240, 25), "RMS_BeastState".Translate(beast.tier, beast.tierLevel, status.Translate()));
            if (primary)
            {
                bool onField = beast.presence == BeastPresence.OnField;
                if (Widgets.ButtonText(new Rect(width - 170, y + 8, 160, 36), (onField ? "RMS_Recall" : "RMS_Summon").Translate()))
                {
                    string error;
                    bool ok = onField ? SummonerGameplay.TryRecall(master, id, out error) : SummonerGameplay.TrySummon(master, id, out error);
                    SummonerCommands.ShowResult(ok, error, master);
                }
            }
            else if (Widgets.ButtonText(new Rect(width - 170, y + 8, 160, 36), "RMS_MakePrimary".Translate()))
            { bool ok = SummonerGameplay.TrySwitchPrimary(master, id, out var error); SummonerCommands.ShowResult(ok, error, master); }
            y += 64;
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.TryGetAttackVerb))]
    internal static class SummonerRangedAttack
    {
        private static void Postfix(Pawn __instance, Thing target, ref Verb __result)
        {
            if (__instance.def.GetModExtension<SummonerBeastDef>()?.ranged != true
                || __instance.GetComp<CompSummonerBeastLink>()?.RecordOrNull() == null) return;
            // Retain claws in melee range; at range select the innate wind verb.
            if (target != null && __instance.Position.DistanceToSquared(target.Position) <= 2) return;
            var ranged = __instance.VerbTracker.AllVerbs.FirstOrDefault(v => !v.IsMeleeAttack && v.Available());
            if (ranged != null) __result = ranged;
        }
    }
}
