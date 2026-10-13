using System;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse;
using VanillaPsycastsExpanded;
using VEF.Abilities;

namespace RimMod.Summoner
{
    // Opt-in integration tests, only in an isolated -quicktest process. No effect in normal play.
    public sealed class SummonerSelfTest : GameComponent
    {
        private bool ran;
        private int phase, savedWolfId;
        private Pawn savedOwner, savedWolf;
        private bool pendingLoad;
        public SummonerSelfTest(Game game) { }
        private static bool Enabled => Environment.GetCommandLineArgs().Contains("-summoner-selftest");
        public override void FinalizeInit()
        {
            if (Enabled) Log.Message("[SummonerTest] World initialized; phase=" + phase);
        }
        public override void GameComponentUpdate()
        {
            if (!Enabled) return;
            if (pendingLoad)
            {
                pendingLoad = false;
                GameDataSaveLoader.LoadGame("SummonerIntegration");
            }
            else if (phase == 1 && !ran && Find.CurrentMap != null)
            {
                ran = true;
                try { AfterLoad(); } catch (Exception ex) { Log.Error("[SummonerTest] FAILED " + ex); }
            }
        }
        public override void ExposeData()
        {
            Scribe_Values.Look(ref phase, "summonerTestPhase");
            Scribe_Values.Look(ref savedWolfId, "summonerTestWolfId");
            Scribe_References.Look(ref savedOwner, "summonerTestOwner");
            Scribe_References.Look(ref savedWolf, "summonerTestWolf");
        }
        private static void Check(bool ok, string label)
        {
            if (!ok) throw new Exception("FAIL: " + label);
            Log.Message("[SummonerTest] PASS " + label);
        }
        public override void GameComponentTick()
        {
            if (!Enabled || ran || Find.CurrentMap == null || Find.TickManager.TicksGame < 200) return;
            ran = true;
            try
            {
                if (phase == 1) { AfterLoad(); return; }
                Run();
            }
            catch (Exception ex) { Log.Error("[SummonerTest] FAILED " + ex); }
        }
        private void Run()
        {
            var map = Find.CurrentMap;
            var owner = map.mapPawns.FreeColonistsSpawned.First();
            owner.ChangePsylinkLevel(1, false);
            var path = DefDatabase<PsycasterPathDef>.GetNamed("RMS_Summoner");
            owner.Psycasts().unlockedPaths.Add(path);
            owner.psychicEntropy.OffsetPsyfocusDirectly(1f);
            var abilities = owner.GetComp<CompAbilities>();
            foreach (var n in new[] { "RMS_Contract", "RMS_Call", "RMS_Link" }) abilities.GiveAbility(DefDatabase<VEF.Abilities.AbilityDef>.GetNamed(n));
            Check(path.abilities.Count == 3 && path.MaxLevel == 3, "VPE path has exactly three skills");
            var r = Soul.Game.Seeking(owner);
            Check(r != null && r.candidate == null && r.nextSearchTick > Soul.Now, "learning schedules delayed rescue event");
            r.nextSearchTick = Soul.Now;
            Soul.Game.BeginSearch(owner);
            Check(r.candidate != null && r.guards.Count >= 3 && SummonerGame.GuardsRemain(r), "delayed event creates guarded wilderness camp");
            var wolf = r.candidate;
            Check(Math.Abs(wolf.RaceProps.baseBodySize - 1f) < .001f && wolf.ageTracker.CurKindLifeStage.bodyGraphicData.drawSize.x >= 2.19f,
                "adult spirit wolf is physically and visually larger than a vanilla timber wolf");
            Soul.Game.BeginSearch(owner);
            Check(r.candidate == wolf && Soul.Game.records.Count(x => x.owner == owner && x.state == ContractState.Seeking) == 1, "repeated search keeps single entity");
            Check(wolf.Sterile(), "candidate cannot reproduce");
            Check(!TameUtility.CanTame(wolf), "candidate cannot receive normal taming jobs");
            var other = map.mapPawns.FreeColonistsSpawned.First(p => p != owner);
            Check(!Soul.Game.CanBind(other, wolf, out _), "other pawn cannot steal candidate");
            MoveNear(wolf, owner);
            Check(!Soul.Game.CanBind(owner, wolf, out _), "living tribal guards block the rescue contract");
            foreach (var guard in r.guards.Where(g => g != null && !g.Dead).ToList()) guard.Kill(null);
            Check(!SummonerGame.GuardsRemain(r), "defeating guards frees the candidate");
            Check(Soul.Game.Bind(owner, wolf), "contract binds candidate");
            Check(Soul.Has(owner, "RMS_Weakness") && Soul.Game.Active(owner)?.beast == wolf, "new contract has two-day weakness");
            Check(Soul.Has(wolf, "RMS_ContractBlessing") && !Soul.Has(wolf, "RMS_SoulSnare"), "contract removes snare and applies combat enhancement");
            Check(wolf.IsPlayerControlled && wolf.drafter != null && wolf.training.HasLearned(TrainableDefOf.Obedience), "contract grants direct draft control without obedience training");
            wolf.drafter.Drafted = true;
            Check(wolf.Drafted, "spirit wolf can be drafted directly");
            var moveCell = CellFinder.RandomClosewalkCellNear(wolf.Position, map, 4);
            Check(wolf.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, moveCell)) && wolf.CurJobDef == JobDefOf.Goto,
                "drafted spirit wolf accepts a direct move order");
            wolf.jobs.StopAll();
            wolf.drafter.Drafted = false;
            Check(wolf.needs.food != null && wolf.needs.rest != null && wolf.RaceProps.IsFlesh, "real animal food sleep and flesh biology");
            Check(!TradeUtility.PlayerSellableNow(wolf, null), "contract beast cannot trade");
            Check(!TrainableUtility.TamenessCanDecay(wolf), "contract prevents accidental wildness decay");
            InteractionWorker_RecruitAttempt.DoRecruit(other, wolf, false);
            Check(Soul.Wolf(wolf).owner == owner, "ordinary recruitment cannot replace owner");
            int id = wolf.thingIDNumber;
            wolf.TakeDamage(new DamageInfo(DamageDefOf.Cut, 2));
            var injuries = wolf.health.hediffSet.hediffs.OfType<Hediff_Injury>().ToList();
            float hunger = wolf.needs.food.CurLevel;
            var call = abilities.LearnedAbilities.OfType<Ability_Call>().Single();
            wolf.DeSpawn();
            Check(!call.IsEnabledForPawn(out _), "unspawned beast cannot be recalled or duplicated");
            GenSpawn.Spawn(wolf, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 2), owner.Map);
            Check(call.IsEnabledForPawn(out _), "recall enabled on living same-map beast");
            call.Cast(new GlobalTargetInfo(owner));
            Check(wolf.thingIDNumber == id && wolf.needs.food.CurLevel == hunger && injuries.All(h => wolf.health.hediffSet.hediffs.Contains(h)), "recall preserves ID hunger and wounds");
            var c = Soul.Wolf(wolf);
            float bond = c.bond;
            c.TickBond(); Check(c.bond > bond, "cohabitation increases bond");
            bond = c.bond;
            var item = ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("RMS_TrainingCrystal"));
            item.TryGetComp<CompUseEffect_Training>().DoEffect(owner);
            Check(c.xp == 100 && c.bond == bond && item.Destroyed, "experience item consumed without buying bond");
            c.GainXP(10000); Check(c.xp == 400, "combat experience has daily cap");
            c.GainXP(10000, false); Check(c.xp == 1400 && c.Level == 5, "level five experience clamp");
            Check(!c.Eligible && !c.Evolve(), "XP alone cannot evolve");
            c.GainBond(75); Check(c.Evolve(), "level and bond unlock evolution");
            Check(c.Evolved && !c.Evolve() && wolf.thingIDNumber == id && injuries.All(h => wolf.health.hediffSet.hediffs.Contains(h)), "evolution occurs once and preserves identity and injuries");
            Check(wolf.KindLabel == "巨牙灵狼", "evolved form is identified in animal label");
            var enemy = PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, Faction.OfAncientsHostile);
            Check(!path.CanPawnUnlock(enemy), "enemy pawns cannot gain the summoner path");
            GenSpawn.Spawn(enemy, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 5), owner.Map);
            Check(c.Attack(enemy, true) && Soul.Has(wolf, "RMS_SoulHunt") && Soul.Has(wolf, "RMS_HuntCooldown"), "evolved pursuit orders attack and grants timed sprint");
            enemy.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 2, instigator: wolf));
            Check(c.lastCombatTick == Soul.Now, "hostile damage records combat participation");
            enemy.Destroy(); wolf.jobs.StopAll();
            var link = abilities.LearnedAbilities.OfType<Ability_Link>().Single();
            link.Cast(new GlobalTargetInfo(owner));
            Check(r.linkEnabled, "soul link toggles on");
            float before = wolf.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity);
            owner.TakeDamage(new DamageInfo(DamageDefOf.Blunt, 5));
            Check(wolf.health.hediffSet.hediffs.OfType<Hediff_Injury>().Sum(h => h.Severity) > before, "link deals real share damage to wolf");
            link.Cast(new GlobalTargetInfo(owner)); Check(!r.linkEnabled, "soul link toggles off");
            // Save/reload the same live entity and its growth; continue tests on the loaded world.
            savedOwner = owner; savedWolf = wolf; savedWolfId = id; phase = 1;
            GameDataSaveLoader.SaveGame("SummonerIntegration");
            Log.Message("[SummonerTest] SAVE COMPLETE; loading saved contract");
            pendingLoad = true;
        }
        private void AfterLoad()
        {
            var r = Soul.Game.Active(savedOwner);
            var c = Soul.Wolf(savedWolf);
            Check(r?.beast == savedWolf && savedWolf.thingIDNumber == savedWolfId && c.owner == savedOwner && c.Evolved && c.Level == 5, "save reload preserves entity owner level and evolution");
            savedWolf.Kill(null);
            Check(Soul.Game.Active(savedOwner) == null && Soul.Has(savedOwner, "RMS_Trauma"), "wolf death ends contract and causes trauma");
            var trauma = savedOwner.health.hediffSet.GetFirstHediffOfDef(Soul.H("RMS_Trauma"));
            Check(trauma.TryGetComp<HediffComp_Disappears>().ticksToDisappear == 420000, "trauma lasts seven days");
            Check(Soul.Game.BeginSearch(savedOwner) == null, "trauma blocks new event");
            var call = savedOwner.GetComp<CompAbilities>().LearnedAbilities.OfType<Ability_Call>().Single();
            Check(!call.IsEnabledForPawn(out _), "dead beast cannot be recalled");
            Soul.Remove(savedOwner, "RMS_Trauma");
            Check(Soul.Game.BeginSearch(savedOwner) == null, "removing trauma early cannot bypass seven-day recovery");
            r.nextSearchTick = Soul.Now; // Simulate the saved recovery timestamp elapsing.
            var fresh = Soul.Game.BeginSearch(savedOwner);
            Check(fresh != null && fresh.candidate == null, "recovered summoner receives another delayed rescue event");
            fresh.nextSearchTick = Soul.Now;
            Soul.Game.BeginSearch(savedOwner);
            Check(fresh.candidate != null && fresh.candidate != savedWolf && Soul.Wolf(fresh.candidate).xp == 0, "after delay event creates fresh level-one entity");
            var newWolf = fresh.candidate;
            MoveNear(newWolf, savedOwner);
            foreach (var guard in fresh.guards.Where(g => g != null && !g.Dead).ToList()) guard.Kill(null);
            Check(Soul.Game.Bind(savedOwner, newWolf), "replacement contract binds");
            var newGrowth = Soul.Wolf(newWolf);
            var foe = PawnGenerator.GeneratePawn(PawnKindDefOf.Pirate, Faction.OfAncientsHostile);
            GenSpawn.Spawn(foe, CellFinder.RandomClosewalkCellNear(savedOwner.Position, savedOwner.Map, 5), savedOwner.Map);
            foe.TakeDamage(new DamageInfo(DamageDefOf.Cut, 3, 999f, instigator: newWolf));
            Check(newGrowth.xp > 0, "actual hostile damage grants experience");
            float xpBeforeKill = newGrowth.xp;
            foe.TakeDamage(new DamageInfo(DamageDefOf.Cut, 999, 999f, instigator: newWolf, hitPart: foe.health.hediffSet.GetBrain()));
            Check(foe.Dead && newGrowth.xp > xpBeforeKill, "hostile lethal hit grants combat and kill experience");
            newGrowth.GainXP(800, false);
            savedOwner.health.AddHediff(HediffDefOf.Anesthetic);
            Check(savedOwner.Downed, "guardian fixture has an actually downed owner");
            newGrowth.TryGuard();
            Check(Soul.Has(newWolf, "RMS_Guard"), "level-four wolf activates guardian buff for downed owner");
            savedOwner.Kill(null);
            Check(!newWolf.Dead && Soul.Wolf(newWolf).owner == null && Soul.Wolf(newWolf).lostUntil > Soul.Now && Soul.Has(newWolf, "RMS_LostSoul"), "owner death leaves living lost-soul beast");
            Soul.Wolf(newWolf).ReturnWild();
            Check(!newWolf.Dead && newWolf.Faction == null && !Soul.Wolf(newWolf).Protected, "lost soul eventually returns wild alive");
            Log.Message("[SummonerTest] ALL TESTS PASSED");
            phase = 2;
            Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
        }
        private static void MoveNear(Pawn beast, Pawn owner)
        {
            beast.DeSpawn();
            GenSpawn.Spawn(beast, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 2), owner.Map);
        }
    }
}
