using RimWorld;
using RimWorld.Planet;
using Verse;
using VEF.Abilities;
using Ability = VEF.Abilities.Ability;

namespace RimMod.Summoner
{
    public sealed class Ability_Contract : Ability
    {
        public override bool IsEnabledForPawn(out string reason)
        {
            if (!base.IsEnabledForPawn(out reason)) return false;
            if (Soul.Game?.CanSeek(pawn) == true) return true;
            reason = "已有契约、灵魂创伤尚未结束，或当前无法寻找灵狼。"; return false;
        }
        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            bool valid = target.Pawn == pawn ? Soul.Game.CanSeek(pawn) && Soul.Game.Seeking(pawn)?.candidate == null : Soul.Game.CanBind(pawn, target.Pawn, out _);
            if (!valid && showMessages) Soul.Reject("对自己的候选灵狼施法；没有候选时可对自己施法寻找。");
            return valid && base.ValidateTarget(target, showMessages);
        }
        public override void Cast(params GlobalTargetInfo[] targets)
        {
            if (targets.Length == 0) return;
            var target = targets[0].Thing as Pawn;
            if (target == pawn)
            {
                if (!Soul.Game.CanSeek(pawn) || Soul.Game.Seeking(pawn)?.candidate != null) return;
                base.Cast(targets); Soul.Game.BeginSearch(pawn);
            }
            else if (Soul.Game.CanBind(pawn, target, out _)) { base.Cast(targets); Soul.Game.Bind(pawn, target); }
        }
    }

    public sealed class Ability_Call : Ability
    {
        public override bool IsEnabledForPawn(out string reason)
        {
            if (!base.IsEnabledForPawn(out reason)) return false;
            var beast = Soul.Game?.Active(pawn)?.beast;
            if (beast != null && !beast.Dead && beast.Spawned && pawn.Spawned && beast.Map == pawn.Map && !beast.InMentalState) return true;
            reason = "契约兽必须存活、在同一地图且未精神失常。呼唤不复活或生成新狼。"; return false;
        }
        public override void Cast(params GlobalTargetInfo[] targets)
        {
            if (!IsEnabledForPawn(out var why)) { Soul.Reject(why); return; }
            var beast = Soul.Game.Active(pawn).beast;
            var cell = CellFinder.RandomClosewalkCellNear(pawn.Position, pawn.Map, 2);
            if (!cell.IsValid || !cell.Standable(pawn.Map)) return;
            base.Cast(targets);
            beast.jobs.StopAll();
            beast.DeSpawn();
            GenSpawn.Spawn(beast, cell, pawn.Map);
        }
    }

    public sealed class Ability_Link : Ability
    {
        public override bool IsEnabledForPawn(out string reason)
        {
            var r = Soul.Game?.Active(pawn);
            // Allow turning off even if focus has run out.
            if (r?.linkEnabled == true) { reason = null; return Soul.Ready(pawn); }
            if (!base.IsEnabledForPawn(out reason)) return false;
            if (r?.beast != null && !r.beast.Dead && Soul.Together(pawn, r.beast, 30)) return true;
            reason = "需要存活的契约兽在同地图30格内。"; return false;
        }
        public override void Cast(params GlobalTargetInfo[] targets)
        {
            if (!IsEnabledForPawn(out var why)) { Soul.Reject(why); return; }
            var r = Soul.Game.Active(pawn);
            if (!r.linkEnabled) base.Cast(targets);
            r.linkEnabled = !r.linkEnabled;
            Messages.Message(r.linkEnabled ? "灵魂链接开启：分摊20%伤害。" : "灵魂链接关闭。", pawn, MessageTypeDefOf.NeutralEvent, false);
        }
    }

    public sealed class CompProperties_Training : CompProperties_UseEffect
    {
        public float experience = 100;
        public CompProperties_Training() { compClass = typeof(CompUseEffect_Training); }
    }
    public sealed class CompUseEffect_Training : CompUseEffect
    {
        public override AcceptanceReport CanBeUsedBy(Pawn p)
        {
            var c = Soul.Wolf(Soul.Game?.Active(p)?.beast);
            if (c == null || !c.Bound || !Soul.Together(p, c.Pawn, 30)) return "需要自己的契约兽在同地图30格内。";
            return c.xp < GrowthRules.MaxXP ? (AcceptanceReport)true : "灵狼经验已满。";
        }
        public override void DoEffect(Pawn usedBy)
        {
            if (!CanBeUsedBy(usedBy).Accepted) return;
            Soul.Wolf(Soul.Game.Active(usedBy).beast).GainXP(((CompProperties_Training)props).experience, false);
            parent.SplitOff(1).Destroy();
        }
    }
}
