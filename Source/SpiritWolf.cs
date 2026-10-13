using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.AI;

namespace RimMod.Summoner
{
    public sealed class EvolutionDef : Def
    {
        public int requiredLevel = 5;
        public float requiredBond = 75f;
        public HediffDef hediff;
    }

    public static class GrowthRules
    {
        public const float MaxXP = 1400, MaxBond = 100, DailyCombatXP = 300;
        public static int Level(float xp) => xp >= 1400 ? 5 : xp >= 800 ? 4 : xp >= 400 ? 3 : xp >= 150 ? 2 : 1;
        public static float Next(float xp) => xp >= 800 ? 1400 : xp >= 400 ? 800 : xp >= 150 ? 400 : 150;
        public static float ClampXP(float xp) => Math.Max(0, Math.Min(MaxXP, xp));
    }

    public sealed class CompProperties_SpiritWolf : CompProperties
    {
        public CompProperties_SpiritWolf() { compClass = typeof(CompSpiritWolf); }
    }

    public sealed class CompSpiritWolf : ThingComp
    {
        public Pawn owner, candidateOwner;
        public int contractId, lostUntil;
        public IntVec3 candidateAnchor = IntVec3.Invalid;
        public float xp, bond;
        public string evolutionId;
        public bool evolutionNotified;
        public int lastCombatTick = -100000, lastProtectTick = -100000, guardCooldown, lastTendTick = -100000;
        public int growthDay = -1;
        public float dayCombatXP, daySharedBond;
        public Pawn Pawn => (Pawn)parent;
        public int Level => GrowthRules.Level(xp);
        public bool Evolved => !string.IsNullOrEmpty(evolutionId);
        public bool Bound => owner != null && Soul.Game?.ForBeast(Pawn) != null;
        public bool Protected => Bound || candidateOwner != null || lostUntil > 0;
        public EvolutionDef Evolution => DefDatabase<EvolutionDef>.GetNamed("RMS_Greatfang");
        public bool Eligible => Bound && !Evolved && Level >= Evolution.requiredLevel && bond >= Evolution.requiredBond;
        public bool CanEvolve => Eligible && Soul.Ready(owner) && Soul.Ready(Pawn) && Soul.Together(owner, Pawn, 12);

        public override void PostExposeData()
        {
            Scribe_References.Look(ref owner, "soulOwner");
            Scribe_References.Look(ref candidateOwner, "candidateOwner");
            Scribe_Values.Look(ref contractId, "contractId");
            Scribe_Values.Look(ref lostUntil, "lostUntil");
            Scribe_Values.Look(ref candidateAnchor, "candidateAnchor", IntVec3.Invalid);
            Scribe_Values.Look(ref xp, "soulXP");
            Scribe_Values.Look(ref bond, "soulBond");
            Scribe_Values.Look(ref evolutionId, "evolutionId");
            Scribe_Values.Look(ref evolutionNotified, "evolutionNotified");
            Scribe_Values.Look(ref lastCombatTick, "lastCombatTick", -100000);
            Scribe_Values.Look(ref lastProtectTick, "lastProtectTick", -100000);
            Scribe_Values.Look(ref guardCooldown, "guardCooldown");
            Scribe_Values.Look(ref lastTendTick, "lastTendTick", -100000);
            Scribe_Values.Look(ref growthDay, "growthDay", -1);
            Scribe_Values.Look(ref dayCombatXP, "dayCombatXP");
            Scribe_Values.Look(ref daySharedBond, "daySharedBond");
        }

        private void RefreshDay()
        {
            int today = Soul.Now / Soul.Day;
            if (growthDay == today) return;
            growthDay = today; dayCombatXP = 0; daySharedBond = 0;
        }

        public void GainXP(float amount, bool combat = true)
        {
            if (!Bound || amount <= 0) return;
            RefreshDay();
            if (combat) { amount = Mathf.Min(amount, GrowthRules.DailyCombatXP - dayCombatXP); dayCombatXP += amount; }
            int previous = Level;
            xp = GrowthRules.ClampXP(xp + amount);
            if (previous < 2 && Level >= 2) Soul.Add(Pawn, "RMS_Familiar");
            CheckEvolution();
        }

        public void GainBond(float amount)
        {
            if (!Bound || amount <= 0) return;
            bond = Mathf.Clamp(bond + amount, 0, GrowthRules.MaxBond);
            CheckEvolution();
        }

        public void SharedDamage(float damage)
        {
            GainXP(damage * .5f);
            RefreshDay();
            float gain = Mathf.Min(Mathf.Max(0, damage) * .02f, 3 - daySharedBond);
            daySharedBond += gain;
            GainBond(gain);
        }

        private void CheckEvolution()
        {
            if (!Eligible || evolutionNotified) return;
            evolutionNotified = true;
            Soul.Tell("灵魂成熟", "灵狼已达到Lv5与75羁绊。让主人和灵狼清醒地站在12格内，选择灵狼的「进化：巨牙灵狼」。进化保留同一个体及全部伤病。", Pawn);
        }

        public bool Evolve()
        {
            if (!CanEvolve) { Soul.Reject("需要Lv5、75羁绊、尚未进化，且主人和灵狼清醒、同地图12格内。"); return false; }
            evolutionId = Evolution.defName;
            Pawn.health.AddHediff(Evolution.hediff);
            Soul.Tell("巨牙灵狼", "契约伙伴完成了进化。它仍然是原来的那只狼，追猎和守护能力已增强。", Pawn);
            return true;
        }

        public void ReturnWild()
        {
            lostUntil = 0; owner = null; candidateOwner = null; candidateAnchor = IntVec3.Invalid;
            Soul.Remove(Pawn, "RMS_LostSoul");
            Soul.Remove(Pawn, "RMS_ContractBlessing");
            if (Pawn.drafter != null) { Pawn.drafter.Drafted = false; Pawn.drafter = null; }
            if (Pawn.Dead || Pawn.Destroyed) return;
            var caravan = Pawn.GetCaravan();
            if (caravan != null) caravan.RemovePawn(Pawn);
            Pawn.SetFaction(null);
            if (Pawn.Spawned)
            {
                Pawn.jobs.StopAll();
                Pawn.mindState.exitMapAfterTick = Soul.Now;
            }
            else if (!Pawn.IsWorldPawn()) Find.WorldPawns.PassToWorld(Pawn, PawnDiscardDecideMode.KeepForever);
        }

        public void TickBond()
        {
            if (!Bound || Pawn.Dead) return;
            if (Soul.Together(owner, Pawn, 25))
            {
                GainBond(2f / 24f);
                if (Soul.Now - lastCombatTick <= 2500 && (owner.Drafted || owner.CurJobDef == JobDefOf.AttackMelee || Soul.Now - owner.mindState.lastAttackTargetTick <= 2500))
                { GainXP(5); GainBond(.5f); }
            }
            if (Level >= 2 && owner.Downed && Soul.Ready(Pawn) && Soul.Together(owner, Pawn, 40) && !Soul.Together(owner, Pawn, 3))
            {
                if (Pawn.CurJobDef != JobDefOf.AttackMelee && Pawn.CurJobDef != JobDefOf.Goto)
                    Pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, CellFinder.RandomClosewalkCellNear(owner.Position, owner.Map, 2)));
            }
            TryGuard();
        }

        public void EnableContractControl()
        {
            if (!Bound || Pawn.Dead || Pawn.Destroyed) return;
            if (Pawn.drafter == null) Pawn.drafter = new Pawn_DraftController(Pawn);
            Soul.Add(Pawn, "RMS_ContractBlessing");
            if (Pawn.training != null)
            {
                // A soul contract replaces animal-breaking and obedience drills.
                // Release is granted so vanilla master behaviour also works while
                // the explicit draft controls remain available.
                Pawn.training.Train(TrainableDefOf.Tameness, owner, true);
                Pawn.training.Train(TrainableDefOf.Obedience, owner, true);
                Pawn.training.Train(TrainableDefOf.Release, owner, true);
            }
            if (Pawn.playerSettings != null) Pawn.playerSettings.Master = owner;
        }

        public void SeverControl()
        {
            Soul.Remove(Pawn, "RMS_ContractBlessing");
            if (Pawn.drafter != null) { Pawn.drafter.Drafted = false; Pawn.drafter = null; }
        }

        public void KeepCandidateAtCamp()
        {
            if (candidateOwner == null || !candidateAnchor.IsValid || !Pawn.Spawned || Pawn.Downed || Pawn.InMentalState) return;
            if (!Pawn.Position.InHorDistOf(candidateAnchor, 7f) && Pawn.CurJobDef != JobDefOf.Goto)
                Pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, candidateAnchor));
        }

        public void TryGuard()
        {
            if (!Bound || (!Evolved && Level < 4) || !Soul.Ready(Pawn) || !Soul.Together(owner, Pawn, 30) || Soul.Now < guardCooldown) return;
            if (!owner.Downed && owner.health.summaryHealth.SummaryHealthPercent >= .35f) return;
            guardCooldown = Soul.Now + 3600;
            Soul.Add(Pawn, Evolved ? "RMS_LastGuard" : "RMS_Guard");
        }

        public bool Attack(Thing target, bool commanded)
        {
            if (!Bound || !Soul.Ready(Pawn) || (!Soul.Ready(owner) && !owner.Downed) || target == null || !target.Spawned || target.Map != Pawn.Map || !target.HostileTo(owner)) return false;
            if (!Pawn.CanReach(target, PathEndMode.Touch, Danger.Deadly)) return false;
            var job = JobMaker.MakeJob(JobDefOf.AttackMelee, target);
            job.expiryInterval = 1200; job.checkOverrideOnExpire = true;
            Pawn.jobs.TryTakeOrderedJob(job);
            lastCombatTick = Soul.Now;
            if (commanded && Level >= 3)
            {
                // A timed cooldown hediff prevents repeated commands from refreshing a permanent sprint.
                if (!Soul.Has(Pawn, "RMS_HuntCooldown"))
                { Soul.Add(Pawn, Evolved ? "RMS_SoulHunt" : "RMS_Hunt"); Soul.Add(Pawn, "RMS_HuntCooldown"); }
            }
            return true;
        }

        public string Status => Evolved ? "巨牙灵狼" : "灵狼";
        public override string CompInspectStringExtra()
        {
            string state = Bound ? "契约稳定" : candidateOwner != null ? "等待契约" : lostUntil > 0 ? "失魂" : "无契约";
            return $"{Status} · {state}\n主人：{(owner ?? candidateOwner)?.LabelShort ?? "无"}\nLv{Level}  经验：{xp:0}/{GrowthRules.Next(xp):0}\n羁绊：{bond:0.0}/100\n契约强化：{(Bound ? "伤害+15%，移速+10%，承伤-15%，攻击间隔-10%" : "未生效")}\n进化：{(Evolved ? "已完成" : Eligible ? "已满足条件" : "需要Lv5与75羁绊")}";
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (!Bound || Pawn.Faction != Faction.OfPlayer) yield break;
            yield return new Command_Action
            {
                defaultLabel = "猎杀指令", defaultDesc = "指定敌对目标。Lv3后短暂追击加速；30秒冷却。",
                icon = ContentFinder<Texture2D>.Get("UI/Summoner/Hunt"),
                action = () => Find.Targeter.BeginTargeting(new TargetingParameters { canTargetPawns = true, canTargetBuildings = true, canTargetLocations = false }, t => { if (!Attack(t.Thing, true)) Soul.Reject("无法攻击该目标。"); })
            };
            if (!Evolved)
            {
                var g = new Command_Action
                {
                    defaultLabel = "进化：巨牙灵狼", defaultDesc = "Lv5、羁绊75，主人和灵狼清醒且在12格内。保留个体伤病。",
                    icon = ContentFinder<Texture2D>.Get("UI/Summoner/Evolve"),
                    action = () => Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("让这只灵狼进化为巨牙灵狼？进化不会治疗现有伤病。", () => Evolve()))
                };
                if (!CanEvolve) g.Disable("尚未满足进化条件或双方当前无法进行仪式。");
                yield return g;
            }
        }
    }

    public sealed class ITab_SoulContract : ITab
    {
        public ITab_SoulContract() { size = new Vector2(390, 315); labelKey = "RMS_ContractTab"; }
        public override bool IsVisible => Soul.Wolf(SelPawn) != null;
        protected override void FillTab()
        {
            var c = Soul.Wolf(SelPawn);
            if (c == null) return;
            var listing = new Listing_Standard();
            listing.Begin(new Rect(15, 15, 360, 285));
            listing.Label(c.CompInspectStringExtra());
            listing.GapLine();
            var r = Soul.Game?.ForBeast(c.Pawn);
            listing.Label("灵魂链接：" + (r?.linkEnabled == true ? "已开启（同地图30格内生效）" : "关闭"));
            listing.Label("经验道具只能增加经验；羁绊来自共同经历。");
            listing.Label("死亡永久失去契约及个体成长。");
            listing.End();
        }
    }
}
