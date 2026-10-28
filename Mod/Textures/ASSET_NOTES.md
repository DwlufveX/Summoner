# Art sources and placeholders

UI/Summoner/*.png are original geometric placeholders authored by Tools/Create-Placeholders.ps1 for this project. They may be replaced with final art later.

The druid spirit wolf uses original directional art under `Things/Pawn/Animal/SpiritWolf/`. The source sheet is kept under `ArtSource/SpiritWolf/` and the deterministic crop/rescale step is `Tools/Build-SpiritWolfTextures.ps1`. The design is a charcoal wolf with emerald eyes, soul markings, and a restrained green aura.

SpiritBear and SpiritHawk under `Things/Pawn/Summoner/` are temporary single-view cutouts. Their references came from the user-supplied `docs/References/SummonerClass.zip`. The originals had a baked checkerboard instead of real transparency; they are preserved as `ArtSource/Reference/*-original.png`. Codex used the built-in imagegen tool on 2026-10-14 to remove the background. The current PNGs have genuine alpha and were visually inspected. They are not the final four-direction art set.

The rescue cage and wind projectile temporarily reference vanilla graphics; those graphics are not bundled.

No RimWorld of Magic or other third-party art is included.
