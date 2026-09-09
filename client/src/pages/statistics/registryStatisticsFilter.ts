// SPDX-License-Identifier: AGPL-3.0-or-later
import type {
  RegistryCorrelationParams,
  RegistryDistributionParams,
  RegistryRegionsParams,
} from '../../api/hooks.ts';

/**
 * What narrows a registry statistic, kept in the address rather than in a component.
 *
 * The address is where the narrowing lives, not a copy of somewhere it lives properly: a
 * distribution somebody is looking at is a link, it survives a reload, and it comes back the same
 * on somebody else's screen — with their own access applied to it, which is the one thing about
 * it that legitimately differs.
 *
 * <p>
 * A word the address carries that this application does not know is not corrected here. The
 * server refuses an unknown measurement, a bin count outside what the registry will publish and a
 * floor below the one it enforces, and its refusal names which control is wrong. Silently
 * substituting something legal would draw a different distribution under the same address and
 * tell nobody.
 * </p>
 */
export interface RegistryScope {
  /** A karst area to count within. Carried from the address; there is no control for it yet. */
  areaId?: string;
  caveTypeId?: number;
  rockTypeId?: number;
  /** Matched exactly by the server, not searched. */
  region?: string;
}

/** A scope, plus how the measurement is chosen and cut up. */
export interface RegistryDistributionFilter extends RegistryScope {
  /** The measurement's own name, as the server spells it. Passed on unchecked. */
  measure: string;
  bins?: number;
  minimumBinCaveCount?: number;
}

/**
 * The measurement a reader who asked for nothing gets. Surveyed length is the column the registry
 * carries most of, so it is the one whose distribution says something on a fresh installation.
 */
export const DefaultRegistryMeasure = 'surveyedLength';

/** What the server publishes when no bin count is asked for; written here so the control agrees. */
export const DefaultRegistryBinCount = 20;

/** The bounds the server enforces, mirrored so a control can offer only what it will accept. */
export const RegistryBinCountBounds = { minimum: 2, maximum: 100 } as const;

/**
 * The floor on how few caves an interval may be published with. A caller may raise it and may not
 * lower it: asking for something finer than the registry will publish is refused rather than
 * quietly answered with something else.
 */
export const RegistryBinCaveCountBounds = { minimum: 3, maximum: 50 } as const;

/** An unnarrowed scope, which is the whole registry as this reader may read it. */
export const EmptyRegistryScope: RegistryScope = {};

/** Whether anything is narrowing the set a figure is computed over. */
export function isRegistryScopeNarrowed(scope: RegistryScope): boolean {
  return (
    scope.areaId !== undefined ||
    scope.caveTypeId !== undefined ||
    scope.rockTypeId !== undefined ||
    (scope.region !== undefined && scope.region !== '')
  );
}

function one(value: string | null): string | undefined {
  const trimmed = value?.trim();
  return trimmed === undefined || trimmed === '' ? undefined : trimmed;
}

function numeric(value: string | null): number | undefined {
  const text = one(value);
  if (text === undefined) {
    return undefined;
  }
  const parsed = Number(text);
  // Not a number at all is nothing asked for; a number out of range is passed on, because the
  // server is the one that says what range it publishes and its refusal names the control.
  return Number.isFinite(parsed) ? parsed : undefined;
}

/** Read a distribution's whole state out of the address. */
export function readRegistryDistributionFilter(
  params: URLSearchParams,
): RegistryDistributionFilter {
  return {
    measure: one(params.get('measure')) ?? DefaultRegistryMeasure,
    bins: numeric(params.get('bins')),
    minimumBinCaveCount: numeric(params.get('minimumBinCaveCount')),
    areaId: one(params.get('areaId')),
    caveTypeId: numeric(params.get('caveTypeId')),
    rockTypeId: numeric(params.get('rockTypeId')),
    region: one(params.get('region')),
  };
}

/**
 * Write it back. A default is an absent key and never an explicit one, so the plain question has
 * a bare address and two links asking the same thing are the same link.
 */
export function writeRegistryDistributionFilter(
  filter: RegistryDistributionFilter,
): URLSearchParams {
  const params = new URLSearchParams();
  const set = (key: string, value: string | number | undefined) => {
    if (value !== undefined && value !== '') {
      params.set(key, String(value));
    }
  };
  if (filter.measure !== DefaultRegistryMeasure) {
    set('measure', filter.measure);
  }
  set('bins', filter.bins);
  set('minimumBinCaveCount', filter.minimumBinCaveCount);
  set('areaId', filter.areaId);
  set('caveTypeId', filter.caveTypeId);
  set('rockTypeId', filter.rockTypeId);
  set('region', filter.region);
  return params;
}

/** The narrowings alone, which is what every registry statistic shares. */
export function registryScopeQuery(scope: RegistryScope): RegistryScope {
  return {
    areaId: scope.areaId,
    caveTypeId: scope.caveTypeId,
    rockTypeId: scope.rockTypeId,
    region: scope.region,
  };
}

/**
 * What the distribution route is asked, screen and file alike.
 *
 * One object feeds the query and the export URL, so a control added to the screen and forgotten
 * on the way to the file cannot happen: there is no second place to forget it.
 */
export function registryDistributionQuery(
  filter: RegistryDistributionFilter,
): RegistryDistributionParams {
  return {
    ...registryScopeQuery(filter),
    measure: filter.measure,
    bins: filter.bins,
    minimumBinCaveCount: filter.minimumBinCaveCount,
  };
}

/** A scope, plus the two measurements set against each other and the form of the fit. */
export interface RegistryCorrelationFilter extends RegistryScope {
  /** The horizontal measurement's own name, as the server spells it. Passed on unchecked. */
  x: string;
  /** The vertical one. The server refuses the two being the same rather than fitting it. */
  y: string;
  /** Whether the fit is taken over the logarithms of both. Absent means the server's own answer. */
  logarithmic?: boolean;
}

/**
 * The pair a reader who asked for nothing gets, and the one the relationship is best known for:
 * how far a cave goes against how deep it goes.
 */
export const DefaultCorrelationX = 'surveyedLength';
export const DefaultCorrelationY = 'depth';

/**
 * What the server does when nothing is said about the form of the fit. Written here so the
 * control shows the state the answer was actually computed in rather than an empty one: a length
 * against a depth is a straight line only on logarithmic axes.
 */
export const DefaultCorrelationLogarithmic = true;

/** Read a correlation's whole state out of the address. */
export function readRegistryCorrelationFilter(params: URLSearchParams): RegistryCorrelationFilter {
  const logarithmic = one(params.get('logarithmic'));
  return {
    x: one(params.get('x')) ?? DefaultCorrelationX,
    y: one(params.get('y')) ?? DefaultCorrelationY,
    // Anything but the two words the address is written with is nothing asked for, which is the
    // server's own default — never the opposite of what was typed.
    logarithmic: logarithmic === 'false' ? false : logarithmic === 'true' ? true : undefined,
    areaId: one(params.get('areaId')),
    caveTypeId: numeric(params.get('caveTypeId')),
    rockTypeId: numeric(params.get('rockTypeId')),
    region: one(params.get('region')),
  };
}

/** Write it back, defaults left out so the plain question has a bare address. */
export function writeRegistryCorrelationFilter(
  filter: RegistryCorrelationFilter,
): URLSearchParams {
  const params = new URLSearchParams();
  const set = (key: string, value: string | number | undefined) => {
    if (value !== undefined && value !== '') {
      params.set(key, String(value));
    }
  };
  if (filter.x !== DefaultCorrelationX) {
    set('x', filter.x);
  }
  if (filter.y !== DefaultCorrelationY) {
    set('y', filter.y);
  }
  if (filter.logarithmic !== undefined && filter.logarithmic !== DefaultCorrelationLogarithmic) {
    set('logarithmic', String(filter.logarithmic));
  }
  set('areaId', filter.areaId);
  set('caveTypeId', filter.caveTypeId);
  set('rockTypeId', filter.rockTypeId);
  set('region', filter.region);
  return params;
}

/**
 * What the correlation route is asked, screen and file alike — the same object for both, so a
 * control the screen uses cannot be missing from the file taken off it.
 */
export function registryCorrelationQuery(
  filter: RegistryCorrelationFilter,
): RegistryCorrelationParams {
  return {
    ...registryScopeQuery(filter),
    x: filter.x,
    y: filter.y,
    logarithmic: filter.logarithmic,
  };
}

/**
 * The measurements the registry will describe, spelled as the wire spells them.
 *
 * A closed list because a picker needs one and the generated client's union cannot be enumerated
 * at runtime. Altitude is deliberately not among them: the registry does not offer a distribution
 * of it, and a control offering one would produce a refusal for every reader who tried.
 */
export const RegistryMeasureNames = [
  'surveyedLength',
  'estimatedLength',
  'depth',
  'positiveDepth',
  'negativeDepth',
  'realExtension',
  'projectedExtension',
  'volume',
  'area',
  'ramificationIndex',
] as const;

/**
 * A regional breakdown is narrowed by the scope and nothing else: it has no measurement to pick
 * and no shape to fit, so the four narrowings are its whole state.
 */
export type RegistryRegionsFilter = RegistryScope;

/** Read a breakdown's whole state out of the address. */
export function readRegistryRegionsFilter(params: URLSearchParams): RegistryRegionsFilter {
  return {
    areaId: one(params.get('areaId')),
    caveTypeId: numeric(params.get('caveTypeId')),
    rockTypeId: numeric(params.get('rockTypeId')),
    region: one(params.get('region')),
  };
}

/** Write it back, an absent narrowing being an absent key so the whole registry has a bare address. */
export function writeRegistryRegionsFilter(filter: RegistryRegionsFilter): URLSearchParams {
  const params = new URLSearchParams();
  const set = (key: string, value: string | number | undefined) => {
    if (value !== undefined && value !== '') {
      params.set(key, String(value));
    }
  };
  set('areaId', filter.areaId);
  set('caveTypeId', filter.caveTypeId);
  set('rockTypeId', filter.rockTypeId);
  set('region', filter.region);
  return params;
}

/**
 * What the breakdown route is asked, screen and file alike — the same object for both, for the
 * same reason the other two share theirs: a file taken off a narrowed screen is that screen's
 * answer, and building the question twice is how it stops being.
 */
export function registryRegionsQuery(filter: RegistryRegionsFilter): RegistryRegionsParams {
  return registryScopeQuery(filter);
}
