using System.Collections.Generic;
using Verse;

namespace RimMod.Summoner
{
    // One detached pawn, paired with a stable beast id.
    // ThingOwner is the entity owner. The beast record only points here after load.
    public sealed class SoulRealmEntry : IExposable, IThingHolder
    {
        public string beastId = "";
        public SoulRealmContainer container;
        private ThingOwner<Pawn> pawns;

        public SoulRealmEntry()
        {
            EnsureOwner();
        }

        public Pawn HeldPawn
        {
            get
            {
                EnsureOwner();
                List<Pawn> list = pawns.InnerListForReading;
                return list.Count == 0 ? null : list[0];
            }
        }

        public IThingHolder ParentHolder => container;

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (outChildren == null)
                return;
            EnsureOwner();
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, pawns);
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            EnsureOwner();
            return pawns;
        }

        public bool TryHold(Pawn pawn, out string error)
        {
            error = null;
            EnsureOwner();
            if (pawn == null)
            {
                error = "Cannot hold a null pawn.";
                return false;
            }
            Pawn current = HeldPawn;
            if (current == pawn)
                return true;
            if (current != null)
            {
                error = "This soul-realm entry already holds a pawn.";
                return false;
            }
            if (!pawns.TryAdd(pawn, false))
            {
                error = "The pawn already has an owner and was not moved.";
                return false;
            }
            return true;
        }

        public void Release(Pawn pawn)
        {
            if (pawn == null || pawns == null || !pawns.Contains(pawn))
                return;
            pawns.Remove(pawn);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref beastId, "beastId", "");
            Scribe_Deep.Look(ref pawns, "pawns", this, false, LookMode.Deep, false);
            EnsureOwner();
        }

        private void EnsureOwner()
        {
            if (pawns == null)
                pawns = new ThingOwner<Pawn>(this, false, LookMode.Deep, false);
        }
    }

    // Game-level owner of in-realm pawn entities. Field pawns are not stored here.
    public sealed class SoulRealmContainer : IExposable, IThingHolder
    {
        public SummonerArchive archive;
        public List<SoulRealmEntry> entries = new List<SoulRealmEntry>();

        public IThingHolder ParentHolder => archive;

        public bool HasAnyPawn
        {
            get
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    if (entries[i] != null && entries[i].HeldPawn != null)
                        return true;
                }
                return false;
            }
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            if (outChildren == null)
                return;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null)
                    outChildren.Add(entries[i]);
            }
        }

        public ThingOwner GetDirectlyHeldThings() => null;

        public void Attach(SummonerArchive owner)
        {
            archive = owner;
            if (entries == null)
                entries = new List<SoulRealmEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null)
                    entries[i].container = this;
            }
        }

        public SoulRealmEntry FindEntry(string beastId)
        {
            if (string.IsNullOrEmpty(beastId) || entries == null)
                return null;
            for (int i = 0; i < entries.Count; i++)
            {
                SoulRealmEntry entry = entries[i];
                if (entry != null && entry.beastId == beastId)
                    return entry;
            }
            return null;
        }

        public Pawn PawnFor(string beastId)
        {
            SoulRealmEntry entry = FindEntry(beastId);
            return entry == null ? null : entry.HeldPawn;
        }

        // The entry joins the container only after the pawn is actually held.
        public bool TryHoldForBeast(string beastId, Pawn pawn, out string error)
        {
            error = null;
            if (string.IsNullOrEmpty(beastId))
            {
                error = "A soul-realm entry needs a beast id.";
                return false;
            }
            if (entries == null)
                entries = new List<SoulRealmEntry>();
            SoulRealmEntry entry = FindEntry(beastId);
            bool created = false;
            if (entry == null)
            {
                entry = new SoulRealmEntry();
                entry.beastId = beastId;
                entry.container = this;
                created = true;
            }
            if (!entry.TryHold(pawn, out error))
                return false;
            if (created)
                entries.Add(entry);
            return true;
        }

        public bool HoldsPawn(Pawn pawn)
        {
            if (pawn == null)
                return false;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null && entries[i].HeldPawn == pawn)
                    return true;
            }
            return false;
        }

        public void RemoveEntry(string beastId)
        {
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (entries[i] != null && entries[i].beastId == beastId)
                    entries.RemoveAt(i);
            }
        }

        public void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "entries", LookMode.Deep);
            if (entries == null)
                entries = new List<SoulRealmEntry>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] != null)
                    entries[i].container = this;
            }
        }
    }
}
