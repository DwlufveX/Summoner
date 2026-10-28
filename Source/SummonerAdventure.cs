using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMod.Summoner
{
    public sealed class CompUseEffect_AwakenSummoner : CompUseEffect
    {
        public override AcceptanceReport CanBeUsedBy(Pawn pawn)
        {
            if (!SummonerGameplay.UsableMaster(pawn)) return "RMS_MasterUnavailable".Translate().ToString();
            if (SummonerArchive.Current?.FindSummoner(pawn) != null) return "RMS_AlreadyAwakened".Translate().ToString();
            return true;
        }

        public override void DoEffect(Pawn usedBy)
        {
            if (!SummonerGameplay.TryAwaken(usedBy, out var error))
            { Messages.Message(error, usedBy, MessageTypeDefOf.RejectInput, false); return; }
            base.DoEffect(usedBy);
            parent.SplitOff(1).Destroy();
            Messages.Message("RMS_Awakened".Translate(usedBy.LabelShort), usedBy, MessageTypeDefOf.PositiveEvent);
        }
    }

    // A map-local rescue introduction. Guarded world quests are a later milestone.
    public sealed class SummonerAdventure : GameComponent
    {
        private static readonly string[] kinds = { "RMS_SpiritWolf", "RMS_SpiritBear", "RMS_SpiritHawk" };
        public SummonerAdventure(Game game) { }

        public override void LoadedGame()
        {
            foreach (var beast in SummonerArchive.Current.Beasts)
                if (SummonerGameplay.IsSpiritBeast(beast.pawn) && beast.pawn.Faction == Faction.OfPlayer)
                    SummonerGameplay.EnsureControl(beast.pawn);
        }

        public override void GameComponentTick()
        {
            int now = Find.TickManager.TicksGame;
            if (now % 600 != 0) return;
            foreach (var owner in SummonerArchive.Current.Summoners)
            {
                if (owner.nextRescueTick < 0 || owner.nextRescueTick > now || !SummonerGameplay.UsableMaster(owner.master)
                    || SummonerGameplay.AvailableSlot(owner, "druid") == null) continue;
                var map = owner.master.Map;
                if (map.listerThings.AllThings.OfType<Building_SpiritCage>().Any(c => c.HasAnyContents)
                    || map.mapPawns.AllPawnsSpawned.Any(p => SummonerGameplay.IsSpiritBeast(p)
                        && p.GetComp<CompSummonerBeastLink>().RecordOrNull() == null && !p.Dead)) continue;
                var kind = DefDatabase<PawnKindDef>.GetNamed(kinds[owner.rescueSequence % kinds.Length]);
                if (TryCreateRescue(owner, kind, out var cage))
                {
                    owner.rescueSequence++;
                    owner.nextRescueTick = now + 2 * GenDate.TicksPerDay;
                    Find.LetterStack.ReceiveLetter("RMS_RescueTitle".Translate(),
                        "RMS_RescueText".Translate(owner.master.LabelShort, kind.label), LetterDefOf.PositiveEvent, cage);
                }
            }
        }

        public static bool TryCreateRescue(SummonerRecord owner, PawnKindDef kind, out Building_SpiritCage cage)
        {
            cage = null;
            if (!SummonerGameplay.UsableMaster(owner?.master) || kind?.race.GetModExtension<SummonerBeastDef>() == null) return false;
            var map = owner.master.Map;
            if (!CellFinder.TryFindRandomCellNear(owner.master.Position, map, 16,
                c => c.Standable(map) && !c.Fogged(map) && c.DistanceTo(owner.master.Position) >= 6
                    && c.GetThingList(map).All(t => t.def.category != ThingCategory.Building)
                    && owner.master.CanReach(c, PathEndMode.Touch, Danger.Some), out var cell)) return false;
            var pawn = PawnGenerator.GeneratePawn(kind);
            cage = (Building_SpiritCage)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RMS_SpiritCage"));
            if (!cage.TryAcceptThing(pawn)) { pawn.Destroy(); cage.Destroy(); cage = null; return false; }
            GenSpawn.Spawn(cage, cell, map);
            return true;
        }
    }

    public sealed class Building_SpiritCage : Building_Casket
    {
        public override int OpenTicks => 180;
        public override void Open()
        {
            var pawn = ContainedThing as Pawn;
            base.Open();
            if (pawn != null && pawn.Spawned && !pawn.Dead)
            {
                pawn.SetFaction(Faction.OfPlayer);
                Messages.Message("RMS_BeastRescued".Translate(pawn.LabelShort), pawn, MessageTypeDefOf.PositiveEvent);
            }
        }

        public override IEnumerable<Gizmo> GetGizmos()
        {
            foreach (var gizmo in base.GetGizmos()) yield return gizmo;
            if (!CanOpen) yield break;
            yield return new Command_Action
            {
                defaultLabel = "RMS_OpenCage".Translate(), defaultDesc = "RMS_OpenCageDesc".Translate(),
                icon = SummonerCommands.Icon("Contract"),
                action = () => Find.WindowStack.Add(new FloatMenu(Map.mapPawns.FreeColonistsSpawned
                    .Where(p => !p.Downed && p.CanReach(this, PathEndMode.InteractionCell, Danger.Some))
                    .Select(p => new FloatMenuOption(p.LabelShort, () =>
                    {
                        if (Map.designationManager.DesignationOn(this, DesignationDefOf.Open) == null)
                            Map.designationManager.AddDesignation(new Designation(this, DesignationDefOf.Open));
                        p.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Open, this));
                    })).ToList()))
            };
        }
    }

    public sealed class JobDriver_SummonerContract : JobDriver
    {
        public override bool TryMakePreToilReservations(bool errorOnFailed) => pawn.Reserve(job.targetA, job, 1, -1, null, errorOnFailed);
        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(TargetIndex.A);
            yield return Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            yield return Toils_General.WaitWith(TargetIndex.A, 120, true);
            var finish = ToilMaker.MakeToil("ContractSpiritBeast");
            finish.initAction = () =>
            {
                if (SummonerGameplay.TryContract(pawn, job.targetA.Pawn, out var error))
                    Messages.Message("RMS_Contracted".Translate(), pawn, MessageTypeDefOf.PositiveEvent);
                else Messages.Message(error, pawn, MessageTypeDefOf.RejectInput, false);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }
    }
}
