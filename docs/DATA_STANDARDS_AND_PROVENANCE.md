# GTA Viet Life — Data Standards, Canonical Sources and Provenance

**Status:** Mandatory engineering policy  
**Applies to:** Engine, GTA V adapter, gameplay modules, configuration, content catalogs, imported datasets, persistence and integrations.

## 1. Single source of truth

- The GitHub repository is the version-controlled source of truth for project-owned data contracts, schemas, enums, configuration definitions, canonical catalogs and migration rules.
- Every shared data concept must have one canonical definition. Modules must consume that definition rather than creating competing local variants.
- Runtime databases and save files are operational state stores, not independent authorities for schema or business rules.
- Do not maintain undocumented copies of canonical values in source code, INI files, JSON files, spreadsheets or external services. If a runtime copy is required, define its generation and synchronization path from the canonical source.

## 2. Required data contract

Every persistent or cross-module data type must define, as applicable:
- stable identifier and namespace;
- field names, types, required/optional status and null semantics;
- units, precision, valid ranges and enum values;
- currency and monetary precision;
- timezone and timestamp format;
- coordinate reference/space, axis convention and measurement unit;
- ownership, authority and write permissions;
- schema version, compatibility rules and migration path;
- validation rules and error behavior.

Use explicit, versioned schemas and machine-readable contracts where practical (for example JSON Schema, Protocol Buffers or typed language contracts). Select one format per boundary and document the reason; do not create unnecessary parallel formats.

## 3. External data sources and GitHub upstreams

- Prefer primary and authoritative sources: official government/open-data portals, standards bodies, official project repositories and upstream GitHub repositories maintained by the original authors.
- A GitHub location alone does not establish authority or correctness. Verify repository ownership, maintainer identity, release/tag or commit, documentation, update history and license.
- For every imported external dataset or specification, record source URL/repository, exact release/tag/commit or dataset version, retrieval date, license/attribution, checksum where feasible, transformation steps and known limitations.
- Pin dependencies and imported reference datasets to explicit versions or commit hashes. Do not silently track a moving branch such as `main` or `master` for reproducible releases.
- If sources conflict, record the conflict and apply a documented source-priority rule. Do not silently merge incompatible values.
- If no reliable source exists, mark the field/data as unknown or unavailable. Do not fabricate or infer authoritative facts.

## 4. Canonical domain data

The following must be centrally defined and consistently reused where relevant:
- world, district, building, room, road and interaction-point identities;
- NPC, household, organization, employer and business identities;
- jobs, schedules, activities, needs, skills, traits and relationship types;
- items, inventory units, product categories and equipment;
- money, wages, prices, rent, tax, debt, interest and transaction semantics;
- time, calendar, simulation ticks and offline advancement;
- traffic, vehicle classes, road rules, incidents and mobility events;
- adapter capabilities, command kinds, event kinds and result states.

Game-native handles, model hashes and transient runtime identifiers must remain adapter-local and must not become canonical cross-game identities.

## 5. Data quality and validation

- Validate data at ingestion and at every external/module boundary.
- Reject or quarantine malformed, out-of-range, contradictory or unversioned data; do not silently coerce it into plausible-looking values.
- Distinguish unknown, not applicable, unavailable, estimated and verified values. Never encode all of them as zero or an empty string.
- Record provenance and confidence for externally sourced or AI-proposed facts.
- AI-generated content may propose data, dialogue or actions, but authoritative world state changes only after deterministic validation and an authorized transaction.
- Provide deterministic fallback behavior when an external source or provider is unavailable.

## 6. Schema evolution and migration

- Every persistent schema has an explicit version.
- Changes require migration code or a documented compatibility strategy, validation and rollback.
- Preserve existing GTA Viet Life save files and legacy `GVL.*` contracts. Add adapters or migrations; do not silently rename, delete or reinterpret existing fields.
- Test old-to-new migration, new-to-old rollback where supported, interrupted migration recovery and idempotent re-run.
- Breaking changes require a new major contract version and an explicit adapter compatibility decision.

## 7. Reproducibility and release manifest

Each integrated release candidate must include a data/dependency manifest recording:
- repository commit and schema versions;
- external datasets and upstream repositories with pinned versions;
- dependency versions and licenses;
- generated catalog/configuration build inputs;
- migrations applied and compatibility range;
- validation results and known data limitations.

A release must be reproducible from declared inputs to the extent permitted by licensed assets and external services.

## 8. Acceptance criteria

Data governance is complete only when:
1. every cross-module/persistent data type has a canonical schema;
2. every external dataset has traceable provenance and license information;
3. shared IDs, units, time, money and coordinate conventions are consistent;
4. ingestion and migrations are validated;
5. modules do not maintain conflicting undocumented definitions;
6. the release manifest identifies the exact data and schema versions used.

This policy does not require all runtime world state to be committed to Git. Dynamic player saves, NPC memories and simulation events belong in runtime persistence; GitHub governs their schemas, versioned reference data, migrations and reproducible configuration.
