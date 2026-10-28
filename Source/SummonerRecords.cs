using System.Collections.Generic;
using Verse;

namespace RimMod.Summoner
{
    public static class SummonerLimits
    {
        public const int InitialFieldCapacity = 1;
        public const int InitialContractSlots = 1;
        public const int MaxReservesPerSlot = 3;
        public const int MinTier = 1;
        public const int MaxTier = 4;
        public const int MinTierLevel = 1;
        public const int MaxTierLevel = 40;
        public const int NoCooldownTick = -1;
    }

    // Location, rest, roster role, and lockout are separate.
    // They are not collapsed into one state enum.
    public enum BeastPresence
    {
        InRealm = 0,
        OnField = 1
    }

    public enum BeastRest
    {
        None = 0,
        Sleeping = 1
    }

    public enum BeastRosterRole
    {
        Unassigned = 0,
        Primary = 1,
        Reserve = 2
    }

    public enum RewardSubjectKind
    {
        Summoner = 0,
        Beast = 1
    }

    // Stored kind only. This round does not decide how many rewards a tier grants.
    public enum PendingRewardKind
    {
        Unspecified = 0,
        BeastGrowthChoice = 1,
        BeastAbilityChoice = 2,
        SummonerFeedbackChoice = 3
    }

    public sealed class StatInvestmentRecord : IExposable
    {
        public string statId = "";
        public int count;

        public void ExposeData()
        {
            Scribe_Values.Look(ref statId, "statId", "");
            Scribe_Values.Look(ref count, "count", 0);
        }
    }

    public sealed class MutationRecord : IExposable
    {
        public string mutationId = "";
        public int stacks;

        public void ExposeData()
        {
            Scribe_Values.Look(ref mutationId, "mutationId", "");
            Scribe_Values.Look(ref stacks, "stacks", 0);
        }
    }

    public sealed class SkillRecord : IExposable
    {
        public string skillId = "";
        public int level;
        public int selectionCount;

        public void ExposeData()
        {
            Scribe_Values.Look(ref skillId, "skillId", "");
            Scribe_Values.Look(ref level, "level", 0);
            Scribe_Values.Look(ref selectionCount, "selectionCount", 0);
        }
    }

    public sealed class WorkRecord : IExposable
    {
        public string workId = "";
        public int level;

        public void ExposeData()
        {
            Scribe_Values.Look(ref workId, "workId", "");
            Scribe_Values.Look(ref level, "level", 0);
        }
    }

    public sealed class RewardChoiceState : IExposable
    {
        public List<string> optionIds = new List<string>();
        public int selectedIndex = -1;
        public bool selectionResolved;

        public void ExposeData()
        {
            Scribe_Collections.Look(ref optionIds, "optionIds", LookMode.Value);
            Scribe_Values.Look(ref selectedIndex, "selectedIndex", -1);
            Scribe_Values.Look(ref selectionResolved, "selectionResolved", false);
            if (optionIds == null)
                optionIds = new List<string>();
        }
    }

    public sealed class PendingRewardRecord : IExposable
    {
        public string id = "";
        public RewardSubjectKind subjectKind;
        public string subjectId = "";
        public PendingRewardKind kind;
        public int sourceTier = SummonerLimits.MinTier;
        public int sourceLevel = SummonerLimits.MinTierLevel;
        public RewardChoiceState choice = new RewardChoiceState();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref subjectKind, "subjectKind", RewardSubjectKind.Summoner);
            Scribe_Values.Look(ref subjectId, "subjectId", "");
            Scribe_Values.Look(ref kind, "kind", PendingRewardKind.Unspecified);
            Scribe_Values.Look(ref sourceTier, "sourceTier", SummonerLimits.MinTier);
            Scribe_Values.Look(ref sourceLevel, "sourceLevel", SummonerLimits.MinTierLevel);
            Scribe_Deep.Look(ref choice, "choice");
            if (choice == null)
                choice = new RewardChoiceState();
        }
    }

    public sealed class ContractSlotRecord : IExposable
    {
        public string id = "";
        public string bloodlineId = "";
        public string primaryBeastId = "";
        public List<string> reserveBeastIds = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref bloodlineId, "bloodlineId", "");
            Scribe_Values.Look(ref primaryBeastId, "primaryBeastId", "");
            Scribe_Collections.Look(ref reserveBeastIds, "reserveBeastIds", LookMode.Value);
            if (reserveBeastIds == null)
                reserveBeastIds = new List<string>();
        }
    }

    public sealed class BeastRecord : IExposable
    {
        public string id = "";
        public string ownerId = "";
        public string bloodlineId = "";
        public int tier = SummonerLimits.MinTier;
        public int tierLevel = SummonerLimits.MinTierLevel;
        public float experience;
        public int occupySlots = 1;
        public BeastPresence presence = BeastPresence.InRealm;
        public BeastRest rest = BeastRest.None;
        public int lockoutEndTick = SummonerLimits.NoCooldownTick;
        public int sleepEndTick = SummonerLimits.NoCooldownTick;
        public bool dead;

        public List<StatInvestmentRecord> statInvestments = new List<StatInvestmentRecord>();
        public List<MutationRecord> mutations = new List<MutationRecord>();
        public List<SkillRecord> skills = new List<SkillRecord>();
        public List<WorkRecord> works = new List<WorkRecord>();

        // Rebuilt from contract slots. Slot lists are the saved membership.
        public string contractSlotId = "";
        public BeastRosterRole role = BeastRosterRole.Unassigned;

        // Runtime pawn pointer. Saved as a reference only while OnField.
        public Pawn pawn;

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_Values.Look(ref ownerId, "ownerId", "");
            Scribe_Values.Look(ref bloodlineId, "bloodlineId", "");
            Scribe_Values.Look(ref tier, "tier", SummonerLimits.MinTier);
            Scribe_Values.Look(ref tierLevel, "tierLevel", SummonerLimits.MinTierLevel);
            Scribe_Values.Look(ref experience, "experience", 0f);
            Scribe_Values.Look(ref occupySlots, "occupySlots", 1);
            Scribe_Values.Look(ref presence, "presence", BeastPresence.InRealm);
            Scribe_Values.Look(ref rest, "rest", BeastRest.None);
            Scribe_Values.Look(ref lockoutEndTick, "lockoutEndTick", SummonerLimits.NoCooldownTick);
            Scribe_Values.Look(ref sleepEndTick, "sleepEndTick", SummonerLimits.NoCooldownTick);
            Scribe_Values.Look(ref dead, "dead", false);
            Scribe_Collections.Look(ref statInvestments, "statInvestments", LookMode.Deep);
            Scribe_Collections.Look(ref mutations, "mutations", LookMode.Deep);
            Scribe_Collections.Look(ref skills, "skills", LookMode.Deep);
            Scribe_Collections.Look(ref works, "works", LookMode.Deep);
            if (statInvestments == null)
                statInvestments = new List<StatInvestmentRecord>();
            if (mutations == null)
                mutations = new List<MutationRecord>();
            if (skills == null)
                skills = new List<SkillRecord>();
            if (works == null)
                works = new List<WorkRecord>();

            Pawn fieldPawn = null;
            if (Scribe.mode == LoadSaveMode.Saving && presence == BeastPresence.OnField)
                fieldPawn = pawn;
            Scribe_References.Look(ref fieldPawn, "fieldPawn");
            if (presence == BeastPresence.OnField && Scribe.mode == LoadSaveMode.ResolvingCrossRefs)
                pawn = fieldPawn;
        }
    }

    public sealed class SummonerRecord : IExposable
    {
        public string id = "";
        public Pawn master;
        public int proficiency;
        public int fieldCapacity = SummonerLimits.InitialFieldCapacity;
        public int contractSlotCapacity = SummonerLimits.InitialContractSlots;
        public int nextRescueTick = -1;
        public int rescueSequence;
        public List<ContractSlotRecord> slots = new List<ContractSlotRecord>();
        public List<PendingRewardRecord> pendingRewards = new List<PendingRewardRecord>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id", "");
            Scribe_References.Look(ref master, "master");
            Scribe_Values.Look(ref proficiency, "proficiency", 0);
            Scribe_Values.Look(ref fieldCapacity, "fieldCapacity", SummonerLimits.InitialFieldCapacity);
            Scribe_Values.Look(ref contractSlotCapacity, "contractSlotCapacity", SummonerLimits.InitialContractSlots);
            Scribe_Values.Look(ref nextRescueTick, "nextRescueTick", -1);
            Scribe_Values.Look(ref rescueSequence, "rescueSequence");
            Scribe_Collections.Look(ref slots, "slots", LookMode.Deep);
            Scribe_Collections.Look(ref pendingRewards, "pendingRewards", LookMode.Deep);
            if (slots == null)
                slots = new List<ContractSlotRecord>();
            if (pendingRewards == null)
                pendingRewards = new List<PendingRewardRecord>();
        }
    }
}
