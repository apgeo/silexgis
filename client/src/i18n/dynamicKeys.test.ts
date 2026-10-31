// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import en from './locales/en.json';
import ro from './locales/ro.json';

/**
 * The key test in `i18n.test.ts` reads literal `t('a.b')` calls, and each closed vocabulary there
 * is guarded by a list somebody remembered to write. Two kinds of lookup walk past both:
 *
 * - a key built from a template (`t(`x.${value}`)`), whose tail only exists at run time;
 * - a key held in a variable or a table (`labelKey: 'x.y'` ... `t(row.labelKey)`), where the call
 *   names nothing.
 *
 * This file closes both. A template-built key belongs to a *family* — the static head up to the
 * `${` — and every family is declared in one list below. A new family fails the test until it is
 * added here, which is the moment to also give its values a closed list and a parity test (see the
 * `Record<Union, true>` lists in `i18n.test.ts`). A key kept in a variable is covered because every
 * string literal in the source that opens with a translation namespace must name a real entry.
 */

/** Static heads of every template-built translation key in the application. Keep sorted. */
const DYNAMIC_KEY_FAMILIES: readonly string[] = [
  'access.actions.',
  'access.domains.',
  'access.effects.',
  'access.levels.',
  'access.scopes.',
  'accessKeys.states.',
  'admin.basemap.refusal.',
  'admin.basemap.side.',
  'admin.basemap.state.',
  'admin.documentTypes.kinds.',
  'admin.featureTypes.categories.',
  'admin.featureTypes.displays.',
  'admin.featureTypes.shapeNames.',
  'admin.templates.name_',
  'admin.tripTypes.',
  'admin.tripTypes.kinds.',
  'attachments.roles.',
  'auth.mfaMethod_',
  'calendar.grouping.dimensions.',
  'calendar.sourceValues.',
  'calendar.views.',
  'caves.explorationValues.',
  'caves.visibilityValues.',
  'caveview.compare.',
  'caveview.movie.aspects.',
  'caveview.movie.cameras.',
  'caveview.movie.caverLabels.',
  'caveview.movie.colourByHelp.',
  'caveview.movie.colourByValues.',
  'caveview.movie.formatHelp.',
  'caveview.movie.formats.',
  'caveview.movie.groups.',
  'caveview.movie.layers.',
  'caveview.movie.pictureCorners.',
  'caveview.movie.pictureModeHelp.',
  'caveview.movie.pictureModes.',
  'caveview.movie.presetHelp.',
  'caveview.movie.presets.',
  'caveview.movie.progress.',
  'caveview.movie.qualities.',
  'caveview.movie.rotationModes.',
  'caveview.movie.shadings.',
  'caveview.movie.timelineHelp.',
  'caveview.movie.timelineModes.',
  'caveview.movie.viewDirections.',
  'caveview.picked.',
  'cavingGroups.roles.',
  'cavingGroups.types.',
  'centerlines.sourceValues.',
  'checklists.visibilityValues.',
  'closestApproach.absence.',
  'coordinates.assumptions.',
  'coordinates.choices.',
  'coordinates.converter.batchErrors.',
  'coordinates.converter.separators.',
  'coordinates.errors.',
  'coordinates.formats.',
  'coordinates.settingLabels.',
  'coordinates.settings.notes.',
  'coordinates.settings.reading.',
  'dashboard.counts.',
  'dashboard.kind.',
  'documents.languages.',
  'documents.textStates.',
  'entrances.qualityValues.',
  'events.kindValues.',
  'events.repeatFrequencyValues.',
  'events.seriesFrequencyValues.',
  'expeditions.leadStates.',
  'expeditions.rosterRoleValues.',
  'expeditions.sharing.',
  'expeditions.sharing.skippedReason.',
  'externalIds.systems.',
  'featureKinds.',
  'features.categories.',
  'features.geometryTypes.',
  'features.kinds.',
  'filterLayers.colors.',
  'filterLayers.symbols.',
  'filters.editor.ops.',
  'filters.editor.problems.',
  'filters.editor.spatial.anchorKinds.',
  'filters.editor.spatial.outlineKinds.',
  'filters.editor.spatial.polygonProblems.',
  'filters.editor.values.',
  'filters.sorts.',
  'gallery.licences.',
  'geodata.mapKinds.',
  'geodata.rasterStatusValues.',
  'geodata.statusValues.',
  'history.action.',
  'history.entity.',
  'history.field.',
  'history.skipReason.',
  'history.source.',
  'karstArea.class.',
  'karstArea.component.',
  'karstDensity.pattern.',
  'karstlinkExport.treatments.',
  'map.karstDensity.bands.',
  'map.karstDensity.mode.',
  'map.karstLegend.scheme.',
  'map.karstLegend.unit.',
  'mapEdit.groups.',
  'mapEdit.shapes.',
  'notificationHealth.outcomes.',
  'notificationHealth.statuses.',
  'overburden.gap.',
  'overburden.readoutAbsent.',
  'panel.sections.',
  'passagePattern.caveat.',
  'passagePattern.figure.',
  'passagePattern.outcome.',
  'passagePattern.pattern.',
  'passagePattern.rule.',
  'permissions.',
  'photoFacts.orientations.',
  'photoImport.actions.',
  'photoImport.confidence.',
  'photoImport.sources.',
  'publicTrip.past.report',
  'publicTrip.standing.',
  'publicTrip.state.',
  'publishedTrips.statusHelp.',
  'publishedTrips.statuses.',
  'rastermap.viewKinds.',
  'recordings.standing.',
  'recordings.standingHint.',
  'registryStats.absence.',
  'registryStats.basis.',
  'registryStats.categories.',
  'registryStats.measures.',
  'resLinks.anchorEditors.regionHelp.',
  'resLinks.anchorEditors.regionShapes.',
  'resLinks.anchorKinds.',
  'resLinks.anchors.regionShapes.',
  'resLinks.point.audience.',
  'resLinks.relations.',
  'resLinks.targetTypes.',
  'review.',
  'review.act.',
  'review.candidate.',
  'review.candidate.actions.',
  'review.decision.',
  'review.field.',
  'review.mapTypes.',
  'review.problem.',
  'review.section.',
  'review.source.',
  'review.state.',
  'savedFilters.',
  'savedFilters.explain.',
  'savedFilters.states.',
  'scene3d.',
  'scene3d.catalogueLayer.',
  'scene3d.preset.',
  'scene3d.presetGlyph.',
  'search.divisions.',
  'security.methodHint_',
  'security.method_',
  'settings.accessibility.densityValues.',
  'settings.accessibility.landingValues.',
  'settings.accessibility.themeValues.',
  'settings.account.export.status.',
  'settings.avatar.presetNames.',
  'settings.nav.',
  'settings.notifications.channels.',
  'settings.notifications.choices.',
  'settings.notifications.events.',
  'settings.visibilityValues.',
  'shares.modes.',
  'speleolocImport.blockers.',
  'speleolocImport.errors.',
  'speleolocImport.failures.',
  'speleolocImport.states.',
  'surveyModels.statusValues.',
  'surveyUploads.fileRoles.',
  'surveyUploads.fileStates.',
  'surveyUploads.link.audience.',
  'surveyUploads.link.audienceHint.',
  'surveyUploads.link.refusal.',
  'surveyUploads.origins.',
  'surveyUploads.outputs.',
  'surveyUploads.stages.',
  'surveyUploads.status.',
  'termRules.languages.',
  'termRules.matchModes.',
  'termRules.scopes.',
  'termRules.stripModes.',
  'terrain.depthBands.',
  'terrain.derivativeForm.lightings.',
  'terrain.derivativeForm.rampProblems.',
  'terrain.derivativeForm.ruggednessFits.',
  'terrain.derivativeForm.slopeUnits.',
  'terrain.derivativeForm.surfaceFits.',
  'terrain.derivatives.kinds.',
  'terrain.derivatives.staleHints.',
  'terrain.derivatives.statuses.',
  'terrain.phases.',
  'terrain.sourceKinds.',
  'tripAttention.reasons.',
  'tripImport.dateOrderSources.',
  'tripImport.encodingSources.',
  'tripImport.encodings.',
  'tripImport.fields.',
  'tripImport.problems.',
  'trips.',
  'trips.calloutStateValues.',
  'trips.grouping.dimensions.',
  'trips.participantRoleValues.',
  'trips.publishAudience.',
  'trips.roles.',
  'trips.sectionFields.',
  'trips.sectionValues.',
  'trips.sections.',
  'trips.stateValues.',
  'trips.track.problems.',
  'trips.tracking.csvImport.delimiters.',
  'trips.tracking.csvImport.encodings.',
  'trips.tracking.csvImport.fields.',
  'trips.tracking.csvImport.problemNames.',
  'trips.tracking.kinds.',
  'trips.tracking.modelObstacleBody.',
  'trips.tracking.modelObstacleOption.',
  'trips.tracking.modelObstacleTag.',
  'trips.tracking.modelObstacleTitle.',
  'trips.tracking.stateValues.',
  'trips.typeValues.',
  'uploads.batchSourceValues.',
  'uploads.batchStatusValues.',
  'vectorImport.actions.',
  'vectorImport.geometry.',
  'vectorImport.kinds.',
  'vectorImport.mapping.',
  'vectorImport.modes.',
  'vectorImport.upload.outcome.',
  'vectorImport.upload.reason.',
  'workspace.preset.',
  'workspace.presetHint.',
];

/**
 * Files that build keys from a base held in a variable (`t(`${base}.title`)`), so the call has no
 * static head. The base itself is a string literal in the same file, which the literal check below
 * resolves; the file is named here so a second such file is a decision, not an accident.
 */
const BASE_IN_VARIABLE_FILES: readonly string[] = ['components/invitations/InvitationsPanel.tsx'];

const sourceText = import.meta.glob('../**/*.{ts,tsx}', {
  query: '?raw',
  import: 'default',
  eager: true,
}) as Record<string, string>;

const appSource = Object.entries(sourceText)
  .filter(([file]) => !file.includes('.test.') && !file.startsWith('../api/'))
  .map(([file, text]) => ({ file: file.replace('../', ''), text }));

const namespaces = new Set(Object.keys(en));

function node(locale: object, path: string): unknown {
  return path
    .split('.')
    .reduce<unknown>(
      (at, part) => (typeof at === 'object' && at !== null ? (at as Record<string, unknown>)[part] : undefined),
      locale,
    );
}

const isObject = (value: unknown): value is Record<string, unknown> =>
  typeof value === 'object' && value !== null;

/** Heads of template-built keys: every `t(`...`)` call, and any template that opens with a namespace. */
function templateHeads(): { head: string; file: string }[] {
  const found: { head: string; file: string }[] = [];
  for (const { file, text } of appSource) {
    for (const match of text.matchAll(/\bt\(\s*`([^`]*)`/g)) {
      if (match[1].includes('${')) found.push({ head: match[1].split('${')[0], file });
    }
    for (const match of text.matchAll(/`([^`\n]*\$\{[^`]*)`/g)) {
      const head = match[1].split('${')[0];
      if (head.includes('.') && namespaces.has(head.split('.')[0])) found.push({ head, file });
    }
  }
  return found;
}

describe('template-built translation keys', () => {
  const heads = templateHeads();

  it('finds the template-built keys (so the scan cannot silently go blind)', () => {
    expect(heads.length).toBeGreaterThan(300);
  });

  it('declares every family of template-built keys in one list', () => {
    const declared = new Set(DYNAMIC_KEY_FAMILIES);
    const undeclared = [...new Set(heads.filter(({ head }) => head !== '').map(({ head }) => head))]
      .filter((head) => !declared.has(head))
      .sort();
    expect(
      undeclared,
      'A key built from a template needs its family added to DYNAMIC_KEY_FAMILIES, a closed list of its values and a both-locales test beside the others in i18n.test.ts.',
    ).toEqual([]);
  });

  it('keeps no family that nothing builds any more', () => {
    const built = new Set(heads.map(({ head }) => head));
    expect(DYNAMIC_KEY_FAMILIES.filter((family) => !built.has(family))).toEqual([]);
  });

  it('keeps the list sorted and free of duplicates', () => {
    expect([...new Set(DYNAMIC_KEY_FAMILIES)]).toEqual([...DYNAMIC_KEY_FAMILIES].sort());
  });

  it('names, for every family, a branch of both locales that has entries', () => {
    const empty = DYNAMIC_KEY_FAMILIES.flatMap((family) =>
      (
        [
          ['en', en],
          ['ro', ro],
        ] as const
      ).flatMap(([name, locale]) => {
        // `a.b.` names the branch `a.b`; `a.b.name_` names the leaves of `a.b` that start `name_`.
        const lastDot = family.lastIndexOf('.');
        const branch = node(locale, family.slice(0, lastDot));
        const tail = family.slice(lastDot + 1);
        const entries = isObject(branch) ? Object.keys(branch).filter((key) => key.startsWith(tail)) : [];
        return entries.length > 0 ? [] : [`${name}: ${family}`];
      }),
    );
    expect(empty).toEqual([]);
  });

  it('allows a base held in a variable only in the files named for it', () => {
    const files = [...new Set(heads.filter(({ head }) => head === '').map(({ file }) => file))].sort();
    expect(files).toEqual([...BASE_IN_VARIABLE_FILES]);
  });
});

describe('translation keys held in variables and tables', () => {
  /**
   * Server problem codes (`access.create_forbidden`) and file names (`map.png`) share the shape of
   * a key and the first word of a namespace, but are not looked up in the locale files. Some real
   * keys end in a snake_case word too, so this is applied only to a literal that does not resolve.
   */
  const notAKey = (literal: string): boolean =>
    /_/.test(literal.split('.').pop() ?? '') || /\.(png|csv|json|gpx|pdf|xlsx|zip|jpe?g|svg|txt)$/.test(literal);

  /** A literal that is the label of a `case`, an object key, or one side of a comparison is a code, not a key. */
  const inCodePosition = (text: string, start: number, end: number): boolean =>
    /\b(case|===|!==)\s*$/.test(text.slice(Math.max(0, start - 12), start)) || /^\s*:/.test(text.slice(end, end + 3));

  const literals = appSource.flatMap(({ file, text }) =>
    [...text.matchAll(/(['"])([A-Za-z]\w*(?:\.\w+)+)\1/g)]
      .filter((match) => namespaces.has(match[2].split('.')[0]))
      .map((match) => ({
        literal: match[2],
        file,
        code: inCodePosition(text, match.index, match.index + match[0].length),
      })),
  );

  it('finds the string literals that look like keys (so the scan cannot silently go blind)', () => {
    expect(literals.length).toBeGreaterThan(5000);
  });

  it('resolves every string literal that opens with a translation namespace, in both locales', () => {
    const unresolved = literals
      .filter(({ literal, code }) => !code && !notAKey(literal))
      .flatMap(({ literal, file }) =>
        (
          [
            ['en', en],
            ['ro', ro],
          ] as const
        ).flatMap(([name, locale]) => (node(locale, literal) === undefined ? [`${name}: ${literal} (${file})`] : [])),
      );
    expect(
      unresolved,
      'A key kept in a variable or a table is looked up by a call that names nothing. Add the entry to both locales, or fix the typo.',
    ).toEqual([]);
  });
});
