using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.AI.Group;
using VEF.Abilities;

namespace RimMod.Summoner
{
    public enum ContractState { Seeking, Active, BeastDead, Severed, Expired }

    public sealed class ContractRecord : IExposable
    {
        public int id;
        public Pawn owner, beast, candidate;
        public ContractState state;
        public int nextSearchTick, candidateExpiry, lostUntil;
        public IntVec3 campCell = IntVec3.Invalid;
        public List<Pawn> guards = new List<Pawn>();
        public bool linkEnabled, guardsDefeatedNotified;
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_References.Look(ref owner, "owner");
            Scribe_References.Look(ref beast, "beast");
            Scribe_References.Look(ref candidate, "candidate");
            Scribe_Values.Look(ref state, "state");
            Scribe_Values.Look(ref nextSearchTick, "nextSearchTick");
            Scribe_Values.Look(ref candidateExpiry, "candidateExpiry");
            Scribe_Values.Look(ref lostUntil, "lostUntil");
            Scribe_Values.Look(ref campCell, "campCell", IntVec3.Invalid);
            Scribe_Collections.Look(ref guards, "guards", LookMode.Reference);
            Scribe_Values.Look(ref linkEnabled, "linkEnabled");
            Scribe_Values.Look(ref guardsDefeatedNotified, "guardsDefeatedNotified");
            if (Scribe.mode == LoadSaveMode.PostLoadInit && guards == null) guards = new List<Pawn>();
        }
    }

    public static class Soul
    {
        public const int Day = 60000;
        public static int Now => Find.TickManager.TicksGame;
        public static SummonerGame Game => Current.Game?.GetComponent<SummonerGame>();
        public static CompSpiritWolf Wolf(Pawn p) => p?.TryGetComp<CompSpiritWolf>();
        public static HediffDef H(string name) => DefDatabase<HediffDef>.GetNamed(name);
        public static bool Has(Pawn p, string name) => p?.health?.hediffSet.HasHediff(H(name)) == true;
        public static bool Knows(Pawn p, string name) => p?.GetComp<CompAbilities>()?.LearnedAbilities.Any(a => a.def.defName == name) == true;
        public static bool Ready(Pawn p) => p != null && !p.Dead && !p.Destroyed && p.Spawned && !p.Downed && !p.InMentalState;
        public static bool Together(Pawn a, Pawn b, float range) => a?.Spawned == true && b?.Spawned == true && a.Map == b.Map && a.Position.InHorDistOf(b.Position, range);
        public static void Add(Pawn p, string name)
        {
            if (p != null && !Has(p, name)) p.health.AddHediff(H(name));
        }
        public static void Remove(Pawn p, string name)
        {
            var h = p?.health?.hediffSet.GetFirstHediffOfDef(H(name));
            if (h != null) p.health.RemoveHediff(h);
        }
        public static void Tell(string title, string body, Pawn p) => Find.LetterStack.ReceiveLetter(title, body, LetterDefOf.NeutralEvent, p);
        public static void Reject(string reason) => Messages.Message(reason, MessageTypeDefOf.RejectInput, false);
        public static bool Protected(Pawn p) => Wolf(p)?.Protected == true;
    }

    public sealed class SummonerGame : GameComponent
    {
        public List<ContractRecord> records = new List<ContractRecord>();
        private int nextId = 1;
        public SummonerGame(Game game) { }
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref records, "summonerContracts", LookMode.Deep);
            Scribe_Values.Look(ref nextId, "summonerNextId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && records == null) records = new List<ContractRecord>();
        }
        public ContractRecord Active(Pawn owner) => records.FirstOrDefault(r => r.owner == owner && r.state == ContractState.Active);
        public ContractRecord Seeking(Pawn owner) => records.FirstOrDefault(r => r.owner == owner && r.state == ContractState.Seeking);
        public ContractRecord ForBeast(Pawn beast) => records.FirstOrDefault(r => r.beast == beast && r.state == ContractState.Active);
        public bool CanSeek(Pawn owner) => Soul.Ready(owner) && owner.Faction == Faction.OfPlayer && Soul.Knows(owner, "RMS_Contract") && !Soul.Has(owner, "RMS_Trauma") && Active(owner) == null
            && !records.Any(r => r.owner == owner && r.state == ContractState.BeastDead && Soul.Now < r.nextSearchTick);

        public ContractRecord BeginSearch(Pawn owner)
        {
            if (!CanSeek(owner)) return null;
            var existing = Seeking(owner);
            if (existing != null) { TryArrival(existing); return existing; }
            var r = new ContractRecord
            {
                id = nextId++, owner = owner, state = ContractState.Seeking,
                // The call is first sensed, then located. This keeps the event from
                // dropping a wolf at the colony entrance the instant a skill is learned.
                nextSearchTick = Soul.Now + Rand.RangeInclusive(Soul.Day, 2 * Soul.Day)
            };
            records.Add(r);
            Soul.Tell("遥远的灵魂呼唤", $"{owner.LabelShort}察觉到一股被压制的灵魂气息。大约一至两天后，契约感应会确定它在当前区域的位置。", owner);
            TryArrival(r);
            return r;
        }

        private void TryArrival(ContractRecord r)
        {
            if (!CanSeek(r.owner) || r.candidate != null || Soul.Now < r.nextSearchTick) return;
            if (!TryFindCampCell(r.owner, out var cell))
            { r.nextSearchTick = Soul.Now + 2500; return; }
            var wolf = PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("RMS_SpiritWolf"), fixedBiologicalAge: 2f));
            r.candidate = wolf;
            r.candidateExpiry = Soul.Now + 3 * Soul.Day;
            r.campCell = cell;
            var comp = Soul.Wolf(wolf);
            comp.candidateOwner = r.owner;
            comp.contractId = r.id;
            comp.candidateAnchor = cell;
            GenSpawn.Spawn(wolf, cell, r.owner.Map);
            Soul.Add(wolf, "RMS_SoulSnare");
            SpawnGuards(r);
            Soul.Tell("被囚禁的灵狼", $"{r.owner.LabelShort}终于锁定了呼唤：一只灵狼被{r.guards.Count}名部落守卫困在野外营地。击败或驱离守卫，救下灵狼，再在三天内靠近施放「灵魂契约」。", wolf);
        }

        private static bool TryFindCampCell(Pawn owner, out IntVec3 cell)
        {
            var map = owner.Map;
            return CellFinderLoose.TryFindRandomNotEdgeCellWith(12, c =>
                c.Standable(map) && !c.Fogged(map) && !c.Roofed(map) &&
                c.DistanceTo(owner.Position) >= 35f && c.DistanceTo(owner.Position) <= 90f &&
                map.reachability.CanReach(owner.Position, c, PathEndMode.OnCell, TraverseParms.For(TraverseMode.PassDoors)), map, out cell);
        }

        private static void SpawnGuards(ContractRecord r)
        {
            var map = r.owner.Map;
            var faction = Find.FactionManager.AllFactionsVisible
                .Where(f => !f.IsPlayer && f.HostileTo(Faction.OfPlayer) && f.def.techLevel == TechLevel.Neolithic)
                .RandomElementWithFallback(Faction.OfAncientsHostile);
            var kinds = new[] { "Tribal_Warrior", "Tribal_Archer", "Tribal_Hunter" }
                .Select(name => DefDatabase<PawnKindDef>.GetNamed(name)).ToList();
            int count = Math.Max(3, Math.Min(6, 2 + map.mapPawns.FreeColonistsSpawnedCount / 2));
            var lord = LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(r.campCell), map);
            for (int i = 0; i < count; i++)
            {
                var guard = PawnGenerator.GeneratePawn(new PawnGenerationRequest(kinds[i % kinds.Count], faction, PawnGenerationContext.NonPlayer, map.Tile));
                if (!CellFinder.TryFindRandomSpawnCellForPawnNear(r.campCell, map, out var spawn, 7)) { guard.Destroy(); continue; }
                GenSpawn.Spawn(guard, spawn, map);
                lord.AddPawn(guard);
                r.guards.Add(guard);
            }
        }

        public static bool GuardsRemain(ContractRecord r) => r.guards != null && r.guards.Any(g => g != null && !g.Dead && !g.Destroyed && g.Spawned && !g.Downed && g.Faction != null && g.Faction.HostileTo(Faction.OfPlayer));

        public bool CanBind(Pawn owner, Pawn wolf, out string reason)
        {
            var r = Seeking(owner);
            if (!CanSeek(owner)) reason = "已有契约、灵魂创伤尚未结束，或当前无法建立契约。";
            else if (r == null || r.candidate != wolf || Soul.Now >= r.candidateExpiry) reason = "只能契约灵魂呼唤事件中属于你的那只灵狼。";
            else if (GuardsRemain(r)) reason = "部落守卫仍控制着灵狼。先击败或驱离他们。";
            else if (!Soul.Ready(wolf) || !Soul.Together(owner, wolf, 12)) reason = "需要双方清醒，且在同地图12格内。";
            else { reason = null; return true; }
            return false;
        }

        public bool Bind(Pawn owner, Pawn wolf)
        {
            if (!CanBind(owner, wolf, out var why)) { Soul.Reject(why); return false; }
            var r = Seeking(owner);
            r.beast = wolf; r.candidate = null; r.state = ContractState.Active;
            var comp = Soul.Wolf(wolf);
            comp.owner = owner; comp.candidateOwner = null; comp.candidateAnchor = IntVec3.Invalid;
            wolf.SetFaction(owner.Faction);
            wolf.mindState.mentalStateHandler.Reset();
            Soul.Remove(wolf, "RMS_SoulSnare");
            comp.EnableContractControl();
            if (wolf.playerSettings != null) wolf.playerSettings.medCare = MedicalCareCategory.Best;
            Soul.Add(owner, "RMS_Weakness");
            Soul.Tell("灵魂契约已建立", $"{owner.LabelShort}与{wolf.LabelShort}建立了唯一的灵魂契约。主人将虚弱两天。灵狼死亡不会重生，请照顾好它。", wolf);
            return true;
        }

        public void NotifyDeath(Pawn pawn)
        {
            foreach (var r in records.ToList())
            {
                if (r.state == ContractState.Active && r.beast == pawn)
                {
                    r.state = ContractState.BeastDead; r.linkEnabled = false;
                    Soul.Wolf(pawn).owner = null;
                    if (r.owner != null && !r.owner.Dead)
                    {
                        Soul.Add(r.owner, "RMS_Trauma");
                        r.nextSearchTick = Soul.Now + 7 * Soul.Day;
                        Soul.Tell("契约兽永久死亡", $"{pawn.LabelShort}的生命已经结束。{r.owner.LabelShort}留下七天灵魂创伤，期间无法重新契约。新契约兽将从零成长。", r.owner);
                    }
                }
                if (r.owner == pawn && r.state == ContractState.Active)
                {
                    r.state = ContractState.Severed; r.linkEnabled = false; r.lostUntil = Soul.Now + Soul.Day;
                    var c = Soul.Wolf(r.beast);
                    if (c != null) { c.SeverControl(); c.owner = null; c.lostUntil = r.lostUntil; }
                    Soul.Add(r.beast, "RMS_LostSoul");
                    Soul.Tell("契约断裂", "召唤师死亡，灵狼陷入失魂。它仍然活着，一天后会恢复野性并尝试离开。", r.beast);
                }
            }
        }

        public override void GameComponentTick()
        {
            if (Soul.Now % 250 != 0) return;
            foreach (var r in records.ToList())
            {
                if (r.state == ContractState.Active)
                {
                    if (r.owner == null || r.owner.Dead || r.owner.Destroyed)
                    {
                        if (r.owner != null) NotifyDeath(r.owner);
                        else { r.state = ContractState.Severed; var c = Soul.Wolf(r.beast); if(c != null) { c.owner = null; c.lostUntil = Soul.Now + Soul.Day; } }
                    }
                    else if (r.beast == null || r.beast.Dead || r.beast.Destroyed)
                    {
                        if (r.beast != null) NotifyDeath(r.beast);
                        else { r.state = ContractState.BeastDead; Soul.Add(r.owner, "RMS_Trauma"); r.nextSearchTick = Soul.Now + 7 * Soul.Day; }
                    }
                    else Soul.Wolf(r.beast)?.EnableContractControl();
                }
                if (r.state == ContractState.Seeking)
                {
                    if (r.owner == null || r.owner.Dead || r.owner.Destroyed) { ReleaseCandidate(r); r.state = ContractState.Expired; continue; }
                    if (r.candidate != null && (r.candidate.Dead || r.candidate.Destroyed || !r.candidate.Spawned || Soul.Now >= r.candidateExpiry))
                    { ReleaseCandidate(r); r.nextSearchTick = Soul.Now + Soul.Day; }
                    if (r.candidate != null && !GuardsRemain(r) && !r.guardsDefeatedNotified)
                    {
                        r.guardsDefeatedNotified = true;
                        Soul.Tell("守卫已被击退", "部落守卫已经失去战斗能力。现在可以靠近灵狼、处理它的伤势并签订灵魂契约。", r.candidate);
                    }
                    TryArrival(r);
                    Soul.Wolf(r.candidate)?.KeepCandidateAtCamp();
                }
                if (r.state == ContractState.BeastDead && Soul.Now >= r.nextSearchTick && CanSeek(r.owner))
                { r.state = ContractState.Expired; BeginSearch(r.owner); }
                // World pawns/caravans need the same loss timer as spawned pawns.
                var lost = Soul.Wolf(r.beast);
                if (lost != null && lost.lostUntil > 0 && Soul.Now >= lost.lostUntil) lost.ReturnWild();
            }
            if (Soul.Now % 2500 == 0)
                foreach (var p in Find.Maps.SelectMany(m => m.mapPawns.FreeColonistsSpawned).ToList())
                    if (CanSeek(p) && !records.Any(r => r.owner == p && (r.state == ContractState.Active || r.state == ContractState.Seeking || r.state == ContractState.BeastDead))) BeginSearch(p);
        }

        private static void ReleaseCandidate(ContractRecord r)
        {
            var c = Soul.Wolf(r.candidate);
            if (c != null) { c.candidateOwner = null; c.candidateAnchor = IntVec3.Invalid; }
            Soul.Remove(r.candidate, "RMS_SoulSnare");
            if (r.candidate?.Spawned == true && !r.candidate.Dead) r.candidate.mindState.exitMapAfterTick = Soul.Now;
            foreach (var guard in r.guards ?? Enumerable.Empty<Pawn>())
            {
                if (guard == null || guard.Dead || guard.Destroyed || !guard.Spawned) continue;
                guard.GetLord()?.Notify_PawnLost(guard, PawnLostCondition.ForcedToJoinOtherLord);
                guard.mindState.exitMapAfterTick = Soul.Now;
            }
            r.candidate = null;
        }
    }
}
