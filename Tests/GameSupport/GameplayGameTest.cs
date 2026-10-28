using System;
using System.IO;
using System.Linq;
using RimMod.Summoner;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimMod.Summoner.Tests
{
    public sealed class GameplayGameTest : GameComponent
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("-summoner-gameplay-test");
        private int step, phase, deadline, targetHp, injuryId;
        private Pawn master, wolf, bear, hawk;
        private Building_SpiritCage cage;
        private Building_SpiritCage pendingCage;
        private Pawn captive;
        private Thing attackTarget;
        private IntVec3 moveCell;
        private string ownerId, wolfId, bearId, hawkId;
        private bool ran, pendingLoad;
        public GameplayGameTest(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref phase, "phase");
            Scribe_Values.Look(ref injuryId, "injuryId");
            Scribe_Values.Look(ref ownerId, "ownerId");
            Scribe_Values.Look(ref wolfId, "wolfId");
            Scribe_Values.Look(ref bearId, "bearId");
            Scribe_Values.Look(ref hawkId, "hawkId");
            Scribe_References.Look(ref master, "master");
            Scribe_References.Look(ref wolf, "wolf");
            Scribe_References.Look(ref bear, "bear");
            Scribe_References.Look(ref hawk, "hawk");
            Scribe_References.Look(ref pendingCage, "pendingCage");
            Scribe_References.Look(ref captive, "captive");
        }
        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Log.Message("[SummonerGameplayTest] PASS " + label);
        }
        public override void GameComponentTick()
        {
            if (!Enabled || phase > 0 || Find.CurrentMap == null || Find.TickManager.TicksGame < 200) return;
            try
            {
                ArchiveTestLog.RequireClean();
                int now = Find.TickManager.TicksGame;
                if (step > 0 && now > deadline) throw new InvalidOperationException("Timed out at gameplay step " + step);
                if (step == 0)
                {
                    master = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
                    var probe = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("RMS_SpiritWolf"), Faction.OfPlayer);
                    GenSpawn.Spawn(probe, master.Position, master.Map);
                    Check(probe.GetGizmos().OfType<Command_Action>().Any(g => g.defaultLabel == "RMS_Contract".Translate() && g.Disabled),
                        "unbound beast contract button disabled before any summoner awakens");
                    probe.Destroy();
                    Log.Message("[SummonerGameplayTest] game=" + VersionControl.CurrentVersionString + " cache=" + GenFilePaths.TempFolderPath);
                    var seed = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RMS_AwakeningSeed"));
                    seed.stackCount = 2; GenSpawn.Spawn(seed, master.Position, master.Map);
                    var effect = seed.TryGetComp<CompUseEffect_AwakenSummoner>();
                    Check(effect.CanBeUsedBy(master), "awakening item usable by colonist");
                    effect.DoEffect(master);
                    var owner = SummonerArchive.Current.FindSummoner(master); ownerId = owner.id;
                    Check(owner.fieldCapacity == 1 && owner.slots.Count == 1 && seed.stackCount == 1, "awakening consumes one seed and grants initial capacities");
                    Check(!effect.CanBeUsedBy(master) && !SummonerGameplay.TryAwaken(master, out _), "duplicate awakening rejected");
                    Check(master.GetGizmos().OfType<Command_Action>().Any(g => g.defaultLabel == "RMS_Roster".Translate()), "master roster gizmo available");
                    step = 1; deadline = now + 15000;
                }
                else if (step == 1)
                {
                    cage = master.Map.listerThings.AllThings.OfType<Building_SpiritCage>().FirstOrDefault(c => c.HasAnyContents);
                    if (cage == null) return;
                    Check(cage.ContainedThing.def.defName == "RMS_SpiritWolf", "scheduled first rescue contains wolf");
                    wolf = (Pawn)cage.ContainedThing;
                    master.Map.designationManager.AddDesignation(new Designation(cage, DesignationDefOf.Open));
                    master.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Open, cage));
                    step = 2;
                }
                else if (step == 2)
                {
                    if (cage.HasAnyContents) return;
                    Check(wolf.Spawned && wolf.Faction == Faction.OfPlayer, "actual open-cage job releases rescued beast");
                    master.jobs.TryTakeOrderedJob(JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("RMS_ContractBeast"), wolf));
                    step = 3;
                }
                else if (step == 3)
                {
                    var record = SummonerArchive.Current.FindBeastByPawn(wolf);
                    if (record == null) return;
                    wolfId = record.id;
                    Check(!wolf.Spawned && record.role == BeastRosterRole.Primary && SummonerArchive.Current.FindSummoner(ownerId).slots[0].bloodlineId == "druid", "actual contract job assigns primary and locks bloodline");
                    Check(SummonerGameplay.TrySummon(master, wolfId, out var error), "summon primary: " + error);
                    Check(wolf.CanTakeOrder && !wolf.IsColonist && wolf.needs.food != null && wolf.needs.rest != null && wolf.needs.mood == null, "beast control and needs without colonist reclassification");
                    Check(wolf.GetGizmos().OfType<Command_Toggle>().Any(), "beast draft gizmo available");
                    wolf.drafter.Drafted = true;
                    Check(CellFinder.TryFindRandomCellNear(wolf.Position, wolf.Map, 3,
                        c => c != wolf.Position && c.Standable(wolf.Map) && wolf.CanReach(c, PathEndMode.OnCell, Danger.None), out moveCell), "movement target available");
                    wolf.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, moveCell));
                    step = 4;
                }
                else if (step == 4)
                {
                    if (now % 600 == 0) Log.Message("[SummonerGameplayTest] MOVE position=" + wolf.Position + " target=" + moveCell + " job=" + wolf.CurJob + " drafted=" + wolf.Drafted);
                    if (wolf.Position != moveCell) return;
                    Check(wolf.Drafted, "actual drafted move job completes without auto-undraft");
                    SetupRotationAndCombat();
                    step = 5;
                }
                else if (step == 5)
                {
                    if (attackTarget.HitPoints >= targetHp) return;
                    Check(attackTarget.HitPoints < targetHp, "hawk wind projectile damages real target");
                    hawk.jobs.StopAll(); hawk.drafter.Drafted = false;
                    SetupSavedState();
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                    phase = 1; ran = true; SaveAndLoad();
                }
            }
            catch (Exception ex) { Fail(ex); }
        }

        private Pawn ContractNew(string kind, out string id)
        {
            var pawn = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed(kind), Faction.OfPlayer);
            GenSpawn.Spawn(pawn, master.Position, master.Map);
            Check(SummonerGameplay.TryContract(master, pawn, out var error), "contract " + kind + ": " + error);
            id = SummonerArchive.Current.FindBeastByPawn(pawn).id;
            return pawn;
        }
        private void SetupRotationAndCombat()
        {
            var archive = SummonerArchive.Current;
            bear = ContractNew("RMS_SpiritBear", out bearId);
            hawk = ContractNew("RMS_SpiritHawk", out hawkId);
            Check(!SummonerGameplay.TrySummon(master, bearId, out _) && !bear.Spawned && wolf.Spawned, "reserve cannot deploy directly");
            archive.TrySetRest(hawkId, BeastRest.Sleeping, Find.TickManager.TicksGame + 180000, out _);
            Check(!SummonerGameplay.TrySwitchPrimary(master, hawkId, out _) && wolf.Spawned, "sleeping switch rejected before recalling old beast");
            archive.TrySetRest(hawkId, BeastRest.None, -1, out _);
            archive.TrySetOccupySlots(hawkId, 2, out _);
            Check(!SummonerGameplay.TrySwitchPrimary(master, hawkId, out _) && wolf.Spawned, "oversized replacement rejected without losing old field pawn");
            archive.TrySetOccupySlots(hawkId, 1, out _);
            var other = master.Map.mapPawns.FreeColonistsSpawned.First(p => p != master);
            archive.RegisterSummoner(other);
            Check(!SummonerGameplay.TryRecall(other, wolfId, out _) && wolf.Spawned, "other summoner cannot recall owned beast");
            Check(SummonerGameplay.TrySwitchPrimary(master, bearId, out var error), "switch to bear: " + error);
            Check(bear.Spawned && !wolf.Spawned && archive.FieldLoad(ownerId) == 1, "rotation transfers one field capacity");
            Check(SummonerGameplay.TrySwitchPrimary(master, hawkId, out error), "switch to hawk: " + error);
            Check(hawk.Spawned && !bear.Spawned && archive.FieldLoad(ownerId) == 1, "second rotation keeps single primary on field");
            hawk.drafter.Drafted = true;
            Check(CellFinder.TryFindRandomCellNear(hawk.Position, hawk.Map, 10,
                c => c.Standable(hawk.Map) && c.DistanceTo(hawk.Position) >= 5 && GenSight.LineOfSight(hawk.Position, c, hawk.Map)
                    && c.GetThingList(hawk.Map).All(t => t.def.category != ThingCategory.Building), out var targetCell), "ranged target position available");
            attackTarget = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.Steel);
            GenSpawn.Spawn(attackTarget, targetCell, hawk.Map); targetHp = attackTarget.HitPoints;
            var shoot = JobMaker.MakeJob(JobDefOf.AttackStatic, attackTarget); shoot.maxNumStaticAttacks = 15;
            hawk.jobs.TryTakeOrderedJob(shoot);
        }
        private void SetupSavedState()
        {
            var archive = SummonerArchive.Current;
            ContractNew("RMS_SpiritWolf", out _);
            int count = archive.Beasts.Count;
            var overflow = PawnGenerator.GeneratePawn(DefDatabase<PawnKindDef>.GetNamed("RMS_SpiritBear"), Faction.OfPlayer);
            GenSpawn.Spawn(overflow, master.Position, master.Map);
            Check(!SummonerGameplay.TryContract(master, overflow, out _) && overflow.Spawned && archive.Beasts.Count == count,
                "fifth contract rejected without removing pawn or creating record");
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, wolf, wolf.RaceProps.body.corePart);
            cut.Severity = 2.5f; wolf.health.AddHediff(cut); injuryId = cut.loadID;
            var steel = ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount = 7; wolf.inventory.innerContainer.TryAdd(steel);
            Check(archive.ConsistencyReport() == "", "gameplay data consistent before save");
            Check(SummonerAdventure.TryCreateRescue(archive.FindSummoner(ownerId), DefDatabase<PawnKindDef>.GetNamed("RMS_SpiritBear"), out pendingCage),
                "unopened rescue fixture created for save validation");
            captive = (Pawn)pendingCage.ContainedThing;
        }
        private void SaveAndLoad()
        {
            GameDataSaveLoader.SaveGame("SummonerGameplayIntegration"); ArchiveTestLog.RequireClean();
            Log.Message("[SummonerGameplayTest] SAVE COMPLETE round=" + phase); pendingLoad = true;
        }
        public override void GameComponentUpdate()
        {
            if (!Enabled) return;
            try
            {
                if (phase == 0 && Current.ProgramState == ProgramState.Playing && Find.TickManager != null)
                {
                    if (Find.TickManager.CurTimeSpeed == TimeSpeed.Paused)
                        Log.Message("[SummonerGameplayTest] resume paused fixture step=" + step);
                    Find.TickManager.CurTimeSpeed = TimeSpeed.Normal;
                }
                if (pendingLoad) { pendingLoad = false; GameDataSaveLoader.LoadGame("SummonerGameplayIntegration"); return; }
                if (phase == 0 || ran || Current.ProgramState != ProgramState.Playing || Find.CurrentMap == null || Scribe.mode != LoadSaveMode.Inactive) return;
                ran = true; ArchiveTestLog.RequireClean();
                var archive = SummonerArchive.Current; var owner = archive.FindSummoner(ownerId);
                Check(owner.master == master && owner.rescueSequence == 1 && owner.nextRescueTick > 0, "awakening and rescue progression restored");
                Check(owner.slots[0].primaryBeastId == hawkId && owner.slots[0].reserveBeastIds.Count == 3, "primary and three reserves restored");
                Check(archive.FindBeast(hawkId).pawn == hawk && hawk.Spawned && hawk.CanTakeOrder && !hawk.IsColonist, "field hawk identity and control restored");
                Check(archive.SoulRealm.PawnFor(wolfId) == wolf && archive.SoulRealm.PawnFor(bearId) == bear && !wolf.Spawned && !bear.Spawned, "realm wolf and bear identities restored");
                Check(wolf.health.hediffSet.hediffs.Any(h => h.loadID == injuryId && Math.Abs(h.Severity - 2.5f) < .001f)
                    && wolf.inventory.innerContainer[0].stackCount == 7, "realm injury and inventory restored");
                Check(archive.ConsistencyReport() == "", "loaded gameplay archive consistent");
                if (phase == 1)
                {
                    Check(pendingCage.Spawned && pendingCage.ContainedThing == captive && !captive.Spawned && !captive.Dead,
                        "unopened cage and captive deep ownership restored");
                    pendingCage.Open();
                    Check(captive.Spawned && captive.Faction == Faction.OfPlayer && !pendingCage.HasAnyContents,
                        "restored rescue cage opens without duplicating captive");
                    Check(SummonerGameplay.TryRecall(master, hawkId, out var error), "recall after load: " + error);
                    Check(SummonerGameplay.TrySwitchPrimary(master, wolfId, out error) && SummonerGameplay.TrySummon(master, wolfId, out error), "switch and summon after load: " + error);
                    Check(SummonerGameplay.TrySwitchPrimary(master, hawkId, out error), "restore hawk rotation: " + error);
                    phase = 2; SaveAndLoad(); return;
                }
                Check(archive.Beasts.Count == 4 && archive.FieldLoad(ownerId) == 1, "second load without duplicate beasts or field overflow");
                Check(captive.Spawned && !pendingCage.HasAnyContents && captive.GetComp<CompSummonerBeastLink>().RecordOrNull() == null,
                    "second load preserves released unbound captive");
                ArchiveTestLog.RequireClean(); Log.Message("[SummonerGameplayTest] ALL TESTS PASSED");
                File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "gameplay-result.txt"),
                    "PASS: awakening, real rescue/contract/move jobs, three beasts, rotation, ranged combat, constraints and two save/load rounds; zero captured errors.");
                Application.Quit();
            }
            catch (Exception ex) { Fail(ex); }
        }
        private static void Fail(Exception ex)
        {
            File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "gameplay-result.txt"), "FAIL: " + ex);
            Log.Error("[SummonerGameplayTest] FAILED " + ex); Application.Quit();
        }
    }
}
