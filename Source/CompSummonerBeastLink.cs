using Verse;

namespace RimMod.Summoner
{
    public class CompProperties_SummonerBeastLink : CompProperties
    {
        public CompProperties_SummonerBeastLink()
        {
            compClass = typeof(CompSummonerBeastLink);
        }
    }

    // Foreign key only. Growth, roster, and cooldowns live on SummonerArchive.
    public class CompSummonerBeastLink : ThingComp
    {
        public string beastId = "";

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref beastId, "beastId", "");
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (parent is Pawn pawn)
                SummonerArchive.Current?.NoteSpawnedLink(pawn, beastId);
        }

        public BeastRecord RecordOrNull()
        {
            return SummonerArchive.Current?.FindBeast(beastId);
        }
    }
}
