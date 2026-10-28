using HarmonyLib;
using Verse;

namespace RimMod.Summoner
{
    // Large-version entry point. Future gameplay follows the v0.10.2 design.
    public sealed class SummonerMod : Mod
    {
        public SummonerMod(ModContentPack content) : base(content)
        {
            new Harmony("rimmod.summoner.gameplay").PatchAll(typeof(SummonerMod).Assembly);
        }
    }
}
