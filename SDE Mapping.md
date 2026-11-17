# CCP JSONL → SQLite reference

**Schema v5** · [Database guide and SQL examples](database.md) · [README overview](../README.md#sqlite-database-at-a-glance)

## Reading the reference

Browse by [CCP source file](#by-source-file) or [SQLite table or view](#by-table-or-view).
Each source section lists its output tables. Each table shows its keys, columns, and joins.

Paths use JSON Pointer syntax. `[]` identifies an array element, `{}` a numeric map key, and
`{language}` a locale entry. SQL NULL represents absent fields and explicit JSON nulls.
Translation tables contain English rows by default. Use `--all-languages` to project every locale.
Use `--keep-raw` to inspect empty containers, exact JSON representations, and unprojected future fields in SQLite.

## By source file

<details>
<summary>Show the alphabetical JSONL file index</summary>

- [_sde.jsonl](#source-_sde)
- [accountingEntryTypes.jsonl](#source-accountingentrytypes)
- [agentTypes.jsonl](#source-agenttypes)
- [agentsInSpace.jsonl](#source-agentsinspace)
- [ancestries.jsonl](#source-ancestries)
- [appliedProximityEffects.jsonl](#source-appliedproximityeffects)
- [archetypes.jsonl](#source-archetypes)
- [bloodlines.jsonl](#source-bloodlines)
- [blueprints.jsonl](#source-blueprints)
- [categories.jsonl](#source-categories)
- [certificates.jsonl](#source-certificates)
- [characterAttributes.jsonl](#source-characterattributes)
- [characterTitles.jsonl](#source-charactertitles)
- [cloneGrades.jsonl](#source-clonegrades)
- [compressibleTypes.jsonl](#source-compressibletypes)
- [contrabandTypes.jsonl](#source-contrabandtypes)
- [controlTowerResources.jsonl](#source-controltowerresources)
- [corporationActivities.jsonl](#source-corporationactivities)
- [corporationRoleGroups.jsonl](#source-corporationrolegroups)
- [corporationRoles.jsonl](#source-corporationroles)
- [dbuffCollections.jsonl](#source-dbuffcollections)
- [dogmaAttributeCategories.jsonl](#source-dogmaattributecategories)
- [dogmaAttributes.jsonl](#source-dogmaattributes)
- [dogmaEffects.jsonl](#source-dogmaeffects)
- [dogmaUnits.jsonl](#source-dogmaunits)
- [dungeons.jsonl](#source-dungeons)
- [dynamicItemAttributes.jsonl](#source-dynamicitemattributes)
- [epicArcs.jsonl](#source-epicarcs)
- [expertSystems.jsonl](#source-expertsystems)
- [factions.jsonl](#source-factions)
- [fighterAbilities.jsonl](#source-fighterabilities)
- [fighterAbilitiesByType.jsonl](#source-fighterabilitiesbytype)
- [freelanceJobSchemas.jsonl](#source-freelancejobschemas)
- [graphicMaterialSets.jsonl](#source-graphicmaterialsets)
- [graphics.jsonl](#source-graphics)
- [groups.jsonl](#source-groups)
- [icons.jsonl](#source-icons)
- [industryActivities.jsonl](#source-industryactivities)
- [industryAssemblyLines.jsonl](#source-industryassemblylines)
- [industryInstallationTypes.jsonl](#source-industryinstallationtypes)
- [industryModifierSources.jsonl](#source-industrymodifiersources)
- [industryTargetFilters.jsonl](#source-industrytargetfilters)
- [landmarks.jsonl](#source-landmarks)
- [linkWithShip.jsonl](#source-linkwithship)
- [mapAsteroidBelts.jsonl](#source-mapasteroidbelts)
- [mapConstellations.jsonl](#source-mapconstellations)
- [mapMoons.jsonl](#source-mapmoons)
- [mapPlanets.jsonl](#source-mapplanets)
- [mapRegions.jsonl](#source-mapregions)
- [mapSecondarySuns.jsonl](#source-mapsecondarysuns)
- [mapSolarSystems.jsonl](#source-mapsolarsystems)
- [mapStargates.jsonl](#source-mapstargates)
- [mapStars.jsonl](#source-mapstars)
- [marketGroups.jsonl](#source-marketgroups)
- [masteries.jsonl](#source-masteries)
- [mercenaryTacticalOperations.jsonl](#source-mercenarytacticaloperations)
- [metaGroups.jsonl](#source-metagroups)
- [metenoxMoonDrill.jsonl](#source-metenoxmoondrill)
- [militaryCampaignObjectives.jsonl](#source-militarycampaignobjectives)
- [militaryCampaigns.jsonl](#source-militarycampaigns)
- [missions.jsonl](#source-missions)
- [notificationTypes.jsonl](#source-notificationtypes)
- [npcCharacters.jsonl](#source-npccharacters)
- [npcCorporationDivisions.jsonl](#source-npccorporationdivisions)
- [npcCorporations.jsonl](#source-npccorporations)
- [npcStations.jsonl](#source-npcstations)
- [planetResources.jsonl](#source-planetresources)
- [planetSchematics.jsonl](#source-planetschematics)
- [proximityTrap.jsonl](#source-proximitytrap)
- [races.jsonl](#source-races)
- [schoolMap.jsonl](#source-schoolmap)
- [schools.jsonl](#source-schools)
- [shipTreeElements.jsonl](#source-shiptreeelements)
- [shipTreeFactions.jsonl](#source-shiptreefactions)
- [shipTreeGroups.jsonl](#source-shiptreegroups)
- [skillPlans.jsonl](#source-skillplans)
- [skinLicenses.jsonl](#source-skinlicenses)
- [skinMaterials.jsonl](#source-skinmaterials)
- [skinrComponentCategories.jsonl](#source-skinrcomponentcategories)
- [skinrComponentPointValues.jsonl](#source-skinrcomponentpointvalues)
- [skinrComponentRarities.jsonl](#source-skinrcomponentrarities)
- [skinrComponents.jsonl](#source-skinrcomponents)
- [skinrSlotCategories.jsonl](#source-skinrslotcategories)
- [skinrSlotConfigurations.jsonl](#source-skinrslotconfigurations)
- [skinrSlotNames.jsonl](#source-skinrslotnames)
- [skinrSlots.jsonl](#source-skinrslots)
- [skinrSlotsToMaterials.jsonl](#source-skinrslotstomaterials)
- [skinrTierThresholds.jsonl](#source-skinrtierthresholds)
- [skins.jsonl](#source-skins)
- [sovereigntyUpgrades.jsonl](#source-sovereigntyupgrades)
- [stationOperations.jsonl](#source-stationoperations)
- [stationServices.jsonl](#source-stationservices)
- [stationStandingsRestrictions.jsonl](#source-stationstandingsrestrictions)
- [systemDbuffEmitters.jsonl](#source-systemdbuffemitters)
- [systemWideEffects.jsonl](#source-systemwideeffects)
- [translationLanguages.jsonl](#source-translationlanguages)
- [typeBonus.jsonl](#source-typebonus)
- [typeDogma.jsonl](#source-typedogma)
- [typeElements.jsonl](#source-typeelements)
- [typeLists.jsonl](#source-typelists)
- [typeMaterials.jsonl](#source-typematerials)
- [types.jsonl](#source-types)

</details>

## By table or view

<details>
<summary>Show the alphabetical SQLite table and view index</summary>

- [`_hki_import_metadata`](#table-_hki_import_metadata)
- [`sde__sde`](#table-sde__sde)
- [`sde_accountingEntryTypes`](#table-sde_accountingentrytypes)
- [`sde_accountingEntryTypes_description_translations`](#table-sde_accountingentrytypes_description_translations)
- [`sde_accountingEntryTypes_journalMessage_translations`](#table-sde_accountingentrytypes_journalmessage_translations)
- [`sde_accountingEntryTypes_name_translations`](#table-sde_accountingentrytypes_name_translations)
- [`sde_agentsInSpace`](#table-sde_agentsinspace)
- [`sde_agentTypes`](#table-sde_agenttypes)
- [`sde_ancestries`](#table-sde_ancestries)
- [`sde_ancestries_description_translations`](#table-sde_ancestries_description_translations)
- [`sde_ancestries_name_translations`](#table-sde_ancestries_name_translations)
- [`sde_appliedProximityEffects`](#table-sde_appliedproximityeffects)
- [`sde_appliedProximityEffects_dbuffs_items`](#table-sde_appliedproximityeffects_dbuffs_items)
- [`sde_archetypes`](#table-sde_archetypes)
- [`sde_archetypes_description_translations`](#table-sde_archetypes_description_translations)
- [`sde_archetypes_title_translations`](#table-sde_archetypes_title_translations)
- [`sde_bloodlines`](#table-sde_bloodlines)
- [`sde_bloodlines_description_translations`](#table-sde_bloodlines_description_translations)
- [`sde_bloodlines_name_translations`](#table-sde_bloodlines_name_translations)
- [`sde_blueprints`](#table-sde_blueprints)
- [`sde_blueprints_activities_copying_materials_items`](#table-sde_blueprints_activities_copying_materials_items)
- [`sde_blueprints_activities_copying_skills_items`](#table-sde_blueprints_activities_copying_skills_items)
- [`sde_blueprints_activities_invention_materials_items`](#table-sde_blueprints_activities_invention_materials_items)
- [`sde_blueprints_activities_invention_products_items`](#table-sde_blueprints_activities_invention_products_items)
- [`sde_blueprints_activities_invention_skills_items`](#table-sde_blueprints_activities_invention_skills_items)
- [`sde_blueprints_activities_manufacturing_materials_items`](#table-sde_blueprints_activities_manufacturing_materials_items)
- [`sde_blueprints_activities_manufacturing_products_items`](#table-sde_blueprints_activities_manufacturing_products_items)
- [`sde_blueprints_activities_manufacturing_skills_items`](#table-sde_blueprints_activities_manufacturing_skills_items)
- [`sde_blueprints_activities_reaction_materials_items`](#table-sde_blueprints_activities_reaction_materials_items)
- [`sde_blueprints_activities_reaction_products_items`](#table-sde_blueprints_activities_reaction_products_items)
- [`sde_blueprints_activities_reaction_skills_items`](#table-sde_blueprints_activities_reaction_skills_items)
- [`sde_blueprints_activities_research_material_materials_items`](#table-sde_blueprints_activities_research_material_materials_items)
- [`sde_blueprints_activities_research_material_skills_items`](#table-sde_blueprints_activities_research_material_skills_items)
- [`sde_blueprints_activities_research_time_materials_items`](#table-sde_blueprints_activities_research_time_materials_items)
- [`sde_blueprints_activities_research_time_skills_items`](#table-sde_blueprints_activities_research_time_skills_items)
- [`sde_categories`](#table-sde_categories)
- [`sde_categories_name_translations`](#table-sde_categories_name_translations)
- [`sde_certificates`](#table-sde_certificates)
- [`sde_certificates_description_translations`](#table-sde_certificates_description_translations)
- [`sde_certificates_name_translations`](#table-sde_certificates_name_translations)
- [`sde_certificates_recommendedFor_items`](#table-sde_certificates_recommendedfor_items)
- [`sde_certificates_skillTypes_items`](#table-sde_certificates_skilltypes_items)
- [`sde_characterAttributes`](#table-sde_characterattributes)
- [`sde_characterAttributes_name_translations`](#table-sde_characterattributes_name_translations)
- [`sde_characterTitles`](#table-sde_charactertitles)
- [`sde_characterTitles_name_translations`](#table-sde_charactertitles_name_translations)
- [`sde_cloneGrades`](#table-sde_clonegrades)
- [`sde_cloneGrades_skills_items`](#table-sde_clonegrades_skills_items)
- [`sde_compressibleTypes`](#table-sde_compressibletypes)
- [`sde_contrabandTypes`](#table-sde_contrabandtypes)
- [`sde_contrabandTypes_factions_items`](#table-sde_contrabandtypes_factions_items)
- [`sde_controlTowerResources`](#table-sde_controltowerresources)
- [`sde_controlTowerResources_resources_items`](#table-sde_controltowerresources_resources_items)
- [`sde_corporationActivities`](#table-sde_corporationactivities)
- [`sde_corporationActivities_name_translations`](#table-sde_corporationactivities_name_translations)
- [`sde_corporationRoleGroups`](#table-sde_corporationrolegroups)
- [`sde_corporationRoleGroups_name_translations`](#table-sde_corporationrolegroups_name_translations)
- [`sde_corporationRoles`](#table-sde_corporationroles)
- [`sde_corporationRoles_description_translations`](#table-sde_corporationroles_description_translations)
- [`sde_corporationRoles_name_translations`](#table-sde_corporationroles_name_translations)
- [`sde_corporationRoles_roleGroupIDs_items`](#table-sde_corporationroles_rolegroupids_items)
- [`sde_dbuffCollections`](#table-sde_dbuffcollections)
- [`sde_dbuffCollections_displayName_translations`](#table-sde_dbuffcollections_displayname_translations)
- [`sde_dbuffCollections_itemModifiers_items`](#table-sde_dbuffcollections_itemmodifiers_items)
- [`sde_dbuffCollections_locationGroupModifiers_items`](#table-sde_dbuffcollections_locationgroupmodifiers_items)
- [`sde_dbuffCollections_locationModifiers_items`](#table-sde_dbuffcollections_locationmodifiers_items)
- [`sde_dbuffCollections_locationRequiredSkillModifiers_items`](#table-sde_dbuffcollections_locationrequiredskillmodifiers_items)
- [`sde_dogmaAttributeCategories`](#table-sde_dogmaattributecategories)
- [`sde_dogmaAttributes`](#table-sde_dogmaattributes)
- [`sde_dogmaAttributes_displayName_translations`](#table-sde_dogmaattributes_displayname_translations)
- [`sde_dogmaAttributes_tooltipDescription_translations`](#table-sde_dogmaattributes_tooltipdescription_translations)
- [`sde_dogmaAttributes_tooltipTitle_translations`](#table-sde_dogmaattributes_tooltiptitle_translations)
- [`sde_dogmaEffects`](#table-sde_dogmaeffects)
- [`sde_dogmaEffects_description_translations`](#table-sde_dogmaeffects_description_translations)
- [`sde_dogmaEffects_displayName_translations`](#table-sde_dogmaeffects_displayname_translations)
- [`sde_dogmaEffects_modifierInfo_items`](#table-sde_dogmaeffects_modifierinfo_items)
- [`sde_dogmaUnits`](#table-sde_dogmaunits)
- [`sde_dogmaUnits_description_translations`](#table-sde_dogmaunits_description_translations)
- [`sde_dogmaUnits_displayName_translations`](#table-sde_dogmaunits_displayname_translations)
- [`sde_dungeons`](#table-sde_dungeons)
- [`sde_dungeons_allowedShipsList_items`](#table-sde_dungeons_allowedshipslist_items)
- [`sde_dungeons_description_translations`](#table-sde_dungeons_description_translations)
- [`sde_dungeons_gameplayDescription_translations`](#table-sde_dungeons_gameplaydescription_translations)
- [`sde_dungeons_name_translations`](#table-sde_dungeons_name_translations)
- [`sde_dynamicItemAttributes`](#table-sde_dynamicitemattributes)
- [`sde_dynamicItemAttributes_attributeIDs_items`](#table-sde_dynamicitemattributes_attributeids_items)
- [`sde_dynamicItemAttributes_inputOutputMapping_items`](#table-sde_dynamicitemattributes_inputoutputmapping_items)
- [`sde_dynamicItemAttributes_inputOutputMapping_items_applicableTypes_items`](#table-sde_dynamicitemattributes_inputoutputmapping_items_applicabletypes_items)
- [`sde_epicArcs`](#table-sde_epicarcs)
- [`sde_epicArcs_missions_items`](#table-sde_epicarcs_missions_items)
- [`sde_epicArcs_missions_items_nextMissions_items`](#table-sde_epicarcs_missions_items_nextmissions_items)
- [`sde_epicArcs_name_translations`](#table-sde_epicarcs_name_translations)
- [`sde_expertSystems`](#table-sde_expertsystems)
- [`sde_expertSystems_associatedShipTypes_items`](#table-sde_expertsystems_associatedshiptypes_items)
- [`sde_expertSystems_skillsGranted_items`](#table-sde_expertsystems_skillsgranted_items)
- [`sde_factions`](#table-sde_factions)
- [`sde_factions_description_translations`](#table-sde_factions_description_translations)
- [`sde_factions_memberRaces_items`](#table-sde_factions_memberraces_items)
- [`sde_factions_name_translations`](#table-sde_factions_name_translations)
- [`sde_factions_shortDescription_translations`](#table-sde_factions_shortdescription_translations)
- [`sde_fighterAbilities`](#table-sde_fighterabilities)
- [`sde_fighterAbilities_displayName_translations`](#table-sde_fighterabilities_displayname_translations)
- [`sde_fighterAbilities_tooltipText_translations`](#table-sde_fighterabilities_tooltiptext_translations)
- [`sde_fighterAbilitiesByType`](#table-sde_fighterabilitiesbytype)
- [`sde_freelanceJobSchemas`](#table-sde_freelancejobschemas)
- [`sde_freelanceJobSchemas__value_items`](#table-sde_freelancejobschemas__value_items)
- [`sde_freelanceJobSchemas__value_items_contentTags_items`](#table-sde_freelancejobschemas__value_items_contenttags_items)
- [`sde_freelanceJobSchemas__value_items_contributionMultiplier_description_translations`](#table-sde_freelancejobschemas__value_items_contributionmultiplier_description_translations)
- [`sde_freelanceJobSchemas__value_items_contributionMultiplier_title_translations`](#table-sde_freelancejobschemas__value_items_contributionmultiplier_title_translations)
- [`sde_freelanceJobSchemas__value_items_contributionMultiplier_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_contributionmultiplier_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_description_translations`](#table-sde_freelancejobschemas__value_items_description_translations)
- [`sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_description_translations`](#table-sde_freelancejobschemas__value_items_maxcontributionsperparticipant_description_translations)
- [`sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_title_translations`](#table-sde_freelancejobschemas__value_items_maxcontributionsperparticipant_title_translations)
- [`sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_maxcontributionsperparticipant_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_maxProgressPerContribution_description_translations`](#table-sde_freelancejobschemas__value_items_maxprogresspercontribution_description_translations)
- [`sde_freelanceJobSchemas__value_items_maxProgressPerContribution_title_translations`](#table-sde_freelancejobschemas__value_items_maxprogresspercontribution_title_translations)
- [`sde_freelanceJobSchemas__value_items_maxProgressPerContribution_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_maxprogresspercontribution_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_choiceLabel_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_choicelabel_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionFalse_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optionfalse_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionFalse_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optionfalse_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionTrue_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optiontrue_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionTrue_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optiontrue_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_acceptedValueTypes_items`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_acceptedvaluetypes_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_acceptedValueTypes_items`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_acceptedvaluetypes_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_acceptedValueTypes_items`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_acceptedvaluetypes_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_progressDescription_translations`](#table-sde_freelancejobschemas__value_items_progressdescription_translations)
- [`sde_freelanceJobSchemas__value_items_rewardDescription_translations`](#table-sde_freelancejobschemas__value_items_rewarddescription_translations)
- [`sde_freelanceJobSchemas__value_items_targetDescription_translations`](#table-sde_freelancejobschemas__value_items_targetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_title_translations`](#table-sde_freelancejobschemas__value_items_title_translations)
- [`sde_graphicMaterialSets`](#table-sde_graphicmaterialsets)
- [`sde_graphics`](#table-sde_graphics)
- [`sde_graphics_sofLayout_items`](#table-sde_graphics_soflayout_items)
- [`sde_groups`](#table-sde_groups)
- [`sde_groups_name_translations`](#table-sde_groups_name_translations)
- [`sde_icons`](#table-sde_icons)
- [`sde_industryActivities`](#table-sde_industryactivities)
- [`sde_industryAssemblyLines`](#table-sde_industryassemblylines)
- [`sde_industryAssemblyLines_detailsPerCategory_items`](#table-sde_industryassemblylines_detailspercategory_items)
- [`sde_industryAssemblyLines_detailsPerGroup_items`](#table-sde_industryassemblylines_detailspergroup_items)
- [`sde_industryAssemblyLines_detailsPerTypeList_items`](#table-sde_industryassemblylines_detailspertypelist_items)
- [`sde_industryInstallationTypes`](#table-sde_industryinstallationtypes)
- [`sde_industryInstallationTypes_assemblyLines_items`](#table-sde_industryinstallationtypes_assemblylines_items)
- [`sde_industryModifierSources`](#table-sde_industrymodifiersources)
- [`sde_industryModifierSources_copying_cost_items`](#table-sde_industrymodifiersources_copying_cost_items)
- [`sde_industryModifierSources_copying_time_items`](#table-sde_industrymodifiersources_copying_time_items)
- [`sde_industryModifierSources_invention_cost_items`](#table-sde_industrymodifiersources_invention_cost_items)
- [`sde_industryModifierSources_invention_time_items`](#table-sde_industrymodifiersources_invention_time_items)
- [`sde_industryModifierSources_manufacturing_cost_items`](#table-sde_industrymodifiersources_manufacturing_cost_items)
- [`sde_industryModifierSources_manufacturing_material_items`](#table-sde_industrymodifiersources_manufacturing_material_items)
- [`sde_industryModifierSources_manufacturing_time_items`](#table-sde_industrymodifiersources_manufacturing_time_items)
- [`sde_industryModifierSources_reaction_material_items`](#table-sde_industrymodifiersources_reaction_material_items)
- [`sde_industryModifierSources_reaction_time_items`](#table-sde_industrymodifiersources_reaction_time_items)
- [`sde_industryModifierSources_researchMaterial_cost_items`](#table-sde_industrymodifiersources_researchmaterial_cost_items)
- [`sde_industryModifierSources_researchMaterial_time_items`](#table-sde_industrymodifiersources_researchmaterial_time_items)
- [`sde_industryModifierSources_researchTime_cost_items`](#table-sde_industrymodifiersources_researchtime_cost_items)
- [`sde_industryModifierSources_researchTime_time_items`](#table-sde_industrymodifiersources_researchtime_time_items)
- [`sde_industryTargetFilters`](#table-sde_industrytargetfilters)
- [`sde_industryTargetFilters_categoryIDs_items`](#table-sde_industrytargetfilters_categoryids_items)
- [`sde_industryTargetFilters_groupIDs_items`](#table-sde_industrytargetfilters_groupids_items)
- [`sde_landmarks`](#table-sde_landmarks)
- [`sde_landmarks_description_translations`](#table-sde_landmarks_description_translations)
- [`sde_landmarks_name_translations`](#table-sde_landmarks_name_translations)
- [`sde_linkWithShip`](#table-sde_linkwithship)
- [`sde_linkWithShip_dbuffs_items`](#table-sde_linkwithship_dbuffs_items)
- [`sde_mapAsteroidBelts`](#table-sde_mapasteroidbelts)
- [`sde_mapAsteroidBelts_uniqueName_translations`](#table-sde_mapasteroidbelts_uniquename_translations)
- [`sde_mapConstellations`](#table-sde_mapconstellations)
- [`sde_mapConstellations_name_translations`](#table-sde_mapconstellations_name_translations)
- [`sde_mapConstellations_solarSystemIDs_items`](#table-sde_mapconstellations_solarsystemids_items)
- [`sde_mapMoons`](#table-sde_mapmoons)
- [`sde_mapMoons_npcStationIDs_items`](#table-sde_mapmoons_npcstationids_items)
- [`sde_mapMoons_uniqueName_translations`](#table-sde_mapmoons_uniquename_translations)
- [`sde_mapPlanets`](#table-sde_mapplanets)
- [`sde_mapPlanets_asteroidBeltIDs_items`](#table-sde_mapplanets_asteroidbeltids_items)
- [`sde_mapPlanets_moonIDs_items`](#table-sde_mapplanets_moonids_items)
- [`sde_mapPlanets_npcStationIDs_items`](#table-sde_mapplanets_npcstationids_items)
- [`sde_mapPlanets_uniqueName_translations`](#table-sde_mapplanets_uniquename_translations)
- [`sde_mapRegions`](#table-sde_mapregions)
- [`sde_mapRegions_constellationIDs_items`](#table-sde_mapregions_constellationids_items)
- [`sde_mapRegions_description_translations`](#table-sde_mapregions_description_translations)
- [`sde_mapRegions_name_translations`](#table-sde_mapregions_name_translations)
- [`sde_mapSecondarySuns`](#table-sde_mapsecondarysuns)
- [`sde_mapSolarSystems`](#table-sde_mapsolarsystems)
- [`sde_mapSolarSystems_disallowedAnchorCategories_items`](#table-sde_mapsolarsystems_disallowedanchorcategories_items)
- [`sde_mapSolarSystems_disallowedAnchorGroups_items`](#table-sde_mapsolarsystems_disallowedanchorgroups_items)
- [`sde_mapSolarSystems_name_translations`](#table-sde_mapsolarsystems_name_translations)
- [`sde_mapSolarSystems_planetIDs_items`](#table-sde_mapsolarsystems_planetids_items)
- [`sde_mapSolarSystems_stargateIDs_items`](#table-sde_mapsolarsystems_stargateids_items)
- [`sde_mapStargates`](#table-sde_mapstargates)
- [`sde_mapStars`](#table-sde_mapstars)
- [`sde_marketGroups`](#table-sde_marketgroups)
- [`sde_marketGroups_description_translations`](#table-sde_marketgroups_description_translations)
- [`sde_marketGroups_name_translations`](#table-sde_marketgroups_name_translations)
- [`sde_masteries`](#table-sde_masteries)
- [`sde_masteries__value_items`](#table-sde_masteries__value_items)
- [`sde_masteries__value_items__value_items`](#table-sde_masteries__value_items__value_items)
- [`sde_mercenaryTacticalOperations`](#table-sde_mercenarytacticaloperations)
- [`sde_mercenaryTacticalOperations_description_translations`](#table-sde_mercenarytacticaloperations_description_translations)
- [`sde_mercenaryTacticalOperations_name_translations`](#table-sde_mercenarytacticaloperations_name_translations)
- [`sde_metaGroups`](#table-sde_metagroups)
- [`sde_metaGroups_description_translations`](#table-sde_metagroups_description_translations)
- [`sde_metaGroups_name_translations`](#table-sde_metagroups_name_translations)
- [`sde_metenoxMoonDrill`](#table-sde_metenoxmoondrill)
- [`sde_militaryCampaignObjectives`](#table-sde_militarycampaignobjectives)
- [`sde_militaryCampaignObjectives_annotations_restrictionTooltip_translations`](#table-sde_militarycampaignobjectives_annotations_restrictiontooltip_translations)
- [`sde_militaryCampaignObjectives_annotations_warning1_translations`](#table-sde_militarycampaignobjectives_annotations_warning1_translations)
- [`sde_militaryCampaignObjectives_annotations_warning2_translations`](#table-sde_militarycampaignobjectives_annotations_warning2_translations)
- [`sde_militaryCampaignObjectives_contentTags_items`](#table-sde_militarycampaignobjectives_contenttags_items)
- [`sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items)
- [`sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items_matcher_values_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items_matcher_values_items)
- [`sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items_matcher_values_items_values_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items_matcher_values_items_values_items)
- [`sde_militaryCampaignObjectives_subtitle_translations`](#table-sde_militarycampaignobjectives_subtitle_translations)
- [`sde_militaryCampaignObjectives_title_translations`](#table-sde_militarycampaignobjectives_title_translations)
- [`sde_militaryCampaigns`](#table-sde_militarycampaigns)
- [`sde_militaryCampaigns_annotations_briefingFailureDescription_translations`](#table-sde_militarycampaigns_annotations_briefingfailuredescription_translations)
- [`sde_militaryCampaigns_annotations_briefingFailureHeader_translations`](#table-sde_militarycampaigns_annotations_briefingfailureheader_translations)
- [`sde_militaryCampaigns_annotations_briefingFinalWords_translations`](#table-sde_militarycampaigns_annotations_briefingfinalwords_translations)
- [`sde_militaryCampaigns_annotations_briefingGoalDescription_translations`](#table-sde_militarycampaigns_annotations_briefinggoaldescription_translations)
- [`sde_militaryCampaigns_annotations_briefingHeader_translations`](#table-sde_militarycampaigns_annotations_briefingheader_translations)
- [`sde_militaryCampaigns_annotations_briefingSuccessDescription_translations`](#table-sde_militarycampaigns_annotations_briefingsuccessdescription_translations)
- [`sde_militaryCampaigns_annotations_briefingSuccessHeader_translations`](#table-sde_militarycampaigns_annotations_briefingsuccessheader_translations)
- [`sde_militaryCampaigns_annotations_finishedCampaignEnded_translations`](#table-sde_militarycampaigns_annotations_finishedcampaignended_translations)
- [`sde_militaryCampaigns_annotations_finishedFailureDescription_translations`](#table-sde_militarycampaigns_annotations_finishedfailuredescription_translations)
- [`sde_militaryCampaigns_annotations_finishedResolutionStateFailure_translations`](#table-sde_militarycampaigns_annotations_finishedresolutionstatefailure_translations)
- [`sde_militaryCampaigns_annotations_finishedResolutionStateSuccess_translations`](#table-sde_militarycampaigns_annotations_finishedresolutionstatesuccess_translations)
- [`sde_militaryCampaigns_annotations_finishedSuccessDescription_translations`](#table-sde_militarycampaigns_annotations_finishedsuccessdescription_translations)
- [`sde_militaryCampaigns_annotations_mapHeader_translations`](#table-sde_militarycampaigns_annotations_mapheader_translations)
- [`sde_militaryCampaigns_annotations_mapSection1Paragraph_translations`](#table-sde_militarycampaigns_annotations_mapsection1paragraph_translations)
- [`sde_militaryCampaigns_annotations_mapSection1Title_translations`](#table-sde_militarycampaigns_annotations_mapsection1title_translations)
- [`sde_militaryCampaigns_annotations_mapSection2Paragraph_translations`](#table-sde_militarycampaigns_annotations_mapsection2paragraph_translations)
- [`sde_militaryCampaigns_annotations_mapSection2Title_translations`](#table-sde_militarycampaigns_annotations_mapsection2title_translations)
- [`sde_militaryCampaigns_annotations_mapSection3Paragraph_translations`](#table-sde_militarycampaigns_annotations_mapsection3paragraph_translations)
- [`sde_militaryCampaigns_annotations_mapSection3Title_translations`](#table-sde_militarycampaigns_annotations_mapsection3title_translations)
- [`sde_militaryCampaigns_annotations_mapSubheader_translations`](#table-sde_militarycampaigns_annotations_mapsubheader_translations)
- [`sde_militaryCampaigns_annotations_mapTitle_translations`](#table-sde_militarycampaigns_annotations_maptitle_translations)
- [`sde_militaryCampaigns_annotations_presentingCharacterName_translations`](#table-sde_militarycampaigns_annotations_presentingcharactername_translations)
- [`sde_militaryCampaigns_annotations_presentingCharacterSubtitle_translations`](#table-sde_militarycampaigns_annotations_presentingcharactersubtitle_translations)
- [`sde_militaryCampaigns_subtitle_translations`](#table-sde_militarycampaigns_subtitle_translations)
- [`sde_militaryCampaigns_title_translations`](#table-sde_militarycampaigns_title_translations)
- [`sde_missions`](#table-sde_missions)
- [`sde_missions_extraStandings_items`](#table-sde_missions_extrastandings_items)
- [`sde_missions_messages_items`](#table-sde_missions_messages_items)
- [`sde_missions_messages_items_translations`](#table-sde_missions_messages_items_translations)
- [`sde_missions_name_translations`](#table-sde_missions_name_translations)
- [`sde_notificationTypes`](#table-sde_notificationtypes)
- [`sde_notificationTypes_displayName_translations`](#table-sde_notificationtypes_displayname_translations)
- [`sde_npcCharacters`](#table-sde_npccharacters)
- [`sde_npcCharacters_name_translations`](#table-sde_npccharacters_name_translations)
- [`sde_npcCharacters_skills_items`](#table-sde_npccharacters_skills_items)
- [`sde_npcCorporationDivisions`](#table-sde_npccorporationdivisions)
- [`sde_npcCorporationDivisions_description_translations`](#table-sde_npccorporationdivisions_description_translations)
- [`sde_npcCorporationDivisions_leaderTypeName_translations`](#table-sde_npccorporationdivisions_leadertypename_translations)
- [`sde_npcCorporationDivisions_name_translations`](#table-sde_npccorporationdivisions_name_translations)
- [`sde_npcCorporations`](#table-sde_npccorporations)
- [`sde_npcCorporations_allowedMemberRaces_items`](#table-sde_npccorporations_allowedmemberraces_items)
- [`sde_npcCorporations_corporationTrades_items`](#table-sde_npccorporations_corporationtrades_items)
- [`sde_npcCorporations_description_translations`](#table-sde_npccorporations_description_translations)
- [`sde_npcCorporations_divisions_items`](#table-sde_npccorporations_divisions_items)
- [`sde_npcCorporations_exchangeRates_items`](#table-sde_npccorporations_exchangerates_items)
- [`sde_npcCorporations_investors_items`](#table-sde_npccorporations_investors_items)
- [`sde_npcCorporations_lpOfferTables_items`](#table-sde_npccorporations_lpoffertables_items)
- [`sde_npcCorporations_name_translations`](#table-sde_npccorporations_name_translations)
- [`sde_npcStations`](#table-sde_npcstations)
- [`sde_planetResources`](#table-sde_planetresources)
- [`sde_planetSchematics`](#table-sde_planetschematics)
- [`sde_planetSchematics_name_translations`](#table-sde_planetschematics_name_translations)
- [`sde_planetSchematics_pins_items`](#table-sde_planetschematics_pins_items)
- [`sde_planetSchematics_types_items`](#table-sde_planetschematics_types_items)
- [`sde_proximityTrap`](#table-sde_proximitytrap)
- [`sde_proximityTrap_dbuffs_items`](#table-sde_proximitytrap_dbuffs_items)
- [`sde_races`](#table-sde_races)
- [`sde_races_description_translations`](#table-sde_races_description_translations)
- [`sde_races_name_translations`](#table-sde_races_name_translations)
- [`sde_races_skills_items`](#table-sde_races_skills_items)
- [`sde_schoolMap`](#table-sde_schoolmap)
- [`sde_schools`](#table-sde_schools)
- [`sde_schools_careerAgents_items`](#table-sde_schools_careeragents_items)
- [`sde_schools_characterDescription_translations`](#table-sde_schools_characterdescription_translations)
- [`sde_schools_description_translations`](#table-sde_schools_description_translations)
- [`sde_schools_name_translations`](#table-sde_schools_name_translations)
- [`sde_schools_startingStations_items`](#table-sde_schools_startingstations_items)
- [`sde_schools_title_translations`](#table-sde_schools_title_translations)
- [`sde_shipTreeElements`](#table-sde_shiptreeelements)
- [`sde_shipTreeElements_description_translations`](#table-sde_shiptreeelements_description_translations)
- [`sde_shipTreeElements_name_translations`](#table-sde_shiptreeelements_name_translations)
- [`sde_shipTreeFactions`](#table-sde_shiptreefactions)
- [`sde_shipTreeFactions_description_translations`](#table-sde_shiptreefactions_description_translations)
- [`sde_shipTreeFactions_elements_items`](#table-sde_shiptreefactions_elements_items)
- [`sde_shipTreeGroups`](#table-sde_shiptreegroups)
- [`sde_shipTreeGroups_description_translations`](#table-sde_shiptreegroups_description_translations)
- [`sde_shipTreeGroups_elements_items`](#table-sde_shiptreegroups_elements_items)
- [`sde_shipTreeGroups_name_translations`](#table-sde_shiptreegroups_name_translations)
- [`sde_shipTreeGroups_preReqSkills_items`](#table-sde_shiptreegroups_prereqskills_items)
- [`sde_shipTreeGroups_preReqSkills_items_skills_items`](#table-sde_shiptreegroups_prereqskills_items_skills_items)
- [`sde_skillPlans`](#table-sde_skillplans)
- [`sde_skillPlans_description_translations`](#table-sde_skillplans_description_translations)
- [`sde_skillPlans_milestones_items`](#table-sde_skillplans_milestones_items)
- [`sde_skillPlans_name_translations`](#table-sde_skillplans_name_translations)
- [`sde_skillPlans_skillRequirements_items`](#table-sde_skillplans_skillrequirements_items)
- [`sde_skinLicenses`](#table-sde_skinlicenses)
- [`sde_skinMaterials`](#table-sde_skinmaterials)
- [`sde_skinMaterials_displayName_translations`](#table-sde_skinmaterials_displayname_translations)
- [`sde_skinrComponentCategories`](#table-sde_skinrcomponentcategories)
- [`sde_skinrComponentPointValues`](#table-sde_skinrcomponentpointvalues)
- [`sde_skinrComponentPointValues__value_items`](#table-sde_skinrcomponentpointvalues__value_items)
- [`sde_skinrComponentRarities`](#table-sde_skinrcomponentrarities)
- [`sde_skinrComponentRarities_name_translations`](#table-sde_skinrcomponentrarities_name_translations)
- [`sde_skinrComponents`](#table-sde_skinrcomponents)
- [`sde_skinrComponents_associatedTypeIds_items`](#table-sde_skinrcomponents_associatedtypeids_items)
- [`sde_skinrComponents_name_translations`](#table-sde_skinrcomponents_name_translations)
- [`sde_skinrSlotCategories`](#table-sde_skinrslotcategories)
- [`sde_skinrSlotConfigurations`](#table-sde_skinrslotconfigurations)
- [`sde_skinrSlotConfigurations_config_items`](#table-sde_skinrslotconfigurations_config_items)
- [`sde_skinrSlotConfigurations_ships_items`](#table-sde_skinrslotconfigurations_ships_items)
- [`sde_skinrSlotNames`](#table-sde_skinrslotnames)
- [`sde_skinrSlots`](#table-sde_skinrslots)
- [`sde_skinrSlots_allowedDesignComponentCategories_items`](#table-sde_skinrslots_alloweddesigncomponentcategories_items)
- [`sde_skinrSlots_name_translations`](#table-sde_skinrslots_name_translations)
- [`sde_skinrSlotsToMaterials`](#table-sde_skinrslotstomaterials)
- [`sde_skinrSlotsToMaterials__value_items`](#table-sde_skinrslotstomaterials__value_items)
- [`sde_skinrTierThresholds`](#table-sde_skinrtierthresholds)
- [`sde_skinrTierThresholds__value_items`](#table-sde_skinrtierthresholds__value_items)
- [`sde_skins`](#table-sde_skins)
- [`sde_skins_skinDescription_translations`](#table-sde_skins_skindescription_translations)
- [`sde_skins_types_items`](#table-sde_skins_types_items)
- [`sde_sovereigntyUpgrades`](#table-sde_sovereigntyupgrades)
- [`sde_stationOperations`](#table-sde_stationoperations)
- [`sde_stationOperations_description_translations`](#table-sde_stationoperations_description_translations)
- [`sde_stationOperations_operationName_translations`](#table-sde_stationoperations_operationname_translations)
- [`sde_stationOperations_services_items`](#table-sde_stationoperations_services_items)
- [`sde_stationOperations_stationTypes_items`](#table-sde_stationoperations_stationtypes_items)
- [`sde_stationServices`](#table-sde_stationservices)
- [`sde_stationServices_description_translations`](#table-sde_stationservices_description_translations)
- [`sde_stationServices_serviceName_translations`](#table-sde_stationservices_servicename_translations)
- [`sde_stationStandingsRestrictions`](#table-sde_stationstandingsrestrictions)
- [`sde_stationStandingsRestrictions_services_items`](#table-sde_stationstandingsrestrictions_services_items)
- [`sde_systemDbuffEmitters`](#table-sde_systemdbuffemitters)
- [`sde_systemDbuffEmitters_dbuffs_items`](#table-sde_systemdbuffemitters_dbuffs_items)
- [`sde_systemWideEffects`](#table-sde_systemwideeffects)
- [`sde_systemWideEffects_dbuffs_items`](#table-sde_systemwideeffects_dbuffs_items)
- [`sde_translationLanguages`](#table-sde_translationlanguages)
- [`sde_typeBonus`](#table-sde_typebonus)
- [`sde_typeBonus_miscBonuses_items`](#table-sde_typebonus_miscbonuses_items)
- [`sde_typeBonus_miscBonuses_items_bonusText_translations`](#table-sde_typebonus_miscbonuses_items_bonustext_translations)
- [`sde_typeBonus_roleBonuses_items`](#table-sde_typebonus_rolebonuses_items)
- [`sde_typeBonus_roleBonuses_items_bonusText_translations`](#table-sde_typebonus_rolebonuses_items_bonustext_translations)
- [`sde_typeBonus_types_items`](#table-sde_typebonus_types_items)
- [`sde_typeBonus_types_items__value_items`](#table-sde_typebonus_types_items__value_items)
- [`sde_typeBonus_types_items__value_items_bonusText_translations`](#table-sde_typebonus_types_items__value_items_bonustext_translations)
- [`sde_typeDogma`](#table-sde_typedogma)
- [`sde_typeDogma_dogmaAttributes_items`](#table-sde_typedogma_dogmaattributes_items)
- [`sde_typeDogma_dogmaEffects_items`](#table-sde_typedogma_dogmaeffects_items)
- [`sde_typeElements`](#table-sde_typeelements)
- [`sde_typeElements_elements_items`](#table-sde_typeelements_elements_items)
- [`sde_typeLists`](#table-sde_typelists)
- [`sde_typeLists_displayDescription_translations`](#table-sde_typelists_displaydescription_translations)
- [`sde_typeLists_displayName_translations`](#table-sde_typelists_displayname_translations)
- [`sde_typeLists_excludedCategoryIDs_items`](#table-sde_typelists_excludedcategoryids_items)
- [`sde_typeLists_excludedGroupIDs_items`](#table-sde_typelists_excludedgroupids_items)
- [`sde_typeLists_excludedTypeIDs_items`](#table-sde_typelists_excludedtypeids_items)
- [`sde_typeLists_includedCategoryIDs_items`](#table-sde_typelists_includedcategoryids_items)
- [`sde_typeLists_includedGroupIDs_items`](#table-sde_typelists_includedgroupids_items)
- [`sde_typeLists_includedTypeIDs_items`](#table-sde_typelists_includedtypeids_items)
- [`sde_typeMaterials`](#table-sde_typematerials)
- [`sde_typeMaterials_materials_items`](#table-sde_typematerials_materials_items)
- [`sde_typeMaterials_randomizedMaterials_items`](#table-sde_typematerials_randomizedmaterials_items)
- [`sde_types`](#table-sde_types)
- [`sde_types_description_translations`](#table-sde_types_description_translations)
- [`sde_types_name_translations`](#table-sde_types_name_translations)
- [`sdeCoverageDiagnostics`](#table-sdecoveragediagnostics)
- [`sdeMapObjects`](#table-sdemapobjects)
- [`sdeRawRecords`](#table-sderawrecords)
- [`sdeSourceFiles`](#table-sdesourcefiles)

</details>

## Import metadata, raw records, and views


### Table sdeSourceFiles

- `sourceFile` (TEXT, primary key) is the archive-relative filename.
- `recordCount` (INTEGER, required) counts nonblank source lines.

### Table sdeRawRecords

- Present when imported with `--keep-raw`. Records retain all source locales.
- Primary key `(sourceFile, lineNumber)`.
- `sourceFile` (TEXT, required) references `sdeSourceFiles.sourceFile`.
- `lineNumber` (INTEGER, required) is the one-based physical source line.
- `rawJson` (TEXT, required) is the original line without its terminator. Blank lines are excluded.

### Table sdeCoverageDiagnostics

- Primary key `(sourceFile, sourcePath)`.
- `sourceFile` (TEXT, required) references `sdeSourceFiles.sourceFile`.
- `sourcePath` (TEXT, required) identifies an unmapped file, field path, or unexpected shape.
- `occurrences` (INTEGER, required) counts occurrences within the source file.

### Table _hki_import_metadata

- `id` (INTEGER, primary key) is the generated import run ID.
- `started_at_utc` and `completed_at_utc` (TEXT, required) are ISO 8601 UTC timestamps.
- `duration_ms` (INTEGER, required) is the elapsed time in milliseconds.
- `row_counts_json` (TEXT, required) holds counts keyed by table and a `sourceRecords` total.
- `warning_count` and `error_count` (INTEGER, required) count import messages by severity.
- `warnings_json` and `errors_json` (TEXT, nullable) contain JSON arrays of messages.

### Table sdeMapObjects

- View over asteroid belts, moons, planets, secondary suns, stargates, stars, and NPC stations.
- `objectKind` (TEXT, required) identifies which source table contributed the row.
- `objectID` (INTEGER, required) is the source table's `_key`. Pair it with `objectKind` for identity.
- `solarSystemID` (INTEGER, nullable) joins `sde_mapSolarSystems._key`.
- `typeID` (INTEGER, nullable) joins `sde_types._key`.

## Source _sde

**CCP file** `_sde.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde__sde`](#table-sde__sde)

### Table sde__sde

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (TEXT) from `/_key`.
- `buildNumber` (INTEGER) from `/buildNumber`.
- `releaseDate` (TEXT) from `/releaseDate`.

## Source accountingEntryTypes

**CCP file** `accountingEntryTypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_accountingEntryTypes`](#table-sde_accountingentrytypes)
- [`sde_accountingEntryTypes_description_translations`](#table-sde_accountingentrytypes_description_translations)
- [`sde_accountingEntryTypes_journalMessage_translations`](#table-sde_accountingentrytypes_journalmessage_translations)
- [`sde_accountingEntryTypes_name_translations`](#table-sde_accountingentrytypes_name_translations)

### Table sde_accountingEntryTypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `internalName` (TEXT) from `/internalName`.

### Table sde_accountingEntryTypes_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_accountingentrytypes`](#table-sde_accountingentrytypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_accountingEntryTypes_journalMessage_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_accountingentrytypes`](#table-sde_accountingentrytypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/journalMessage/{language} key`.
- `value` (TEXT) from `/journalMessage/{language}`.

### Table sde_accountingEntryTypes_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_accountingentrytypes`](#table-sde_accountingentrytypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source agentTypes

**CCP file** `agentTypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_agentTypes`](#table-sde_agenttypes)

### Table sde_agentTypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

## Source agentsInSpace

**CCP file** `agentsInSpace.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_agentsInSpace`](#table-sde_agentsinspace)

### Table sde_agentsInSpace

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `dungeonID` (INTEGER) from `/dungeonID`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `spawnPointID` (INTEGER) from `/spawnPointID`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Optional join from `dungeonID` to [`sde_dungeons._key`](#table-sde_dungeons).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source ancestries

**CCP file** `ancestries.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_ancestries`](#table-sde_ancestries)
- [`sde_ancestries_description_translations`](#table-sde_ancestries_description_translations)
- [`sde_ancestries_name_translations`](#table-sde_ancestries_name_translations)

### Table sde_ancestries

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `bloodlineID` (INTEGER) from `/bloodlineID`.
- `charisma` (INTEGER) from `/charisma`.
- `iconID` (INTEGER) from `/iconID`.
- `intelligence` (INTEGER) from `/intelligence`.
- `memory` (INTEGER) from `/memory`.
- `perception` (INTEGER) from `/perception`.
- `shortDescription` (TEXT) from `/shortDescription`.
- `willpower` (INTEGER) from `/willpower`.

**Domain joins**

- Enforced foreign key from `bloodlineID` to [`sde_bloodlines._key`](#table-sde_bloodlines).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_ancestries_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_ancestries`](#table-sde_ancestries) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_ancestries_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_ancestries`](#table-sde_ancestries) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source appliedProximityEffects

**CCP file** `appliedProximityEffects.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_appliedProximityEffects`](#table-sde_appliedproximityeffects)
- [`sde_appliedProximityEffects_dbuffs_items`](#table-sde_appliedproximityeffects_dbuffs_items)

### Table sde_appliedProximityEffects

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `delaySeconds` (INTEGER) from `/delaySeconds`.
- `radius` (INTEGER) from `/radius`.

### Table sde_appliedProximityEffects_dbuffs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_appliedproximityeffects`](#table-sde_appliedproximityeffects) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dbuffs/[] key`.
- `_key_2` (INTEGER) from `/dbuffs/[]/_key`.
- `_value` (REAL) from `/dbuffs/[]/_value`.

## Source archetypes

**CCP file** `archetypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_archetypes`](#table-sde_archetypes)
- [`sde_archetypes_description_translations`](#table-sde_archetypes_description_translations)
- [`sde_archetypes_title_translations`](#table-sde_archetypes_title_translations)

### Table sde_archetypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_archetypes_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_archetypes`](#table-sde_archetypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_archetypes_title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_archetypes`](#table-sde_archetypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/title/{language} key`.
- `value` (TEXT) from `/title/{language}`.

## Source bloodlines

**CCP file** `bloodlines.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_bloodlines`](#table-sde_bloodlines)
- [`sde_bloodlines_description_translations`](#table-sde_bloodlines_description_translations)
- [`sde_bloodlines_name_translations`](#table-sde_bloodlines_name_translations)

### Table sde_bloodlines

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `charisma` (INTEGER) from `/charisma`.
- `corporationID` (INTEGER) from `/corporationID`.
- `iconID` (INTEGER) from `/iconID`.
- `intelligence` (INTEGER) from `/intelligence`.
- `memory` (INTEGER) from `/memory`.
- `perception` (INTEGER) from `/perception`.
- `raceID` (INTEGER) from `/raceID`.
- `willpower` (INTEGER) from `/willpower`.

**Domain joins**

- Enforced foreign key from `corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `raceID` to [`sde_races._key`](#table-sde_races).

### Table sde_bloodlines_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_bloodlines`](#table-sde_bloodlines) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_bloodlines_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_bloodlines`](#table-sde_bloodlines) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source blueprints

**CCP file** `blueprints.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_blueprints`](#table-sde_blueprints)
- [`sde_blueprints_activities_copying_materials_items`](#table-sde_blueprints_activities_copying_materials_items)
- [`sde_blueprints_activities_copying_skills_items`](#table-sde_blueprints_activities_copying_skills_items)
- [`sde_blueprints_activities_invention_materials_items`](#table-sde_blueprints_activities_invention_materials_items)
- [`sde_blueprints_activities_invention_products_items`](#table-sde_blueprints_activities_invention_products_items)
- [`sde_blueprints_activities_invention_skills_items`](#table-sde_blueprints_activities_invention_skills_items)
- [`sde_blueprints_activities_manufacturing_materials_items`](#table-sde_blueprints_activities_manufacturing_materials_items)
- [`sde_blueprints_activities_manufacturing_products_items`](#table-sde_blueprints_activities_manufacturing_products_items)
- [`sde_blueprints_activities_manufacturing_skills_items`](#table-sde_blueprints_activities_manufacturing_skills_items)
- [`sde_blueprints_activities_reaction_materials_items`](#table-sde_blueprints_activities_reaction_materials_items)
- [`sde_blueprints_activities_reaction_products_items`](#table-sde_blueprints_activities_reaction_products_items)
- [`sde_blueprints_activities_reaction_skills_items`](#table-sde_blueprints_activities_reaction_skills_items)
- [`sde_blueprints_activities_research_material_materials_items`](#table-sde_blueprints_activities_research_material_materials_items)
- [`sde_blueprints_activities_research_material_skills_items`](#table-sde_blueprints_activities_research_material_skills_items)
- [`sde_blueprints_activities_research_time_materials_items`](#table-sde_blueprints_activities_research_time_materials_items)
- [`sde_blueprints_activities_research_time_skills_items`](#table-sde_blueprints_activities_research_time_skills_items)

### Table sde_blueprints

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `activities_copying_time` (INTEGER) from `/activities/copying/time`.
- `activities_invention_time` (INTEGER) from `/activities/invention/time`.
- `activities_manufacturing_time` (INTEGER) from `/activities/manufacturing/time`.
- `activities_reaction_time` (INTEGER) from `/activities/reaction/time`.
- `activities_research_material_time` (INTEGER) from `/activities/research_material/time`.
- `activities_research_time_time` (INTEGER) from `/activities/research_time/time`.
- `blueprintTypeID` (INTEGER) from `/blueprintTypeID`.
- `maxProductionLimit` (INTEGER) from `/maxProductionLimit`.

**Domain joins**

- Enforced foreign key from `blueprintTypeID` to [`sde_types._key`](#table-sde_types).
- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_copying_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/copying/materials/[] key`.
- `quantity` (INTEGER) from `/activities/copying/materials/[]/quantity`.
- `typeID` (INTEGER) from `/activities/copying/materials/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_copying_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/copying/skills/[] key`.
- `level` (INTEGER) from `/activities/copying/skills/[]/level`.
- `typeID` (INTEGER) from `/activities/copying/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_invention_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/invention/materials/[] key`.
- `quantity` (INTEGER) from `/activities/invention/materials/[]/quantity`.
- `typeID` (INTEGER) from `/activities/invention/materials/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_invention_products_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/invention/products/[] key`.
- `probability` (REAL) from `/activities/invention/products/[]/probability`.
- `quantity` (INTEGER) from `/activities/invention/products/[]/quantity`.
- `typeID` (INTEGER) from `/activities/invention/products/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_invention_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/invention/skills/[] key`.
- `level` (INTEGER) from `/activities/invention/skills/[]/level`.
- `typeID` (INTEGER) from `/activities/invention/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_manufacturing_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/manufacturing/materials/[] key`.
- `quantity` (INTEGER) from `/activities/manufacturing/materials/[]/quantity`.
- `typeID` (INTEGER) from `/activities/manufacturing/materials/[]/typeID`.

**Domain joins**

- Optional join from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_manufacturing_products_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/manufacturing/products/[] key`.
- `quantity` (INTEGER) from `/activities/manufacturing/products/[]/quantity`.
- `typeID` (INTEGER) from `/activities/manufacturing/products/[]/typeID`.

**Domain joins**

- Optional join from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_manufacturing_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/manufacturing/skills/[] key`.
- `level` (INTEGER) from `/activities/manufacturing/skills/[]/level`.
- `typeID` (INTEGER) from `/activities/manufacturing/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_reaction_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/reaction/materials/[] key`.
- `quantity` (INTEGER) from `/activities/reaction/materials/[]/quantity`.
- `typeID` (INTEGER) from `/activities/reaction/materials/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_reaction_products_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/reaction/products/[] key`.
- `quantity` (INTEGER) from `/activities/reaction/products/[]/quantity`.
- `typeID` (INTEGER) from `/activities/reaction/products/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_reaction_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/reaction/skills/[] key`.
- `level` (INTEGER) from `/activities/reaction/skills/[]/level`.
- `typeID` (INTEGER) from `/activities/reaction/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_research_material_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/research_material/materials/[] key`.
- `quantity` (INTEGER) from `/activities/research_material/materials/[]/quantity`.
- `typeID` (INTEGER) from `/activities/research_material/materials/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_research_material_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/research_material/skills/[] key`.
- `level` (INTEGER) from `/activities/research_material/skills/[]/level`.
- `typeID` (INTEGER) from `/activities/research_material/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_research_time_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/research_time/materials/[] key`.
- `quantity` (INTEGER) from `/activities/research_time/materials/[]/quantity`.
- `typeID` (INTEGER) from `/activities/research_time/materials/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_blueprints_activities_research_time_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_blueprints`](#table-sde_blueprints) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/activities/research_time/skills/[] key`.
- `level` (INTEGER) from `/activities/research_time/skills/[]/level`.
- `typeID` (INTEGER) from `/activities/research_time/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source categories

**CCP file** `categories.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_categories`](#table-sde_categories)
- [`sde_categories_name_translations`](#table-sde_categories_name_translations)

### Table sde_categories

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `iconID` (INTEGER) from `/iconID`.
- `published` (INTEGER) from `/published`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_categories_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_categories`](#table-sde_categories) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source certificates

**CCP file** `certificates.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_certificates`](#table-sde_certificates)
- [`sde_certificates_description_translations`](#table-sde_certificates_description_translations)
- [`sde_certificates_name_translations`](#table-sde_certificates_name_translations)
- [`sde_certificates_recommendedFor_items`](#table-sde_certificates_recommendedfor_items)
- [`sde_certificates_skillTypes_items`](#table-sde_certificates_skilltypes_items)

### Table sde_certificates

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `groupID` (INTEGER) from `/groupID`.

**Domain joins**

- Enforced foreign key from `groupID` to [`sde_groups._key`](#table-sde_groups).

### Table sde_certificates_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_certificates`](#table-sde_certificates) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_certificates_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_certificates`](#table-sde_certificates) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_certificates_recommendedFor_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_certificates`](#table-sde_certificates) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/recommendedFor/[] key`.
- `value` (INTEGER) from `/recommendedFor/[]`.

### Table sde_certificates_skillTypes_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_certificates`](#table-sde_certificates) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/skillTypes/[] key`.
- `_key_2` (INTEGER) from `/skillTypes/[]/_key`.
- `advanced` (INTEGER) from `/skillTypes/[]/advanced`.
- `basic` (INTEGER) from `/skillTypes/[]/basic`.
- `elite` (INTEGER) from `/skillTypes/[]/elite`.
- `improved` (INTEGER) from `/skillTypes/[]/improved`.
- `standard` (INTEGER) from `/skillTypes/[]/standard`.

## Source characterAttributes

**CCP file** `characterAttributes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_characterAttributes`](#table-sde_characterattributes)
- [`sde_characterAttributes_name_translations`](#table-sde_characterattributes_name_translations)

### Table sde_characterAttributes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `description` (TEXT) from `/description`.
- `iconID` (INTEGER) from `/iconID`.
- `notes` (TEXT) from `/notes`.
- `shortDescription` (TEXT) from `/shortDescription`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_characterAttributes_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_characterattributes`](#table-sde_characterattributes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source characterTitles

**CCP file** `characterTitles.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_characterTitles`](#table-sde_charactertitles)
- [`sde_characterTitles_name_translations`](#table-sde_charactertitles_name_translations)

### Table sde_characterTitles

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (TEXT) from `/_key`.

### Table sde_characterTitles_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_charactertitles`](#table-sde_charactertitles) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source cloneGrades

**CCP file** `cloneGrades.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_cloneGrades`](#table-sde_clonegrades)
- [`sde_cloneGrades_skills_items`](#table-sde_clonegrades_skills_items)

### Table sde_cloneGrades

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

### Table sde_cloneGrades_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_clonegrades`](#table-sde_clonegrades) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/skills/[] key`.
- `level` (INTEGER) from `/skills/[]/level`.
- `typeID` (INTEGER) from `/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source compressibleTypes

**CCP file** `compressibleTypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_compressibleTypes`](#table-sde_compressibletypes)

### Table sde_compressibleTypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `compressedTypeID` (INTEGER) from `/compressedTypeID`.

**Domain joins**

- Enforced foreign key from `compressedTypeID` to [`sde_types._key`](#table-sde_types).
- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

## Source contrabandTypes

**CCP file** `contrabandTypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_contrabandTypes`](#table-sde_contrabandtypes)
- [`sde_contrabandTypes_factions_items`](#table-sde_contrabandtypes_factions_items)

### Table sde_contrabandTypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_contrabandTypes_factions_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_contrabandtypes`](#table-sde_contrabandtypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/factions/[] key`.
- `_key_2` (INTEGER) from `/factions/[]/_key`.
- `attackMinSec` (REAL) from `/factions/[]/attackMinSec`.
- `confiscateMinSec` (REAL) from `/factions/[]/confiscateMinSec`.
- `fineByValue` (REAL) from `/factions/[]/fineByValue`.
- `standingLoss` (REAL) from `/factions/[]/standingLoss`.

## Source controlTowerResources

**CCP file** `controlTowerResources.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_controlTowerResources`](#table-sde_controltowerresources)
- [`sde_controlTowerResources_resources_items`](#table-sde_controltowerresources_resources_items)

### Table sde_controlTowerResources

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_controlTowerResources_resources_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_controltowerresources`](#table-sde_controltowerresources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/resources/[] key`.
- `factionID` (INTEGER) from `/resources/[]/factionID`.
- `minSecurityLevel` (REAL) from `/resources/[]/minSecurityLevel`.
- `purpose` (INTEGER) from `/resources/[]/purpose`.
- `quantity` (INTEGER) from `/resources/[]/quantity`.
- `resourceTypeID` (INTEGER) from `/resources/[]/resourceTypeID`.

**Domain joins**

- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Enforced foreign key from `resourceTypeID` to [`sde_types._key`](#table-sde_types).

## Source corporationActivities

**CCP file** `corporationActivities.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_corporationActivities`](#table-sde_corporationactivities)
- [`sde_corporationActivities_name_translations`](#table-sde_corporationactivities_name_translations)

### Table sde_corporationActivities

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_corporationActivities_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_corporationactivities`](#table-sde_corporationactivities) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source corporationRoleGroups

**CCP file** `corporationRoleGroups.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_corporationRoleGroups`](#table-sde_corporationrolegroups)
- [`sde_corporationRoleGroups_name_translations`](#table-sde_corporationrolegroups_name_translations)

### Table sde_corporationRoleGroups

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `appliesTo` (TEXT) from `/appliesTo`.
- `appliesToGrantable` (TEXT) from `/appliesToGrantable`.
- `isDivisional` (INTEGER) from `/isDivisional`.
- `isLocational` (INTEGER) from `/isLocational`.

### Table sde_corporationRoleGroups_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_corporationrolegroups`](#table-sde_corporationrolegroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source corporationRoles

**CCP file** `corporationRoles.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_corporationRoles`](#table-sde_corporationroles)
- [`sde_corporationRoles_description_translations`](#table-sde_corporationroles_description_translations)
- [`sde_corporationRoles_name_translations`](#table-sde_corporationroles_name_translations)
- [`sde_corporationRoles_roleGroupIDs_items`](#table-sde_corporationroles_rolegroupids_items)

### Table sde_corporationRoles

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `shortName` (TEXT) from `/shortName`.

### Table sde_corporationRoles_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_corporationroles`](#table-sde_corporationroles) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_corporationRoles_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_corporationroles`](#table-sde_corporationroles) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_corporationRoles_roleGroupIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_corporationroles`](#table-sde_corporationroles) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/roleGroupIDs/[] key`.
- `value` (INTEGER) from `/roleGroupIDs/[]`.

## Source dbuffCollections

**CCP file** `dbuffCollections.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dbuffCollections`](#table-sde_dbuffcollections)
- [`sde_dbuffCollections_displayName_translations`](#table-sde_dbuffcollections_displayname_translations)
- [`sde_dbuffCollections_itemModifiers_items`](#table-sde_dbuffcollections_itemmodifiers_items)
- [`sde_dbuffCollections_locationGroupModifiers_items`](#table-sde_dbuffcollections_locationgroupmodifiers_items)
- [`sde_dbuffCollections_locationModifiers_items`](#table-sde_dbuffcollections_locationmodifiers_items)
- [`sde_dbuffCollections_locationRequiredSkillModifiers_items`](#table-sde_dbuffcollections_locationrequiredskillmodifiers_items)

### Table sde_dbuffCollections

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `aggregateMode` (TEXT) from `/aggregateMode`.
- `developerDescription` (TEXT) from `/developerDescription`.
- `operationName` (TEXT) from `/operationName`.
- `showOutputValueInUI` (TEXT) from `/showOutputValueInUI`.

### Table sde_dbuffCollections_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dbuffcollections`](#table-sde_dbuffcollections) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

### Table sde_dbuffCollections_itemModifiers_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dbuffcollections`](#table-sde_dbuffcollections) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/itemModifiers/[] key`.
- `dogmaAttributeID` (INTEGER) from `/itemModifiers/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_dbuffCollections_locationGroupModifiers_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dbuffcollections`](#table-sde_dbuffcollections) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/locationGroupModifiers/[] key`.
- `dogmaAttributeID` (INTEGER) from `/locationGroupModifiers/[]/dogmaAttributeID`.
- `groupID` (INTEGER) from `/locationGroupModifiers/[]/groupID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `groupID` to [`sde_groups._key`](#table-sde_groups).

### Table sde_dbuffCollections_locationModifiers_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dbuffcollections`](#table-sde_dbuffcollections) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/locationModifiers/[] key`.
- `dogmaAttributeID` (INTEGER) from `/locationModifiers/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_dbuffCollections_locationRequiredSkillModifiers_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dbuffcollections`](#table-sde_dbuffcollections) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/locationRequiredSkillModifiers/[] key`.
- `dogmaAttributeID` (INTEGER) from `/locationRequiredSkillModifiers/[]/dogmaAttributeID`.
- `skillID` (INTEGER) from `/locationRequiredSkillModifiers/[]/skillID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

## Source dogmaAttributeCategories

**CCP file** `dogmaAttributeCategories.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dogmaAttributeCategories`](#table-sde_dogmaattributecategories)

### Table sde_dogmaAttributeCategories

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `description` (TEXT) from `/description`.
- `name` (TEXT) from `/name`.

## Source dogmaAttributes

**CCP file** `dogmaAttributes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dogmaAttributes`](#table-sde_dogmaattributes)
- [`sde_dogmaAttributes_displayName_translations`](#table-sde_dogmaattributes_displayname_translations)
- [`sde_dogmaAttributes_tooltipDescription_translations`](#table-sde_dogmaattributes_tooltipdescription_translations)
- [`sde_dogmaAttributes_tooltipTitle_translations`](#table-sde_dogmaattributes_tooltiptitle_translations)

### Table sde_dogmaAttributes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `attributeCategoryID` (INTEGER) from `/attributeCategoryID`.
- `chargeRechargeTimeID` (INTEGER) from `/chargeRechargeTimeID`.
- `dataType` (INTEGER) from `/dataType`.
- `defaultValue` (REAL) from `/defaultValue`.
- `description` (TEXT) from `/description`.
- `displayWhenZero` (INTEGER) from `/displayWhenZero`.
- `highIsGood` (INTEGER) from `/highIsGood`.
- `iconID` (INTEGER) from `/iconID`.
- `maxAttributeID` (INTEGER) from `/maxAttributeID`.
- `minAttributeID` (INTEGER) from `/minAttributeID`.
- `name` (TEXT) from `/name`.
- `published` (INTEGER) from `/published`.
- `stackable` (INTEGER) from `/stackable`.
- `unitID` (INTEGER) from `/unitID`.

**Domain joins**

- Enforced foreign key from `attributeCategoryID` to [`sde_dogmaAttributeCategories._key`](#table-sde_dogmaattributecategories).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `maxAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `minAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `unitID` to [`sde_dogmaUnits._key`](#table-sde_dogmaunits).

### Table sde_dogmaAttributes_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaattributes`](#table-sde_dogmaattributes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

### Table sde_dogmaAttributes_tooltipDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaattributes`](#table-sde_dogmaattributes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/tooltipDescription/{language} key`.
- `value` (TEXT) from `/tooltipDescription/{language}`.

### Table sde_dogmaAttributes_tooltipTitle_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaattributes`](#table-sde_dogmaattributes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/tooltipTitle/{language} key`.
- `value` (TEXT) from `/tooltipTitle/{language}`.

## Source dogmaEffects

**CCP file** `dogmaEffects.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dogmaEffects`](#table-sde_dogmaeffects)
- [`sde_dogmaEffects_description_translations`](#table-sde_dogmaeffects_description_translations)
- [`sde_dogmaEffects_displayName_translations`](#table-sde_dogmaeffects_displayname_translations)
- [`sde_dogmaEffects_modifierInfo_items`](#table-sde_dogmaeffects_modifierinfo_items)

### Table sde_dogmaEffects

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `disallowAutoRepeat` (INTEGER) from `/disallowAutoRepeat`.
- `dischargeAttributeID` (INTEGER) from `/dischargeAttributeID`.
- `distribution` (INTEGER) from `/distribution`.
- `durationAttributeID` (INTEGER) from `/durationAttributeID`.
- `effectCategoryID` (INTEGER) from `/effectCategoryID`.
- `electronicChance` (INTEGER) from `/electronicChance`.
- `falloffAttributeID` (INTEGER) from `/falloffAttributeID`.
- `fittingUsageChanceAttributeID` (INTEGER) from `/fittingUsageChanceAttributeID`.
- `guid` (TEXT) from `/guid`.
- `iconID` (INTEGER) from `/iconID`.
- `isAssistance` (INTEGER) from `/isAssistance`.
- `isOffensive` (INTEGER) from `/isOffensive`.
- `isWarpSafe` (INTEGER) from `/isWarpSafe`.
- `name` (TEXT) from `/name`.
- `npcActivationChanceAttributeID` (INTEGER) from `/npcActivationChanceAttributeID`.
- `npcUsageChanceAttributeID` (INTEGER) from `/npcUsageChanceAttributeID`.
- `propulsionChance` (INTEGER) from `/propulsionChance`.
- `published` (INTEGER) from `/published`.
- `rangeAttributeID` (INTEGER) from `/rangeAttributeID`.
- `rangeChance` (INTEGER) from `/rangeChance`.
- `resistanceAttributeID` (INTEGER) from `/resistanceAttributeID`.
- `trackingSpeedAttributeID` (INTEGER) from `/trackingSpeedAttributeID`.

**Domain joins**

- Enforced foreign key from `dischargeAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `durationAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `falloffAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `fittingUsageChanceAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `npcActivationChanceAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `npcUsageChanceAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `rangeAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `resistanceAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `trackingSpeedAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_dogmaEffects_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaeffects`](#table-sde_dogmaeffects) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_dogmaEffects_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaeffects`](#table-sde_dogmaeffects) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

### Table sde_dogmaEffects_modifierInfo_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaeffects`](#table-sde_dogmaeffects) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/modifierInfo/[] key`.
- `domain` (TEXT) from `/modifierInfo/[]/domain`.
- `effectID` (INTEGER) from `/modifierInfo/[]/effectID`.
- `func` (TEXT) from `/modifierInfo/[]/func`.
- `groupID` (INTEGER) from `/modifierInfo/[]/groupID`.
- `modifiedAttributeID` (INTEGER) from `/modifierInfo/[]/modifiedAttributeID`.
- `modifyingAttributeID` (INTEGER) from `/modifierInfo/[]/modifyingAttributeID`.
- `operation` (INTEGER) from `/modifierInfo/[]/operation`.
- `skillTypeID` (INTEGER) from `/modifierInfo/[]/skillTypeID`.

**Domain joins**

- Enforced foreign key from `effectID` to [`sde_dogmaEffects._key`](#table-sde_dogmaeffects).
- Enforced foreign key from `groupID` to [`sde_groups._key`](#table-sde_groups).
- Enforced foreign key from `modifiedAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `modifyingAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).
- Enforced foreign key from `skillTypeID` to [`sde_types._key`](#table-sde_types).

## Source dogmaUnits

**CCP file** `dogmaUnits.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dogmaUnits`](#table-sde_dogmaunits)
- [`sde_dogmaUnits_description_translations`](#table-sde_dogmaunits_description_translations)
- [`sde_dogmaUnits_displayName_translations`](#table-sde_dogmaunits_displayname_translations)

### Table sde_dogmaUnits

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

### Table sde_dogmaUnits_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaunits`](#table-sde_dogmaunits) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_dogmaUnits_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dogmaunits`](#table-sde_dogmaunits) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

## Source dungeons

**CCP file** `dungeons.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dungeons`](#table-sde_dungeons)
- [`sde_dungeons_allowedShipsList_items`](#table-sde_dungeons_allowedshipslist_items)
- [`sde_dungeons_description_translations`](#table-sde_dungeons_description_translations)
- [`sde_dungeons_gameplayDescription_translations`](#table-sde_dungeons_gameplaydescription_translations)
- [`sde_dungeons_name_translations`](#table-sde_dungeons_name_translations)

### Table sde_dungeons

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `archetypeID` (INTEGER) from `/archetypeID`.
- `factionID` (INTEGER) from `/factionID`.

**Domain joins**

- Enforced foreign key from `archetypeID` to [`sde_archetypes._key`](#table-sde_archetypes).
- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).

### Table sde_dungeons_allowedShipsList_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dungeons`](#table-sde_dungeons) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/allowedShipsList/[] key`.
- `value` (INTEGER) from `/allowedShipsList/[]`.

### Table sde_dungeons_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dungeons`](#table-sde_dungeons) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_dungeons_gameplayDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dungeons`](#table-sde_dungeons) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/gameplayDescription/{language} key`.
- `value` (TEXT) from `/gameplayDescription/{language}`.

### Table sde_dungeons_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dungeons`](#table-sde_dungeons) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source dynamicItemAttributes

**CCP file** `dynamicItemAttributes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_dynamicItemAttributes`](#table-sde_dynamicitemattributes)
- [`sde_dynamicItemAttributes_attributeIDs_items`](#table-sde_dynamicitemattributes_attributeids_items)
- [`sde_dynamicItemAttributes_inputOutputMapping_items`](#table-sde_dynamicitemattributes_inputoutputmapping_items)
- [`sde_dynamicItemAttributes_inputOutputMapping_items_applicableTypes_items`](#table-sde_dynamicitemattributes_inputoutputmapping_items_applicabletypes_items)

### Table sde_dynamicItemAttributes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_dynamicItemAttributes_attributeIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dynamicitemattributes`](#table-sde_dynamicitemattributes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/attributeIDs/[] key`.
- `_key_2` (INTEGER) from `/attributeIDs/[]/_key`.
- `highIsGood` (INTEGER) from `/attributeIDs/[]/highIsGood`.
- `max` (REAL) from `/attributeIDs/[]/max`.
- `min` (REAL) from `/attributeIDs/[]/min`.

### Table sde_dynamicItemAttributes_inputOutputMapping_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dynamicitemattributes`](#table-sde_dynamicitemattributes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/inputOutputMapping/[] key`.
- `resultingType` (INTEGER) from `/inputOutputMapping/[]/resultingType`.

### Table sde_dynamicItemAttributes_inputOutputMapping_items_applicableTypes_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_dynamicitemattributes_inputoutputmapping_items`](#table-sde_dynamicitemattributes_inputoutputmapping_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/inputOutputMapping/[] key`.
- `ordinal2` (INTEGER) from `/inputOutputMapping/[]/applicableTypes/[] key`.
- `value` (INTEGER) from `/inputOutputMapping/[]/applicableTypes/[]`.

## Source epicArcs

**CCP file** `epicArcs.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_epicArcs`](#table-sde_epicarcs)
- [`sde_epicArcs_missions_items`](#table-sde_epicarcs_missions_items)
- [`sde_epicArcs_name_translations`](#table-sde_epicarcs_name_translations)
- [`sde_epicArcs_missions_items_nextMissions_items`](#table-sde_epicarcs_missions_items_nextmissions_items)

### Table sde_epicArcs

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `arcRestartInterval` (INTEGER) from `/arcRestartInterval`.
- `factionID` (INTEGER) from `/factionID`.
- `iconID` (INTEGER) from `/iconID`.

**Domain joins**

- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_epicArcs_missions_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_epicarcs`](#table-sde_epicarcs) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/missions/[] key`.
- `_key_2` (INTEGER) from `/missions/[]/_key`.
- `agentID` (INTEGER) from `/missions/[]/agentID`.
- `failMissionID` (INTEGER) from `/missions/[]/failMissionID`.

### Table sde_epicArcs_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_epicarcs`](#table-sde_epicarcs) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_epicArcs_missions_items_nextMissions_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_epicarcs_missions_items`](#table-sde_epicarcs_missions_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/missions/[] key`.
- `ordinal2` (INTEGER) from `/missions/[]/nextMissions/[] key`.
- `value` (INTEGER) from `/missions/[]/nextMissions/[]`.

## Source expertSystems

**CCP file** `expertSystems.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_expertSystems`](#table-sde_expertsystems)
- [`sde_expertSystems_associatedShipTypes_items`](#table-sde_expertsystems_associatedshiptypes_items)
- [`sde_expertSystems_skillsGranted_items`](#table-sde_expertsystems_skillsgranted_items)

### Table sde_expertSystems

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `durationDays` (INTEGER) from `/durationDays`.
- `hidden` (INTEGER) from `/hidden`.
- `internalName` (TEXT) from `/internalName`.
- `retired` (INTEGER) from `/retired`.

### Table sde_expertSystems_associatedShipTypes_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_expertsystems`](#table-sde_expertsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/associatedShipTypes/[] key`.
- `value` (INTEGER) from `/associatedShipTypes/[]`.

### Table sde_expertSystems_skillsGranted_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_expertsystems`](#table-sde_expertsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/skillsGranted/[] key`.
- `level` (INTEGER) from `/skillsGranted/[]/level`.
- `typeID` (INTEGER) from `/skillsGranted/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source factions

**CCP file** `factions.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_factions`](#table-sde_factions)
- [`sde_factions_description_translations`](#table-sde_factions_description_translations)
- [`sde_factions_memberRaces_items`](#table-sde_factions_memberraces_items)
- [`sde_factions_name_translations`](#table-sde_factions_name_translations)
- [`sde_factions_shortDescription_translations`](#table-sde_factions_shortdescription_translations)

### Table sde_factions

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `corporationID` (INTEGER) from `/corporationID`.
- `flatLogo` (TEXT) from `/flatLogo`.
- `flatLogoWithName` (TEXT) from `/flatLogoWithName`.
- `iconID` (INTEGER) from `/iconID`.
- `militiaCorporationID` (INTEGER) from `/militiaCorporationID`.
- `sizeFactor` (REAL) from `/sizeFactor`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `uniqueName` (INTEGER) from `/uniqueName`.

**Domain joins**

- Enforced foreign key from `corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).

### Table sde_factions_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_factions`](#table-sde_factions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_factions_memberRaces_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_factions`](#table-sde_factions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/memberRaces/[] key`.
- `value` (INTEGER) from `/memberRaces/[]`.

### Table sde_factions_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_factions`](#table-sde_factions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_factions_shortDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_factions`](#table-sde_factions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/shortDescription/{language} key`.
- `value` (TEXT) from `/shortDescription/{language}`.

## Source fighterAbilities

**CCP file** `fighterAbilities.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_fighterAbilities`](#table-sde_fighterabilities)
- [`sde_fighterAbilities_displayName_translations`](#table-sde_fighterabilities_displayname_translations)
- [`sde_fighterAbilities_tooltipText_translations`](#table-sde_fighterabilities_tooltiptext_translations)

### Table sde_fighterAbilities

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `disallowInHighSec` (INTEGER) from `/disallowInHighSec`.
- `disallowInLowSec` (INTEGER) from `/disallowInLowSec`.
- `iconID` (INTEGER) from `/iconID`.
- `targetMode` (TEXT) from `/targetMode`.
- `turretGraphicID` (INTEGER) from `/turretGraphicID`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_fighterAbilities_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_fighterabilities`](#table-sde_fighterabilities) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

### Table sde_fighterAbilities_tooltipText_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_fighterabilities`](#table-sde_fighterabilities) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/tooltipText/{language} key`.
- `value` (TEXT) from `/tooltipText/{language}`.

## Source fighterAbilitiesByType

**CCP file** `fighterAbilitiesByType.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_fighterAbilitiesByType`](#table-sde_fighterabilitiesbytype)

### Table sde_fighterAbilitiesByType

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `abilitySlot0_abilityID` (INTEGER) from `/abilitySlot0/abilityID`.
- `abilitySlot1_abilityID` (INTEGER) from `/abilitySlot1/abilityID`.
- `abilitySlot1_cooldownSeconds` (INTEGER) from `/abilitySlot1/cooldownSeconds`.
- `abilitySlot2_abilityID` (INTEGER) from `/abilitySlot2/abilityID`.
- `abilitySlot2_charges_chargeCount` (INTEGER) from `/abilitySlot2/charges/chargeCount`.
- `abilitySlot2_charges_rearmTimeSeconds` (INTEGER) from `/abilitySlot2/charges/rearmTimeSeconds`.

## Source freelanceJobSchemas

**CCP file** `freelanceJobSchemas.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_freelanceJobSchemas`](#table-sde_freelancejobschemas)
- [`sde_freelanceJobSchemas__value_items`](#table-sde_freelancejobschemas__value_items)
- [`sde_freelanceJobSchemas__value_items_contentTags_items`](#table-sde_freelancejobschemas__value_items_contenttags_items)
- [`sde_freelanceJobSchemas__value_items_description_translations`](#table-sde_freelancejobschemas__value_items_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items)
- [`sde_freelanceJobSchemas__value_items_progressDescription_translations`](#table-sde_freelancejobschemas__value_items_progressdescription_translations)
- [`sde_freelanceJobSchemas__value_items_rewardDescription_translations`](#table-sde_freelancejobschemas__value_items_rewarddescription_translations)
- [`sde_freelanceJobSchemas__value_items_targetDescription_translations`](#table-sde_freelancejobschemas__value_items_targetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_title_translations`](#table-sde_freelancejobschemas__value_items_title_translations)
- [`sde_freelanceJobSchemas__value_items_contributionMultiplier_description_translations`](#table-sde_freelancejobschemas__value_items_contributionmultiplier_description_translations)
- [`sde_freelanceJobSchemas__value_items_contributionMultiplier_title_translations`](#table-sde_freelancejobschemas__value_items_contributionmultiplier_title_translations)
- [`sde_freelanceJobSchemas__value_items_contributionMultiplier_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_contributionmultiplier_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_description_translations`](#table-sde_freelancejobschemas__value_items_maxcontributionsperparticipant_description_translations)
- [`sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_title_translations`](#table-sde_freelancejobschemas__value_items_maxcontributionsperparticipant_title_translations)
- [`sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_maxcontributionsperparticipant_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_maxProgressPerContribution_description_translations`](#table-sde_freelancejobschemas__value_items_maxprogresspercontribution_description_translations)
- [`sde_freelanceJobSchemas__value_items_maxProgressPerContribution_title_translations`](#table-sde_freelancejobschemas__value_items_maxprogresspercontribution_title_translations)
- [`sde_freelanceJobSchemas__value_items_maxProgressPerContribution_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_maxprogresspercontribution_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_choiceLabel_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_choicelabel_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_acceptedValueTypes_items`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_acceptedvaluetypes_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_matcher_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_matcher_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionFalse_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optionfalse_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionFalse_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optionfalse_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionTrue_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optiontrue_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionTrue_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_boolean_optiontrue_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_acceptedValueTypes_items`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_acceptedvaluetypes_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_deliverylocation_unsetdescription_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_acceptedValueTypes_items`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_acceptedvaluetypes_items)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_description_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_description_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_title_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_title_translations)
- [`sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_unsetDescription_translations`](#table-sde_freelancejobschemas__value_items_parameters_items_itemdelivery_inventorytype_unsetdescription_translations)

### Table sde_freelanceJobSchemas

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_freelanceJobSchemas__value_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas`](#table-sde_freelancejobschemas) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `_key_2` (TEXT) from `/_value/[]/_key`.
- `contributionMultiplier_defaultValue` (REAL) from `/_value/[]/contributionMultiplier/defaultValue`.
- `contributionMultiplier_iconID` (TEXT) from `/_value/[]/contributionMultiplier/iconID`.
- `contributionMultiplier_maxValue` (REAL) from `/_value/[]/contributionMultiplier/maxValue`.
- `contributionMultiplier_minValue` (REAL) from `/_value/[]/contributionMultiplier/minValue`.
- `iconID` (TEXT) from `/_value/[]/iconID`.
- `maxContributionsPerParticipant_iconID` (TEXT) from `/_value/[]/maxContributionsPerParticipant/iconID`.
- `maxProgressPerContribution_iconID` (TEXT) from `/_value/[]/maxProgressPerContribution/iconID`.

**Domain joins**

- Optional join from `contributionMultiplier_iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `maxContributionsPerParticipant_iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `maxProgressPerContribution_iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_freelanceJobSchemas__value_items_contentTags_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/contentTags/[] key`.
- `value` (TEXT) from `/_value/[]/contentTags/[]`.

### Table sde_freelanceJobSchemas__value_items_description_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/description/{language} key`.
- `value` (TEXT) from `/_value/[]/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `_key_2` (TEXT) from `/_value/[]/parameters/[]/_key`.
- `boolean_default` (INTEGER) from `/_value/[]/parameters/[]/boolean/default`.
- `boolean_iconID` (TEXT) from `/_value/[]/parameters/[]/boolean/iconID`.
- `itemDelivery_deliveryLocation_iconID` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/iconID`.
- `itemDelivery_deliveryLocation_maxEntries` (INTEGER) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/maxEntries`.
- `itemDelivery_iconID` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/iconID`.
- `itemDelivery_inventoryType_iconID` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/iconID`.
- `matcher_iconID` (TEXT) from `/_value/[]/parameters/[]/matcher/iconID`.
- `matcher_maxEntries` (INTEGER) from `/_value/[]/parameters/[]/matcher/maxEntries`.
- `matcher_optional` (INTEGER) from `/_value/[]/parameters/[]/matcher/optional`.
- `matcher_type` (TEXT) from `/_value/[]/parameters/[]/matcher/type`.

**Domain joins**

- Optional join from `boolean_iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `itemDelivery_deliveryLocation_iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `itemDelivery_iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `itemDelivery_inventoryType_iconID` to [`sde_icons._key`](#table-sde_icons).
- Optional join from `matcher_iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_freelanceJobSchemas__value_items_progressDescription_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/progressDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/progressDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_rewardDescription_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/rewardDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/rewardDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_targetDescription_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/targetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/targetDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_title_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/title/{language} key`.
- `value` (TEXT) from `/_value/[]/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_contributionMultiplier_description_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/contributionMultiplier/description/{language} key`.
- `value` (TEXT) from `/_value/[]/contributionMultiplier/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_contributionMultiplier_title_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/contributionMultiplier/title/{language} key`.
- `value` (TEXT) from `/_value/[]/contributionMultiplier/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_contributionMultiplier_unsetDescription_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/contributionMultiplier/unsetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/contributionMultiplier/unsetDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_description_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/maxContributionsPerParticipant/description/{language} key`.
- `value` (TEXT) from `/_value/[]/maxContributionsPerParticipant/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_title_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/maxContributionsPerParticipant/title/{language} key`.
- `value` (TEXT) from `/_value/[]/maxContributionsPerParticipant/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_maxContributionsPerParticipant_unsetDescription_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/maxContributionsPerParticipant/unsetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/maxContributionsPerParticipant/unsetDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_maxProgressPerContribution_description_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/maxProgressPerContribution/description/{language} key`.
- `value` (TEXT) from `/_value/[]/maxProgressPerContribution/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_maxProgressPerContribution_title_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/maxProgressPerContribution/title/{language} key`.
- `value` (TEXT) from `/_value/[]/maxProgressPerContribution/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_maxProgressPerContribution_unsetDescription_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items`](#table-sde_freelancejobschemas__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `language2` (TEXT) from `/_value/[]/maxProgressPerContribution/unsetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/maxProgressPerContribution/unsetDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_choiceLabel_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/choiceLabel/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/choiceLabel/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_matcher_acceptedValueTypes_items

**Primary key** `_key, ordinal1, ordinal2, ordinal3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `ordinal3` (INTEGER) from `/_value/[]/parameters/[]/matcher/acceptedValueTypes/[] key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/matcher/acceptedValueTypes/[]`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_matcher_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/matcher/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/matcher/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_matcher_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/matcher/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/matcher/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_matcher_unsetDescription_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/matcher/unsetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/matcher/unsetDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionFalse_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/optionFalse/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/optionFalse/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionFalse_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/optionFalse/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/optionFalse/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionTrue_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/optionTrue/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/optionTrue/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_boolean_optionTrue_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/boolean/optionTrue/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/boolean/optionTrue/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_acceptedValueTypes_items

**Primary key** `_key, ordinal1, ordinal2, ordinal3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `ordinal3` (INTEGER) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/acceptedValueTypes/[] key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/acceptedValueTypes/[]`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_deliveryLocation_unsetDescription_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/unsetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/deliveryLocation/unsetDescription/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_acceptedValueTypes_items

**Primary key** `_key, ordinal1, ordinal2, ordinal3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `ordinal3` (INTEGER) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/acceptedValueTypes/[] key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/acceptedValueTypes/[]`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_description_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/description/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/description/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_title_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/title/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/title/{language}`.

### Table sde_freelanceJobSchemas__value_items_parameters_items_itemDelivery_inventoryType_unsetDescription_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_freelancejobschemas__value_items_parameters_items`](#table-sde_freelancejobschemas__value_items_parameters_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/parameters/[] key`.
- `language3` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/unsetDescription/{language} key`.
- `value` (TEXT) from `/_value/[]/parameters/[]/itemDelivery/inventoryType/unsetDescription/{language}`.

## Source graphicMaterialSets

**CCP file** `graphicMaterialSets.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_graphicMaterialSets`](#table-sde_graphicmaterialsets)

### Table sde_graphicMaterialSets

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `colorHull_a` (REAL) from `/colorHull/a`.
- `colorHull_b` (REAL) from `/colorHull/b`.
- `colorHull_g` (REAL) from `/colorHull/g`.
- `colorHull_r` (REAL) from `/colorHull/r`.
- `colorPrimary_a` (REAL) from `/colorPrimary/a`.
- `colorPrimary_b` (REAL) from `/colorPrimary/b`.
- `colorPrimary_g` (REAL) from `/colorPrimary/g`.
- `colorPrimary_r` (REAL) from `/colorPrimary/r`.
- `colorSecondary_a` (REAL) from `/colorSecondary/a`.
- `colorSecondary_b` (REAL) from `/colorSecondary/b`.
- `colorSecondary_g` (REAL) from `/colorSecondary/g`.
- `colorSecondary_r` (REAL) from `/colorSecondary/r`.
- `colorWindow_a` (REAL) from `/colorWindow/a`.
- `colorWindow_b` (REAL) from `/colorWindow/b`.
- `colorWindow_g` (REAL) from `/colorWindow/g`.
- `colorWindow_r` (REAL) from `/colorWindow/r`.
- `custommaterial1` (TEXT) from `/custommaterial1`.
- `custommaterial2` (TEXT) from `/custommaterial2`.
- `description` (TEXT) from `/description`.
- `material1` (TEXT) from `/material1`.
- `material2` (TEXT) from `/material2`.
- `material3` (TEXT) from `/material3`.
- `material4` (TEXT) from `/material4`.
- `resPathInsert` (TEXT) from `/resPathInsert`.
- `sofFactionName` (TEXT) from `/sofFactionName`.
- `sofPatternName` (TEXT) from `/sofPatternName`.
- `sofRaceHint` (TEXT) from `/sofRaceHint`.

## Source graphics

**CCP file** `graphics.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_graphics`](#table-sde_graphics)
- [`sde_graphics_sofLayout_items`](#table-sde_graphics_soflayout_items)

### Table sde_graphics

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `graphicFile` (TEXT) from `/graphicFile`.
- `iconFolder` (TEXT) from `/iconFolder`.
- `sofFactionName` (TEXT) from `/sofFactionName`.
- `sofHullName` (TEXT) from `/sofHullName`.
- `sofMaterialSetID` (INTEGER) from `/sofMaterialSetID`.
- `sofRaceName` (TEXT) from `/sofRaceName`.

### Table sde_graphics_sofLayout_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_graphics`](#table-sde_graphics) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/sofLayout/[] key`.
- `value` (TEXT) from `/sofLayout/[]`.

## Source groups

**CCP file** `groups.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_groups`](#table-sde_groups)
- [`sde_groups_name_translations`](#table-sde_groups_name_translations)

### Table sde_groups

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `anchorable` (INTEGER) from `/anchorable`.
- `anchored` (INTEGER) from `/anchored`.
- `categoryID` (INTEGER) from `/categoryID`.
- `fittableNonSingleton` (INTEGER) from `/fittableNonSingleton`.
- `iconID` (INTEGER) from `/iconID`.
- `published` (INTEGER) from `/published`.
- `useBasePrice` (INTEGER) from `/useBasePrice`.

**Domain joins**

- Enforced foreign key from `categoryID` to [`sde_categories._key`](#table-sde_categories).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_groups_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_groups`](#table-sde_groups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source icons

**CCP file** `icons.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_icons`](#table-sde_icons)

### Table sde_icons

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `iconFile` (TEXT) from `/iconFile`.

## Source industryActivities

**CCP file** `industryActivities.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_industryActivities`](#table-sde_industryactivities)

### Table sde_industryActivities

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `description` (TEXT) from `/description`.
- `name` (TEXT) from `/name`.

## Source industryAssemblyLines

**CCP file** `industryAssemblyLines.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_industryAssemblyLines`](#table-sde_industryassemblylines)
- [`sde_industryAssemblyLines_detailsPerCategory_items`](#table-sde_industryassemblylines_detailspercategory_items)
- [`sde_industryAssemblyLines_detailsPerGroup_items`](#table-sde_industryassemblylines_detailspergroup_items)
- [`sde_industryAssemblyLines_detailsPerTypeList_items`](#table-sde_industryassemblylines_detailspertypelist_items)

### Table sde_industryAssemblyLines

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `activityID` (INTEGER) from `/activityID`.
- `baseCostMultiplier` (REAL) from `/baseCostMultiplier`.
- `baseMaterialMultiplier` (REAL) from `/baseMaterialMultiplier`.
- `baseTimeMultiplier` (REAL) from `/baseTimeMultiplier`.
- `description` (TEXT) from `/description`.
- `name` (TEXT) from `/name`.

### Table sde_industryAssemblyLines_detailsPerCategory_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industryassemblylines`](#table-sde_industryassemblylines) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/detailsPerCategory/[] key`.
- `categoryID` (INTEGER) from `/detailsPerCategory/[]/categoryID`.
- `costMultiplier` (REAL) from `/detailsPerCategory/[]/costMultiplier`.
- `materialMultiplier` (REAL) from `/detailsPerCategory/[]/materialMultiplier`.
- `timeMultiplier` (REAL) from `/detailsPerCategory/[]/timeMultiplier`.

**Domain joins**

- Enforced foreign key from `categoryID` to [`sde_categories._key`](#table-sde_categories).

### Table sde_industryAssemblyLines_detailsPerGroup_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industryassemblylines`](#table-sde_industryassemblylines) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/detailsPerGroup/[] key`.
- `costMultiplier` (REAL) from `/detailsPerGroup/[]/costMultiplier`.
- `groupID` (INTEGER) from `/detailsPerGroup/[]/groupID`.
- `materialMultiplier` (REAL) from `/detailsPerGroup/[]/materialMultiplier`.
- `timeMultiplier` (REAL) from `/detailsPerGroup/[]/timeMultiplier`.

**Domain joins**

- Enforced foreign key from `groupID` to [`sde_groups._key`](#table-sde_groups).

### Table sde_industryAssemblyLines_detailsPerTypeList_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industryassemblylines`](#table-sde_industryassemblylines) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/detailsPerTypeList/[] key`.
- `materialMultiplier` (REAL) from `/detailsPerTypeList/[]/materialMultiplier`.
- `timeMultiplier` (REAL) from `/detailsPerTypeList/[]/timeMultiplier`.
- `typeListID` (INTEGER) from `/detailsPerTypeList/[]/typeListID`.

**Domain joins**

- Enforced foreign key from `typeListID` to [`sde_typeLists._key`](#table-sde_typelists).

## Source industryInstallationTypes

**CCP file** `industryInstallationTypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_industryInstallationTypes`](#table-sde_industryinstallationtypes)
- [`sde_industryInstallationTypes_assemblyLines_items`](#table-sde_industryinstallationtypes_assemblylines_items)

### Table sde_industryInstallationTypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_industryInstallationTypes_assemblyLines_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industryinstallationtypes`](#table-sde_industryinstallationtypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/assemblyLines/[] key`.
- `assemblyLineID` (INTEGER) from `/assemblyLines/[]/assemblyLineID`.

## Source industryModifierSources

**CCP file** `industryModifierSources.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_industryModifierSources`](#table-sde_industrymodifiersources)
- [`sde_industryModifierSources_copying_cost_items`](#table-sde_industrymodifiersources_copying_cost_items)
- [`sde_industryModifierSources_copying_time_items`](#table-sde_industrymodifiersources_copying_time_items)
- [`sde_industryModifierSources_invention_cost_items`](#table-sde_industrymodifiersources_invention_cost_items)
- [`sde_industryModifierSources_invention_time_items`](#table-sde_industrymodifiersources_invention_time_items)
- [`sde_industryModifierSources_manufacturing_cost_items`](#table-sde_industrymodifiersources_manufacturing_cost_items)
- [`sde_industryModifierSources_manufacturing_material_items`](#table-sde_industrymodifiersources_manufacturing_material_items)
- [`sde_industryModifierSources_manufacturing_time_items`](#table-sde_industrymodifiersources_manufacturing_time_items)
- [`sde_industryModifierSources_reaction_material_items`](#table-sde_industrymodifiersources_reaction_material_items)
- [`sde_industryModifierSources_reaction_time_items`](#table-sde_industrymodifiersources_reaction_time_items)
- [`sde_industryModifierSources_researchMaterial_cost_items`](#table-sde_industrymodifiersources_researchmaterial_cost_items)
- [`sde_industryModifierSources_researchMaterial_time_items`](#table-sde_industrymodifiersources_researchmaterial_time_items)
- [`sde_industryModifierSources_researchTime_cost_items`](#table-sde_industrymodifiersources_researchtime_cost_items)
- [`sde_industryModifierSources_researchTime_time_items`](#table-sde_industrymodifiersources_researchtime_time_items)

### Table sde_industryModifierSources

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_industryModifierSources_copying_cost_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/copying/cost/[] key`.
- `dogmaAttributeID` (INTEGER) from `/copying/cost/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_copying_time_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/copying/time/[] key`.
- `dogmaAttributeID` (INTEGER) from `/copying/time/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_invention_cost_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/invention/cost/[] key`.
- `dogmaAttributeID` (INTEGER) from `/invention/cost/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_invention_time_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/invention/time/[] key`.
- `dogmaAttributeID` (INTEGER) from `/invention/time/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_manufacturing_cost_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/manufacturing/cost/[] key`.
- `dogmaAttributeID` (INTEGER) from `/manufacturing/cost/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_manufacturing_material_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/manufacturing/material/[] key`.
- `dogmaAttributeID` (INTEGER) from `/manufacturing/material/[]/dogmaAttributeID`.
- `filterID` (INTEGER) from `/manufacturing/material/[]/filterID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_manufacturing_time_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/manufacturing/time/[] key`.
- `dogmaAttributeID` (INTEGER) from `/manufacturing/time/[]/dogmaAttributeID`.
- `filterID` (INTEGER) from `/manufacturing/time/[]/filterID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_reaction_material_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/reaction/material/[] key`.
- `dogmaAttributeID` (INTEGER) from `/reaction/material/[]/dogmaAttributeID`.
- `filterID` (INTEGER) from `/reaction/material/[]/filterID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_reaction_time_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/reaction/time/[] key`.
- `dogmaAttributeID` (INTEGER) from `/reaction/time/[]/dogmaAttributeID`.
- `filterID` (INTEGER) from `/reaction/time/[]/filterID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_researchMaterial_cost_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/researchMaterial/cost/[] key`.
- `dogmaAttributeID` (INTEGER) from `/researchMaterial/cost/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_researchMaterial_time_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/researchMaterial/time/[] key`.
- `dogmaAttributeID` (INTEGER) from `/researchMaterial/time/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_researchTime_cost_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/researchTime/cost/[] key`.
- `dogmaAttributeID` (INTEGER) from `/researchTime/cost/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_industryModifierSources_researchTime_time_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrymodifiersources`](#table-sde_industrymodifiersources) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/researchTime/time/[] key`.
- `dogmaAttributeID` (INTEGER) from `/researchTime/time/[]/dogmaAttributeID`.

**Domain joins**

- Enforced foreign key from `dogmaAttributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

## Source industryTargetFilters

**CCP file** `industryTargetFilters.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_industryTargetFilters`](#table-sde_industrytargetfilters)
- [`sde_industryTargetFilters_categoryIDs_items`](#table-sde_industrytargetfilters_categoryids_items)
- [`sde_industryTargetFilters_groupIDs_items`](#table-sde_industrytargetfilters_groupids_items)

### Table sde_industryTargetFilters

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

### Table sde_industryTargetFilters_categoryIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrytargetfilters`](#table-sde_industrytargetfilters) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/categoryIDs/[] key`.
- `value` (INTEGER) from `/categoryIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_categories._key`](#table-sde_categories).

### Table sde_industryTargetFilters_groupIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_industrytargetfilters`](#table-sde_industrytargetfilters) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/groupIDs/[] key`.
- `value` (INTEGER) from `/groupIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_groups._key`](#table-sde_groups).

## Source landmarks

**CCP file** `landmarks.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_landmarks`](#table-sde_landmarks)
- [`sde_landmarks_description_translations`](#table-sde_landmarks_description_translations)
- [`sde_landmarks_name_translations`](#table-sde_landmarks_name_translations)

### Table sde_landmarks

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `iconID` (INTEGER) from `/iconID`.
- `locationID` (INTEGER) from `/locationID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_landmarks_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_landmarks`](#table-sde_landmarks) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_landmarks_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_landmarks`](#table-sde_landmarks) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source linkWithShip

**CCP file** `linkWithShip.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_linkWithShip`](#table-sde_linkwithship)
- [`sde_linkWithShip_dbuffs_items`](#table-sde_linkwithship_dbuffs_items)

### Table sde_linkWithShip

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `applyPvpFlag` (INTEGER) from `/applyPvpFlag`.
- `canRelink` (INTEGER) from `/canRelink`.
- `characterEnergyCost` (REAL) from `/characterEnergyCost`.
- `dbuffPostLinkDuration` (INTEGER) from `/dbuffPostLinkDuration`.
- `generateCynoInhibitor` (INTEGER) from `/generateCynoInhibitor`.
- `keepDbuffDurationOnLinkBreak` (INTEGER) from `/keepDbuffDurationOnLinkBreak`.
- `linkDuration` (INTEGER) from `/linkDuration`.
- `linkEffectGraphicIDOverride` (INTEGER) from `/linkEffectGraphicIDOverride`.
- `linkableShipTypeListID` (INTEGER) from `/linkableShipTypeListID`.
- `maxLinkRange` (INTEGER) from `/maxLinkRange`.
- `omegaOnly` (INTEGER) from `/omegaOnly`.
- `solarsystemInterferenceCost` (REAL) from `/solarsystemInterferenceCost`.

### Table sde_linkWithShip_dbuffs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_linkwithship`](#table-sde_linkwithship) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dbuffs/[] key`.
- `_key_2` (INTEGER) from `/dbuffs/[]/_key`.
- `_value` (REAL) from `/dbuffs/[]/_value`.

## Source mapAsteroidBelts

**CCP file** `mapAsteroidBelts.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapAsteroidBelts`](#table-sde_mapasteroidbelts)
- [`sde_mapAsteroidBelts_uniqueName_translations`](#table-sde_mapasteroidbelts_uniquename_translations)

### Table sde_mapAsteroidBelts

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `celestialIndex` (INTEGER) from `/celestialIndex`.
- `orbitID` (INTEGER) from `/orbitID`.
- `orbitIndex` (INTEGER) from `/orbitIndex`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `radius` (REAL) from `/radius`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `statistics_density` (REAL) from `/statistics/density`.
- `statistics_eccentricity` (REAL) from `/statistics/eccentricity`.
- `statistics_escapeVelocity` (REAL) from `/statistics/escapeVelocity`.
- `statistics_locked` (INTEGER) from `/statistics/locked`.
- `statistics_massDust` (REAL) from `/statistics/massDust`.
- `statistics_massGas` (REAL) from `/statistics/massGas`.
- `statistics_orbitPeriod` (REAL) from `/statistics/orbitPeriod`.
- `statistics_orbitRadius` (REAL) from `/statistics/orbitRadius`.
- `statistics_rotationRate` (REAL) from `/statistics/rotationRate`.
- `statistics_spectralClass` (TEXT) from `/statistics/spectralClass`.
- `statistics_surfaceGravity` (REAL) from `/statistics/surfaceGravity`.
- `statistics_temperature` (REAL) from `/statistics/temperature`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_mapAsteroidBelts_uniqueName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapasteroidbelts`](#table-sde_mapasteroidbelts) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/uniqueName/{language} key`.
- `value` (TEXT) from `/uniqueName/{language}`.

## Source mapConstellations

**CCP file** `mapConstellations.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapConstellations`](#table-sde_mapconstellations)
- [`sde_mapConstellations_name_translations`](#table-sde_mapconstellations_name_translations)
- [`sde_mapConstellations_solarSystemIDs_items`](#table-sde_mapconstellations_solarsystemids_items)

### Table sde_mapConstellations

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `factionID` (INTEGER) from `/factionID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `regionID` (INTEGER) from `/regionID`.
- `wormholeClassID` (INTEGER) from `/wormholeClassID`.

**Domain joins**

- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Enforced foreign key from `regionID` to [`sde_mapRegions._key`](#table-sde_mapregions).

### Table sde_mapConstellations_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapconstellations`](#table-sde_mapconstellations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_mapConstellations_solarSystemIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapconstellations`](#table-sde_mapconstellations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/solarSystemIDs/[] key`.
- `value` (INTEGER) from `/solarSystemIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).

## Source mapMoons

**CCP file** `mapMoons.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapMoons`](#table-sde_mapmoons)
- [`sde_mapMoons_npcStationIDs_items`](#table-sde_mapmoons_npcstationids_items)
- [`sde_mapMoons_uniqueName_translations`](#table-sde_mapmoons_uniquename_translations)

### Table sde_mapMoons

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `attributes_heightMap1` (INTEGER) from `/attributes/heightMap1`.
- `attributes_heightMap2` (INTEGER) from `/attributes/heightMap2`.
- `attributes_shaderPreset` (INTEGER) from `/attributes/shaderPreset`.
- `celestialIndex` (INTEGER) from `/celestialIndex`.
- `orbitID` (INTEGER) from `/orbitID`.
- `orbitIndex` (INTEGER) from `/orbitIndex`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `radius` (REAL) from `/radius`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `statistics_density` (REAL) from `/statistics/density`.
- `statistics_eccentricity` (REAL) from `/statistics/eccentricity`.
- `statistics_escapeVelocity` (REAL) from `/statistics/escapeVelocity`.
- `statistics_locked` (INTEGER) from `/statistics/locked`.
- `statistics_massDust` (REAL) from `/statistics/massDust`.
- `statistics_massGas` (REAL) from `/statistics/massGas`.
- `statistics_orbitPeriod` (REAL) from `/statistics/orbitPeriod`.
- `statistics_orbitRadius` (REAL) from `/statistics/orbitRadius`.
- `statistics_pressure` (REAL) from `/statistics/pressure`.
- `statistics_rotationRate` (REAL) from `/statistics/rotationRate`.
- `statistics_spectralClass` (TEXT) from `/statistics/spectralClass`.
- `statistics_surfaceGravity` (REAL) from `/statistics/surfaceGravity`.
- `statistics_temperature` (REAL) from `/statistics/temperature`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_mapMoons_npcStationIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapmoons`](#table-sde_mapmoons) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/npcStationIDs/[] key`.
- `value` (INTEGER) from `/npcStationIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_npcStations._key`](#table-sde_npcstations).

### Table sde_mapMoons_uniqueName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapmoons`](#table-sde_mapmoons) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/uniqueName/{language} key`.
- `value` (TEXT) from `/uniqueName/{language}`.

## Source mapPlanets

**CCP file** `mapPlanets.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapPlanets`](#table-sde_mapplanets)
- [`sde_mapPlanets_asteroidBeltIDs_items`](#table-sde_mapplanets_asteroidbeltids_items)
- [`sde_mapPlanets_moonIDs_items`](#table-sde_mapplanets_moonids_items)
- [`sde_mapPlanets_npcStationIDs_items`](#table-sde_mapplanets_npcstationids_items)
- [`sde_mapPlanets_uniqueName_translations`](#table-sde_mapplanets_uniquename_translations)

### Table sde_mapPlanets

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `attributes_heightMap1` (INTEGER) from `/attributes/heightMap1`.
- `attributes_heightMap2` (INTEGER) from `/attributes/heightMap2`.
- `attributes_population` (INTEGER) from `/attributes/population`.
- `attributes_shaderPreset` (INTEGER) from `/attributes/shaderPreset`.
- `celestialIndex` (INTEGER) from `/celestialIndex`.
- `orbitID` (INTEGER) from `/orbitID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `radius` (INTEGER) from `/radius`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `statistics_density` (REAL) from `/statistics/density`.
- `statistics_eccentricity` (REAL) from `/statistics/eccentricity`.
- `statistics_escapeVelocity` (REAL) from `/statistics/escapeVelocity`.
- `statistics_locked` (INTEGER) from `/statistics/locked`.
- `statistics_massDust` (REAL) from `/statistics/massDust`.
- `statistics_massGas` (REAL) from `/statistics/massGas`.
- `statistics_orbitPeriod` (REAL) from `/statistics/orbitPeriod`.
- `statistics_orbitRadius` (REAL) from `/statistics/orbitRadius`.
- `statistics_pressure` (REAL) from `/statistics/pressure`.
- `statistics_rotationRate` (REAL) from `/statistics/rotationRate`.
- `statistics_spectralClass` (TEXT) from `/statistics/spectralClass`.
- `statistics_surfaceGravity` (REAL) from `/statistics/surfaceGravity`.
- `statistics_temperature` (REAL) from `/statistics/temperature`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_mapPlanets_asteroidBeltIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapplanets`](#table-sde_mapplanets) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/asteroidBeltIDs/[] key`.
- `value` (INTEGER) from `/asteroidBeltIDs/[]`.

### Table sde_mapPlanets_moonIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapplanets`](#table-sde_mapplanets) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/moonIDs/[] key`.
- `value` (INTEGER) from `/moonIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_mapMoons._key`](#table-sde_mapmoons).

### Table sde_mapPlanets_npcStationIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapplanets`](#table-sde_mapplanets) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/npcStationIDs/[] key`.
- `value` (INTEGER) from `/npcStationIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_npcStations._key`](#table-sde_npcstations).

### Table sde_mapPlanets_uniqueName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapplanets`](#table-sde_mapplanets) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/uniqueName/{language} key`.
- `value` (TEXT) from `/uniqueName/{language}`.

## Source mapRegions

**CCP file** `mapRegions.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapRegions`](#table-sde_mapregions)
- [`sde_mapRegions_constellationIDs_items`](#table-sde_mapregions_constellationids_items)
- [`sde_mapRegions_description_translations`](#table-sde_mapregions_description_translations)
- [`sde_mapRegions_name_translations`](#table-sde_mapregions_name_translations)

### Table sde_mapRegions

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `factionID` (INTEGER) from `/factionID`.
- `nebulaID` (INTEGER) from `/nebulaID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `wormholeClassID` (INTEGER) from `/wormholeClassID`.

**Domain joins**

- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).

### Table sde_mapRegions_constellationIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapregions`](#table-sde_mapregions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/constellationIDs/[] key`.
- `value` (INTEGER) from `/constellationIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_mapConstellations._key`](#table-sde_mapconstellations).

### Table sde_mapRegions_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapregions`](#table-sde_mapregions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_mapRegions_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapregions`](#table-sde_mapregions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source mapSecondarySuns

**CCP file** `mapSecondarySuns.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapSecondarySuns`](#table-sde_mapsecondarysuns)

### Table sde_mapSecondarySuns

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `effectBeaconTypeID` (INTEGER) from `/effectBeaconTypeID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Enforced foreign key from `effectBeaconTypeID` to [`sde_types._key`](#table-sde_types).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source mapSolarSystems

**CCP file** `mapSolarSystems.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapSolarSystems`](#table-sde_mapsolarsystems)
- [`sde_mapSolarSystems_disallowedAnchorCategories_items`](#table-sde_mapsolarsystems_disallowedanchorcategories_items)
- [`sde_mapSolarSystems_disallowedAnchorGroups_items`](#table-sde_mapsolarsystems_disallowedanchorgroups_items)
- [`sde_mapSolarSystems_name_translations`](#table-sde_mapsolarsystems_name_translations)
- [`sde_mapSolarSystems_planetIDs_items`](#table-sde_mapsolarsystems_planetids_items)
- [`sde_mapSolarSystems_stargateIDs_items`](#table-sde_mapsolarsystems_stargateids_items)

### Table sde_mapSolarSystems

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `border` (INTEGER) from `/border`.
- `constellationID` (INTEGER) from `/constellationID`.
- `corridor` (INTEGER) from `/corridor`.
- `factionID` (INTEGER) from `/factionID`.
- `fringe` (INTEGER) from `/fringe`.
- `hub` (INTEGER) from `/hub`.
- `international` (INTEGER) from `/international`.
- `luminosity` (REAL) from `/luminosity`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `position2D_x` (REAL) from `/position2D/x`.
- `position2D_y` (REAL) from `/position2D/y`.
- `radius` (REAL) from `/radius`.
- `regionID` (INTEGER) from `/regionID`.
- `regional` (INTEGER) from `/regional`.
- `securityClass` (TEXT) from `/securityClass`.
- `securityStatus` (REAL) from `/securityStatus`.
- `starID` (INTEGER) from `/starID`.
- `visualEffect` (TEXT) from `/visualEffect`.
- `wormholeClassID` (INTEGER) from `/wormholeClassID`.

**Domain joins**

- Enforced foreign key from `constellationID` to [`sde_mapConstellations._key`](#table-sde_mapconstellations).
- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Enforced foreign key from `regionID` to [`sde_mapRegions._key`](#table-sde_mapregions).
- Enforced foreign key from `starID` to [`sde_mapStars._key`](#table-sde_mapstars).

### Table sde_mapSolarSystems_disallowedAnchorCategories_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapsolarsystems`](#table-sde_mapsolarsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/disallowedAnchorCategories/[] key`.
- `value` (INTEGER) from `/disallowedAnchorCategories/[]`.

### Table sde_mapSolarSystems_disallowedAnchorGroups_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapsolarsystems`](#table-sde_mapsolarsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/disallowedAnchorGroups/[] key`.
- `value` (INTEGER) from `/disallowedAnchorGroups/[]`.

### Table sde_mapSolarSystems_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapsolarsystems`](#table-sde_mapsolarsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_mapSolarSystems_planetIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapsolarsystems`](#table-sde_mapsolarsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/planetIDs/[] key`.
- `value` (INTEGER) from `/planetIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_mapPlanets._key`](#table-sde_mapplanets).

### Table sde_mapSolarSystems_stargateIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mapsolarsystems`](#table-sde_mapsolarsystems) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/stargateIDs/[] key`.
- `value` (INTEGER) from `/stargateIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_mapStargates._key`](#table-sde_mapstargates).

## Source mapStargates

**CCP file** `mapStargates.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapStargates`](#table-sde_mapstargates)

### Table sde_mapStargates

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `destination_solarSystemID` (INTEGER) from `/destination/solarSystemID`.
- `destination_stargateID` (INTEGER) from `/destination/stargateID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Enforced foreign key from `destination_solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `destination_stargateID` to [`sde_mapStargates._key`](#table-sde_mapstargates).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source mapStars

**CCP file** `mapStars.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mapStars`](#table-sde_mapstars)

### Table sde_mapStars

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `radius` (INTEGER) from `/radius`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `statistics_age` (REAL) from `/statistics/age`.
- `statistics_life` (REAL) from `/statistics/life`.
- `statistics_luminosity` (REAL) from `/statistics/luminosity`.
- `statistics_spectralClass` (TEXT) from `/statistics/spectralClass`.
- `statistics_temperature` (REAL) from `/statistics/temperature`.
- `typeID` (INTEGER) from `/typeID`.

**Domain joins**

- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source marketGroups

**CCP file** `marketGroups.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_marketGroups`](#table-sde_marketgroups)
- [`sde_marketGroups_description_translations`](#table-sde_marketgroups_description_translations)
- [`sde_marketGroups_name_translations`](#table-sde_marketgroups_name_translations)

### Table sde_marketGroups

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `hasTypes` (INTEGER) from `/hasTypes`.
- `iconID` (INTEGER) from `/iconID`.
- `parentGroupID` (INTEGER) from `/parentGroupID`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `parentGroupID` to [`sde_marketGroups._key`](#table-sde_marketgroups).

### Table sde_marketGroups_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_marketgroups`](#table-sde_marketgroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_marketGroups_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_marketgroups`](#table-sde_marketgroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source masteries

**CCP file** `masteries.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_masteries`](#table-sde_masteries)
- [`sde_masteries__value_items`](#table-sde_masteries__value_items)
- [`sde_masteries__value_items__value_items`](#table-sde_masteries__value_items__value_items)

### Table sde_masteries

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_masteries__value_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_masteries`](#table-sde_masteries) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `_key_2` (INTEGER) from `/_value/[]/_key`.

### Table sde_masteries__value_items__value_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_masteries__value_items`](#table-sde_masteries__value_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `ordinal2` (INTEGER) from `/_value/[]/_value/[] key`.
- `value` (INTEGER) from `/_value/[]/_value/[]`.

## Source mercenaryTacticalOperations

**CCP file** `mercenaryTacticalOperations.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_mercenaryTacticalOperations`](#table-sde_mercenarytacticaloperations)
- [`sde_mercenaryTacticalOperations_description_translations`](#table-sde_mercenarytacticaloperations_description_translations)
- [`sde_mercenaryTacticalOperations_name_translations`](#table-sde_mercenarytacticaloperations_name_translations)

### Table sde_mercenaryTacticalOperations

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `anarchyImpact` (INTEGER) from `/anarchyImpact`.
- `anarchy_impact` (INTEGER) from `/anarchy_impact`.
- `developmentImpact` (INTEGER) from `/developmentImpact`.
- `development_impact` (INTEGER) from `/development_impact`.
- `dungeonID` (INTEGER) from `/dungeonID`.
- `infomorphBonus` (INTEGER) from `/infomorphBonus`.
- `infomorph_bonus` (INTEGER) from `/infomorph_bonus`.

**Domain joins**

- Enforced foreign key from `dungeonID` to [`sde_dungeons._key`](#table-sde_dungeons).

### Table sde_mercenaryTacticalOperations_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mercenarytacticaloperations`](#table-sde_mercenarytacticaloperations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_mercenaryTacticalOperations_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_mercenarytacticaloperations`](#table-sde_mercenarytacticaloperations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source metaGroups

**CCP file** `metaGroups.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_metaGroups`](#table-sde_metagroups)
- [`sde_metaGroups_description_translations`](#table-sde_metagroups_description_translations)
- [`sde_metaGroups_name_translations`](#table-sde_metagroups_name_translations)

### Table sde_metaGroups

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `color_b` (REAL) from `/color/b`.
- `color_g` (REAL) from `/color/g`.
- `color_r` (REAL) from `/color/r`.
- `iconID` (INTEGER) from `/iconID`.
- `iconSuffix` (TEXT) from `/iconSuffix`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).

### Table sde_metaGroups_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_metagroups`](#table-sde_metagroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_metaGroups_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_metagroups`](#table-sde_metagroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source metenoxMoonDrill

**CCP file** `metenoxMoonDrill.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_metenoxMoonDrill`](#table-sde_metenoxmoondrill)

### Table sde_metenoxMoonDrill

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `miningCycleTime` (INTEGER) from `/miningCycleTime`.
- `miningEfficiency` (REAL) from `/miningEfficiency`.
- `reagentsConsumedPerCycle` (INTEGER) from `/reagentsConsumedPerCycle`.

## Source militaryCampaignObjectives

**CCP file** `militaryCampaignObjectives.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_militaryCampaignObjectives`](#table-sde_militarycampaignobjectives)
- [`sde_militaryCampaignObjectives_contentTags_items`](#table-sde_militarycampaignobjectives_contenttags_items)
- [`sde_militaryCampaignObjectives_subtitle_translations`](#table-sde_militarycampaignobjectives_subtitle_translations)
- [`sde_militaryCampaignObjectives_title_translations`](#table-sde_militarycampaignobjectives_title_translations)
- [`sde_militaryCampaignObjectives_annotations_restrictionTooltip_translations`](#table-sde_militarycampaignobjectives_annotations_restrictiontooltip_translations)
- [`sde_militaryCampaignObjectives_annotations_warning1_translations`](#table-sde_militarycampaignobjectives_annotations_warning1_translations)
- [`sde_militaryCampaignObjectives_annotations_warning2_translations`](#table-sde_militarycampaignobjectives_annotations_warning2_translations)
- [`sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items)
- [`sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items_matcher_values_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items_matcher_values_items)
- [`sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items_matcher_values_items_values_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items_matcher_values_items_values_items)

### Table sde_militaryCampaignObjectives

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (TEXT) from `/_key`.
- `annotations_requiredEnlistmentWithFactionID` (INTEGER) from `/annotations/requiredEnlistmentWithFactionID`.
- `campaignID` (TEXT) from `/campaignID`.
- `careerPath` (TEXT) from `/careerPath`.
- `contributionMethodConfiguration_name` (TEXT) from `/contributionMethodConfiguration/name`.
- `issuer_corporationID` (INTEGER) from `/issuer/corporationID`.
- `maxProgressPerParticipant` (INTEGER) from `/maxProgressPerParticipant`.
- `presentingCharacterID` (INTEGER) from `/presentingCharacterID`.
- `rewards_isk_amountPerInterval` (INTEGER) from `/rewards/isk/amountPerInterval`.
- `rewards_isk_issuer_corporationID` (INTEGER) from `/rewards/isk/issuer/corporationID`.
- `rewards_isk_progressInterval` (INTEGER) from `/rewards/isk/progressInterval`.
- `rewards_lp_amountPerInterval` (INTEGER) from `/rewards/lp/amountPerInterval`.
- `rewards_lp_issuer_corporationID` (INTEGER) from `/rewards/lp/issuer/corporationID`.
- `rewards_lp_progressInterval` (INTEGER) from `/rewards/lp/progressInterval`.
- `rewards_standing_gainPercentPerInterval` (REAL) from `/rewards/standing/gainPercentPerInterval`.
- `rewards_standing_issuer_factionID` (INTEGER) from `/rewards/standing/issuer/factionID`.
- `rewards_standing_progressInterval` (INTEGER) from `/rewards/standing/progressInterval`.
- `targetProgress` (INTEGER) from `/targetProgress`.

**Domain joins**

- Enforced foreign key from `issuer_corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `rewards_isk_issuer_corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `rewards_lp_issuer_corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `rewards_standing_issuer_factionID` to [`sde_factions._key`](#table-sde_factions).

### Table sde_militaryCampaignObjectives_contentTags_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `ordinal1` (INTEGER) from `/contentTags/[] key`.
- `value` (TEXT) from `/contentTags/[]`.

### Table sde_militaryCampaignObjectives_subtitle_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/subtitle/{language} key`.
- `value` (TEXT) from `/subtitle/{language}`.

### Table sde_militaryCampaignObjectives_title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/title/{language} key`.
- `value` (TEXT) from `/title/{language}`.

### Table sde_militaryCampaignObjectives_annotations_restrictionTooltip_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/restrictionTooltip/{language} key`.
- `value` (TEXT) from `/annotations/restrictionTooltip/{language}`.

### Table sde_militaryCampaignObjectives_annotations_warning1_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/warning1/{language} key`.
- `value` (TEXT) from `/annotations/warning1/{language}`.

### Table sde_militaryCampaignObjectives_annotations_warning2_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/warning2/{language} key`.
- `value` (TEXT) from `/annotations/warning2/{language}`.

### Table sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives`](#table-sde_militarycampaignobjectives) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `ordinal1` (INTEGER) from `/contributionMethodConfiguration/parameters/[] key`.
- `key` (TEXT) from `/contributionMethodConfiguration/parameters/[]/key`.

### Table sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items_matcher_values_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `ordinal1` (INTEGER) from `/contributionMethodConfiguration/parameters/[] key`.
- `ordinal2` (INTEGER) from `/contributionMethodConfiguration/parameters/[]/matcher/values/[] key`.
- `valueType` (TEXT) from `/contributionMethodConfiguration/parameters/[]/matcher/values/[]/valueType`.

### Table sde_militaryCampaignObjectives_contributionMethodConfiguration_parameters_items_matcher_values_items_values_items

**Primary key** `_key, ordinal1, ordinal2, ordinal3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items_matcher_values_items`](#table-sde_militarycampaignobjectives_contributionmethodconfiguration_parameters_items_matcher_values_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `ordinal1` (INTEGER) from `/contributionMethodConfiguration/parameters/[] key`.
- `ordinal2` (INTEGER) from `/contributionMethodConfiguration/parameters/[]/matcher/values/[] key`.
- `ordinal3` (INTEGER) from `/contributionMethodConfiguration/parameters/[]/matcher/values/[]/values/[] key`.
- `value` (TEXT) from `/contributionMethodConfiguration/parameters/[]/matcher/values/[]/values/[]`.

## Source militaryCampaigns

**CCP file** `militaryCampaigns.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_militaryCampaigns`](#table-sde_militarycampaigns)
- [`sde_militaryCampaigns_subtitle_translations`](#table-sde_militarycampaigns_subtitle_translations)
- [`sde_militaryCampaigns_title_translations`](#table-sde_militarycampaigns_title_translations)
- [`sde_militaryCampaigns_annotations_briefingFailureDescription_translations`](#table-sde_militarycampaigns_annotations_briefingfailuredescription_translations)
- [`sde_militaryCampaigns_annotations_briefingFailureHeader_translations`](#table-sde_militarycampaigns_annotations_briefingfailureheader_translations)
- [`sde_militaryCampaigns_annotations_briefingFinalWords_translations`](#table-sde_militarycampaigns_annotations_briefingfinalwords_translations)
- [`sde_militaryCampaigns_annotations_briefingGoalDescription_translations`](#table-sde_militarycampaigns_annotations_briefinggoaldescription_translations)
- [`sde_militaryCampaigns_annotations_briefingHeader_translations`](#table-sde_militarycampaigns_annotations_briefingheader_translations)
- [`sde_militaryCampaigns_annotations_briefingSuccessDescription_translations`](#table-sde_militarycampaigns_annotations_briefingsuccessdescription_translations)
- [`sde_militaryCampaigns_annotations_briefingSuccessHeader_translations`](#table-sde_militarycampaigns_annotations_briefingsuccessheader_translations)
- [`sde_militaryCampaigns_annotations_finishedCampaignEnded_translations`](#table-sde_militarycampaigns_annotations_finishedcampaignended_translations)
- [`sde_militaryCampaigns_annotations_finishedFailureDescription_translations`](#table-sde_militarycampaigns_annotations_finishedfailuredescription_translations)
- [`sde_militaryCampaigns_annotations_finishedResolutionStateFailure_translations`](#table-sde_militarycampaigns_annotations_finishedresolutionstatefailure_translations)
- [`sde_militaryCampaigns_annotations_finishedResolutionStateSuccess_translations`](#table-sde_militarycampaigns_annotations_finishedresolutionstatesuccess_translations)
- [`sde_militaryCampaigns_annotations_finishedSuccessDescription_translations`](#table-sde_militarycampaigns_annotations_finishedsuccessdescription_translations)
- [`sde_militaryCampaigns_annotations_mapHeader_translations`](#table-sde_militarycampaigns_annotations_mapheader_translations)
- [`sde_militaryCampaigns_annotations_mapSection1Paragraph_translations`](#table-sde_militarycampaigns_annotations_mapsection1paragraph_translations)
- [`sde_militaryCampaigns_annotations_mapSection1Title_translations`](#table-sde_militarycampaigns_annotations_mapsection1title_translations)
- [`sde_militaryCampaigns_annotations_mapSection2Paragraph_translations`](#table-sde_militarycampaigns_annotations_mapsection2paragraph_translations)
- [`sde_militaryCampaigns_annotations_mapSection2Title_translations`](#table-sde_militarycampaigns_annotations_mapsection2title_translations)
- [`sde_militaryCampaigns_annotations_mapSection3Paragraph_translations`](#table-sde_militarycampaigns_annotations_mapsection3paragraph_translations)
- [`sde_militaryCampaigns_annotations_mapSection3Title_translations`](#table-sde_militarycampaigns_annotations_mapsection3title_translations)
- [`sde_militaryCampaigns_annotations_mapSubheader_translations`](#table-sde_militarycampaigns_annotations_mapsubheader_translations)
- [`sde_militaryCampaigns_annotations_mapTitle_translations`](#table-sde_militarycampaigns_annotations_maptitle_translations)
- [`sde_militaryCampaigns_annotations_presentingCharacterName_translations`](#table-sde_militarycampaigns_annotations_presentingcharactername_translations)
- [`sde_militaryCampaigns_annotations_presentingCharacterSubtitle_translations`](#table-sde_militarycampaigns_annotations_presentingcharactersubtitle_translations)

### Table sde_militaryCampaigns

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (TEXT) from `/_key`.
- `annotations_aoCampaignCardButtonImage` (TEXT) from `/annotations/aoCampaignCardButtonImage`.
- `annotations_backgroundVideoLoop` (TEXT) from `/annotations/backgroundVideoLoop`.
- `annotations_briefingBackground` (TEXT) from `/annotations/briefingBackground`.
- `annotations_briefingForeground` (TEXT) from `/annotations/briefingForeground`.
- `annotations_briefingMiddleground` (TEXT) from `/annotations/briefingMiddleground`.
- `annotations_campaignSet` (TEXT) from `/annotations/campaignSet`.
- `annotations_dashboardAmbientBackground` (TEXT) from `/annotations/dashboardAmbientBackground`.
- `annotations_dashboardBackground` (TEXT) from `/annotations/dashboardBackground`.
- `annotations_dashboardForeground` (TEXT) from `/annotations/dashboardForeground`.
- `annotations_dashboardMiddleground` (TEXT) from `/annotations/dashboardMiddleground`.
- `annotations_foregroundVideoIntro` (TEXT) from `/annotations/foregroundVideoIntro`.
- `annotations_foregroundVideoLoop` (TEXT) from `/annotations/foregroundVideoLoop`.
- `annotations_foregroundVideoOutro` (TEXT) from `/annotations/foregroundVideoOutro`.
- `annotations_mapFocusEntityID` (INTEGER) from `/annotations/mapFocusEntityID`.
- `annotations_middlegroundVideoIntro` (TEXT) from `/annotations/middlegroundVideoIntro`.
- `annotations_middlegroundVideoLoop` (TEXT) from `/annotations/middlegroundVideoLoop`.
- `annotations_middlegroundVideoOutro` (TEXT) from `/annotations/middlegroundVideoOutro`.
- `annotations_presentingCharacterTexturePath` (TEXT) from `/annotations/presentingCharacterTexturePath`.
- `annotations_race` (TEXT) from `/annotations/race`.
- `annotations_themePack` (TEXT) from `/annotations/themePack`.
- `annotations_towCampaignCardButtonImage` (TEXT) from `/annotations/towCampaignCardButtonImage`.
- `issuer_factionID` (INTEGER) from `/issuer/factionID`.
- `targetProgress` (INTEGER) from `/targetProgress`.

**Domain joins**

- Enforced foreign key from `issuer_factionID` to [`sde_factions._key`](#table-sde_factions).

### Table sde_militaryCampaigns_subtitle_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/subtitle/{language} key`.
- `value` (TEXT) from `/subtitle/{language}`.

### Table sde_militaryCampaigns_title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/title/{language} key`.
- `value` (TEXT) from `/title/{language}`.

### Table sde_militaryCampaigns_annotations_briefingFailureDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingFailureDescription/{language} key`.
- `value` (TEXT) from `/annotations/briefingFailureDescription/{language}`.

### Table sde_militaryCampaigns_annotations_briefingFailureHeader_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingFailureHeader/{language} key`.
- `value` (TEXT) from `/annotations/briefingFailureHeader/{language}`.

### Table sde_militaryCampaigns_annotations_briefingFinalWords_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingFinalWords/{language} key`.
- `value` (TEXT) from `/annotations/briefingFinalWords/{language}`.

### Table sde_militaryCampaigns_annotations_briefingGoalDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingGoalDescription/{language} key`.
- `value` (TEXT) from `/annotations/briefingGoalDescription/{language}`.

### Table sde_militaryCampaigns_annotations_briefingHeader_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingHeader/{language} key`.
- `value` (TEXT) from `/annotations/briefingHeader/{language}`.

### Table sde_militaryCampaigns_annotations_briefingSuccessDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingSuccessDescription/{language} key`.
- `value` (TEXT) from `/annotations/briefingSuccessDescription/{language}`.

### Table sde_militaryCampaigns_annotations_briefingSuccessHeader_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/briefingSuccessHeader/{language} key`.
- `value` (TEXT) from `/annotations/briefingSuccessHeader/{language}`.

### Table sde_militaryCampaigns_annotations_finishedCampaignEnded_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/finishedCampaignEnded/{language} key`.
- `value` (TEXT) from `/annotations/finishedCampaignEnded/{language}`.

### Table sde_militaryCampaigns_annotations_finishedFailureDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/finishedFailureDescription/{language} key`.
- `value` (TEXT) from `/annotations/finishedFailureDescription/{language}`.

### Table sde_militaryCampaigns_annotations_finishedResolutionStateFailure_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/finishedResolutionStateFailure/{language} key`.
- `value` (TEXT) from `/annotations/finishedResolutionStateFailure/{language}`.

### Table sde_militaryCampaigns_annotations_finishedResolutionStateSuccess_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/finishedResolutionStateSuccess/{language} key`.
- `value` (TEXT) from `/annotations/finishedResolutionStateSuccess/{language}`.

### Table sde_militaryCampaigns_annotations_finishedSuccessDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/finishedSuccessDescription/{language} key`.
- `value` (TEXT) from `/annotations/finishedSuccessDescription/{language}`.

### Table sde_militaryCampaigns_annotations_mapHeader_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapHeader/{language} key`.
- `value` (TEXT) from `/annotations/mapHeader/{language}`.

### Table sde_militaryCampaigns_annotations_mapSection1Paragraph_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSection1Paragraph/{language} key`.
- `value` (TEXT) from `/annotations/mapSection1Paragraph/{language}`.

### Table sde_militaryCampaigns_annotations_mapSection1Title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSection1Title/{language} key`.
- `value` (TEXT) from `/annotations/mapSection1Title/{language}`.

### Table sde_militaryCampaigns_annotations_mapSection2Paragraph_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSection2Paragraph/{language} key`.
- `value` (TEXT) from `/annotations/mapSection2Paragraph/{language}`.

### Table sde_militaryCampaigns_annotations_mapSection2Title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSection2Title/{language} key`.
- `value` (TEXT) from `/annotations/mapSection2Title/{language}`.

### Table sde_militaryCampaigns_annotations_mapSection3Paragraph_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSection3Paragraph/{language} key`.
- `value` (TEXT) from `/annotations/mapSection3Paragraph/{language}`.

### Table sde_militaryCampaigns_annotations_mapSection3Title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSection3Title/{language} key`.
- `value` (TEXT) from `/annotations/mapSection3Title/{language}`.

### Table sde_militaryCampaigns_annotations_mapSubheader_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapSubheader/{language} key`.
- `value` (TEXT) from `/annotations/mapSubheader/{language}`.

### Table sde_militaryCampaigns_annotations_mapTitle_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/mapTitle/{language} key`.
- `value` (TEXT) from `/annotations/mapTitle/{language}`.

### Table sde_militaryCampaigns_annotations_presentingCharacterName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/presentingCharacterName/{language} key`.
- `value` (TEXT) from `/annotations/presentingCharacterName/{language}`.

### Table sde_militaryCampaigns_annotations_presentingCharacterSubtitle_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_militarycampaigns`](#table-sde_militarycampaigns) joins on `(_key)`.

**Columns**

- `_key` (TEXT) from `/_key`.
- `language1` (TEXT) from `/annotations/presentingCharacterSubtitle/{language} key`.
- `value` (TEXT) from `/annotations/presentingCharacterSubtitle/{language}`.

## Source missions

**CCP file** `missions.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_missions`](#table-sde_missions)
- [`sde_missions_extraStandings_items`](#table-sde_missions_extrastandings_items)
- [`sde_missions_messages_items`](#table-sde_missions_messages_items)
- [`sde_missions_name_translations`](#table-sde_missions_name_translations)
- [`sde_missions_messages_items_translations`](#table-sde_missions_messages_items_translations)

### Table sde_missions

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `agentTypeID` (INTEGER) from `/agentTypeID`.
- `corporationID` (INTEGER) from `/corporationID`.
- `courierMission_objectiveQuantity` (INTEGER) from `/courierMission/objectiveQuantity`.
- `courierMission_objectiveSingleton` (INTEGER) from `/courierMission/objectiveSingleton`.
- `courierMission_objectiveTypeID` (INTEGER) from `/courierMission/objectiveTypeID`.
- `expirationTime` (INTEGER) from `/expirationTime`.
- `factionID` (INTEGER) from `/factionID`.
- `hasStandingRewards` (INTEGER) from `/hasStandingRewards`.
- `initialAgentGiftQuantity` (INTEGER) from `/initialAgentGiftQuantity`.
- `initialAgentGiftTypeID` (INTEGER) from `/initialAgentGiftTypeID`.
- `killMission_dropItemInMissionContainer` (INTEGER) from `/killMission/dropItemInMissionContainer`.
- `killMission_dungeonID` (INTEGER) from `/killMission/dungeonID`.
- `killMission_objectiveQuantity` (INTEGER) from `/killMission/objectiveQuantity`.
- `killMission_objectiveTypeID` (INTEGER) from `/killMission/objectiveTypeID`.
- `missionRewards_bonusReward_rewardQuantity` (INTEGER) from `/missionRewards/bonusReward/rewardQuantity`.
- `missionRewards_bonusReward_rewardTypeID` (INTEGER) from `/missionRewards/bonusReward/rewardTypeID`.
- `missionRewards_bonusTimeInterval` (INTEGER) from `/missionRewards/bonusTimeInterval`.
- `missionRewards_reward_rewardQuantity` (INTEGER) from `/missionRewards/reward/rewardQuantity`.
- `missionRewards_reward_rewardTypeID` (INTEGER) from `/missionRewards/reward/rewardTypeID`.

**Domain joins**

- Enforced foreign key from `agentTypeID` to [`sde_agentTypes._key`](#table-sde_agenttypes).
- Enforced foreign key from `corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `courierMission_objectiveTypeID` to [`sde_types._key`](#table-sde_types).
- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Enforced foreign key from `initialAgentGiftTypeID` to [`sde_types._key`](#table-sde_types).
- Optional join from `killMission_dungeonID` to [`sde_dungeons._key`](#table-sde_dungeons).
- Enforced foreign key from `killMission_objectiveTypeID` to [`sde_types._key`](#table-sde_types).
- Enforced foreign key from `missionRewards_bonusReward_rewardTypeID` to [`sde_types._key`](#table-sde_types).
- Enforced foreign key from `missionRewards_reward_rewardTypeID` to [`sde_types._key`](#table-sde_types).

### Table sde_missions_extraStandings_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_missions`](#table-sde_missions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/extraStandings/[] key`.
- `_key_2` (INTEGER) from `/extraStandings/[]/_key`.
- `_value` (REAL) from `/extraStandings/[]/_value`.

### Table sde_missions_messages_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_missions`](#table-sde_missions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/messages/[] key`.
- `_key_2` (TEXT) from `/messages/[]/_key`.

### Table sde_missions_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_missions`](#table-sde_missions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_missions_messages_items_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_missions_messages_items`](#table-sde_missions_messages_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/messages/[] key`.
- `language2` (TEXT) from `/messages/[]/{language} key`.
- `value` (TEXT) from `/messages/[]/{language}`.

## Source notificationTypes

**CCP file** `notificationTypes.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_notificationTypes`](#table-sde_notificationtypes)
- [`sde_notificationTypes_displayName_translations`](#table-sde_notificationtypes_displayname_translations)

### Table sde_notificationTypes

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `internalName` (TEXT) from `/internalName`.

### Table sde_notificationTypes_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_notificationtypes`](#table-sde_notificationtypes) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

## Source npcCharacters

**CCP file** `npcCharacters.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_npcCharacters`](#table-sde_npccharacters)
- [`sde_npcCharacters_name_translations`](#table-sde_npccharacters_name_translations)
- [`sde_npcCharacters_skills_items`](#table-sde_npccharacters_skills_items)

### Table sde_npcCharacters

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `agent_agentTypeID` (INTEGER) from `/agent/agentTypeID`.
- `agent_divisionID` (INTEGER) from `/agent/divisionID`.
- `agent_isLocator` (INTEGER) from `/agent/isLocator`.
- `agent_level` (INTEGER) from `/agent/level`.
- `ancestryID` (INTEGER) from `/ancestryID`.
- `bloodlineID` (INTEGER) from `/bloodlineID`.
- `careerID` (INTEGER) from `/careerID`.
- `ceo` (INTEGER) from `/ceo`.
- `corporationID` (INTEGER) from `/corporationID`.
- `description` (TEXT) from `/description`.
- `gender` (INTEGER) from `/gender`.
- `locationID` (INTEGER) from `/locationID`.
- `raceID` (INTEGER) from `/raceID`.
- `schoolID` (INTEGER) from `/schoolID`.
- `specialityID` (INTEGER) from `/specialityID`.
- `startDate` (TEXT) from `/startDate`.
- `uniqueName` (INTEGER) from `/uniqueName`.

**Domain joins**

- Enforced foreign key from `agent_agentTypeID` to [`sde_agentTypes._key`](#table-sde_agenttypes).
- Enforced foreign key from `agent_divisionID` to [`sde_npcCorporationDivisions._key`](#table-sde_npccorporationdivisions).
- Enforced foreign key from `ancestryID` to [`sde_ancestries._key`](#table-sde_ancestries).
- Enforced foreign key from `bloodlineID` to [`sde_bloodlines._key`](#table-sde_bloodlines).
- Enforced foreign key from `corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `raceID` to [`sde_races._key`](#table-sde_races).
- Optional join from `schoolID` to [`sde_schools._key`](#table-sde_schools).

### Table sde_npcCharacters_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccharacters`](#table-sde_npccharacters) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_npcCharacters_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccharacters`](#table-sde_npccharacters) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/skills/[] key`.
- `typeID` (INTEGER) from `/skills/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source npcCorporationDivisions

**CCP file** `npcCorporationDivisions.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_npcCorporationDivisions`](#table-sde_npccorporationdivisions)
- [`sde_npcCorporationDivisions_description_translations`](#table-sde_npccorporationdivisions_description_translations)
- [`sde_npcCorporationDivisions_leaderTypeName_translations`](#table-sde_npccorporationdivisions_leadertypename_translations)
- [`sde_npcCorporationDivisions_name_translations`](#table-sde_npccorporationdivisions_name_translations)

### Table sde_npcCorporationDivisions

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `displayName` (TEXT) from `/displayName`.
- `internalName` (TEXT) from `/internalName`.

### Table sde_npcCorporationDivisions_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporationdivisions`](#table-sde_npccorporationdivisions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_npcCorporationDivisions_leaderTypeName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporationdivisions`](#table-sde_npccorporationdivisions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/leaderTypeName/{language} key`.
- `value` (TEXT) from `/leaderTypeName/{language}`.

### Table sde_npcCorporationDivisions_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporationdivisions`](#table-sde_npccorporationdivisions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source npcCorporations

**CCP file** `npcCorporations.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_npcCorporations`](#table-sde_npccorporations)
- [`sde_npcCorporations_allowedMemberRaces_items`](#table-sde_npccorporations_allowedmemberraces_items)
- [`sde_npcCorporations_corporationTrades_items`](#table-sde_npccorporations_corporationtrades_items)
- [`sde_npcCorporations_description_translations`](#table-sde_npccorporations_description_translations)
- [`sde_npcCorporations_divisions_items`](#table-sde_npccorporations_divisions_items)
- [`sde_npcCorporations_exchangeRates_items`](#table-sde_npccorporations_exchangerates_items)
- [`sde_npcCorporations_investors_items`](#table-sde_npccorporations_investors_items)
- [`sde_npcCorporations_lpOfferTables_items`](#table-sde_npccorporations_lpoffertables_items)
- [`sde_npcCorporations_name_translations`](#table-sde_npccorporations_name_translations)

### Table sde_npcCorporations

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ceoID` (INTEGER) from `/ceoID`.
- `deleted` (INTEGER) from `/deleted`.
- `enemyID` (INTEGER) from `/enemyID`.
- `extent` (TEXT) from `/extent`.
- `factionID` (INTEGER) from `/factionID`.
- `friendID` (INTEGER) from `/friendID`.
- `hasPlayerPersonnelManager` (INTEGER) from `/hasPlayerPersonnelManager`.
- `iconID` (INTEGER) from `/iconID`.
- `initialPrice` (INTEGER) from `/initialPrice`.
- `mainActivityID` (INTEGER) from `/mainActivityID`.
- `memberLimit` (INTEGER) from `/memberLimit`.
- `minSecurity` (REAL) from `/minSecurity`.
- `minimumJoinStanding` (INTEGER) from `/minimumJoinStanding`.
- `raceID` (INTEGER) from `/raceID`.
- `secondaryActivityID` (INTEGER) from `/secondaryActivityID`.
- `sendCharTerminationMessage` (INTEGER) from `/sendCharTerminationMessage`.
- `shares` (INTEGER) from `/shares`.
- `size` (TEXT) from `/size`.
- `sizeFactor` (REAL) from `/sizeFactor`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `stationID` (INTEGER) from `/stationID`.
- `taxRate` (REAL) from `/taxRate`.
- `tickerName` (TEXT) from `/tickerName`.
- `uniqueName` (INTEGER) from `/uniqueName`.

**Domain joins**

- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `raceID` to [`sde_races._key`](#table-sde_races).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Optional join from `stationID` to [`sde_npcStations._key`](#table-sde_npcstations).

### Table sde_npcCorporations_allowedMemberRaces_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/allowedMemberRaces/[] key`.
- `value` (INTEGER) from `/allowedMemberRaces/[]`.

### Table sde_npcCorporations_corporationTrades_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/corporationTrades/[] key`.
- `_key_2` (INTEGER) from `/corporationTrades/[]/_key`.
- `_value` (REAL) from `/corporationTrades/[]/_value`.

### Table sde_npcCorporations_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_npcCorporations_divisions_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/divisions/[] key`.
- `_key_2` (INTEGER) from `/divisions/[]/_key`.
- `divisionNumber` (INTEGER) from `/divisions/[]/divisionNumber`.
- `leaderID` (INTEGER) from `/divisions/[]/leaderID`.
- `size` (INTEGER) from `/divisions/[]/size`.

### Table sde_npcCorporations_exchangeRates_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/exchangeRates/[] key`.
- `_key_2` (INTEGER) from `/exchangeRates/[]/_key`.
- `_value` (REAL) from `/exchangeRates/[]/_value`.

### Table sde_npcCorporations_investors_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/investors/[] key`.
- `_key_2` (INTEGER) from `/investors/[]/_key`.
- `_value` (INTEGER) from `/investors/[]/_value`.

### Table sde_npcCorporations_lpOfferTables_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/lpOfferTables/[] key`.
- `value` (INTEGER) from `/lpOfferTables/[]`.

### Table sde_npcCorporations_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_npccorporations`](#table-sde_npccorporations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source npcStations

**CCP file** `npcStations.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_npcStations`](#table-sde_npcstations)

### Table sde_npcStations

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `celestialIndex` (INTEGER) from `/celestialIndex`.
- `operationID` (INTEGER) from `/operationID`.
- `orbitID` (INTEGER) from `/orbitID`.
- `orbitIndex` (INTEGER) from `/orbitIndex`.
- `ownerID` (INTEGER) from `/ownerID`.
- `position_x` (REAL) from `/position/x`.
- `position_y` (REAL) from `/position/y`.
- `position_z` (REAL) from `/position/z`.
- `reprocessingEfficiency` (REAL) from `/reprocessingEfficiency`.
- `reprocessingHangarFlag` (INTEGER) from `/reprocessingHangarFlag`.
- `reprocessingStationsTake` (REAL) from `/reprocessingStationsTake`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.
- `typeID` (INTEGER) from `/typeID`.
- `useOperationName` (INTEGER) from `/useOperationName`.

**Domain joins**

- Enforced foreign key from `operationID` to [`sde_stationOperations._key`](#table-sde_stationoperations).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).
- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source planetResources

**CCP file** `planetResources.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_planetResources`](#table-sde_planetresources)

### Table sde_planetResources

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `power` (INTEGER) from `/power`.
- `reagent_amount_per_cycle` (INTEGER) from `/reagent/amount_per_cycle`.
- `reagent_cycle_period` (INTEGER) from `/reagent/cycle_period`.
- `reagent_secured_capacity` (INTEGER) from `/reagent/secured_capacity`.
- `reagent_type_id` (INTEGER) from `/reagent/type_id`.
- `reagent_unsecured_capacity` (INTEGER) from `/reagent/unsecured_capacity`.
- `workforce` (INTEGER) from `/workforce`.

## Source planetSchematics

**CCP file** `planetSchematics.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_planetSchematics`](#table-sde_planetschematics)
- [`sde_planetSchematics_name_translations`](#table-sde_planetschematics_name_translations)
- [`sde_planetSchematics_pins_items`](#table-sde_planetschematics_pins_items)
- [`sde_planetSchematics_types_items`](#table-sde_planetschematics_types_items)

### Table sde_planetSchematics

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `cycleTime` (INTEGER) from `/cycleTime`.

### Table sde_planetSchematics_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_planetschematics`](#table-sde_planetschematics) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_planetSchematics_pins_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_planetschematics`](#table-sde_planetschematics) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/pins/[] key`.
- `value` (INTEGER) from `/pins/[]`.

### Table sde_planetSchematics_types_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_planetschematics`](#table-sde_planetschematics) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/types/[] key`.
- `_key_2` (INTEGER) from `/types/[]/_key`.
- `isInput` (INTEGER) from `/types/[]/isInput`.
- `quantity` (INTEGER) from `/types/[]/quantity`.

## Source proximityTrap

**CCP file** `proximityTrap.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_proximityTrap`](#table-sde_proximitytrap)
- [`sde_proximityTrap_dbuffs_items`](#table-sde_proximitytrap_dbuffs_items)

### Table sde_proximityTrap

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `dbuffDuration` (INTEGER) from `/dbuffDuration`.
- `forceDecloakDuration` (INTEGER) from `/forceDecloakDuration`.
- `resetDelay` (INTEGER) from `/resetDelay`.
- `showPerimeterLights` (INTEGER) from `/showPerimeterLights`.
- `triggerDelay` (INTEGER) from `/triggerDelay`.
- `triggerFilterTypeListID` (INTEGER) from `/triggerFilterTypeListID`.
- `triggerRange` (INTEGER) from `/triggerRange`.

### Table sde_proximityTrap_dbuffs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_proximitytrap`](#table-sde_proximitytrap) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dbuffs/[] key`.
- `_key_2` (INTEGER) from `/dbuffs/[]/_key`.
- `_value` (REAL) from `/dbuffs/[]/_value`.

## Source races

**CCP file** `races.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_races`](#table-sde_races)
- [`sde_races_description_translations`](#table-sde_races_description_translations)
- [`sde_races_name_translations`](#table-sde_races_name_translations)
- [`sde_races_skills_items`](#table-sde_races_skills_items)

### Table sde_races

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `iconID` (INTEGER) from `/iconID`.
- `shipTypeID` (INTEGER) from `/shipTypeID`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `shipTypeID` to [`sde_types._key`](#table-sde_types).

### Table sde_races_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_races`](#table-sde_races) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_races_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_races`](#table-sde_races) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_races_skills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_races`](#table-sde_races) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/skills/[] key`.
- `_key_2` (INTEGER) from `/skills/[]/_key`.
- `_value` (INTEGER) from `/skills/[]/_value`.

## Source schoolMap

**CCP file** `schoolMap.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_schoolMap`](#table-sde_schoolmap)

### Table sde_schoolMap

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `schoolID` (INTEGER) from `/schoolID`.
- `solarSystemID` (INTEGER) from `/solarSystemID`.

**Domain joins**

- Enforced foreign key from `schoolID` to [`sde_schools._key`](#table-sde_schools).
- Enforced foreign key from `solarSystemID` to [`sde_mapSolarSystems._key`](#table-sde_mapsolarsystems).

## Source schools

**CCP file** `schools.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_schools`](#table-sde_schools)
- [`sde_schools_careerAgents_items`](#table-sde_schools_careeragents_items)
- [`sde_schools_characterDescription_translations`](#table-sde_schools_characterdescription_translations)
- [`sde_schools_description_translations`](#table-sde_schools_description_translations)
- [`sde_schools_name_translations`](#table-sde_schools_name_translations)
- [`sde_schools_startingStations_items`](#table-sde_schools_startingstations_items)
- [`sde_schools_title_translations`](#table-sde_schools_title_translations)

### Table sde_schools

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `careerID` (INTEGER) from `/careerID`.
- `corporationID` (INTEGER) from `/corporationID`.
- `iconID` (INTEGER) from `/iconID`.
- `isStarterSpaceSchool` (INTEGER) from `/isStarterSpaceSchool`.
- `raceID` (INTEGER) from `/raceID`.

**Domain joins**

- Enforced foreign key from `corporationID` to [`sde_npcCorporations._key`](#table-sde_npccorporations).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `raceID` to [`sde_races._key`](#table-sde_races).

### Table sde_schools_careerAgents_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_schools`](#table-sde_schools) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/careerAgents/[] key`.
- `value` (INTEGER) from `/careerAgents/[]`.

### Table sde_schools_characterDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_schools`](#table-sde_schools) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/characterDescription/{language} key`.
- `value` (TEXT) from `/characterDescription/{language}`.

### Table sde_schools_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_schools`](#table-sde_schools) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_schools_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_schools`](#table-sde_schools) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_schools_startingStations_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_schools`](#table-sde_schools) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/startingStations/[] key`.
- `value` (INTEGER) from `/startingStations/[]`.

### Table sde_schools_title_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_schools`](#table-sde_schools) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/title/{language} key`.
- `value` (TEXT) from `/title/{language}`.

## Source shipTreeElements

**CCP file** `shipTreeElements.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_shipTreeElements`](#table-sde_shiptreeelements)
- [`sde_shipTreeElements_description_translations`](#table-sde_shiptreeelements_description_translations)
- [`sde_shipTreeElements_name_translations`](#table-sde_shiptreeelements_name_translations)

### Table sde_shipTreeElements

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `icon` (TEXT) from `/icon`.

### Table sde_shipTreeElements_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreeelements`](#table-sde_shiptreeelements) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_shipTreeElements_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreeelements`](#table-sde_shiptreeelements) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source shipTreeFactions

**CCP file** `shipTreeFactions.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_shipTreeFactions`](#table-sde_shiptreefactions)
- [`sde_shipTreeFactions_description_translations`](#table-sde_shiptreefactions_description_translations)
- [`sde_shipTreeFactions_elements_items`](#table-sde_shiptreefactions_elements_items)

### Table sde_shipTreeFactions

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `icon` (TEXT) from `/icon`.

### Table sde_shipTreeFactions_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreefactions`](#table-sde_shiptreefactions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_shipTreeFactions_elements_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreefactions`](#table-sde_shiptreefactions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/elements/[] key`.
- `_key_2` (INTEGER) from `/elements/[]/_key`.
- `_value` (INTEGER) from `/elements/[]/_value`.

## Source shipTreeGroups

**CCP file** `shipTreeGroups.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_shipTreeGroups`](#table-sde_shiptreegroups)
- [`sde_shipTreeGroups_description_translations`](#table-sde_shiptreegroups_description_translations)
- [`sde_shipTreeGroups_elements_items`](#table-sde_shiptreegroups_elements_items)
- [`sde_shipTreeGroups_name_translations`](#table-sde_shiptreegroups_name_translations)
- [`sde_shipTreeGroups_preReqSkills_items`](#table-sde_shiptreegroups_prereqskills_items)
- [`sde_shipTreeGroups_preReqSkills_items_skills_items`](#table-sde_shiptreegroups_prereqskills_items_skills_items)

### Table sde_shipTreeGroups

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `icon` (TEXT) from `/icon`.
- `iconLarge` (TEXT) from `/iconLarge`.
- `iconSmall` (TEXT) from `/iconSmall`.
- `iconSmallNPC` (TEXT) from `/iconSmallNPC`.

### Table sde_shipTreeGroups_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreegroups`](#table-sde_shiptreegroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_shipTreeGroups_elements_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreegroups`](#table-sde_shiptreegroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/elements/[] key`.
- `_key_2` (INTEGER) from `/elements/[]/_key`.
- `_value` (INTEGER) from `/elements/[]/_value`.

### Table sde_shipTreeGroups_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreegroups`](#table-sde_shiptreegroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_shipTreeGroups_preReqSkills_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreegroups`](#table-sde_shiptreegroups) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/preReqSkills/[] key`.
- `_key_2` (INTEGER) from `/preReqSkills/[]/_key`.

### Table sde_shipTreeGroups_preReqSkills_items_skills_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_shiptreegroups_prereqskills_items`](#table-sde_shiptreegroups_prereqskills_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/preReqSkills/[] key`.
- `ordinal2` (INTEGER) from `/preReqSkills/[]/skills/[] key`.
- `_key_2` (INTEGER) from `/preReqSkills/[]/skills/[]/_key`.
- `display` (INTEGER) from `/preReqSkills/[]/skills/[]/display`.
- `level` (INTEGER) from `/preReqSkills/[]/skills/[]/level`.

## Source skillPlans

**CCP file** `skillPlans.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skillPlans`](#table-sde_skillplans)
- [`sde_skillPlans_description_translations`](#table-sde_skillplans_description_translations)
- [`sde_skillPlans_milestones_items`](#table-sde_skillplans_milestones_items)
- [`sde_skillPlans_name_translations`](#table-sde_skillplans_name_translations)
- [`sde_skillPlans_skillRequirements_items`](#table-sde_skillplans_skillrequirements_items)

### Table sde_skillPlans

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `careerPathID` (INTEGER) from `/careerPathID`.
- `factionID` (INTEGER) from `/factionID`.
- `internalName` (TEXT) from `/internalName`.
- `npcCorporationDivision` (INTEGER) from `/npcCorporationDivision`.

**Domain joins**

- Enforced foreign key from `factionID` to [`sde_factions._key`](#table-sde_factions).

### Table sde_skillPlans_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skillplans`](#table-sde_skillplans) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_skillPlans_milestones_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skillplans`](#table-sde_skillplans) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/milestones/[] key`.
- `level` (INTEGER) from `/milestones/[]/level`.
- `typeID` (INTEGER) from `/milestones/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_skillPlans_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skillplans`](#table-sde_skillplans) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

### Table sde_skillPlans_skillRequirements_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skillplans`](#table-sde_skillplans) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/skillRequirements/[] key`.
- `level` (INTEGER) from `/skillRequirements/[]/level`.
- `typeID` (INTEGER) from `/skillRequirements/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

## Source skinLicenses

**CCP file** `skinLicenses.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinLicenses`](#table-sde_skinlicenses)

### Table sde_skinLicenses

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `duration` (INTEGER) from `/duration`.
- `isSingleUse` (INTEGER) from `/isSingleUse`.
- `licenseTypeID` (INTEGER) from `/licenseTypeID`.
- `skinID` (INTEGER) from `/skinID`.

**Domain joins**

- Optional join from `licenseTypeID` to [`sde_types._key`](#table-sde_types).
- Optional join from `skinID` to [`sde_skins._key`](#table-sde_skins).

## Source skinMaterials

**CCP file** `skinMaterials.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinMaterials`](#table-sde_skinmaterials)
- [`sde_skinMaterials_displayName_translations`](#table-sde_skinmaterials_displayname_translations)

### Table sde_skinMaterials

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `materialSetID` (INTEGER) from `/materialSetID`.

### Table sde_skinMaterials_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinmaterials`](#table-sde_skinmaterials) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

## Source skinrComponentCategories

**CCP file** `skinrComponentCategories.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrComponentCategories`](#table-sde_skinrcomponentcategories)

### Table sde_skinrComponentCategories

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

## Source skinrComponentPointValues

**CCP file** `skinrComponentPointValues.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrComponentPointValues`](#table-sde_skinrcomponentpointvalues)
- [`sde_skinrComponentPointValues__value_items`](#table-sde_skinrcomponentpointvalues__value_items)

### Table sde_skinrComponentPointValues

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_skinrComponentPointValues__value_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrcomponentpointvalues`](#table-sde_skinrcomponentpointvalues) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `_key_2` (INTEGER) from `/_value/[]/_key`.
- `_value` (INTEGER) from `/_value/[]/_value`.

## Source skinrComponentRarities

**CCP file** `skinrComponentRarities.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrComponentRarities`](#table-sde_skinrcomponentrarities)
- [`sde_skinrComponentRarities_name_translations`](#table-sde_skinrcomponentrarities_name_translations)

### Table sde_skinrComponentRarities

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `rank` (INTEGER) from `/rank`.

### Table sde_skinrComponentRarities_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrcomponentrarities`](#table-sde_skinrcomponentrarities) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source skinrComponents

**CCP file** `skinrComponents.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrComponents`](#table-sde_skinrcomponents)
- [`sde_skinrComponents_associatedTypeIds_items`](#table-sde_skinrcomponents_associatedtypeids_items)
- [`sde_skinrComponents_name_translations`](#table-sde_skinrcomponents_name_translations)

### Table sde_skinrComponents

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `category` (INTEGER) from `/category`.
- `finish` (TEXT) from `/finish`.
- `iconFile` (TEXT) from `/iconFile`.
- `projectionTypeU` (TEXT) from `/projectionTypeU`.
- `projectionTypeV` (TEXT) from `/projectionTypeV`.
- `published` (INTEGER) from `/published`.
- `rarity` (INTEGER) from `/rarity`.
- `resourceFile` (TEXT) from `/resourceFile`.
- `sequenceBinder_count` (INTEGER) from `/sequenceBinder/count`.
- `sequenceBinder_itemTypeID` (INTEGER) from `/sequenceBinder/itemTypeID`.

**Domain joins**

- Enforced foreign key from `sequenceBinder_itemTypeID` to [`sde_types._key`](#table-sde_types).

### Table sde_skinrComponents_associatedTypeIds_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrcomponents`](#table-sde_skinrcomponents) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/associatedTypeIds/[] key`.
- `licenseUsesGranted` (INTEGER) from `/associatedTypeIds/[]/licenseUsesGranted`.
- `typeID` (INTEGER) from `/associatedTypeIds/[]/typeID`.

**Domain joins**

- Enforced foreign key from `typeID` to [`sde_types._key`](#table-sde_types).

### Table sde_skinrComponents_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrcomponents`](#table-sde_skinrcomponents) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source skinrSlotCategories

**CCP file** `skinrSlotCategories.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrSlotCategories`](#table-sde_skinrslotcategories)

### Table sde_skinrSlotCategories

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

## Source skinrSlotConfigurations

**CCP file** `skinrSlotConfigurations.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrSlotConfigurations`](#table-sde_skinrslotconfigurations)
- [`sde_skinrSlotConfigurations_config_items`](#table-sde_skinrslotconfigurations_config_items)
- [`sde_skinrSlotConfigurations_ships_items`](#table-sde_skinrslotconfigurations_ships_items)

### Table sde_skinrSlotConfigurations

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `allowAllShips` (INTEGER) from `/allowAllShips`.
- `name` (TEXT) from `/name`.
- `priority` (INTEGER) from `/priority`.

### Table sde_skinrSlotConfigurations_config_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrslotconfigurations`](#table-sde_skinrslotconfigurations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/config/[] key`.
- `value` (INTEGER) from `/config/[]`.

### Table sde_skinrSlotConfigurations_ships_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrslotconfigurations`](#table-sde_skinrslotconfigurations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/ships/[] key`.
- `value` (INTEGER) from `/ships/[]`.

## Source skinrSlotNames

**CCP file** `skinrSlotNames.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrSlotNames`](#table-sde_skinrslotnames)

### Table sde_skinrSlotNames

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

## Source skinrSlots

**CCP file** `skinrSlots.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrSlots`](#table-sde_skinrslots)
- [`sde_skinrSlots_allowedDesignComponentCategories_items`](#table-sde_skinrslots_alloweddesigncomponentcategories_items)
- [`sde_skinrSlots_name_translations`](#table-sde_skinrslots_name_translations)

### Table sde_skinrSlots

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `category` (INTEGER) from `/category`.

### Table sde_skinrSlots_allowedDesignComponentCategories_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrslots`](#table-sde_skinrslots) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/allowedDesignComponentCategories/[] key`.
- `value` (INTEGER) from `/allowedDesignComponentCategories/[]`.

### Table sde_skinrSlots_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrslots`](#table-sde_skinrslots) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.

## Source skinrSlotsToMaterials

**CCP file** `skinrSlotsToMaterials.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrSlotsToMaterials`](#table-sde_skinrslotstomaterials)
- [`sde_skinrSlotsToMaterials__value_items`](#table-sde_skinrslotstomaterials__value_items)

### Table sde_skinrSlotsToMaterials

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_skinrSlotsToMaterials__value_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrslotstomaterials`](#table-sde_skinrslotstomaterials) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `materialID` (INTEGER) from `/_value/[]/materialID`.
- `slotID` (INTEGER) from `/_value/[]/slotID`.

## Source skinrTierThresholds

**CCP file** `skinrTierThresholds.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skinrTierThresholds`](#table-sde_skinrtierthresholds)
- [`sde_skinrTierThresholds__value_items`](#table-sde_skinrtierthresholds__value_items)

### Table sde_skinrTierThresholds

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_skinrTierThresholds__value_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skinrtierthresholds`](#table-sde_skinrtierthresholds) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/_value/[] key`.
- `_key_2` (INTEGER) from `/_value/[]/_key`.
- `_value` (INTEGER) from `/_value/[]/_value`.

## Source skins

**CCP file** `skins.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_skins`](#table-sde_skins)
- [`sde_skins_skinDescription_translations`](#table-sde_skins_skindescription_translations)
- [`sde_skins_types_items`](#table-sde_skins_types_items)

### Table sde_skins

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `allowCCPDevs` (INTEGER) from `/allowCCPDevs`.
- `internalName` (TEXT) from `/internalName`.
- `isStructureSkin` (INTEGER) from `/isStructureSkin`.
- `skinMaterialID` (INTEGER) from `/skinMaterialID`.
- `visibleSerenity` (INTEGER) from `/visibleSerenity`.
- `visibleTranquility` (INTEGER) from `/visibleTranquility`.

**Domain joins**

- Enforced foreign key from `skinMaterialID` to [`sde_skinMaterials._key`](#table-sde_skinmaterials).

### Table sde_skins_skinDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skins`](#table-sde_skins) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/skinDescription/{language} key`.
- `value` (TEXT) from `/skinDescription/{language}`.

### Table sde_skins_types_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_skins`](#table-sde_skins) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/types/[] key`.
- `value` (INTEGER) from `/types/[]`.

## Source sovereigntyUpgrades

**CCP file** `sovereigntyUpgrades.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_sovereigntyUpgrades`](#table-sde_sovereigntyupgrades)

### Table sde_sovereigntyUpgrades

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `fuel_hourly_upkeep` (INTEGER) from `/fuel/hourly_upkeep`.
- `fuel_startup_cost` (INTEGER) from `/fuel/startup_cost`.
- `fuel_type_id` (INTEGER) from `/fuel/type_id`.
- `mutually_exclusive_group` (TEXT) from `/mutually_exclusive_group`.
- `power_allocation` (INTEGER) from `/power_allocation`.
- `power_production` (INTEGER) from `/power_production`.
- `workforce_allocation` (INTEGER) from `/workforce_allocation`.
- `workforce_production` (INTEGER) from `/workforce_production`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

## Source stationOperations

**CCP file** `stationOperations.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_stationOperations`](#table-sde_stationoperations)
- [`sde_stationOperations_description_translations`](#table-sde_stationoperations_description_translations)
- [`sde_stationOperations_operationName_translations`](#table-sde_stationoperations_operationname_translations)
- [`sde_stationOperations_services_items`](#table-sde_stationoperations_services_items)
- [`sde_stationOperations_stationTypes_items`](#table-sde_stationoperations_stationtypes_items)

### Table sde_stationOperations

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `activityID` (INTEGER) from `/activityID`.
- `border` (REAL) from `/border`.
- `corridor` (REAL) from `/corridor`.
- `fringe` (REAL) from `/fringe`.
- `hub` (REAL) from `/hub`.
- `manufacturingFactor` (REAL) from `/manufacturingFactor`.
- `ratio` (REAL) from `/ratio`.
- `researchFactor` (REAL) from `/researchFactor`.

### Table sde_stationOperations_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationoperations`](#table-sde_stationoperations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_stationOperations_operationName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationoperations`](#table-sde_stationoperations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/operationName/{language} key`.
- `value` (TEXT) from `/operationName/{language}`.

### Table sde_stationOperations_services_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationoperations`](#table-sde_stationoperations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/services/[] key`.
- `value` (INTEGER) from `/services/[]`.

### Table sde_stationOperations_stationTypes_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationoperations`](#table-sde_stationoperations) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/stationTypes/[] key`.
- `_key_2` (INTEGER) from `/stationTypes/[]/_key`.
- `_value` (INTEGER) from `/stationTypes/[]/_value`.

## Source stationServices

**CCP file** `stationServices.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_stationServices`](#table-sde_stationservices)
- [`sde_stationServices_description_translations`](#table-sde_stationservices_description_translations)
- [`sde_stationServices_serviceName_translations`](#table-sde_stationservices_servicename_translations)

### Table sde_stationServices

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_stationServices_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationservices`](#table-sde_stationservices) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_stationServices_serviceName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationservices`](#table-sde_stationservices) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/serviceName/{language} key`.
- `value` (TEXT) from `/serviceName/{language}`.

## Source stationStandingsRestrictions

**CCP file** `stationStandingsRestrictions.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_stationStandingsRestrictions`](#table-sde_stationstandingsrestrictions)
- [`sde_stationStandingsRestrictions_services_items`](#table-sde_stationstandingsrestrictions_services_items)

### Table sde_stationStandingsRestrictions

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_stationStandingsRestrictions_services_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_stationstandingsrestrictions`](#table-sde_stationstandingsrestrictions) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/services/[] key`.
- `_key_2` (INTEGER) from `/services/[]/_key`.
- `_value` (REAL) from `/services/[]/_value`.

## Source systemDbuffEmitters

**CCP file** `systemDbuffEmitters.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_systemDbuffEmitters`](#table-sde_systemdbuffemitters)
- [`sde_systemDbuffEmitters_dbuffs_items`](#table-sde_systemdbuffemitters_dbuffs_items)

### Table sde_systemDbuffEmitters

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `duration` (INTEGER) from `/duration`.
- `excludeProtected` (INTEGER) from `/excludeProtected`.
- `interval` (INTEGER) from `/interval`.

### Table sde_systemDbuffEmitters_dbuffs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_systemdbuffemitters`](#table-sde_systemdbuffemitters) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dbuffs/[] key`.
- `_key_2` (INTEGER) from `/dbuffs/[]/_key`.
- `_value` (REAL) from `/dbuffs/[]/_value`.

## Source systemWideEffects

**CCP file** `systemWideEffects.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_systemWideEffects`](#table-sde_systemwideeffects)
- [`sde_systemWideEffects_dbuffs_items`](#table-sde_systemwideeffects_dbuffs_items)

### Table sde_systemWideEffects

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `eligibleTypeListID` (INTEGER) from `/eligibleTypeListID`.
- `environmentTypeID` (INTEGER) from `/environmentTypeID`.

**Domain joins**

- Enforced foreign key from `environmentTypeID` to [`sde_types._key`](#table-sde_types).

### Table sde_systemWideEffects_dbuffs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_systemwideeffects`](#table-sde_systemwideeffects) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dbuffs/[] key`.
- `_key_2` (INTEGER) from `/dbuffs/[]/_key`.
- `_value` (REAL) from `/dbuffs/[]/_value`.

## Source translationLanguages

**CCP file** `translationLanguages.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_translationLanguages`](#table-sde_translationlanguages)

### Table sde_translationLanguages

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (TEXT) from `/_key`.
- `name` (TEXT) from `/name`.

## Source typeBonus

**CCP file** `typeBonus.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_typeBonus`](#table-sde_typebonus)
- [`sde_typeBonus_miscBonuses_items`](#table-sde_typebonus_miscbonuses_items)
- [`sde_typeBonus_roleBonuses_items`](#table-sde_typebonus_rolebonuses_items)
- [`sde_typeBonus_types_items`](#table-sde_typebonus_types_items)
- [`sde_typeBonus_miscBonuses_items_bonusText_translations`](#table-sde_typebonus_miscbonuses_items_bonustext_translations)
- [`sde_typeBonus_roleBonuses_items_bonusText_translations`](#table-sde_typebonus_rolebonuses_items_bonustext_translations)
- [`sde_typeBonus_types_items__value_items`](#table-sde_typebonus_types_items__value_items)
- [`sde_typeBonus_types_items__value_items_bonusText_translations`](#table-sde_typebonus_types_items__value_items_bonustext_translations)

### Table sde_typeBonus

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `iconID` (INTEGER) from `/iconID`.

**Domain joins**

- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_typeBonus_miscBonuses_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus`](#table-sde_typebonus) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/miscBonuses/[] key`.
- `bonus` (REAL) from `/miscBonuses/[]/bonus`.
- `importance` (INTEGER) from `/miscBonuses/[]/importance`.
- `isPositive` (INTEGER) from `/miscBonuses/[]/isPositive`.
- `unitID` (INTEGER) from `/miscBonuses/[]/unitID`.

**Domain joins**

- Enforced foreign key from `unitID` to [`sde_dogmaUnits._key`](#table-sde_dogmaunits).

### Table sde_typeBonus_roleBonuses_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus`](#table-sde_typebonus) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/roleBonuses/[] key`.
- `bonus` (REAL) from `/roleBonuses/[]/bonus`.
- `importance` (INTEGER) from `/roleBonuses/[]/importance`.
- `unitID` (INTEGER) from `/roleBonuses/[]/unitID`.

**Domain joins**

- Enforced foreign key from `unitID` to [`sde_dogmaUnits._key`](#table-sde_dogmaunits).

### Table sde_typeBonus_types_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus`](#table-sde_typebonus) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/types/[] key`.
- `_key_2` (INTEGER) from `/types/[]/_key`.

### Table sde_typeBonus_miscBonuses_items_bonusText_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus_miscbonuses_items`](#table-sde_typebonus_miscbonuses_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/miscBonuses/[] key`.
- `language2` (TEXT) from `/miscBonuses/[]/bonusText/{language} key`.
- `value` (TEXT) from `/miscBonuses/[]/bonusText/{language}`.

### Table sde_typeBonus_roleBonuses_items_bonusText_translations

**Primary key** `_key, ordinal1, language2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus_rolebonuses_items`](#table-sde_typebonus_rolebonuses_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/roleBonuses/[] key`.
- `language2` (TEXT) from `/roleBonuses/[]/bonusText/{language} key`.
- `value` (TEXT) from `/roleBonuses/[]/bonusText/{language}`.

### Table sde_typeBonus_types_items__value_items

**Primary key** `_key, ordinal1, ordinal2`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus_types_items`](#table-sde_typebonus_types_items) joins on `(_key, ordinal1)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/types/[] key`.
- `ordinal2` (INTEGER) from `/types/[]/_value/[] key`.
- `bonus` (REAL) from `/types/[]/_value/[]/bonus`.
- `importance` (INTEGER) from `/types/[]/_value/[]/importance`.
- `unitID` (INTEGER) from `/types/[]/_value/[]/unitID`.

**Domain joins**

- Enforced foreign key from `unitID` to [`sde_dogmaUnits._key`](#table-sde_dogmaunits).

### Table sde_typeBonus_types_items__value_items_bonusText_translations

**Primary key** `_key, ordinal1, ordinal2, language3`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typebonus_types_items__value_items`](#table-sde_typebonus_types_items__value_items) joins on `(_key, ordinal1, ordinal2)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/types/[] key`.
- `ordinal2` (INTEGER) from `/types/[]/_value/[] key`.
- `language3` (TEXT) from `/types/[]/_value/[]/bonusText/{language} key`.
- `value` (TEXT) from `/types/[]/_value/[]/bonusText/{language}`.

## Source typeDogma

**CCP file** `typeDogma.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_typeDogma`](#table-sde_typedogma)
- [`sde_typeDogma_dogmaAttributes_items`](#table-sde_typedogma_dogmaattributes_items)
- [`sde_typeDogma_dogmaEffects_items`](#table-sde_typedogma_dogmaeffects_items)

### Table sde_typeDogma

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_typeDogma_dogmaAttributes_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typedogma`](#table-sde_typedogma) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dogmaAttributes/[] key`.
- `attributeID` (INTEGER) from `/dogmaAttributes/[]/attributeID`.
- `value` (REAL) from `/dogmaAttributes/[]/value`.

**Domain joins**

- Enforced foreign key from `attributeID` to [`sde_dogmaAttributes._key`](#table-sde_dogmaattributes).

### Table sde_typeDogma_dogmaEffects_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typedogma`](#table-sde_typedogma) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/dogmaEffects/[] key`.
- `effectID` (INTEGER) from `/dogmaEffects/[]/effectID`.
- `isDefault` (INTEGER) from `/dogmaEffects/[]/isDefault`.

**Domain joins**

- Enforced foreign key from `effectID` to [`sde_dogmaEffects._key`](#table-sde_dogmaeffects).

## Source typeElements

**CCP file** `typeElements.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_typeElements`](#table-sde_typeelements)
- [`sde_typeElements_elements_items`](#table-sde_typeelements_elements_items)

### Table sde_typeElements

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

### Table sde_typeElements_elements_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typeelements`](#table-sde_typeelements) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/elements/[] key`.
- `_key_2` (INTEGER) from `/elements/[]/_key`.
- `_value` (INTEGER) from `/elements/[]/_value`.

## Source typeLists

**CCP file** `typeLists.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_typeLists`](#table-sde_typelists)
- [`sde_typeLists_displayDescription_translations`](#table-sde_typelists_displaydescription_translations)
- [`sde_typeLists_displayName_translations`](#table-sde_typelists_displayname_translations)
- [`sde_typeLists_excludedCategoryIDs_items`](#table-sde_typelists_excludedcategoryids_items)
- [`sde_typeLists_excludedGroupIDs_items`](#table-sde_typelists_excludedgroupids_items)
- [`sde_typeLists_excludedTypeIDs_items`](#table-sde_typelists_excludedtypeids_items)
- [`sde_typeLists_includedCategoryIDs_items`](#table-sde_typelists_includedcategoryids_items)
- [`sde_typeLists_includedGroupIDs_items`](#table-sde_typelists_includedgroupids_items)
- [`sde_typeLists_includedTypeIDs_items`](#table-sde_typelists_includedtypeids_items)

### Table sde_typeLists

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `name` (TEXT) from `/name`.

### Table sde_typeLists_displayDescription_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayDescription/{language} key`.
- `value` (TEXT) from `/displayDescription/{language}`.

### Table sde_typeLists_displayName_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/displayName/{language} key`.
- `value` (TEXT) from `/displayName/{language}`.

### Table sde_typeLists_excludedCategoryIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/excludedCategoryIDs/[] key`.
- `value` (INTEGER) from `/excludedCategoryIDs/[]`.

### Table sde_typeLists_excludedGroupIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/excludedGroupIDs/[] key`.
- `value` (INTEGER) from `/excludedGroupIDs/[]`.

### Table sde_typeLists_excludedTypeIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/excludedTypeIDs/[] key`.
- `value` (INTEGER) from `/excludedTypeIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_types._key`](#table-sde_types).

### Table sde_typeLists_includedCategoryIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/includedCategoryIDs/[] key`.
- `value` (INTEGER) from `/includedCategoryIDs/[]`.

### Table sde_typeLists_includedGroupIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/includedGroupIDs/[] key`.
- `value` (INTEGER) from `/includedGroupIDs/[]`.

### Table sde_typeLists_includedTypeIDs_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typelists`](#table-sde_typelists) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/includedTypeIDs/[] key`.
- `value` (INTEGER) from `/includedTypeIDs/[]`.

**Domain joins**

- Enforced foreign key from `value` to [`sde_types._key`](#table-sde_types).

## Source typeMaterials

**CCP file** `typeMaterials.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_typeMaterials`](#table-sde_typematerials)
- [`sde_typeMaterials_materials_items`](#table-sde_typematerials_materials_items)
- [`sde_typeMaterials_randomizedMaterials_items`](#table-sde_typematerials_randomizedmaterials_items)

### Table sde_typeMaterials

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.

**Domain joins**

- Enforced foreign key from `_key` to [`sde_types._key`](#table-sde_types).

### Table sde_typeMaterials_materials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typematerials`](#table-sde_typematerials) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/materials/[] key`.
- `materialTypeID` (INTEGER) from `/materials/[]/materialTypeID`.
- `quantity` (INTEGER) from `/materials/[]/quantity`.

**Domain joins**

- Enforced foreign key from `materialTypeID` to [`sde_types._key`](#table-sde_types).

### Table sde_typeMaterials_randomizedMaterials_items

**Primary key** `_key, ordinal1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_typematerials`](#table-sde_typematerials) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `ordinal1` (INTEGER) from `/randomizedMaterials/[] key`.
- `materialTypeID` (INTEGER) from `/randomizedMaterials/[]/materialTypeID`.
- `quantityMax` (INTEGER) from `/randomizedMaterials/[]/quantityMax`.
- `quantityMin` (INTEGER) from `/randomizedMaterials/[]/quantityMin`.

**Domain joins**

- Enforced foreign key from `materialTypeID` to [`sde_types._key`](#table-sde_types).

## Source types

**CCP file** `types.jsonl` · [Source index](#by-source-file) · [Table index](#by-table-or-view)

**Output tables**

- [`sde_types`](#table-sde_types)
- [`sde_types_description_translations`](#table-sde_types_description_translations)
- [`sde_types_name_translations`](#table-sde_types_name_translations)

### Table sde_types

**Primary key** `_key`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `basePrice` (REAL, ISK) from `/basePrice`.
- `capacity` (REAL, m³) from `/capacity`.
- `factionID` (INTEGER) from `/factionID`.
- `graphicID` (INTEGER) from `/graphicID`.
- `groupID` (INTEGER) from `/groupID`.
- `iconID` (INTEGER) from `/iconID`.
- `isDynamicType` (INTEGER) from `/isDynamicType`.
- `isRepackable` (INTEGER) from `/isRepackable`.
- `marketGroupID` (INTEGER) from `/marketGroupID`.
- `mass` (REAL, kg) from `/mass`.
- `metaGroupID` (INTEGER) from `/metaGroupID`.
- `metaLevel` (INTEGER) from `/metaLevel`.
- `packagedVolume` (REAL, m³) from `/packagedVolume`.
- `portionSize` (INTEGER) from `/portionSize`.
- `published` (INTEGER) from `/published`.
- `raceID` (INTEGER) from `/raceID`.
- `radius` (REAL, m) from `/radius`.
- `shipTreeGroupID` (INTEGER) from `/shipTreeGroupID`.
- `soundID` (INTEGER) from `/soundID`.
- `techLevel` (INTEGER) from `/techLevel`.
- `variationParentTypeID` (INTEGER) from `/variationParentTypeID`.
- `volume` (REAL, m³) from `/volume`.

**Domain joins**

- Optional join from `factionID` to [`sde_factions._key`](#table-sde_factions).
- Optional join from `graphicID` to [`sde_graphics._key`](#table-sde_graphics).
- Enforced foreign key from `groupID` to [`sde_groups._key`](#table-sde_groups).
- Enforced foreign key from `iconID` to [`sde_icons._key`](#table-sde_icons).
- Enforced foreign key from `marketGroupID` to [`sde_marketGroups._key`](#table-sde_marketgroups).
- Enforced foreign key from `metaGroupID` to [`sde_metaGroups._key`](#table-sde_metagroups).
- Enforced foreign key from `raceID` to [`sde_races._key`](#table-sde_races).
- Enforced foreign key from `shipTreeGroupID` to [`sde_shipTreeGroups._key`](#table-sde_shiptreegroups).
- Enforced foreign key from `variationParentTypeID` to [`sde_types._key`](#table-sde_types).

### Table sde_types_description_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_types`](#table-sde_types) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/description/{language} key`.
- `value` (TEXT) from `/description/{language}`.

### Table sde_types_name_translations

**Primary key** `_key, language1`.
Primary key columns are required. Other columns are nullable. Array ordinals start at zero.

**Parent table** [`sde_types`](#table-sde_types) joins on `(_key)`.

**Columns**

- `_key` (INTEGER) from `/_key`.
- `language1` (TEXT) from `/name/{language} key`.
- `value` (TEXT) from `/name/{language}`.
