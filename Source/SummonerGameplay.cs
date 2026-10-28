using System;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace RimMod.Summoner
{
    public sealed class SummonerBeastDef : DefModExtension
    {
        public string bloodlineId = "druid";
        public bool ranged;
    }

    public static class SummonerGameplay
    {
        public static bool IsSpiritBeast(Pawn pawn) => pawn?.def.GetModExtension<SummonerBeastDef>() != null;
        public static bool UsableMaster(Pawn pawn) => pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed
            && pawn.IsColonistPlayerControlled && pawn.Faction == Faction.OfPlayer;
        private static string Message(string key) => key.Translate().ToString();

        public static bool TryAwaken(Pawn master, out string error)
        {
            error = null;
            var archive = SummonerArchive.Current;
            if (!UsableMaster(master) || archive == null) { error = Message("RMS_MasterUnavailable"); return false; }
            if (archive.FindSummoner(master) != null) { error = Message("RMS_AlreadyAwakened"); return false; }
            var owner = archive.RegisterSummoner(master);
            owner.nextRescueTick = Find.TickManager.TicksGame + 600;
            return true;
        }

        public static ContractSlotRecord AvailableSlot(SummonerRecord owner, string bloodline)
        {
            return owner?.slots.FirstOrDefault(slot => ((string.IsNullOrEmpty(slot.bloodlineId)
                && string.IsNullOrEmpty(slot.primaryBeastId) && slot.reserveBeastIds.Count == 0) || slot.bloodlineId == bloodline)
                && (string.IsNullOrEmpty(slot.primaryBeastId) || slot.reserveBeastIds.Count < SummonerLimits.MaxReservesPerSlot));
        }

        public static bool TryContract(Pawn master, Pawn pawn, out string error)
        {
            error = null;
            var archive = SummonerArchive.Current;
            var owner = archive?.FindSummoner(master);
            if (!UsableMaster(master) || owner == null) { error = Message("RMS_MasterUnavailable"); return false; }
            if (!IsSpiritBeast(pawn) || pawn.Dead || pawn.Destroyed || pawn.Downed || !pawn.Spawned
                || pawn.Map != master.Map || (pawn.Faction != null && pawn.Faction != Faction.OfPlayer)
                || pawn.GetComp<CompSummonerBeastLink>().RecordOrNull() != null)
            { error = Message("RMS_CannotContract"); return false; }
            if (!master.Position.AdjacentTo8WayOrInside(pawn.Position)) { error = Message("RMS_TooFarToContract"); return false; }
            var spec = pawn.def.GetModExtension<SummonerBeastDef>();
            var slot = AvailableSlot(owner, spec.bloodlineId);
            if (slot == null) { error = Message("RMS_RosterFull"); return false; }
            var map = pawn.Map; var position = pawn.Position; var rotation = pawn.Rotation; var faction = pawn.Faction;
            var beast = archive.RegisterBeast(owner.id, out error);
            if (beast == null) return false;
            archive.TrySetBeastBloodline(beast.id, spec.bloodlineId, out error);
            string previousBloodline = slot.bloodlineId;
            if (!archive.TrySetSlotBloodline(slot.id, spec.bloodlineId, out error))
            { archive.RollbackNewBeast(beast); return false; }
            EnsureControl(pawn);
            if (pawn.Faction != Faction.OfPlayer) pawn.SetFaction(Faction.OfPlayer);
            pawn.jobs.StopAll();
            pawn.DeSpawn();
            if (!archive.BindRealmPawn(beast.id, pawn, out error)
                || !(string.IsNullOrEmpty(slot.primaryBeastId) ? archive.TryAssignPrimary(slot.id, beast.id, out error)
                    : archive.TryAddReserve(slot.id, beast.id, out error)))
            {
                archive.RollbackNewBeast(beast);
                archive.TrySetSlotBloodline(slot.id, previousBloodline, out _);
                pawn.SetFaction(faction);
                GenSpawn.Spawn(pawn, position, map, rotation);
                return false;
            }
            pawn.training.Train(TrainableDefOf.Obedience, master, true);
            pawn.training.Train(DefDatabase<TrainableDef>.GetNamed("Haul"), master, true);
            pawn.playerSettings.Master = master;
            return true;
        }

        public static void EnsureControl(Pawn pawn)
        {
            if (pawn.drafter == null) pawn.drafter = new Pawn_DraftController(pawn);
            if (pawn.playerSettings == null) pawn.playerSettings = new Pawn_PlayerSettings(pawn);
        }

        private static bool Owned(Pawn master, string beastId, out SummonerRecord owner, out BeastRecord beast, out string error)
        {
            error = null;
            owner = SummonerArchive.Current?.FindSummoner(master);
            beast = SummonerArchive.Current?.FindBeast(beastId);
            if (!UsableMaster(master) || owner == null || beast == null || beast.ownerId != owner.id)
            { error = Message("RMS_MasterUnavailable"); return false; }
            return true;
        }

        private static bool Ready(BeastRecord beast, out string error)
        {
            error = null;
            if (beast.dead || beast.pawn == null || beast.pawn.Dead || beast.pawn.Destroyed || beast.pawn.Downed
                || beast.pawn.Faction != Faction.OfPlayer)
            { error = Message("RMS_BeastUnavailable"); return false; }
            if (beast.sleepEndTick > Find.TickManager.TicksGame)
            { error = Message("RMS_Sleeping"); return false; }
            return true;
        }

        public static bool TrySummon(Pawn master, string beastId, out string error)
        {
            if (!Owned(master, beastId, out var owner, out var beast, out error) || !Ready(beast, out error)) return false;
            if (beast.role != BeastRosterRole.Primary) { error = Message("RMS_PrimaryOnly"); return false; }
            if (beast.presence == BeastPresence.OnField) { error = Message("RMS_AlreadyOnField"); return false; }
            var archive = SummonerArchive.Current;
            if (archive.FieldLoad(owner.id) + beast.occupySlots > owner.fieldCapacity)
            { error = Message("RMS_CapacityFull"); return false; }
            var entry = archive.SoulRealm.FindEntry(beast.id);
            if (entry?.HeldPawn != beast.pawn) { error = Message("RMS_BeastUnavailable"); return false; }
            if (!CellFinder.TryFindRandomCellNear(master.Position, master.Map, 3, c => c.Standable(master.Map)
                && !c.Fogged(master.Map), out var cell)) { error = Message("RMS_NoSpawnCell"); return false; }
            var pawn = beast.pawn;
            entry.Release(pawn);
            archive.SoulRealm.RemoveEntry(beast.id);
            try
            {
                GenSpawn.Spawn(pawn, cell, master.Map);
                if (!archive.BindFieldPawn(beast.id, pawn, out error))
                {
                    pawn.DeSpawn();
                    archive.BindRealmPawn(beast.id, pawn, out _);
                    return false;
                }
                EnsureControl(pawn);
                if (beast.rest == BeastRest.Sleeping) archive.TrySetRest(beast.id, BeastRest.None, -1, out _);
                return true;
            }
            catch
            {
                if (pawn.Spawned) pawn.DeSpawn();
                archive.BindRealmPawn(beast.id, pawn, out _);
                throw;
            }
        }

        public static bool TryRecall(Pawn master, string beastId, out string error)
        {
            if (!Owned(master, beastId, out _, out var beast, out error)) return false;
            var pawn = beast.pawn;
            if (beast.presence != BeastPresence.OnField || pawn == null || !pawn.Spawned || pawn.Map != master.Map
                || pawn.Dead || pawn.Destroyed || pawn.Faction != Faction.OfPlayer)
            { error = Message("RMS_CannotRecall"); return false; }
            var map = pawn.Map; var position = pawn.Position; var rotation = pawn.Rotation;
            if (pawn.carryTracker?.CarriedThing != null && !pawn.carryTracker.TryDropCarriedThing(position, ThingPlaceMode.Near, out _))
            { error = Message("RMS_CannotRecall"); return false; }
            if (pawn.drafter != null) pawn.drafter.Drafted = false;
            pawn.jobs.StopAll();
            pawn.DeSpawn();
            if (SummonerArchive.Current.BindRealmPawn(beast.id, pawn, out error)) return true;
            GenSpawn.Spawn(pawn, position, map, rotation);
            return false;
        }

        public static bool TrySwitchPrimary(Pawn master, string beastId, out string error)
        {
            if (!Owned(master, beastId, out var owner, out var target, out error) || !Ready(target, out error)) return false;
            var slot = owner.slots.FirstOrDefault(s => s.reserveBeastIds.Contains(beastId));
            if (slot == null || target.presence != BeastPresence.InRealm)
            { error = Message("RMS_PrimaryOnly"); return false; }
            var archive = SummonerArchive.Current;
            var old = archive.FindBeast(slot.primaryBeastId);
            bool wasOnField = old?.presence == BeastPresence.OnField;
            if (wasOnField && (old.pawn == null || old.pawn.Map != master.Map)) { error = Message("RMS_CannotRecall"); return false; }
            int resultingLoad = archive.FieldLoad(owner.id) - (wasOnField ? old.occupySlots : 0) + target.occupySlots;
            if (wasOnField && resultingLoad > owner.fieldCapacity) { error = Message("RMS_CapacityFull"); return false; }
            if (wasOnField && !TryRecall(master, old.id, out error)) return false;
            if (!archive.TrySwapPrimaryWithReserve(slot.id, target.id, out error))
            {
                if (wasOnField) TrySummon(master, old.id, out _);
                return false;
            }
            if (!wasOnField || TrySummon(master, target.id, out error)) return true;
            archive.TrySwapPrimaryWithReserve(slot.id, old.id, out _);
            TrySummon(master, old.id, out _);
            return false;
        }
    }

    public sealed class JobGiver_SummonerDrafted : ThinkNode_JobGiver
    {
        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!pawn.Drafted) return null;
            var job = JobMaker.MakeJob(JobDefOf.Wait_Combat);
            job.expiryInterval = 120;
            job.checkOverrideOnExpire = true;
            return job;
        }
    }
}
