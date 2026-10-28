using System.Collections.Generic;
using System.Text;
using RimWorld.Planet;
using Verse;

namespace RimMod.Summoner
{
    // Per-game data root. Discovered by Game.FillComponents; do not store this state in static lists.
    //
    // Ownership:
    // - Summoner records, slots, rewards, and beast growth/state live only here.
    // - In-realm pawns are deep-saved by SoulRealmContainer.
    // - On-field pawns and the summoner master are references to map/world entities.
    // - CompSummonerBeastLink stores the beast id and is overwritten from this archive after load.
    public sealed class SummonerArchive : GameComponent, IThingHolder
    {
        private int nextSummoner = 1;
        private int nextSlot = 1;
        private int nextBeast = 1;
        private int nextReward = 1;
        private List<SummonerRecord> summoners = new List<SummonerRecord>();
        private List<BeastRecord> beasts = new List<BeastRecord>();
        private SoulRealmContainer soulRealm = new SoulRealmContainer();

        public SummonerArchive(Game game)
        {
            EnsureCollections();
            soulRealm.Attach(this);
        }

        public static SummonerArchive Current
        {
            get
            {
                Game game = Verse.Current.Game;
                return game == null ? null : game.GetComponent<SummonerArchive>();
            }
        }

        public IThingHolder ParentHolder => null;

        public IReadOnlyList<SummonerRecord> Summoners => summoners;

        public IReadOnlyList<BeastRecord> Beasts => beasts;

        public SoulRealmContainer SoulRealm => soulRealm;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (outChildren != null && soulRealm != null)
                outChildren.Add(soulRealm);
        }

        public ThingOwner GetDirectlyHeldThings() => null;

        public override void StartedNewGame()
        {
            EnsureCollections();
            RebuildRosterCache();
        }

        public override void LoadedGame()
        {
            FinishLoad();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            EnsureCollections();
            Scribe_Values.Look(ref nextSummoner, "nextSummoner", 1);
            Scribe_Values.Look(ref nextSlot, "nextSlot", 1);
            Scribe_Values.Look(ref nextBeast, "nextBeast", 1);
            Scribe_Values.Look(ref nextReward, "nextReward", 1);
            Scribe_Collections.Look(ref summoners, "summoners", LookMode.Deep);
            Scribe_Collections.Look(ref beasts, "beasts", LookMode.Deep);
            Scribe_Deep.Look(ref soulRealm, "soulRealm");
            EnsureCollections();
            if (Scribe.mode != LoadSaveMode.Saving)
                FinishLoad();
        }

        public SummonerRecord RegisterSummoner(Pawn master)
        {
            EnsureCollections();
            if (master != null)
            {
                SummonerRecord existing = FindSummoner(master);
                if (existing != null)
                    return existing;
            }

            SummonerRecord record = new SummonerRecord();
            record.id = TakeId(ref nextSummoner, "summoner");
            record.master = master;
            record.proficiency = 0;
            record.fieldCapacity = SummonerLimits.InitialFieldCapacity;
            record.contractSlotCapacity = SummonerLimits.InitialContractSlots;
            record.slots.Add(CreateSlot());
            summoners.Add(record);
            return record;
        }

        public BeastRecord RegisterBeast(string ownerId, out string error)
        {
            error = null;
            SummonerRecord owner = FindSummoner(ownerId);
            if (owner == null)
            {
                error = "Beast owner is not a known summoner.";
                return null;
            }

            BeastRecord record = new BeastRecord();
            record.id = TakeId(ref nextBeast, "beast");
            record.ownerId = owner.id;
            beasts.Add(record);
            return record;
        }

        public bool TrySetProficiency(string summonerId, int proficiency, out string error)
        {
            error = null;
            SummonerRecord summoner = FindSummoner(summonerId);
            if (summoner == null)
            {
                error = "Unknown summoner.";
                return false;
            }
            if (proficiency < 0)
            {
                error = "Proficiency cannot be negative.";
                return false;
            }
            summoner.proficiency = proficiency;
            return true;
        }

        public bool TrySetCapacities(string summonerId, int fieldCapacity, int contractSlotCapacity, out string error)
        {
            error = null;
            SummonerRecord summoner = FindSummoner(summonerId);
            if (summoner == null)
            {
                error = "Unknown summoner.";
                return false;
            }
            if (fieldCapacity < 1 || contractSlotCapacity < 1)
            {
                error = "Field capacity and contract-slot capacity must be at least 1.";
                return false;
            }
            if (contractSlotCapacity < summoner.slots.Count)
            {
                error = "Contract-slot capacity cannot drop below the existing slot list.";
                return false;
            }
            if (FieldLoad(summoner.id) > fieldCapacity)
            {
                error = "Field capacity cannot drop below the current on-field load.";
                return false;
            }
            summoner.fieldCapacity = fieldCapacity;
            summoner.contractSlotCapacity = contractSlotCapacity;
            return true;
        }

        public bool TryAddContractSlot(string summonerId, out ContractSlotRecord slot, out string error)
        {
            slot = null;
            error = null;
            SummonerRecord summoner = FindSummoner(summonerId);
            if (summoner == null)
            {
                error = "Unknown summoner.";
                return false;
            }
            if (summoner.slots.Count >= summoner.contractSlotCapacity)
            {
                error = "No empty contract slot capacity remains.";
                return false;
            }
            slot = CreateSlot();
            summoner.slots.Add(slot);
            return true;
        }

        public bool TrySetGrowth(string beastId, int tier, int tierLevel, float experience, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (tier < SummonerLimits.MinTier || tier > SummonerLimits.MaxTier)
            {
                error = "Tier must be from 1 to 4.";
                return false;
            }
            if (tierLevel < SummonerLimits.MinTierLevel || tierLevel > SummonerLimits.MaxTierLevel)
            {
                error = "Tier level must be from 1 to 40.";
                return false;
            }
            if (float.IsNaN(experience) || float.IsInfinity(experience) || experience < 0f)
            {
                error = "Experience must be a non-negative finite value.";
                return false;
            }
            beast.tier = tier;
            beast.tierLevel = tierLevel;
            beast.experience = experience;
            return true;
        }

        public bool TrySetOccupySlots(string beastId, int occupySlots, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (occupySlots != 1 && occupySlots != 2)
            {
                error = "Occupy slots must be 1 or 2.";
                return false;
            }
            if (beast.presence == BeastPresence.OnField)
            {
                SummonerRecord owner = FindSummoner(beast.ownerId);
                if (owner == null)
                {
                    error = "Beast has no summoner owner.";
                    return false;
                }
                int projected = FieldLoad(beast.ownerId) - beast.occupySlots + occupySlots;
                if (projected > owner.fieldCapacity)
                {
                    error = "Changing occupy slots would exceed field capacity.";
                    return false;
                }
            }
            beast.occupySlots = occupySlots;
            return true;
        }

        public bool TrySetBeastBloodline(string beastId, string bloodlineId, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            bloodlineId = bloodlineId ?? "";
            if (!string.IsNullOrEmpty(beast.contractSlotId))
            {
                ContractSlotRecord slot = FindSlot(beast.contractSlotId);
                if (slot != null && BloodlinesConflict(slot.bloodlineId, bloodlineId))
                {
                    error = "Beast bloodline conflicts with its contract slot.";
                    return false;
                }
            }
            beast.bloodlineId = bloodlineId;
            return true;
        }

        public bool TrySetSlotBloodline(string slotId, string bloodlineId, out string error)
        {
            error = null;
            ContractSlotRecord slot = FindSlot(slotId);
            if (slot == null)
            {
                error = "Unknown contract slot.";
                return false;
            }
            bloodlineId = bloodlineId ?? "";
            if (BloodlinesConflict(slot.primaryBeastId == null ? "" : BloodlineOf(slot.primaryBeastId), bloodlineId)
                || ReserveBloodlineConflicts(slot, bloodlineId))
            {
                error = "Slot bloodline conflicts with a beast already in the slot.";
                return false;
            }
            slot.bloodlineId = bloodlineId;
            return true;
        }

        public bool TryAssignPrimary(string slotId, string beastId, out string error)
        {
            error = null;
            if (!TryGetAssignment(slotId, beastId, out SummonerRecord summoner, out ContractSlotRecord slot, out BeastRecord beast, out error))
                return false;
            if (!string.IsNullOrEmpty(slot.primaryBeastId))
            {
                error = "The slot already has a primary beast.";
                return false;
            }
            if (slot.reserveBeastIds.Contains(beastId))
            {
                error = "A beast cannot be both primary and reserve in the same slot.";
                return false;
            }
            if (!CanJoinSlot(summoner, slot, beast, out error))
                return false;
            slot.primaryBeastId = beast.id;
            RebuildRosterCache();
            return true;
        }

        public bool TryAddReserve(string slotId, string beastId, out string error)
        {
            error = null;
            if (!TryGetAssignment(slotId, beastId, out SummonerRecord summoner, out ContractSlotRecord slot, out BeastRecord beast, out error))
                return false;
            if (slot.primaryBeastId == beastId)
            {
                error = "The primary beast is not stored in the reserve list.";
                return false;
            }
            if (slot.reserveBeastIds.Contains(beastId))
            {
                error = "That beast is already in this reserve list.";
                return false;
            }
            if (slot.reserveBeastIds.Count >= SummonerLimits.MaxReservesPerSlot)
            {
                error = "Reserve list is full. The primary beast does not consume a reserve place.";
                return false;
            }
            if (!CanJoinSlot(summoner, slot, beast, out error))
                return false;
            slot.reserveBeastIds.Add(beast.id);
            RebuildRosterCache();
            return true;
        }

        public bool TrySetPresence(string beastId, BeastPresence presence, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (presence == BeastPresence.InRealm && beast.pawn != null && !soulRealm.HoldsPawn(beast.pawn))
            {
                error = "In-realm presence cannot adopt a pawn that the soul realm does not own.";
                return false;
            }
            if (presence == BeastPresence.OnField && beast.pawn != null && soulRealm.HoldsPawn(beast.pawn))
            {
                error = "On-field presence cannot keep the same pawn inside the soul realm.";
                return false;
            }
            if (presence == BeastPresence.OnField && !FieldLoadFits(beast, out error))
                return false;
            beast.presence = presence;
            return true;
        }

        public bool TrySwapPrimaryWithReserve(string slotId, string beastId, out string error)
        {
            error = null;
            ContractSlotRecord slot = FindSlot(slotId);
            BeastRecord target = FindBeast(beastId);
            BeastRecord old = slot == null ? null : FindBeast(slot.primaryBeastId);
            if (slot == null || target == null || !slot.reserveBeastIds.Contains(beastId)
                || target.contractSlotId != slotId || target.ownerId != FindSummonerOwningSlot(slotId)?.id)
            {
                error = "The target is not a reserve of this contract slot.";
                return false;
            }
            if (target.presence != BeastPresence.InRealm || (old != null && old.presence != BeastPresence.InRealm))
            {
                error = "Both beasts must be recalled before changing the primary.";
                return false;
            }
            int index = slot.reserveBeastIds.IndexOf(beastId);
            if (old == null) slot.reserveBeastIds.RemoveAt(index);
            else slot.reserveBeastIds[index] = old.id;
            slot.primaryBeastId = target.id;
            RebuildRosterCache();
            return true;
        }

        // Only used to undo an uncommitted contract; never deletes an entity.
        internal void RollbackNewBeast(BeastRecord beast)
        {
            if (beast == null || beast.role != BeastRosterRole.Unassigned) return;
            SoulRealmEntry entry = soulRealm.FindEntry(beast.id);
            if (entry != null) entry.Release(beast.pawn);
            soulRealm.RemoveEntry(beast.id);
            if (beast.pawn?.GetComp<CompSummonerBeastLink>() is CompSummonerBeastLink link) link.beastId = "";
            beasts.Remove(beast);
        }

        public bool TrySetRest(string beastId, BeastRest rest, int sleepEndTick, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (sleepEndTick < SummonerLimits.NoCooldownTick)
            {
                error = "Sleep end tick cannot be below -1.";
                return false;
            }
            beast.rest = rest;
            beast.sleepEndTick = sleepEndTick;
            return true;
        }

        public bool TrySetLockoutEndTick(string beastId, int lockoutEndTick, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (lockoutEndTick < SummonerLimits.NoCooldownTick)
            {
                error = "Lockout end tick cannot be below -1.";
                return false;
            }
            beast.lockoutEndTick = lockoutEndTick;
            return true;
        }

        public void MarkDead(string beastId)
        {
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
                return;
            beast.dead = true;
        }

        public bool BindFieldPawn(string beastId, Pawn pawn, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (pawn == null)
            {
                error = "Field binding requires a pawn reference.";
                return false;
            }
            if (!SameBoundPawn(beast, pawn, out error))
                return false;
            if (beast.pawn == pawn && beast.presence == BeastPresence.OnField && soulRealm.PawnFor(beast.id) == null)
            {
                SyncLink(beast);
                return true;
            }
            Pawn held = soulRealm.PawnFor(beast.id);
            if (held != null)
            {
                error = held == pawn
                    ? "The soul realm still owns this pawn. A field record keeps a reference only, so the map or world must already own it."
                    : "This beast id already owns a different soul-realm pawn.";
                return false;
            }
            if (!IsExternallyOwned(pawn))
            {
                error = "Field binding requires a map or world pawn. This layer does not spawn or transfer pawns.";
                return false;
            }
            if (PawnIsBoundElsewhere(pawn, beast.id))
            {
                error = "That pawn is already bound to another beast.";
                return false;
            }
            BeastPresence previous = beast.presence;
            Pawn previousPawn = beast.pawn;
            beast.presence = BeastPresence.OnField;
            beast.pawn = pawn;
            if (!FieldLoadFits(null, out error))
            {
                beast.presence = previous;
                beast.pawn = previousPawn;
                return false;
            }
            SyncLink(beast);
            return true;
        }

        public bool BindRealmPawn(string beastId, Pawn pawn, out string error)
        {
            error = null;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            if (pawn == null)
            {
                error = "Realm ownership requires a pawn.";
                return false;
            }
            if (pawn.Destroyed)
            {
                error = "A destroyed pawn is not stored in the soul realm.";
                return false;
            }
            if (!SameBoundPawn(beast, pawn, out error))
                return false;
            if (beast.pawn == pawn && beast.presence == BeastPresence.InRealm && soulRealm.PawnFor(beast.id) == pawn)
            {
                SyncLink(beast);
                return true;
            }
            if (IsExternallyOwned(pawn))
            {
                error = "Map and world pawns stay outside the soul realm. This layer does not despawn or transfer them.";
                return false;
            }
            if (PawnIsBoundElsewhere(pawn, beast.id))
            {
                error = "That pawn is already bound to another beast.";
                return false;
            }
            if (!soulRealm.TryHoldForBeast(beast.id, pawn, out error))
                return false;
            beast.presence = BeastPresence.InRealm;
            beast.pawn = pawn;
            SyncLink(beast);
            return true;
        }

        public bool TrySetStatInvestment(string beastId, string statId, int count, out string error)
        {
            return TryUpsert(beastId, statId, count, out error, (beast) => beast.statInvestments, (id) => new StatInvestmentRecord { statId = id }, (row) => row.statId, (row, value) => row.count = value);
        }

        public bool TrySetMutation(string beastId, string mutationId, int stacks, out string error)
        {
            return TryUpsert(beastId, mutationId, stacks, out error, (beast) => beast.mutations, (id) => new MutationRecord { mutationId = id }, (row) => row.mutationId, (row, value) => row.stacks = value);
        }

        public bool TrySetSkillRecord(string beastId, string skillId, int level, int selectionCount, out string error)
        {
            error = null;
            if (!TryFindBeast(beastId, out BeastRecord beast, out error))
                return false;
            if (string.IsNullOrEmpty(skillId) || level < 0 || selectionCount < 0)
            {
                error = "Skill records need an id and non-negative values.";
                return false;
            }
            SkillRecord row = null;
            for (int i = 0; i < beast.skills.Count; i++)
            {
                if (beast.skills[i].skillId == skillId)
                    row = beast.skills[i];
            }
            if (level == 0 && selectionCount == 0)
            {
                if (row != null)
                    beast.skills.Remove(row);
                return true;
            }
            if (row == null)
            {
                row = new SkillRecord();
                row.skillId = skillId;
                beast.skills.Add(row);
            }
            row.level = level;
            row.selectionCount = selectionCount;
            return true;
        }

        public bool TrySetWorkRecord(string beastId, string workId, int level, out string error)
        {
            return TryUpsert(beastId, workId, level, out error, (beast) => beast.works, (id) => new WorkRecord { workId = id }, (row) => row.workId, (row, value) => row.level = value);
        }

        public PendingRewardRecord AddPendingReward(string summonerId, RewardSubjectKind subjectKind, string subjectId, PendingRewardKind kind, int sourceTier, int sourceLevel, out string error)
        {
            error = null;
            SummonerRecord summoner = FindSummoner(summonerId);
            if (summoner == null)
            {
                error = "Unknown summoner.";
                return null;
            }
            if (!RewardSubjectMatches(summoner, subjectKind, subjectId, out error))
                return null;
            PendingRewardRecord reward = new PendingRewardRecord();
            reward.id = TakeId(ref nextReward, "reward");
            reward.subjectKind = subjectKind;
            reward.subjectId = subjectId;
            reward.kind = kind;
            reward.sourceTier = sourceTier;
            reward.sourceLevel = sourceLevel;
            summoner.pendingRewards.Add(reward);
            return reward;
        }

        public bool TrySetRewardOptions(string rewardId, IList<string> optionIds, int selectedIndex, bool selectionResolved, out string error)
        {
            error = null;
            PendingRewardRecord reward = FindReward(rewardId);
            if (reward == null)
            {
                error = "Unknown pending reward.";
                return false;
            }
            List<string> copy = new List<string>();
            if (optionIds != null)
            {
                for (int i = 0; i < optionIds.Count; i++)
                {
                    if (string.IsNullOrEmpty(optionIds[i]))
                    {
                        error = "Reward options need stable ids.";
                        return false;
                    }
                    copy.Add(optionIds[i]);
                }
            }
            if (selectedIndex < -1 || selectedIndex >= copy.Count)
            {
                error = "Selected index is outside the saved options.";
                return false;
            }
            reward.choice.optionIds = copy;
            reward.choice.selectedIndex = selectedIndex;
            reward.choice.selectionResolved = selectionResolved;
            return true;
        }

        public int FieldLoad(string summonerId)
        {
            int load = 0;
            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord beast = beasts[i];
                if (beast == null || beast.ownerId != summonerId)
                    continue;
                if (beast.presence != BeastPresence.OnField)
                    continue;
                load += beast.occupySlots;
            }
            return load;
        }

        public SummonerRecord FindSummoner(string id)
        {
            for (int i = 0; i < summoners.Count; i++)
            {
                if (summoners[i] != null && summoners[i].id == id)
                    return summoners[i];
            }
            return null;
        }

        public SummonerRecord FindSummoner(Pawn master)
        {
            if (master == null)
                return null;
            for (int i = 0; i < summoners.Count; i++)
            {
                if (summoners[i] != null && summoners[i].master == master)
                    return summoners[i];
            }
            return null;
        }

        public BeastRecord FindBeast(string id)
        {
            for (int i = 0; i < beasts.Count; i++)
            {
                if (beasts[i] != null && beasts[i].id == id)
                    return beasts[i];
            }
            return null;
        }

        public BeastRecord FindBeastByPawn(Pawn pawn)
        {
            if (pawn == null)
                return null;
            for (int i = 0; i < beasts.Count; i++)
            {
                if (beasts[i] != null && beasts[i].pawn == pawn)
                    return beasts[i];
            }
            return null;
        }

        public void NoteSpawnedLink(Pawn pawn, string compBeastId)
        {
            if (pawn == null)
                return;
            BeastRecord byPawn = FindBeastByPawn(pawn);
            if (byPawn != null)
            {
                CompSummonerBeastLink comp = pawn.GetComp<CompSummonerBeastLink>();
                if (comp != null)
                    comp.beastId = byPawn.id;
                return;
            }
            if (string.IsNullOrEmpty(compBeastId))
                return;
            BeastRecord byId = FindBeast(compBeastId);
            if (byId == null || byId.presence != BeastPresence.OnField || byId.pawn != null)
                return;
            byId.pawn = pawn;
            CompSummonerBeastLink link = pawn.GetComp<CompSummonerBeastLink>();
            if (link != null)
                link.beastId = byId.id;
        }

        public void RebuildRosterCache()
        {
            HashSet<string> claimed = new HashSet<string>();
            for (int i = 0; i < beasts.Count; i++)
            {
                if (beasts[i] == null)
                    continue;
                beasts[i].contractSlotId = "";
                beasts[i].role = BeastRosterRole.Unassigned;
            }
            for (int s = 0; s < summoners.Count; s++)
            {
                SummonerRecord summoner = summoners[s];
                if (summoner == null)
                    continue;
                for (int i = 0; i < summoner.slots.Count; i++)
                {
                    ContractSlotRecord slot = summoner.slots[i];
                    if (slot == null)
                        continue;
                    AssignCachedRole(slot.primaryBeastId, slot.id, BeastRosterRole.Primary, claimed);
                    for (int r = 0; r < slot.reserveBeastIds.Count; r++)
                        AssignCachedRole(slot.reserveBeastIds[r], slot.id, BeastRosterRole.Reserve, claimed);
                }
            }
        }

        public List<string> CheckConsistency()
        {
            List<string> errors = new List<string>();
            HashSet<string> summonerIds = new HashSet<string>();
            HashSet<string> beastIds = new HashSet<string>();
            HashSet<string> slotIds = new HashSet<string>();
            HashSet<string> rewardIds = new HashSet<string>();
            HashSet<Pawn> masters = new HashSet<Pawn>();
            HashSet<string> slottedBeasts = new HashSet<string>();

            for (int i = 0; i < summoners.Count; i++)
            {
                SummonerRecord summoner = summoners[i];
                if (summoner == null || string.IsNullOrEmpty(summoner.id) || !summonerIds.Add(summoner.id))
                    errors.Add("Duplicate or empty summoner id.");
                if (summoner == null)
                    continue;
                if (summoner.master != null && !masters.Add(summoner.master))
                    errors.Add("Two summoner records share the same master.");
                if (summoner.fieldCapacity < 1 || summoner.contractSlotCapacity < 1)
                    errors.Add("Summoner " + summoner.id + " has a capacity below 1.");
                if (summoner.slots.Count > summoner.contractSlotCapacity)
                    errors.Add("Summoner " + summoner.id + " has more slots than contract capacity.");
                if (FieldLoad(summoner.id) > summoner.fieldCapacity)
                    errors.Add("Summoner " + summoner.id + " field load exceeds field capacity.");

                for (int slotIndex = 0; slotIndex < summoner.slots.Count; slotIndex++)
                {
                    ContractSlotRecord slot = summoner.slots[slotIndex];
                    if (slot == null || string.IsNullOrEmpty(slot.id) || !slotIds.Add(slot.id))
                    {
                        errors.Add("Duplicate or empty contract slot id.");
                        continue;
                    }
                    CheckSlotMember(summoner, slot, slot.primaryBeastId, true, slottedBeasts, errors);
                    HashSet<string> reserves = new HashSet<string>();
                    if (slot.reserveBeastIds.Count > SummonerLimits.MaxReservesPerSlot)
                        errors.Add("Slot " + slot.id + " has more than " + SummonerLimits.MaxReservesPerSlot + " reserves.");
                    for (int r = 0; r < slot.reserveBeastIds.Count; r++)
                    {
                        string reserveId = slot.reserveBeastIds[r];
                        if (string.IsNullOrEmpty(reserveId) || !reserves.Add(reserveId))
                            errors.Add("Slot " + slot.id + " has a duplicate reserve id.");
                        if (reserveId == slot.primaryBeastId)
                            errors.Add("Slot " + slot.id + " lists its primary beast as a reserve.");
                        CheckSlotMember(summoner, slot, reserveId, false, slottedBeasts, errors);
                    }
                }

                for (int rewardIndex = 0; rewardIndex < summoner.pendingRewards.Count; rewardIndex++)
                {
                    PendingRewardRecord reward = summoner.pendingRewards[rewardIndex];
                    if (reward == null || string.IsNullOrEmpty(reward.id) || !rewardIds.Add(reward.id))
                    {
                        errors.Add("Duplicate or empty pending reward id.");
                        continue;
                    }
                    string subjectError;
                    if (!RewardSubjectMatches(summoner, reward.subjectKind, reward.subjectId, out subjectError))
                        errors.Add("Reward " + reward.id + " " + subjectError);
                    if (reward.choice == null)
                    {
                        errors.Add("Reward " + reward.id + " has no choice state.");
                        continue;
                    }
                    int optionCount = reward.choice.optionIds == null ? 0 : reward.choice.optionIds.Count;
                    if (reward.choice.selectedIndex < -1 || reward.choice.selectedIndex >= optionCount)
                        errors.Add("Reward " + reward.id + " selection is outside its saved options.");
                }
            }

            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord beast = beasts[i];
                if (beast == null || string.IsNullOrEmpty(beast.id) || !beastIds.Add(beast.id))
                    errors.Add("Duplicate or empty beast id.");
                if (beast == null)
                    continue;
                if (string.IsNullOrEmpty(beast.ownerId) || FindSummoner(beast.ownerId) == null)
                    errors.Add("Beast " + beast.id + " has no summoner owner.");
                if (beast.occupySlots != 1 && beast.occupySlots != 2)
                    errors.Add("Beast " + beast.id + " occupy slots must be 1 or 2.");
                if (beast.tier < SummonerLimits.MinTier || beast.tier > SummonerLimits.MaxTier)
                    errors.Add("Beast " + beast.id + " tier is outside 1 to 4.");
                if (beast.tierLevel < SummonerLimits.MinTierLevel || beast.tierLevel > SummonerLimits.MaxTierLevel)
                    errors.Add("Beast " + beast.id + " tier level is outside 1 to 40.");
                if (beast.presence == BeastPresence.InRealm)
                {
                    Pawn held = soulRealm.PawnFor(beast.id);
                    if (beast.pawn != null && held != beast.pawn)
                        errors.Add("Beast " + beast.id + " realm pawn does not match the soul-realm entry.");
                    if (held == null && beast.pawn != null)
                        errors.Add("Beast " + beast.id + " points at a pawn the soul realm does not own.");
                }
                if (beast.presence == BeastPresence.OnField && soulRealm.HoldsPawn(beast.pawn))
                    errors.Add("Beast " + beast.id + " is on field while the soul realm also owns its pawn.");
            }

            HashSet<string> realmIds = new HashSet<string>();
            HashSet<Pawn> realmPawns = new HashSet<Pawn>();
            for (int i = 0; i < soulRealm.entries.Count; i++)
            {
                SoulRealmEntry entry = soulRealm.entries[i];
                if (entry == null || string.IsNullOrEmpty(entry.beastId) || !realmIds.Add(entry.beastId))
                    errors.Add("Duplicate or empty soul-realm beast id.");
                if (entry == null)
                    continue;
                Pawn held = entry.HeldPawn;
                if (held == null)
                    errors.Add("Soul-realm entry " + entry.beastId + " holds no pawn.");
                if (held != null && !realmPawns.Add(held))
                    errors.Add("Two soul-realm entries hold the same pawn.");
                if (held != null && IsExternallyOwned(held))
                    errors.Add("Soul realm holds a pawn that the map or world also owns.");
                BeastRecord recorded = string.IsNullOrEmpty(entry.beastId) ? null : FindBeast(entry.beastId);
                if (recorded == null)
                    errors.Add("Soul-realm entry " + entry.beastId + " has no beast record.");
                else if (held != null && (recorded.pawn != held || recorded.presence != BeastPresence.InRealm))
                    errors.Add("Soul-realm entry " + entry.beastId + " does not match its beast record.");
            }

            return errors;
        }

        public string ConsistencyReport()
        {
            List<string> errors = CheckConsistency();
            if (errors.Count == 0)
                return "";
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < errors.Count; i++)
            {
                if (i > 0)
                    builder.AppendLine();
                builder.Append(errors[i]);
            }
            return builder.ToString();
        }

        private void FinishLoad()
        {
            EnsureCollections();
            soulRealm.Attach(this);
            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord beast = beasts[i];
                if (beast != null && beast.presence == BeastPresence.InRealm)
                    beast.pawn = soulRealm.PawnFor(beast.id);
            }
            AdvanceCountersPastExistingIds();
            RebuildRosterCache();
            SyncComps();
        }

        private void AdvanceCountersPastExistingIds()
        {
            for (int i = 0; i < summoners.Count; i++)
            {
                if (summoners[i] == null)
                    continue;
                nextSummoner = Advance(nextSummoner, summoners[i].id, "summoner-");
                for (int slotIndex = 0; slotIndex < summoners[i].slots.Count; slotIndex++)
                {
                    ContractSlotRecord slot = summoners[i].slots[slotIndex];
                    if (slot != null)
                        nextSlot = Advance(nextSlot, slot.id, "slot-");
                }
                for (int rewardIndex = 0; rewardIndex < summoners[i].pendingRewards.Count; rewardIndex++)
                {
                    PendingRewardRecord reward = summoners[i].pendingRewards[rewardIndex];
                    if (reward != null)
                        nextReward = Advance(nextReward, reward.id, "reward-");
                }
            }
            for (int i = 0; i < beasts.Count; i++)
            {
                if (beasts[i] != null)
                    nextBeast = Advance(nextBeast, beasts[i].id, "beast-");
            }
        }

        private static int Advance(int counter, string id, string prefix)
        {
            if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix))
                return counter;
            int number;
            if (int.TryParse(id.Substring(prefix.Length), out number) && number >= counter)
                return number + 1;
            return counter;
        }

        private void SyncComps()
        {
            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord beast = beasts[i];
                if (beast == null || beast.pawn == null)
                    continue;
                SyncLink(beast);
            }
        }

        private static void SyncLink(BeastRecord beast)
        {
            var comp = beast?.pawn?.GetComp<CompSummonerBeastLink>();
            if (comp != null) comp.beastId = beast.id;
        }

        private bool RewardSubjectMatches(SummonerRecord summoner, RewardSubjectKind kind, string subjectId, out string error)
        {
            error = null;
            if (summoner == null)
            {
                error = "Unknown summoner.";
                return false;
            }
            if (string.IsNullOrEmpty(subjectId))
            {
                error = "has no subject.";
                return false;
            }
            if (kind == RewardSubjectKind.Summoner)
            {
                if (subjectId != summoner.id)
                {
                    error = "targets a different summoner.";
                    return false;
                }
                return true;
            }
            if (kind == RewardSubjectKind.Beast)
            {
                BeastRecord beast = FindBeast(subjectId);
                if (beast == null)
                {
                    error = "targets a missing beast.";
                    return false;
                }
                if (beast.ownerId != summoner.id)
                {
                    error = "targets a beast owned by another summoner.";
                    return false;
                }
                return true;
            }
            error = "has an unknown subject kind.";
            return false;
        }

        private bool SameBoundPawn(BeastRecord beast, Pawn pawn, out string error)
        {
            error = null;
            if (beast.pawn != null && beast.pawn != pawn)
            {
                error = "This beast is already bound to a different pawn.";
                return false;
            }
            Pawn held = soulRealm.PawnFor(beast.id);
            if (held != null && held != pawn)
            {
                error = "This beast id already owns a different soul-realm pawn.";
                return false;
            }
            return true;
        }

        private static bool IsExternallyOwned(Pawn pawn)
        {
            if (pawn == null)
                return false;
            if (pawn.Destroyed || pawn.Spawned || pawn.SpawnedOrAnyParentSpawned)
                return true;
            Verse.Game game = Verse.Current.Game;
            if (game == null || game.World == null)
                return false;
            return WorldPawnsUtility.IsWorldPawn(pawn);
        }

        private bool FieldLoadFits(BeastRecord extra, out string error)
        {
            error = null;
            for (int s = 0; s < summoners.Count; s++)
            {
                SummonerRecord summoner = summoners[s];
                if (summoner == null)
                    continue;
                int load = FieldLoad(summoner.id);
                if (extra != null && extra.ownerId == summoner.id && extra.presence != BeastPresence.OnField)
                    load += extra.occupySlots;
                if (load > summoner.fieldCapacity)
                {
                    error = "Field load would exceed field capacity. Reserves that stay in the realm are not counted.";
                    return false;
                }
            }
            return true;
        }

        private bool TryGetAssignment(string slotId, string beastId, out SummonerRecord summoner, out ContractSlotRecord slot, out BeastRecord beast, out string error)
        {
            summoner = null;
            slot = null;
            beast = FindBeast(beastId);
            error = null;
            if (beast == null)
            {
                error = "Unknown beast.";
                return false;
            }
            slot = FindSlot(slotId);
            if (slot == null)
            {
                error = "Unknown contract slot.";
                return false;
            }
            summoner = FindSummonerOwningSlot(slotId);
            if (summoner == null)
            {
                error = "Contract slot has no summoner.";
                return false;
            }
            if (beast.ownerId != summoner.id)
            {
                error = "A beast cannot belong to two summoners or another summoner's slot.";
                return false;
            }
            return true;
        }

        private bool CanJoinSlot(SummonerRecord summoner, ContractSlotRecord slot, BeastRecord beast, out string error)
        {
            error = null;
            if (FindSlotContaining(beast.id) != null)
            {
                error = "That beast is already in a contract slot.";
                return false;
            }
            if (BloodlinesConflict(slot.bloodlineId, beast.bloodlineId))
            {
                error = "Reserve and primary beasts in one slot must share its bloodline id when both ids are set.";
                return false;
            }
            if (summoner == null)
            {
                error = "Contract slot has no summoner.";
                return false;
            }
            return true;
        }

        private void CheckSlotMember(SummonerRecord summoner, ContractSlotRecord slot, string beastId, bool primary, HashSet<string> slottedBeasts, List<string> errors)
        {
            if (string.IsNullOrEmpty(beastId))
                return;
            if (!slottedBeasts.Add(beastId))
                errors.Add("Beast " + beastId + " is listed in more than one primary or reserve place.");
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
            {
                errors.Add("Slot " + slot.id + " names missing beast " + beastId + ".");
                return;
            }
            if (beast.ownerId != summoner.id)
                errors.Add("Beast " + beastId + " is slotted for a summoner it does not belong to.");
            if (BloodlinesConflict(slot.bloodlineId, beast.bloodlineId))
                errors.Add("Beast " + beastId + " bloodline conflicts with slot " + slot.id + ".");
            if (primary && slot.reserveBeastIds.Contains(beastId))
                errors.Add("Beast " + beastId + " is both primary and reserve.");
        }

        private static bool BloodlinesConflict(string left, string right)
        {
            return !string.IsNullOrEmpty(left) && !string.IsNullOrEmpty(right) && left != right;
        }

        private bool ReserveBloodlineConflicts(ContractSlotRecord slot, string bloodlineId)
        {
            for (int i = 0; i < slot.reserveBeastIds.Count; i++)
            {
                if (BloodlinesConflict(BloodlineOf(slot.reserveBeastIds[i]), bloodlineId))
                    return true;
            }
            return false;
        }

        private string BloodlineOf(string beastId)
        {
            BeastRecord beast = FindBeast(beastId);
            return beast == null ? "" : beast.bloodlineId;
        }

        private bool PawnIsBoundElsewhere(Pawn pawn, string beastId)
        {
            for (int i = 0; i < beasts.Count; i++)
            {
                BeastRecord beast = beasts[i];
                if (beast != null && beast.id != beastId && beast.pawn == pawn)
                    return true;
            }
            for (int i = 0; i < soulRealm.entries.Count; i++)
            {
                SoulRealmEntry entry = soulRealm.entries[i];
                if (entry != null && entry.beastId != beastId && entry.HeldPawn == pawn)
                    return true;
            }
            return false;
        }

        private void AssignCachedRole(string beastId, string slotId, BeastRosterRole role, HashSet<string> claimed)
        {
            if (string.IsNullOrEmpty(beastId) || !claimed.Add(beastId))
                return;
            BeastRecord beast = FindBeast(beastId);
            if (beast == null)
                return;
            beast.contractSlotId = slotId;
            beast.role = role;
        }

        private ContractSlotRecord CreateSlot()
        {
            ContractSlotRecord slot = new ContractSlotRecord();
            slot.id = TakeId(ref nextSlot, "slot");
            return slot;
        }

        private static string TakeId(ref int counter, string prefix)
        {
            string id = prefix + "-" + counter.ToString();
            counter++;
            return id;
        }

        private ContractSlotRecord FindSlot(string slotId)
        {
            for (int s = 0; s < summoners.Count; s++)
            {
                SummonerRecord summoner = summoners[s];
                if (summoner == null)
                    continue;
                for (int i = 0; i < summoner.slots.Count; i++)
                {
                    if (summoner.slots[i] != null && summoner.slots[i].id == slotId)
                        return summoner.slots[i];
                }
            }
            return null;
        }

        private SummonerRecord FindSummonerOwningSlot(string slotId)
        {
            for (int s = 0; s < summoners.Count; s++)
            {
                SummonerRecord summoner = summoners[s];
                if (summoner == null)
                    continue;
                for (int i = 0; i < summoner.slots.Count; i++)
                {
                    if (summoner.slots[i] != null && summoner.slots[i].id == slotId)
                        return summoner;
                }
            }
            return null;
        }

        private ContractSlotRecord FindSlotContaining(string beastId)
        {
            for (int s = 0; s < summoners.Count; s++)
            {
                SummonerRecord summoner = summoners[s];
                if (summoner == null)
                    continue;
                for (int i = 0; i < summoner.slots.Count; i++)
                {
                    ContractSlotRecord slot = summoner.slots[i];
                    if (slot == null)
                        continue;
                    if (slot.primaryBeastId == beastId || slot.reserveBeastIds.Contains(beastId))
                        return slot;
                }
            }
            return null;
        }

        private PendingRewardRecord FindReward(string rewardId)
        {
            for (int s = 0; s < summoners.Count; s++)
            {
                SummonerRecord summoner = summoners[s];
                if (summoner == null)
                    continue;
                for (int i = 0; i < summoner.pendingRewards.Count; i++)
                {
                    if (summoner.pendingRewards[i] != null && summoner.pendingRewards[i].id == rewardId)
                        return summoner.pendingRewards[i];
                }
            }
            return null;
        }

        private bool TryFindBeast(string beastId, out BeastRecord beast, out string error)
        {
            beast = FindBeast(beastId);
            error = beast == null ? "Unknown beast." : null;
            return beast != null;
        }

        private bool TryUpsert<T>(string beastId, string key, int value, out string error, System.Func<BeastRecord, List<T>> listOf, System.Func<string, T> create, System.Func<T, string> keyOf, System.Action<T, int> setValue)
        {
            error = null;
            if (!TryFindBeast(beastId, out BeastRecord beast, out error))
                return false;
            if (string.IsNullOrEmpty(key) || value < 0)
            {
                error = "Records need an id and a non-negative value.";
                return false;
            }
            List<T> list = listOf(beast);
            T row = default(T);
            bool found = false;
            for (int i = 0; i < list.Count; i++)
            {
                if (keyOf(list[i]) == key)
                {
                    row = list[i];
                    found = true;
                    break;
                }
            }
            if (value == 0)
            {
                if (found)
                    list.Remove(row);
                return true;
            }
            if (!found)
            {
                row = create(key);
                list.Add(row);
            }
            setValue(row, value);
            return true;
        }

        private void EnsureCollections()
        {
            if (summoners == null)
                summoners = new List<SummonerRecord>();
            if (beasts == null)
                beasts = new List<BeastRecord>();
            if (soulRealm == null)
                soulRealm = new SoulRealmContainer();
            if (nextSummoner < 1)
                nextSummoner = 1;
            if (nextSlot < 1)
                nextSlot = 1;
            if (nextBeast < 1)
                nextBeast = 1;
            if (nextReward < 1)
                nextReward = 1;
        }
    }
}
