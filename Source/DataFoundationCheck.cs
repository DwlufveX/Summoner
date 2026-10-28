using System;
using System.Collections.Generic;
using System.IO;
using LudeonTK;
using Verse;

namespace RimMod.Summoner
{
    // Explicit dev entry. Nothing calls this during mod startup or normal play.
    public static class DataFoundationCheck
    {
        public static bool TryRun(out string report)
        {
            List<string> lines = new List<string>();
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                report = "FULL_CHECK SKIPPED: Scribe is busy; no test or cleanup was started.";
                return false;
            }
            if (Current.Game != null)
            {
                RunStage("IN_MEMORY", RunInMemoryConstraints, lines);
                lines.Add("FIELDS SKIPPED: live game; isolated serialization host required.");
                lines.Add("COMPLETE_RECORDS SKIPPED: live game; isolated serialization host required.");
                lines.Add("ENTITY SKIPPED: live game; no test defs were injected.");
                report = string.Join(Environment.NewLine, lines.ToArray());
                return false;
            }
            bool diagnosticsOk = RunStage("DIAGNOSTICS", RunDiagnosticsRegression, lines);
            bool memoryOk = RunStage("IN_MEMORY", RunInMemory, lines);
            bool fieldsOk = RunStage("FIELDS", RunFields, lines);
            bool recordsOk = RunStage("COMPLETE_RECORDS", RunCompleteRecords, lines);
            bool entityOk = RunStage("ENTITY", RunEntity, lines);
            report = string.Join(Environment.NewLine, lines.ToArray());
            return diagnosticsOk && memoryOk && fieldsOk && recordsOk && entityOk;
        }

        public static bool TryRunDiagnosticsOnly(out string report)
        {
            List<string> lines = new List<string>();
            if (Current.Game != null || Scribe.mode != LoadSaveMode.Inactive)
            {
                report = "DIAGNOSTICS SKIPPED: requires an idle isolated host.";
                return false;
            }
            bool ok = RunStage("DIAGNOSTICS", RunDiagnosticsRegression, lines);
            report = string.Join(Environment.NewLine, lines.ToArray());
            return ok;
        }

        [DebugAction("Summoner", "Data constraints only (live game)", allowedGameStates = AllowedGameStates.Playing)]
        private static void DebugRoundtrip()
        {
            string report;
            if (TryRun(out report))
                Log.Message(report);
            else
                Log.Warning(report);
        }

        private static bool RunStage(string name, Action<List<string>> stage, List<string> lines, bool isolated = false)
        {
            ScribeDiagnostics diagnostics = null;
            try
            {
                diagnostics = new ScribeDiagnostics(isolated);
                stage(lines);
                diagnostics.RequireClean(name);
                lines.Add(name + " PASS");
                return true;
            }
            catch (Exception ex)
            {
                lines.Add(name + " FAIL");
                lines.Add(ex.ToString());
                return false;
            }
            finally
            {
                if (diagnostics != null)
                {
                    lines.Add(name + " diagnostics critical=" + diagnostics.CriticalCount + " environment=" + diagnostics.EnvironmentCount);
                    diagnostics.Dispose();
                }
            }
        }

        private static void RunDiagnosticsRegression(List<string> lines)
        {
            for (int pass = 1; pass <= 2; pass++)
            {
                bool returned = false;
                List<string> nested = new List<string>();
                bool accepted = RunStage("EXPECTED_FAILURE", ignored =>
                {
                    Log.Error("Could not resolve cross refs: Summoner expected diagnostic regression.");
                    returned = true;
                }, nested, true);
                Require(returned && !accepted, "A normally returning operation with an internal error passed.");
                Require(RunStage("RECOVERY", ignored => { }, nested, true), "A previous diagnostic contaminated the next operation.");
                Require(Scribe.mode == LoadSaveMode.Inactive, "Diagnostic regression changed Scribe mode.");
                lines.Add("diagnostic regression " + pass + ": normal return rejected; next operation clean");
            }
        }

        private static void RunInMemory(List<string> lines)
        {
            RunInMemoryConstraints(lines);
            RunBindingRegression(lines);
        }

        private static void RunInMemoryConstraints(List<string> lines)
        {
            SummonerArchive archive = BuildSample(lines);
            string consistency = archive.ConsistencyReport();
            if (consistency.Length != 0)
                throw new InvalidOperationException(consistency);
            lines.Add("consistent summoners=" + archive.Summoners.Count + " beasts=" + archive.Beasts.Count);
            RunMasterDedup(lines);
            RunCrossOwnerRejection(lines);
            RunCorruptionDetection(lines);
            RunCapacityRegression(lines);
            RunRewardRegression(lines);
        }

        private static void RunFields(List<string> lines)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
                throw new InvalidOperationException("Scribe is busy (" + Scribe.mode + "). Refusing to touch an active save.");

            SummonerArchive source = BuildSample(new List<string>());
            string path = Path.Combine(Path.GetTempPath(), "SummonerDataFoundation-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                SaveRoot(source, path);
                SummonerArchive loaded = LoadFields(path);
                AssertSame(source, loaded);
                string savedXml = File.ReadAllText(path);
                lines.Add("field bytes=" + savedXml.Length + " (LoadingVars only, cross-refs not resolved)");

                BeastRecord created = loaded.RegisterBeast(loaded.Summoners[0].id, out string createError);
                if (created == null || created.id != "beast-6")
                    throw new InvalidOperationException("Expected next beast id beast-6, got " + (created == null ? createError : created.id));
                lines.Add("id counter preserved beast-6");

                string stripped = savedXml.Replace("<tier>2</tier>", "").Replace("<lockoutEndTick>60000</lockoutEndTick>", "");
                if (stripped == savedXml)
                    throw new InvalidOperationException("Save XML did not contain the tier or lockout nodes needed for the missing-field check.");
                string strippedPath = path + ".stripped.xml";
                File.WriteAllText(strippedPath, stripped);
                try
                {
                    SummonerArchive defaults = LoadFields(strippedPath);
                    BeastRecord growth = defaults.FindBeast("beast-1");
                    BeastRecord lockout = defaults.FindBeast("beast-4");
                    if (growth == null || growth.tier != SummonerLimits.MinTier)
                        throw new InvalidOperationException("Missing tier did not fall back to 1.");
                    if (lockout == null || lockout.lockoutEndTick != SummonerLimits.NoCooldownTick)
                        throw new InvalidOperationException("Missing lockout tick did not fall back to -1.");
                    lines.Add("missing fields default tier=1 lockout=-1");
                }
                finally
                {
                    if (File.Exists(strippedPath))
                        File.Delete(strippedPath);
                }
            }
            finally
            {
                if (Scribe.mode != LoadSaveMode.Inactive)
                    Scribe.ForceStop();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void RunCompleteRecords(List<string> lines)
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
                throw new InvalidOperationException("Scribe is busy (" + Scribe.mode + ") before the record load.");
            SummonerArchive source = BuildSample(new List<string>());
            string path = Path.Combine(Path.GetTempPath(), "SummonerDataFoundation-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                for (int pass = 1; pass <= 2; pass++)
                {
                    SaveRoot(source, path);
                    SummonerArchive loaded = LoadComplete(path);
                    AssertSame(source, loaded);
                    if (Scribe.mode != LoadSaveMode.Inactive)
                        throw new InvalidOperationException("Scribe mode after complete record load " + pass + " is " + Scribe.mode + ".");
                    lines.Add("complete record load " + pass + " returned");
                }
            }
            finally
            {
                AbortScribe();
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        private static void SaveRoot(IExposable root, string path)
        {
            using var diagnostics = new ScribeDiagnostics();
            try
            {
                Scribe.saver.InitSaving(path, "SummonerDataFoundation");
                root.ExposeData();
                Scribe.saver.FinalizeSaving();
                diagnostics.RequireClean("Save");
            }
            catch (Exception ex)
            {
                AbortScribe();
                throw new InvalidOperationException("Save failed (critical diagnostics=" + diagnostics.CriticalCount + ").", ex);
            }
        }

        private static SummonerArchive LoadFields(string path)
        {
            using var diagnostics = new ScribeDiagnostics();
            SummonerArchive loaded = new SummonerArchive(null);
            try
            {
                Scribe.loader.InitLoading(path);
                loaded.ExposeData();
                diagnostics.RequireClean("Field load");
            }
            catch (Exception ex)
            {
                AbortScribe();
                throw new InvalidOperationException("Field load failed (critical diagnostics=" + diagnostics.CriticalCount + ").", ex);
            }
            AbortScribe();
            if (Scribe.mode != LoadSaveMode.Inactive)
                throw new InvalidOperationException("Field load left Scribe in " + Scribe.mode + ".");
            return loaded;
        }

        private static SummonerArchive LoadComplete(string path)
        {
            using var diagnostics = new ScribeDiagnostics();
            EnsureIsolatedPrefs();
            SummonerArchive loaded = new SummonerArchive(null);
            try
            {
                Scribe.loader.InitLoading(path);
                loaded.ExposeData();
                Scribe.loader.FinalizeLoading();
                diagnostics.RequireClean("Complete record load");
            }
            catch (Exception ex)
            {
                AbortScribe();
                throw new InvalidOperationException("Complete load failed (critical diagnostics=" + diagnostics.CriticalCount + ").", ex);
            }
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                AbortScribe();
                throw new InvalidOperationException("FinalizeLoading returned but Scribe mode is " + Scribe.mode + ".");
            }
            return loaded;
        }

        private static void AbortScribe()
        {
            if (Scribe.mode != LoadSaveMode.Inactive)
                Scribe.ForceStop();
        }

        private static void EnsureIsolatedPrefs()
        {
            System.Reflection.FieldInfo field = typeof(Prefs).GetField("data", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (field != null && field.GetValue(null) == null)
                field.SetValue(null, Activator.CreateInstance(field.FieldType));
        }


        private static SummonerArchive BuildSample(List<string> lines)
        {
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord summoner = archive.RegisterSummoner(null);
            if (summoner.fieldCapacity != 1 || summoner.contractSlotCapacity != 1 || summoner.slots.Count != 1 || summoner.proficiency != 0)
                throw new InvalidOperationException("New summoner did not start at field 1, contract slot 1, and one empty slot.");
            if (archive.TryAddContractSlot(summoner.id, out ContractSlotRecord extraSlot, out string slotError) || extraSlot != null)
                throw new InvalidOperationException("Second contract slot was created without extra capacity.");
            lines.Add("initial capacity field=1 slots=1 addSlot=" + slotError);

            string error;
            BeastRecord primary = archive.RegisterBeast(summoner.id, out error);
            BeastRecord reserveA = archive.RegisterBeast(summoner.id, out error);
            BeastRecord reserveB = archive.RegisterBeast(summoner.id, out error);
            BeastRecord reserveC = archive.RegisterBeast(summoner.id, out error);
            Require(primary != null && reserveA != null && reserveB != null && reserveC != null, error);

            ContractSlotRecord slot = summoner.slots[0];
            Require(archive.TrySetSlotBloodline(slot.id, "druid", out error), error);
            Require(archive.TrySetBeastBloodline(primary.id, "druid", out error), error);
            Require(archive.TrySetBeastBloodline(reserveA.id, "druid", out error), error);
            Require(archive.TrySetBeastBloodline(reserveB.id, "", out error), error);
            Require(archive.TrySetBeastBloodline(reserveC.id, "druid", out error), error);
            Require(archive.TryAssignPrimary(slot.id, primary.id, out error), error);
            Require(archive.TryAddReserve(slot.id, reserveA.id, out error), error);
            Require(archive.TryAddReserve(slot.id, reserveB.id, out error), error);
            Require(archive.TryAddReserve(slot.id, reserveC.id, out error), error);
            if (slot.reserveBeastIds.Count != 3 || slot.primaryBeastId != primary.id)
                throw new InvalidOperationException("Primary and reserves were not counted separately.");

            BeastRecord overflow = archive.RegisterBeast(summoner.id, out error);
            if (archive.TryAddReserve(slot.id, overflow.id, out error))
                throw new InvalidOperationException("A fourth reserve was accepted.");
            lines.Add("reserve cap " + error);

            Require(archive.TrySetBeastBloodline(overflow.id, "undead", out error), error);
            if (archive.TryAddReserve(slot.id, overflow.id, out error))
                throw new InvalidOperationException("A conflicting bloodline joined a full slot.");

            Require(archive.TrySetGrowth(primary.id, 2, 10, 100f, out error), error);
            Require(archive.TrySetOccupySlots(reserveB.id, 2, out error), error);
            Require(archive.TrySetPresence(primary.id, BeastPresence.OnField, out error), error);
            int load = archive.FieldLoad(summoner.id);
            if (load != 1)
                throw new InvalidOperationException("Field load with one on-field beast and three realm reserves was " + load + ".");
            if (archive.TrySetPresence(reserveA.id, BeastPresence.OnField, out error))
                throw new InvalidOperationException("Field capacity ignored a second on-field beast.");
            lines.Add("field load=1 reserves do not add; blocked: " + error);

            Require(archive.TrySetCapacities(summoner.id, 3, 1, out error), error);
            Require(archive.TrySetPresence(reserveB.id, BeastPresence.OnField, out error), error);
            if (archive.FieldLoad(summoner.id) != 3)
                throw new InvalidOperationException("Occupy 2 was not added to field load.");
            if (archive.TrySetPresence(reserveA.id, BeastPresence.OnField, out error))
                throw new InvalidOperationException("Field load exceeded the raised capacity.");
            lines.Add("field load=3 after occupy-2; still blocked: " + error);

            Require(archive.TrySetRest(reserveA.id, BeastRest.Sleeping, 180000, out error), error);
            Require(archive.TrySetLockoutEndTick(reserveC.id, 60000, out error), error);
            Require(archive.TrySetStatInvestment(primary.id, "melee", 3, out error), error);
            Require(archive.TrySetMutation(reserveA.id, "size", 1, out error), error);
            Require(archive.TrySetSkillRecord(reserveC.id, "bite", 2, 1, out error), error);
            Require(archive.TrySetWorkRecord(reserveC.id, "Doctor", 8, out error), error);
            Require(archive.TrySetProficiency(summoner.id, 40, out error), error);

            archive.MarkDead(reserveC.id);
            archive.RebuildRosterCache();
            if (!reserveC.dead || slot.reserveBeastIds.Contains(reserveC.id) == false || reserveC.role != BeastRosterRole.Reserve)
                throw new InvalidOperationException("Death removed the reserve membership or its growth link.");
            if (reserveC.tier != 1 || primary.experience != 100f)
                throw new InvalidOperationException("Death or growth storage changed an unrelated value.");
            lines.Add("dead reserve stays in slot; field load=" + archive.FieldLoad(summoner.id));

            PendingRewardRecord reward = archive.AddPendingReward(summoner.id, RewardSubjectKind.Beast, primary.id, PendingRewardKind.BeastGrowthChoice, 2, 10, out error);
            Require(reward != null, error);
            Require(archive.TrySetRewardOptions(reward.id, new[] { "opt-damage", "opt-speed", "opt-size" }, 1, false, out error), error);

            string consistency = archive.ConsistencyReport();
            if (consistency.Length != 0)
                throw new InvalidOperationException(consistency);
            return archive;
        }

        private static void RunMasterDedup(List<string> lines)
        {
            Pawn master = new Pawn();
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord first = archive.RegisterSummoner(master);
            SummonerRecord second = archive.RegisterSummoner(master);
            if (first != second || archive.Summoners.Count != 1)
                throw new InvalidOperationException("The same master created two summoner records.");
            lines.Add("master dedup " + first.id);
        }

        private static void RunCrossOwnerRejection(List<string> lines)
        {
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord left = archive.RegisterSummoner(null);
            SummonerRecord right = archive.RegisterSummoner(null);
            BeastRecord beast = archive.RegisterBeast(left.id, out string error);
            Require(beast != null, error);
            if (archive.TryAddReserve(right.slots[0].id, beast.id, out error))
                throw new InvalidOperationException("A beast joined a second summoner's slot.");
            lines.Add("cross-owner rejected: " + error);
        }

        private static void RunCorruptionDetection(List<string> lines)
        {
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord summoner = archive.RegisterSummoner(null);
            BeastRecord beast = archive.RegisterBeast(summoner.id, out string error);
            Require(beast != null, error);
            Require(archive.TryAssignPrimary(summoner.slots[0].id, beast.id, out error), error);
            summoner.slots[0].reserveBeastIds.Add(beast.id);
            string report = archive.ConsistencyReport();
            if (report.Length == 0)
                throw new InvalidOperationException("Duplicate primary/reserve membership was not detected.");
            lines.Add("corruption detected");
        }

        private static void RunBindingRegression(List<string> lines)
        {
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord summoner = archive.RegisterSummoner(null);
            BeastRecord beast = archive.RegisterBeast(summoner.id, out string error);
            BeastRecord other = archive.RegisterBeast(summoner.id, out error);
            Require(beast != null && other != null, error);
            Pawn first = MakeDetachedPawn(7101);
            Pawn second = MakeDetachedPawn(7102);
            Require(archive.BindRealmPawn(beast.id, first, out error), error);
            Require(archive.BindRealmPawn(beast.id, first, out error), error);
            if (archive.SoulRealm.entries.Count != 1 || archive.SoulRealm.PawnFor(beast.id) != first || beast.pawn != first || beast.presence != BeastPresence.InRealm)
                throw new InvalidOperationException("Repeating a realm bind changed the existing owner.");
            if (archive.BindRealmPawn(beast.id, second, out error))
                throw new InvalidOperationException("A beast accepted a second realm pawn.");
            if (beast.pawn != first || archive.SoulRealm.PawnFor(beast.id) != first || archive.SoulRealm.entries.Count != 1)
                throw new InvalidOperationException("Rejected realm rebind changed the previous owner. " + error);
            if (archive.BindFieldPawn(beast.id, second, out error))
                throw new InvalidOperationException("A realm-owned beast accepted a different field pawn.");
            if (beast.pawn != first || beast.presence != BeastPresence.InRealm || archive.SoulRealm.PawnFor(beast.id) != first)
                throw new InvalidOperationException("Rejected field rebind changed the previous owner. " + error);
            if (archive.BindRealmPawn(other.id, first, out error) || archive.BindFieldPawn(other.id, first, out error))
                throw new InvalidOperationException("One pawn was bound to two beasts.");
            if (other.pawn != null || archive.SoulRealm.PawnFor(other.id) != null)
                throw new InvalidOperationException("Rejected cross-beast bind created an entry. " + error);

            SoulRealmEntry dangling = new SoulRealmEntry();
            dangling.beastId = beast.id;
            archive.SoulRealm.entries.Add(dangling);
            string duplicate = archive.ConsistencyReport();
            archive.SoulRealm.entries.Remove(dangling);
            if (duplicate.IndexOf("Duplicate or empty soul-realm beast id.", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("Duplicate soul-realm id was not detected: " + duplicate);

            SoulRealmEntry empty = new SoulRealmEntry();
            empty.beastId = other.id;
            archive.SoulRealm.entries.Add(empty);
            string danglingReport = archive.ConsistencyReport();
            archive.SoulRealm.entries.Remove(empty);
            if (danglingReport.IndexOf("holds no pawn", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("An empty soul-realm entry was not detected: " + danglingReport);

            beast.pawn = second;
            string mismatch = archive.ConsistencyReport();
            beast.pawn = first;
            if (mismatch.IndexOf("does not match", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("A record pointing at a different pawn than its realm entry was not detected: " + mismatch);
            beast.presence = BeastPresence.OnField;
            string stillHeld = archive.ConsistencyReport();
            beast.presence = BeastPresence.InRealm;
            if (stillHeld.IndexOf("does not match", StringComparison.Ordinal) < 0 && stillHeld.IndexOf("also owns", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("An on-field record whose id still has a realm pawn was not detected: " + stillHeld);

            List<IThingHolder> children = new List<IThingHolder>();
            archive.SoulRealm.FindEntry(beast.id).GetChildHolders(children);
            if (!ContainsSame(children, first))
                throw new InvalidOperationException("The realm pawn was not enumerated as a child holder.");
            List<Thing> things = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(archive, things, true, (IThingHolder holder) => true);
            if (!ContainsSame(things, first))
                throw new InvalidOperationException("Recursive holder walk did not find the realm pawn.");
            string clean = archive.ConsistencyReport();
            if (clean.Length != 0)
                throw new InvalidOperationException(clean);
            lines.Add("binding reject different pawn; same pawn idempotent; holder walk includes realm pawn");
        }

        private static void RunCapacityRegression(List<string> lines)
        {
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord summoner = archive.RegisterSummoner(null);
            BeastRecord onField = archive.RegisterBeast(summoner.id, out string error);
            BeastRecord reserve = archive.RegisterBeast(summoner.id, out error);
            Require(onField != null && reserve != null, error);
            Require(archive.TrySetPresence(onField.id, BeastPresence.OnField, out error), error);
            if (archive.TrySetOccupySlots(onField.id, 2, out error))
                throw new InvalidOperationException("Occupy 2 was accepted while field capacity was 1.");
            if (onField.occupySlots != 1 || archive.FieldLoad(summoner.id) != 1 || summoner.fieldCapacity != 1)
                throw new InvalidOperationException("Rejected occupy change did not keep the previous load.");
            Require(archive.TrySetCapacities(summoner.id, 2, 1, out error), error);
            Require(archive.TrySetOccupySlots(onField.id, 2, out error), error);
            if (archive.TrySetCapacities(summoner.id, 1, 1, out error))
                throw new InvalidOperationException("Field capacity shrank below the on-field load.");
            if (summoner.fieldCapacity != 2 || summoner.contractSlotCapacity != 1 || onField.occupySlots != 2 || archive.FieldLoad(summoner.id) != 2)
                throw new InvalidOperationException("Rejected capacity change did not keep the previous values.");
            Require(archive.TrySetOccupySlots(reserve.id, 2, out error), error);
            if (reserve.presence != BeastPresence.InRealm || reserve.occupySlots != 2 || archive.FieldLoad(summoner.id) != 2)
                throw new InvalidOperationException("An in-realm reserve changed the on-field load.");
            archive.MarkDead(onField.id);
            if (archive.TrySetCapacities(summoner.id, 1, 1, out error))
                throw new InvalidOperationException("Death of an on-field beast freed field capacity.");
            if (summoner.fieldCapacity != 2 || archive.FieldLoad(summoner.id) != 2)
                throw new InvalidOperationException("Death changed the saved field load.");
            lines.Add("capacity reject occupy and shrink; in-realm reserve excluded; dead on-field still counts");
        }

        private static void RunRewardRegression(List<string> lines)
        {
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord owner = archive.RegisterSummoner(null);
            SummonerRecord other = archive.RegisterSummoner(null);
            BeastRecord beast = archive.RegisterBeast(owner.id, out string error);
            Require(beast != null, error);
            if (archive.AddPendingReward(other.id, RewardSubjectKind.Beast, beast.id, PendingRewardKind.BeastGrowthChoice, 1, 1, out error) != null)
                throw new InvalidOperationException("Another summoner's queue accepted this beast.");
            if (archive.AddPendingReward(owner.id, RewardSubjectKind.Beast, "beast-missing", PendingRewardKind.BeastGrowthChoice, 1, 1, out error) != null)
                throw new InvalidOperationException("A missing beast id was accepted.");
            if (archive.AddPendingReward(owner.id, RewardSubjectKind.Summoner, other.id, PendingRewardKind.SummonerFeedbackChoice, 1, 1, out error) != null)
                throw new InvalidOperationException("A summoner reward targeted a different summoner.");
            if (owner.pendingRewards.Count != 0 || other.pendingRewards.Count != 0)
                throw new InvalidOperationException("A rejected reward was queued.");
            PendingRewardRecord reward = archive.AddPendingReward(owner.id, RewardSubjectKind.Beast, beast.id, PendingRewardKind.BeastGrowthChoice, 1, 1, out error);
            Require(reward != null && reward.id == "reward-1", error + " id=" + (reward == null ? "null" : reward.id));
            PendingRewardRecord self = archive.AddPendingReward(owner.id, RewardSubjectKind.Summoner, owner.id, PendingRewardKind.SummonerFeedbackChoice, 1, 1, out error);
            Require(self != null && self.id == "reward-2", error);
            reward.subjectId = "beast-missing";
            string missing = archive.ConsistencyReport();
            reward.subjectId = beast.id;
            if (missing.IndexOf("missing beast", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("A reward with a missing beast was not detected: " + missing);
            owner.pendingRewards.Remove(reward);
            other.pendingRewards.Add(reward);
            string stolen = archive.ConsistencyReport();
            if (stolen.IndexOf("another summoner", StringComparison.Ordinal) < 0)
                throw new InvalidOperationException("A reward queued for the wrong summoner was not detected: " + stolen);
            lines.Add("reward subject rejected cross-owner and missing target; next id stayed reward-1");
        }

        private static void RunEntity(List<string> lines)
        {
            if (Current.Game != null)
                throw new InvalidOperationException("Entity fixture does not inject test defs into a live game.");
            if (Scribe.mode != LoadSaveMode.Inactive)
                throw new InvalidOperationException("Scribe is busy (" + Scribe.mode + ") before the entity load.");

            string injuryNote = null;
            string compNote = null;
            EntityFixture source = BuildEntityFixture(out injuryNote, out compNote);
            if (injuryNote != null)
                lines.Add("injury setup: " + injuryNote);
            if (compNote != null)
                lines.Add("comp setup: " + compNote);
            string path = Path.Combine(Path.GetTempPath(), "SummonerEntityFoundation-" + Guid.NewGuid().ToString("N") + ".xml");
            try
            {
                for (int pass = 1; pass <= 2; pass++)
                {
                    SaveRoot(source, path);
                    EntityFixture loaded = LoadEntity(path);
                    AssertEntity(source, loaded, lines, pass);
                    if (Scribe.mode != LoadSaveMode.Inactive)
                        throw new InvalidOperationException("Scribe mode after entity load " + pass + " is " + Scribe.mode + ".");
                }
            }
            finally
            {
                AbortScribe();
                if (File.Exists(path))
                    File.Delete(path);
            }
            lines.Add("BindFieldPawn success path not run: the isolated field pawn is not a map or world pawn");
            if (injuryNote != null || compNote != null)
                throw new InvalidOperationException("Entity load finished, but injury or comp setup did not. injury=" + (injuryNote ?? "ok") + " comp=" + (compNote ?? "ok"));
        }

        private static EntityFixture BuildEntityFixture(out string injuryNote, out string compNote)
        {
            injuryNote = null;
            compNote = null;
            Pawn master = CreateIsolatedPawn(8101, false, out _);
            Pawn field = CreateIsolatedPawn(8102, true, out compNote);
            Pawn realm = CreateIsolatedPawn(8103, false, out injuryNote);
            if (compNote != null)
                compNote = "field " + compNote;
            SummonerArchive archive = new SummonerArchive(null);
            SummonerRecord summoner = archive.RegisterSummoner(master);
            BeastRecord fieldBeast = archive.RegisterBeast(summoner.id, out string error);
            BeastRecord realmBeast = archive.RegisterBeast(summoner.id, out error);
            Require(fieldBeast != null && realmBeast != null, error);
            Require(archive.BindRealmPawn(realmBeast.id, realm, out error), error);
            Require(archive.BindRealmPawn(realmBeast.id, realm, out error), error);
            fieldBeast.presence = BeastPresence.OnField;
            fieldBeast.pawn = field;
            Require(archive.TrySetLockoutEndTick(fieldBeast.id, 45000, out error), error);
            Require(archive.TrySetRest(realmBeast.id, BeastRest.Sleeping, 90000, out error), error);
            CompSummonerBeastLink fieldComp = field.GetComp<CompSummonerBeastLink>();
            if (fieldComp == null)
                compNote = (compNote == null ? "" : compNote + "; ") + "field pawn has no link comp before save";
            else
                fieldComp.beastId = fieldBeast.id;
            string consistency = archive.ConsistencyReport();
            if (consistency.Length != 0)
                throw new InvalidOperationException(consistency);
            EntityFixture fixture = new EntityFixture();
            fixture.master = master;
            fixture.field = field;
            fixture.archive = archive;
            return fixture;
        }

        private static Pawn MakeDetachedPawn(int thingId)
        {
            Pawn pawn = new Pawn();
            pawn.thingIDNumber = thingId;
            if (pawn.stackCount < 1)
                pawn.stackCount = 1;
            if (pawn.def == null)
                pawn.def = IsolatedThingDef();
            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            CompAttachBase attach = new CompAttachBase();
            attach.parent = pawn;
            System.Reflection.FieldInfo attachments = typeof(CompAttachBase).GetField("attachments", flags);
            if (attachments != null)
                attachments.SetValue(attach, Activator.CreateInstance(attachments.FieldType));
            System.Reflection.FieldInfo comps = typeof(ThingWithComps).GetField("comps", flags);
            if (comps != null)
            {
                List<ThingComp> list = new List<ThingComp>();
                for (int i = 0; i < 64; i++)
                    list.Add(attach);
                comps.SetValue(pawn, list);
            }
            System.Reflection.FieldInfo byType = typeof(ThingWithComps).GetField("compsByType", flags);
            if (byType != null && byType.GetValue(pawn) == null)
                byType.SetValue(pawn, Activator.CreateInstance(byType.FieldType));
            return pawn;
        }

        private static Pawn CreateIsolatedPawn(int thingId, bool withComp, out string setupNote)
        {
            setupNote = null;
            Pawn pawn = MakeDetachedPawn(thingId);
            pawn.def = IsolatedThingDef();
            try
            {
                RimWorld.PawnComponentsUtility.CreateInitialComponents(pawn);
            }
            catch (Exception ex)
            {
                setupNote = "initial components incomplete: " + ex.GetType().Name + ": " + ex.Message;
            }
            if (withComp)
            {
                try
                {
                    pawn.InitializeComps();
                    if (pawn.GetComp<CompSummonerBeastLink>() == null)
                        setupNote = (setupNote == null ? "" : setupNote + "; ") + "link comp was not attached";
                }
                catch (Exception ex)
                {
                    setupNote = (setupNote == null ? "" : setupNote + "; ") + "InitializeComps " + ex.GetType().Name + ": " + ex.Message;
                }
            }
            else if (pawn.health == null)
                setupNote = (setupNote == null ? "" : setupNote + "; ") + "health tracker was not created";
            else
            {
                string injury = TryApplyInjury(pawn);
                if (injury != null)
                    setupNote = (setupNote == null ? "" : setupNote + "; ") + injury;
            }
            return pawn;
        }

        private static bool ContainsSame(System.Collections.IEnumerable items, object target)
        {
            if (items == null)
                return false;
            foreach (object item in items)
            {
                if (ReferenceEquals(item, target))
                    return true;
            }
            return false;
        }

        private static ThingDef IsolatedThingDef()
        {
            if (Current.Game != null)
                throw new InvalidOperationException("Test definitions must not be injected into a live game.");
            ThingDef existing = DefDatabase<ThingDef>.GetNamedSilentFail("SummonerIsolatedPawn");
            if (existing != null)
                return existing;
            ThingDef def = (ThingDef)Uninitialized(typeof(ThingDef));
            def.defName = "SummonerIsolatedPawn";
            def.race = new RaceProperties();
            def.comps = new List<CompProperties>();
            def.comps.Add(new CompProperties_SummonerBeastLink());
            DefDatabase<ThingDef>.Add(def);
            return def;
        }

        private static string TryApplyInjury(Pawn pawn)
        {
            try
            {
                HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("SummonerIsolatedBruise");
                if (def == null)
                {
                    def = (HediffDef)Uninitialized(typeof(HediffDef));
                    def.defName = "SummonerIsolatedBruise";
                    def.hediffClass = typeof(Hediff);
                    def.label = "bruise";
                    DefDatabase<HediffDef>.Add(def);
                }
                Hediff hediff = (Hediff)Activator.CreateInstance(def.hediffClass);
                hediff.loadID = pawn.thingIDNumber * 100 + 1;
                hediff.def = def;
                hediff.pawn = pawn;
                pawn.health.hediffSet.AddDirect(hediff, null, null);
                return BruiseCount(pawn) == 1 ? null : "hediff count " + BruiseCount(pawn);
            }
            catch (Exception ex)
            {
                return ex.GetType().Name + ": " + ex.Message;
            }
        }

        private static int BruiseCount(Pawn pawn)
        {
            if (pawn == null || pawn.health == null || pawn.health.hediffSet == null || pawn.health.hediffSet.hediffs == null)
                return 0;
            int count = 0;
            for (int i = 0; i < pawn.health.hediffSet.hediffs.Count; i++)
            {
                Hediff hediff = pawn.health.hediffSet.hediffs[i];
                if (hediff != null && hediff.def != null && hediff.def.defName == "SummonerIsolatedBruise")
                    count++;
            }
            return count;
        }

        private static object Uninitialized(Type type)
        {
#if RIMWORLD_1_7
            return System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(type);
#else
            return System.Runtime.Serialization.FormatterServices.GetUninitializedObject(type);
#endif
        }

        private static EntityFixture LoadEntity(string path)
        {
            using var diagnostics = new ScribeDiagnostics();
            EnsureIsolatedPrefs();
            EntityFixture loaded = new EntityFixture();
            try
            {
                Scribe.loader.InitLoading(path);
                loaded.ExposeData();
                Scribe.loader.FinalizeLoading();
                diagnostics.RequireClean("Complete entity load");
            }
            catch (Exception ex)
            {
                AbortScribe();
                throw new InvalidOperationException("Entity load failed (critical diagnostics=" + diagnostics.CriticalCount + ").", ex);
            }
            if (Scribe.mode != LoadSaveMode.Inactive)
            {
                AbortScribe();
                throw new InvalidOperationException("Entity FinalizeLoading returned but Scribe mode is " + Scribe.mode + ".");
            }
            return loaded;
        }

        private static void AssertEntity(EntityFixture source, EntityFixture loaded, List<string> lines, int pass)
        {
            if (loaded.archive == null || loaded.master == null || loaded.field == null)
                throw new InvalidOperationException("Entity load " + pass + " dropped the master, field pawn, or archive.");
            if (loaded.archive.Summoners.Count != 1 || loaded.archive.Summoners[0].master != loaded.master)
                throw new InvalidOperationException("Entity load " + pass + " did not restore the master reference.");
            if (loaded.master.thingIDNumber != source.master.thingIDNumber || loaded.field.thingIDNumber != source.field.thingIDNumber)
                throw new InvalidOperationException("Entity load " + pass + " changed a pawn ThingID.");
            BeastRecord fieldBeast = loaded.archive.FindBeast("beast-1");
            BeastRecord realmBeast = loaded.archive.FindBeast("beast-2");
            if (fieldBeast == null || realmBeast == null)
                throw new InvalidOperationException("Entity load " + pass + " lost a beast id.");
            if (fieldBeast.pawn != loaded.field || fieldBeast.presence != BeastPresence.OnField || fieldBeast.lockoutEndTick != 45000)
                throw new InvalidOperationException("Entity load " + pass + " did not restore the field reference or its cooldown.");
            Pawn realmPawn = loaded.archive.SoulRealm.PawnFor(realmBeast.id);
            if (realmPawn == null || realmBeast.pawn != realmPawn || realmBeast.presence != BeastPresence.InRealm || realmBeast.sleepEndTick != 90000)
                throw new InvalidOperationException("Entity load " + pass + " did not restore the realm pawn or its sleep tick.");
            if (realmPawn.thingIDNumber != 8103)
                throw new InvalidOperationException("Entity load " + pass + " changed the realm pawn ThingID.");
            List<IThingHolder> children = new List<IThingHolder>();
            loaded.archive.SoulRealm.FindEntry(realmBeast.id).GetChildHolders(children);
            if (!ContainsSame(children, realmPawn))
                throw new InvalidOperationException("Entity load " + pass + " did not enumerate the realm pawn as a child holder.");
            List<Thing> things = new List<Thing>();
            ThingOwnerUtility.GetAllThingsRecursively(loaded.archive, things, true, (IThingHolder holder) => true);
            if (!ContainsSame(things, realmPawn))
                throw new InvalidOperationException("Entity load " + pass + " recursive walk missed the realm pawn.");
            if (loaded.archive.SoulRealm.HoldsPawn(loaded.field) || loaded.archive.SoulRealm.HoldsPawn(loaded.master))
                throw new InvalidOperationException("Entity load " + pass + " stored the master or field pawn in the soul realm.");
            CompSummonerBeastLink comp = loaded.field.GetComp<CompSummonerBeastLink>();
            if (comp == null || comp.beastId != fieldBeast.id)
                throw new InvalidOperationException("Entity load " + pass + " did not restore the field comp foreign key.");
            int bruises = BruiseCount(realmPawn);
            if (bruises != 1)
                throw new InvalidOperationException("Entity load " + pass + " restored bruise count " + bruises + ".");
            BeastRecord next = loaded.archive.RegisterBeast(loaded.archive.Summoners[0].id, out string error);
            if (next == null || next.id != "beast-3")
                throw new InvalidOperationException("Entity load " + pass + " next id was " + (next == null ? error : next.id) + ".");
            string consistency = loaded.archive.ConsistencyReport();
            if (consistency.Length != 0)
                throw new InvalidOperationException(consistency);
            lines.Add("entity load " + pass + " restored master " + loaded.master.ThingID + ", field " + loaded.field.ThingID + ", realm " + realmPawn.ThingID + ", bruise=1, comp=" + comp.beastId + "; next id " + next.id);
        }

        private sealed class EntityFixture : IExposable
        {
            public Pawn master;
            public Pawn field;
            public SummonerArchive archive;

            public void ExposeData()
            {
                Scribe_Deep.Look(ref master, "master");
                Scribe_Deep.Look(ref field, "field");
                Scribe_Deep.Look(ref archive, "archive", (Game)null);
            }
        }

        private static void AssertSame(SummonerArchive left, SummonerArchive right)
        {
            if (left.Summoners.Count != right.Summoners.Count || left.Beasts.Count != right.Beasts.Count)
                throw new InvalidOperationException("Loaded collection sizes do not match.");
            for (int i = 0; i < left.Summoners.Count; i++)
                AssertSummoner(left.Summoners[i], right.Summoners[i]);
            for (int i = 0; i < left.Beasts.Count; i++)
                AssertBeast(left.Beasts[i], right.Beasts[i]);
            string consistency = right.ConsistencyReport();
            if (consistency.Length != 0)
                throw new InvalidOperationException(consistency);
        }

        private static void AssertSummoner(SummonerRecord left, SummonerRecord right)
        {
            if (left.id != right.id || left.proficiency != right.proficiency
                || left.fieldCapacity != right.fieldCapacity || left.contractSlotCapacity != right.contractSlotCapacity
                || left.slots.Count != right.slots.Count || left.pendingRewards.Count != right.pendingRewards.Count)
                throw new InvalidOperationException("Summoner record " + left.id + " changed across the save.");
            for (int i = 0; i < left.slots.Count; i++)
            {
                ContractSlotRecord a = left.slots[i];
                ContractSlotRecord b = right.slots[i];
                if (a.id != b.id || a.bloodlineId != b.bloodlineId || a.primaryBeastId != b.primaryBeastId
                    || a.reserveBeastIds.Count != b.reserveBeastIds.Count)
                    throw new InvalidOperationException("Contract slot " + a.id + " changed across the save.");
                for (int r = 0; r < a.reserveBeastIds.Count; r++)
                {
                    if (a.reserveBeastIds[r] != b.reserveBeastIds[r])
                        throw new InvalidOperationException("Reserve order changed for " + a.id + ".");
                }
            }
            for (int i = 0; i < left.pendingRewards.Count; i++)
            {
                PendingRewardRecord a = left.pendingRewards[i];
                PendingRewardRecord b = right.pendingRewards[i];
                if (a.id != b.id || a.subjectKind != b.subjectKind || a.subjectId != b.subjectId || a.kind != b.kind
                    || a.sourceTier != b.sourceTier || a.sourceLevel != b.sourceLevel
                    || a.choice.selectedIndex != b.choice.selectedIndex || a.choice.selectionResolved != b.choice.selectionResolved
                    || a.choice.optionIds.Count != b.choice.optionIds.Count)
                    throw new InvalidOperationException("Pending reward " + a.id + " changed across the save.");
                for (int n = 0; n < a.choice.optionIds.Count; n++)
                {
                    if (a.choice.optionIds[n] != b.choice.optionIds[n])
                        throw new InvalidOperationException("Reward options changed for " + a.id + ".");
                }
            }
        }

        private static void AssertBeast(BeastRecord left, BeastRecord right)
        {
            if (left.id != right.id || left.ownerId != right.ownerId || left.bloodlineId != right.bloodlineId
                || left.tier != right.tier || left.tierLevel != right.tierLevel || left.experience != right.experience
                || left.occupySlots != right.occupySlots || left.presence != right.presence || left.rest != right.rest
                || left.lockoutEndTick != right.lockoutEndTick || left.sleepEndTick != right.sleepEndTick || left.dead != right.dead
                || left.role != right.role || left.contractSlotId != right.contractSlotId)
                throw new InvalidOperationException("Beast record " + left.id + " changed across the save.");
            if (left.statInvestments.Count != right.statInvestments.Count || left.mutations.Count != right.mutations.Count
                || left.skills.Count != right.skills.Count || left.works.Count != right.works.Count)
                throw new InvalidOperationException("Beast records lists changed for " + left.id + ".");
            for (int i = 0; i < left.statInvestments.Count; i++)
            {
                if (left.statInvestments[i].statId != right.statInvestments[i].statId || left.statInvestments[i].count != right.statInvestments[i].count)
                    throw new InvalidOperationException("Stat investment changed for " + left.id + ".");
            }
            for (int i = 0; i < left.mutations.Count; i++)
            {
                if (left.mutations[i].mutationId != right.mutations[i].mutationId || left.mutations[i].stacks != right.mutations[i].stacks)
                    throw new InvalidOperationException("Mutation record changed for " + left.id + ".");
            }
            for (int i = 0; i < left.skills.Count; i++)
            {
                if (left.skills[i].skillId != right.skills[i].skillId || left.skills[i].level != right.skills[i].level || left.skills[i].selectionCount != right.skills[i].selectionCount)
                    throw new InvalidOperationException("Skill record changed for " + left.id + ".");
            }
            for (int i = 0; i < left.works.Count; i++)
            {
                if (left.works[i].workId != right.works[i].workId || left.works[i].level != right.works[i].level)
                    throw new InvalidOperationException("Work record changed for " + left.id + ".");
            }
        }

        private static void Require(bool condition, string error)
        {
            if (!condition)
                throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Data foundation check failed." : error);
        }
    }
}
