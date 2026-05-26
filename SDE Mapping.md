## Official JSONL -> Database Mapping

Legend:
- `1:1` = one JSONL file to one DB table
- `1:many` = one JSONL file to multiple DB tables
- `composed` = file contributes with other files/derived logic

| JSONL file | DB table(s) | Type | Purpose |
|---|---|---|---|
| `_sde.jsonl` | `_hki_sde_build` | `1:1` | SDE build/version metadata |
| `categories.jsonl` | `invCategories` | `1:1` | Item categories |
| `groups.jsonl` | `invGroups` | `1:1` | Item groups |
| `types.jsonl` | `invTypes`, `invMetaTypes`, `invVolumes` | `1:many` | Core type definitions + meta/volume helpers |
| `marketGroups.jsonl` | `invMarketGroups` | `1:1` | Market tree |
| `metaGroups.jsonl` | `invMetaGroups` | `1:1` | Meta group labels |
| `dogmaUnits.jsonl` | `eveUnits` | `1:1` | Dogma units |
| `dogmaAttributeCategories.jsonl` | `dgmAttributeCategories` | `1:1` | Dogma attribute categories |
| `dogmaAttributes.jsonl` | `dgmAttributeTypes` | `1:1` | Dogma attributes |
| `dogmaEffects.jsonl` | `dgmEffects` | `1:1` | Dogma effects |
| `typeDogma.jsonl` | `dgmTypeAttributes`, `dgmTypeEffects` | `1:many` | Type dogma assignments |
| `typeMaterials.jsonl` | `invTypeMaterials` | `1:1` | Type material lists |
| `blueprints.jsonl` | `industryBlueprints`, `industryActivity`, `industryActivityMaterials`, `industryActivityProducts`, `industryActivityProbabilities`, `industryActivitySkills`, `invTypeReactions` | `1:many` | Blueprint and industry activity graph |
| `planetSchematics.jsonl` | `planetSchematics`, `planetSchematicsPinMap`, `planetSchematicsTypeMap` | `1:many` | PI schematic model |
| `graphics.jsonl` | `eveGraphics` | `1:1` | Graphics references |
| `icons.jsonl` | `eveIcons` | `1:1` | Icon references |
| `factions.jsonl` | `chrFactions` | `1:1` | Faction data |
| `races.jsonl` | `chrRaces` | `1:1` | Race data |
| `bloodlines.jsonl` | `chrBloodlines` | `1:1` | Bloodline data |
| `ancestries.jsonl` | `chrAncestries` | `1:1` | Ancestry data |
| `characterAttributes.jsonl` | `chrAttributes` | `1:1` | Character attributes |
| `corporationActivities.jsonl` | `crpActivities` | `1:1` | NPC corp activity dictionary |
| `npcCorporationDivisions.jsonl` | `crpNPCDivisions` | `1:1` | NPC corp division dictionary |
| `npcCorporations.jsonl` | `crpNPCCorporations`, `crpNPCCorporationTrades`, `crpNPCCorporationDivisions` | `1:many` | NPC corp core + trade + division membership |
| `stationOperations.jsonl` | `staOperations`, `staOperationServices` | `1:many` | Station operation definitions |
| `stationServices.jsonl` | `staServices` | `1:1` | Station service dictionary |
| `npcStations.jsonl` | `staStations`, `mapDenormalize`, `invNames`, `invPositions`, `invUniqueNames` | `composed` | NPC station records and location/name composition |
| `mapRegions.jsonl` | `mapRegions`, `mapUniverse`, `mapDenormalize`, `mapLocationScenes`, `mapLocationWormholeClasses` | `composed` | Region geometry/topology |
| `mapConstellations.jsonl` | `mapConstellations`, `mapDenormalize`, `mapLocationWormholeClasses` | `composed` | Constellation geometry/topology |
| `mapSolarSystems.jsonl` | `mapSolarSystems`, `mapDenormalize`, `mapJumps`, `mapRegionJumps`, `mapConstellationJumps`, `mapSolarSystemJumps`, `mapLocationWormholeClasses` | `composed` | Solar system topology and jump composition |
| `mapStargates.jsonl` | `mapJumps`, `mapRegionJumps`, `mapConstellationJumps`, `mapSolarSystemJumps` | `composed` | Stargate edge graph |
| `landmarks.jsonl` | `mapLandmarks` | `1:1` | Landmark points |
| `mapPlanets.jsonl` | `mapDenormalize`, `mapCelestialGraphics`, `mapCelestialStatistics` | `1:many` | Planet entities |
| `mapMoons.jsonl` | `mapDenormalize`, `mapCelestialGraphics`, `mapCelestialStatistics` | `1:many` | Moon entities |
| `mapAsteroidBelts.jsonl` | `mapDenormalize`, `mapCelestialStatistics` | `1:many` | Belt entities |
| `mapStars.jsonl` | `mapDenormalize`, `mapCelestialStatistics` | `1:many` | Star entities |
| `mapSecondarySuns.jsonl` | `mapDenormalize` | `1:1` | Secondary sun entities |
| `contrabandTypes.jsonl` | `invContrabandTypes` | `1:1` | Contraband rules by faction/type |
| `controlTowerResources.jsonl` | `invControlTowerResources`, `invControlTowerResourcePurposes` | `1:many` | Tower resource requirements and purpose dictionary |
| `translationLanguages.jsonl` | `trnTranslationLanguages` | `1:1` | Language metadata |
| `agentTypes.jsonl` | `agtAgentTypes` | `1:1` | Agent type dictionary |
| `agentsInSpace.jsonl` | `agtAgentsInSpace` | `1:1` | Agent placement records |
| `npcCharacters.jsonl` | `agtAgents`, `agtResearchAgents`, `crpNPCCorporationResearchFields` | `1:many` | NPC character/agent data |
| `certificates.jsonl` | `certCerts`, `certSkills` | `1:many` | Certificates and required skills |
| `masteries.jsonl` | `certMasteries` | `1:1` | Type mastery mappings |
| `skins.jsonl` | `skins`, `skinShip` | `1:many` | Skin core + type applicability |
| `skinMaterials.jsonl` | `skinMaterials` | `1:1` | Skin material definitions |
| `skinLicenses.jsonl` | `skinLicense` | `1:1` | Skin licenses |
| `cloneGrades.jsonl` | `chrCloneGrades`, `chrCloneGradeSkills` | `1:many` | Clone grade + included skills |
| `compressibleTypes.jsonl` | `invCompressibleTypes` | `1:1` | Compression mapping |
| `dbuffCollections.jsonl` | `dgmBuffCollections` | `1:1` | Buff collection definitions (`rawJson` preserved) |
| `dynamicItemAttributes.jsonl` | `dgmDynamicItemAttributes` | `1:1` | Dynamic attribute payloads (`rawJson`) |
| `freelanceJobSchemas.jsonl` | `frtFreelanceJobSchemas` | `1:1` | Freelance schema payload (`rawJson`) |
| `mercenaryTacticalOperations.jsonl` | `mercenaryTacticalOperations` | `1:1` | Mercenary tactical operations (`rawJson` + key fields) |
| `planetResources.jsonl` | `planetResources` | `1:1` | Planet resource properties (`rawJson` + key fields) |
| `sovereigntyUpgrades.jsonl` | `sovSovereigntyUpgrades` | `1:1` | Sov upgrade properties (`rawJson` + key fields) |
| `typeBonus.jsonl` | `invTraits` | `1:1` | Type trait/bonus payloads (`rawJson`) |

## Project-Specific Metadata Tables
- `_hki_import_metadata`: import execution metadata (timing, warnings/errors, row counts).
- `_hki_sde_build`: imported SDE build metadata from `_sde.jsonl`.