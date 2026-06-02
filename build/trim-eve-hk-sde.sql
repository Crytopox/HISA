-- Trim eve-hk-sde.db for HISA distribution.
--
-- This script removes unused tables and prunes retained tables to the rows HISA
-- reads. It intentionally does not remove columns from retained tables.
--
-- Run this against a copy of src/Hisa.App/Data/eve-hk-sde.db:
--   sqlite3 eve-hk-sde-trimmed.db < build/trim-eve-hk-sde.sql
--
-- Tables retained for HISA:
--   invGroups
--   invTypes
--   mapCelestialStatistics
--   mapConstellations
--   mapDenormalize
--   mapRegionJumps
--   mapRegions
--   mapSolarSystemJumps
--   mapSolarSystems

PRAGMA foreign_keys = OFF;

DROP TABLE IF EXISTS _hki_import_metadata;
DROP TABLE IF EXISTS _hki_sde_build;
DROP TABLE IF EXISTS agtAgentTypes;
DROP TABLE IF EXISTS agtAgents;
DROP TABLE IF EXISTS agtAgentsInSpace;
DROP TABLE IF EXISTS agtResearchAgents;
DROP TABLE IF EXISTS certCerts;
DROP TABLE IF EXISTS certMasteries;
DROP TABLE IF EXISTS certSkills;
DROP TABLE IF EXISTS chrAncestries;
DROP TABLE IF EXISTS chrAttributes;
DROP TABLE IF EXISTS chrBloodlines;
DROP TABLE IF EXISTS chrCloneGradeSkills;
DROP TABLE IF EXISTS chrCloneGrades;
DROP TABLE IF EXISTS chrFactions;
DROP TABLE IF EXISTS chrRaces;
DROP TABLE IF EXISTS crpActivities;
DROP TABLE IF EXISTS crpNPCCorporationDivisions;
DROP TABLE IF EXISTS crpNPCCorporationResearchFields;
DROP TABLE IF EXISTS crpNPCCorporationTrades;
DROP TABLE IF EXISTS crpNPCCorporations;
DROP TABLE IF EXISTS crpNPCDivisions;
DROP TABLE IF EXISTS dgmAttributeCategories;
DROP TABLE IF EXISTS dgmAttributeTypes;
DROP TABLE IF EXISTS dgmBuffCollections;
DROP TABLE IF EXISTS dgmDynamicItemAttributes;
DROP TABLE IF EXISTS dgmEffects;
DROP TABLE IF EXISTS dgmTypeAttributes;
DROP TABLE IF EXISTS dgmTypeEffects;
DROP TABLE IF EXISTS eveGraphics;
DROP TABLE IF EXISTS eveIcons;
DROP TABLE IF EXISTS eveUnits;
DROP TABLE IF EXISTS frtFreelanceJobSchemas;
DROP TABLE IF EXISTS industryActivity;
DROP TABLE IF EXISTS industryActivityMaterials;
DROP TABLE IF EXISTS industryActivityProbabilities;
DROP TABLE IF EXISTS industryActivityProducts;
DROP TABLE IF EXISTS industryActivitySkills;
DROP TABLE IF EXISTS industryBlueprints;
DROP TABLE IF EXISTS invCategories;
DROP TABLE IF EXISTS invCompressibleTypes;
DROP TABLE IF EXISTS invContrabandTypes;
DROP TABLE IF EXISTS invControlTowerResourcePurposes;
DROP TABLE IF EXISTS invControlTowerResources;
DROP TABLE IF EXISTS invMarketGroups;
DROP TABLE IF EXISTS invMetaGroups;
DROP TABLE IF EXISTS invMetaTypes;
DROP TABLE IF EXISTS invNames;
DROP TABLE IF EXISTS invPositions;
DROP TABLE IF EXISTS invTraits;
DROP TABLE IF EXISTS invTypeMaterials;
DROP TABLE IF EXISTS invTypeReactions;
DROP TABLE IF EXISTS invUniqueNames;
DROP TABLE IF EXISTS invVolumes;
DROP TABLE IF EXISTS mapCelestialGraphics;
DROP TABLE IF EXISTS mapConstellationJumps;
DROP TABLE IF EXISTS mapJumps;
DROP TABLE IF EXISTS mapLandmarks;
DROP TABLE IF EXISTS mapLocationScenes;
DROP TABLE IF EXISTS mapLocationWormholeClasses;
DROP TABLE IF EXISTS mapUniverse;
DROP TABLE IF EXISTS mercenaryTacticalOperations;
DROP TABLE IF EXISTS planetResources;
DROP TABLE IF EXISTS planetSchematics;
DROP TABLE IF EXISTS planetSchematicsPinMap;
DROP TABLE IF EXISTS planetSchematicsTypeMap;
DROP TABLE IF EXISTS skinLicense;
DROP TABLE IF EXISTS skinMaterials;
DROP TABLE IF EXISTS skinShip;
DROP TABLE IF EXISTS skins;
DROP TABLE IF EXISTS sovSovereigntyUpgrades;
DROP TABLE IF EXISTS staOperationServices;
DROP TABLE IF EXISTS staOperations;
DROP TABLE IF EXISTS staServices;
DROP TABLE IF EXISTS staStations;
DROP TABLE IF EXISTS trnTranslationLanguages;

-- Preserve the star records used by map overlays and the category-6 ship records
-- used by intel parsing before deleting unrelated retained-table rows.
DROP TABLE IF EXISTS temp.hisa_keep_star_item_ids;
DROP TABLE IF EXISTS temp.hisa_keep_inv_type_ids;
DROP TABLE IF EXISTS temp.hisa_keep_inv_group_ids;

CREATE TEMP TABLE hisa_keep_star_item_ids (
    itemID INTEGER PRIMARY KEY
);

INSERT INTO hisa_keep_star_item_ids(itemID)
SELECT DISTINCT itemID
FROM mapDenormalize
WHERE groupID = 6
  AND itemID IS NOT NULL;

CREATE TEMP TABLE hisa_keep_inv_type_ids (
    typeID INTEGER PRIMARY KEY
);

INSERT OR IGNORE INTO hisa_keep_inv_type_ids(typeID)
SELECT DISTINCT typeID
FROM mapDenormalize
WHERE groupID = 6
  AND typeID IS NOT NULL;

INSERT OR IGNORE INTO hisa_keep_inv_type_ids(typeID)
SELECT t.typeID
FROM invTypes t
INNER JOIN invGroups g ON g.groupID = t.groupID
WHERE g.categoryID = 6;

CREATE TEMP TABLE hisa_keep_inv_group_ids (
    groupID INTEGER PRIMARY KEY
);

INSERT OR IGNORE INTO hisa_keep_inv_group_ids(groupID)
SELECT DISTINCT t.groupID
FROM invTypes t
INNER JOIN hisa_keep_inv_type_ids keep ON keep.typeID = t.typeID;

DELETE FROM mapCelestialStatistics
WHERE NOT EXISTS (
    SELECT 1
    FROM hisa_keep_star_item_ids keep
    WHERE keep.itemID = mapCelestialStatistics.celestialID
);

DELETE FROM mapDenormalize
WHERE groupID <> 6;

DELETE FROM invTypes
WHERE NOT EXISTS (
    SELECT 1
    FROM hisa_keep_inv_type_ids keep
    WHERE keep.typeID = invTypes.typeID
);

DELETE FROM invGroups
WHERE NOT EXISTS (
    SELECT 1
    FROM hisa_keep_inv_group_ids keep
    WHERE keep.groupID = invGroups.groupID
);

DROP TABLE IF EXISTS temp.hisa_keep_star_item_ids;
DROP TABLE IF EXISTS temp.hisa_keep_inv_type_ids;
DROP TABLE IF EXISTS temp.hisa_keep_inv_group_ids;

PRAGMA optimize;

-- Verification output: expect the 9 retained HISA tables plus SQLite-maintained
-- internal tables such as sqlite_sequence, sqlite_stat1, or sqlite_stat4.
SELECT name
FROM sqlite_master
WHERE type = 'table'
ORDER BY name;

SELECT 'mapDenormalize' AS tableName, COUNT(*) AS retainedRows FROM mapDenormalize
UNION ALL
SELECT 'mapCelestialStatistics', COUNT(*) FROM mapCelestialStatistics
UNION ALL
SELECT 'invTypes', COUNT(*) FROM invTypes
UNION ALL
SELECT 'invGroups', COUNT(*) FROM invGroups;

-- DB Browser for SQLite: click "Write Changes" after this script finishes. Then
-- execute VACUUM separately and click "Write Changes" again to reclaim disk space:
--
--   VACUUM;
