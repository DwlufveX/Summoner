using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using RimMod.Summoner;
using RimWorld;
using Verse;

namespace RimMod.Summoner.Tests
{
    public sealed class ArchiveTestMod : Mod
    {
        public ArchiveTestMod(ModContentPack content) : base(content)
        {
            if (!ArchiveGameTest.Enabled && !GameplayGameTest.Enabled) return;
            ArchiveTestLog.Begin();
            new Harmony("rimmod.summoner.integration").Patch(typeof(Log).GetMethod("Error", new[] { typeof(string) }),
                prefix: new HarmonyMethod(typeof(ArchiveTestLog), nameof(ArchiveTestLog.Observe)));
        }
    }

    public static class ArchiveTestLog
    {
        private static readonly object gate = new object();
        private static readonly List<string> errors = new List<string>();
        private static bool active;
        public static void Begin() { lock (gate) { errors.Clear(); active = true; } }
        public static void Observe(string __0)
        {
            if (__0 == null) return;
            lock (gate)
            {
                if (active) errors.Add(__0);
            }
        }
        public static void RequireClean()
        {
            lock (gate)
                if (errors.Count != 0) throw new InvalidOperationException("Game save/load logged " + errors.Count + " error(s): " + errors[0]);
        }
    }

    // Exists only in the private test-support mod; it is never part of the release mod.
    public sealed class ArchiveGameTest : GameComponent
    {
        public static bool Enabled => Environment.GetCommandLineArgs().Contains("-summoner-archive-test");
        private int phase, ownerThingId, fieldThingId, realmThingId, injuryId, ageTicks;
        private string ownerId, fieldId, realmId;
        private Pawn owner, field, realm;
        private bool ran, pendingLoad;
        public ArchiveGameTest(Game game) { }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref phase, "testPhase");
            Scribe_Values.Look(ref ownerThingId, "ownerThingId");
            Scribe_Values.Look(ref fieldThingId, "fieldThingId");
            Scribe_Values.Look(ref realmThingId, "realmThingId");
            Scribe_Values.Look(ref injuryId, "injuryId");
            Scribe_Values.Look(ref ageTicks, "ageTicks");
            Scribe_Values.Look(ref ownerId, "ownerId");
            Scribe_Values.Look(ref fieldId, "fieldId");
            Scribe_Values.Look(ref realmId, "realmId");
            Scribe_References.Look(ref owner, "owner");
            Scribe_References.Look(ref field, "field");
            Scribe_References.Look(ref realm, "realm");
        }

        public override void GameComponentTick()
        {
            if (!Enabled || ran || phase != 0 || Find.CurrentMap == null || Find.TickManager.TicksGame < 200) return;
            ran = true;
            try { Setup(); } catch (Exception ex) { Fail(ex); }
        }

        public override void GameComponentUpdate()
        {
            if (!Enabled) return;
            if (pendingLoad)
            {
                pendingLoad = false;
                try { GameDataSaveLoader.LoadGame("SummonerArchiveIntegration"); } catch (Exception ex) { Fail(ex); }
                return;
            }
            if (phase > 0 && !ran && Find.CurrentMap != null && Current.ProgramState == ProgramState.Playing && Scribe.mode == LoadSaveMode.Inactive)
            {
                ran = true;
                try { VerifyLoaded(); } catch (Exception ex) { Fail(ex); }
            }
        }

        private static void Check(bool condition, string label)
        {
            if (!condition) throw new InvalidOperationException(label);
            Log.Message("[SummonerArchiveTest] PASS " + label);
        }

        private void Setup()
        {
            ArchiveTestLog.RequireClean();
            Log.Message("[SummonerArchiveTest] game=" + VersionControl.CurrentVersionString + " temporaryCache=" + GenFilePaths.TempFolderPath);
            var archive = SummonerArchive.Current;
            owner = Find.CurrentMap.mapPawns.FreeColonistsSpawned.First();
            var kind = DefDatabase<PawnKindDef>.GetNamed("RMS_ArchiveTestWolf");
            field = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
            realm = PawnGenerator.GeneratePawn(kind, Faction.OfPlayer);
            GenSpawn.Spawn(field, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 3), owner.Map);
            var summoner = archive.RegisterSummoner(owner);
            ownerId = summoner.id;
            var fieldRecord = archive.RegisterBeast(ownerId, out string error);
            var realmRecord = archive.RegisterBeast(ownerId, out error);
            fieldId = fieldRecord.id; realmId = realmRecord.id;
            Check(archive.BindFieldPawn(fieldId, field, out error), "real map pawn binds: " + error);
            Check(archive.BindRealmPawn(realmId, realm, out error), "real detached pawn enters holder: " + error);
            Check(field.GetComp<CompSummonerBeastLink>().RecordOrNull() == fieldRecord && realm.GetComp<CompSummonerBeastLink>().RecordOrNull() == realmRecord, "foreign keys available immediately");
            Check(archive.TrySetSlotBloodline(summoner.slots[0].id, "druid", out error), error);
            Check(archive.TrySetBeastBloodline(fieldId, "druid", out error) && archive.TrySetBeastBloodline(realmId, "druid", out error), error);
            Check(archive.TryAssignPrimary(summoner.slots[0].id, fieldId, out error) && archive.TryAddReserve(summoner.slots[0].id, realmId, out error), error);
            Check(archive.TrySetGrowth(fieldId, 2, 12, 321, out error), error);
            Check(archive.TrySetStatInvestment(fieldId, "melee", 4, out error) && archive.TrySetMutation(realmId, "size", 1, out error), error);
            Check(archive.TrySetSkillRecord(fieldId, "bite", 2, 1, out error) && archive.TrySetWorkRecord(realmId, "Doctor", 8, out error), error);
            Check(archive.TrySetProficiency(ownerId, 337, out error), error);
            Check(archive.TrySetLockoutEndTick(fieldId, 60000, out error) && archive.TrySetRest(realmId, BeastRest.Sleeping, 180000, out error), error);
            var reward = archive.AddPendingReward(ownerId, RewardSubjectKind.Beast, fieldId, PendingRewardKind.BeastGrowthChoice, 2, 12, out error);
            Check(reward != null && archive.TrySetRewardOptions(reward.id, new[] { "damage", "speed", "size" }, -1, false, out error), error);
            var cut = HediffMaker.MakeHediff(HediffDefOf.Cut, realm, realm.RaceProps.body.corePart);
            cut.Severity = 2.5f;
            realm.health.AddHediff(cut);
            injuryId = cut.loadID;
            realm.needs.food.CurLevel = .6f;
            realm.needs.rest.CurLevel = .4f;
            ageTicks = (int)realm.ageTracker.AgeBiologicalTicks;
            var steel = ThingMaker.MakeThing(ThingDefOf.Steel); steel.stackCount = 5;
            Check(realm.inventory.innerContainer.TryAdd(steel), "nested inventory owned by realm pawn");
            ownerThingId = owner.thingIDNumber; fieldThingId = field.thingIDNumber; realmThingId = realm.thingIDNumber;
            Check(archive.ConsistencyReport() == "", "data consistency before save");
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
            phase = 1;
            SaveAndScheduleLoad();
        }

        private void SaveAndScheduleLoad()
        {
            GameDataSaveLoader.SaveGame("SummonerArchiveIntegration");
            ArchiveTestLog.RequireClean();
            Check(File.Exists(GenFilePaths.FilePathForSavedGame("SummonerArchiveIntegration")), "real rws file exists");
            Log.Message("[SummonerArchiveTest] SAVE COMPLETE round=" + phase);
            pendingLoad = true;
        }

        private void VerifyLoaded()
        {
            ArchiveTestLog.RequireClean();
            var archive = SummonerArchive.Current;
            var summoner = archive.FindSummoner(ownerId);
            var a = archive.FindBeast(fieldId);
            var b = archive.FindBeast(realmId);
            Check(summoner != null && summoner.master == owner && owner.thingIDNumber == ownerThingId, "master reference and ID restored");
            Check(a.pawn == field && field.Spawned && field.thingIDNumber == fieldThingId, "map pawn reference and ID restored");
            Check(b.pawn == realm && archive.SoulRealm.PawnFor(realmId) == realm && !realm.Spawned && realm.thingIDNumber == realmThingId, "realm pawn deep ownership and ID restored");
            Check(field.GetComp<CompSummonerBeastLink>().RecordOrNull() == a && realm.GetComp<CompSummonerBeastLink>().RecordOrNull() == b, "component foreign keys restored");
            Check(a.tier == 2 && a.tierLevel == 12 && a.experience == 321 && a.lockoutEndTick == 60000 && b.sleepEndTick == 180000, "growth and cooldowns restored");
            Check(a.statInvestments.Single().count == 4 && b.mutations.Single().stacks == 1 && a.skills.Single().level == 2 && b.works.Single().level == 8, "all growth collections restored");
            Check(summoner.proficiency == 337 && summoner.slots[0].primaryBeastId == fieldId && summoner.slots[0].reserveBeastIds.Single() == realmId, "roster and proficiency restored");
            Check(summoner.pendingRewards.Single().choice.optionIds.SequenceEqual(new[] { "damage", "speed", "size" }), "reward candidates restored");
            var injury = realm.health.hediffSet.hediffs.Single(h => h.loadID == injuryId);
            Check(injury.def == HediffDefOf.Cut && Math.Abs(injury.Severity - 2.5f) < .001f && injury.Part == realm.RaceProps.body.corePart, "injury body part severity and ID restored");
            Check(realm.ageTracker.AgeBiologicalTicks == ageTicks && Math.Abs(realm.needs.food.CurLevel - .6f) < .001f && Math.Abs(realm.needs.rest.CurLevel - .4f) < .001f, "age and needs restored");
            Check(realm.inventory.innerContainer.Count == 1 && realm.inventory.innerContainer[0].def == ThingDefOf.Steel && realm.inventory.innerContainer[0].stackCount == 5, "nested inventory restored");
            var allThings = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(archive, allThings, true, holder => true);
            Check(allThings.Contains(realm) && allThings.Contains(realm.inventory.innerContainer[0]), "recursive holder graph includes nested inventory");
            Check(archive.ConsistencyReport() == "", "loaded data consistent");
            if (phase == 1)
            {
                var next = archive.RegisterBeast(ownerId, out string error);
                Check(next != null && next.id == "beast-3", "ID allocation after load");
                phase = 2;
                SaveAndScheduleLoad();
                return;
            }
            Check(archive.Beasts.Count == 3 && Scribe.mode == LoadSaveMode.Inactive, "second load complete without duplicates");
            ArchiveTestLog.RequireClean();
            Log.Message("[SummonerArchiveTest] ALL TESTS PASSED");
            File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "archive-result.txt"), "PASS: two complete game save/load rounds; zero captured errors.");
            UnityEngine.Application.Quit();
        }

        private static void Fail(Exception error)
        {
            File.WriteAllText(Path.Combine(GenFilePaths.SaveDataFolderPath, "archive-result.txt"), "FAIL: " + error);
            Log.Error("[SummonerArchiveTest] FAILED " + error);
            UnityEngine.Application.Quit();
        }
    }
}
