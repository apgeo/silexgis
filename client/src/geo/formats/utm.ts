// SPDX-License-Identifier: AGPL-3.0-or-later
import { normaliseCoordinateText } from './angles.ts';
import { protectedGridStepMetres } from './protection.ts';
import { DEFAULT_READING_RULES, FORMAT_SETTINGS, type ReadingRules, type UtmSettings } from './settings.ts';
import { invalid, NOT_THIS_FORMAT, type FormatDefinition, type ParsedPosition, type ParseOutcome } from './types.ts';
import { BANDS, bandRange, fromUtm, toUtm, UTM_NORTH_LIMIT, UTM_SOUTH_LIMIT } from './utmMath.ts';

export type { UtmSettings } from './settings.ts';

/**
 * A grid value cut to a step — truncated, not rounded, as grid references are by convention: a
 * printed reference names the square the point lies in, so the same point printed coarser still
 * names a square that contains it.
 */
function truncated(value: number, step: number): string {
  return String(Math.floor(value / step) * step);
}

function write(lon: number, lat: number, zone: UtmSettings['zone'], step: number): string | null {
  const utm = toUtm(lon, lat, zone === 'auto' ? undefined : zone);
  if (!utm) {
    return null;
  }
  return `${utm.zone}${utm.band} ${truncated(utm.easting, step)} E ${truncated(utm.northing, step)} N`;
}

/** Room for a point a band edge away from where its truncated reference puts it. */
const BAND_TOLERANCE_DEGREES = 0.01;

function inBand(lat: number, band: string): boolean {
  const range = bandRange(band);
  return range !== null && lat >= range[0] - BAND_TOLERANCE_DEGREES && lat < range[1] + BAND_TOLERANCE_DEGREES;
}

/**
 * Reads a UTM reference: a zone (1–60), a band letter or a hemisphere letter or neither, then an
 * easting and a northing, each optionally marked E/N (before or after) or with metres, separated
 * by spaces, commas or semicolons — `35T 412345 5043210`, `35 T 412345mE 5043210mN`,
 * `35N, E 412345, N 5043210`.
 *
 * N and S are both band letters and hemisphere letters. N reads the same either way (both are
 * north) unless the latitude falls outside band N, when it is a hemisphere; S as band S means
 * 32°–40°N and as a hemisphere means south, and when both readings are possible both are offered.
 * Without a letter the reference is taken in the hemisphere the reading rules name — northern
 * unless the person chose otherwise — and says so.
 */
export function parseUtm(raw: string, rules: ReadingRules = DEFAULT_READING_RULES): ParseOutcome {
  const text = normaliseCoordinateText(raw);
  const head = /^(\d{1,2})(\s*)([A-Z])?(?=[\s,;])[\s,;]+(.*)$/.exec(text);
  if (!head) {
    return NOT_THIS_FORMAT;
  }
  const zone = Number(head[1]);
  // "35 E 412345" labels the easting; band E written apart from its zone would be read the same
  // way, and is far rarer than the label.
  const labelNotBand = head[3] === 'E' && head[2] !== '';
  const letter = labelNotBand ? null : (head[3] ?? null);
  const rest = labelNotBand ? `E ${head[4]}` : head[4];
  const tokens = rest.split(/[\s,;]+/).filter((token) => token !== '');

  // A standalone E or N labels the number after it when the values start with a label
  // ("E 412345 N 5043210"), and the number before it otherwise ("412345 E 5043210 N").
  const labelsLead = tokens[0] === 'E' || tokens[0] === 'N';
  let pendingLabel: 'E' | 'N' | null = null;
  const numbers: { value: number; label: 'E' | 'N' | null }[] = [];
  for (const token of tokens) {
    if (token === 'E' || token === 'N') {
      if (labelsLead) {
        pendingLabel = token;
      } else if (numbers.length > 0 && numbers[numbers.length - 1].label === null) {
        numbers[numbers.length - 1].label = token;
      } else {
        return NOT_THIS_FORMAT;
      }
      continue;
    }
    const match = /^(?:([EN]))?(\d+(?:\.\d+)?)(?:M)?([EN])?$/.exec(token);
    if (!match) {
      return NOT_THIS_FORMAT;
    }
    const label = (match[1] ?? match[3] ?? pendingLabel) as 'E' | 'N' | null;
    pendingLabel = null;
    numbers.push({ value: Number(match[2]), label });
  }
  if (numbers.length !== 2) {
    return NOT_THIS_FORMAT;
  }
  const [first, second] = numbers;
  const swapped = first.label === 'N' || second.label === 'E';
  const easting = swapped ? second.value : first.value;
  const northing = swapped ? first.value : second.value;

  if (zone < 1 || zone > 60) return invalid('utmZoneRange');
  if (easting < 100000 || easting >= 1000000) return invalid('utmEastingRange');
  if (northing < 0 || northing > 10000000) return invalid('utmNorthingRange');

  const readAs = (hemisphere: 'N' | 'S'): [number, number] => fromUtm({ zone, hemisphere, easting, northing });
  const candidates: ParsedPosition[] = [];
  const within = (lat: number) => lat >= UTM_SOUTH_LIMIT - 1 && lat <= UTM_NORTH_LIMIT + 1;

  if (letter === null) {
    const south = rules.bareUtmHemisphere === 'south';
    const [lon, lat] = readAs(south ? 'S' : 'N');
    if (within(lat)) {
      candidates.push({ lon, lat, format: 'utm', assumptions: [south ? 'southernHemisphere' : 'northernHemisphere'] });
    }
  } else if (letter === 'N' || letter === 'S') {
    // Band N and the northern hemisphere are both read north, so N is one position either way.
    const asBand = readAs('N');
    const asHemisphere = readAs(letter);
    if (letter === 'N') {
      candidates.push({
        lon: asBand[0],
        lat: asBand[1],
        format: 'utm',
        assumptions: inBand(asBand[1], 'N') ? [] : ['utmLetterAsHemisphere'],
      });
    } else {
      if (inBand(asBand[1], 'S')) {
        candidates.push({ lon: asBand[0], lat: asBand[1], format: 'utm', assumptions: ['utmLetterAsBand'] });
      }
      if (within(asHemisphere[1])) {
        candidates.push({ lon: asHemisphere[0], lat: asHemisphere[1], format: 'utm', assumptions: ['utmLetterAsHemisphere'] });
      }
    }
  } else if (BANDS.includes(letter)) {
    const [lon, lat] = readAs(letter >= 'N' ? 'N' : 'S');
    if (!inBand(lat, letter)) return invalid('utmBandMismatch');
    candidates.push({ lon, lat, format: 'utm', assumptions: [] });
  } else {
    return invalid('utmBandUnknown');
  }
  return candidates.length > 0 ? { kind: 'parsed', candidates } : invalid('utmNorthingRange');
}

/**
 * UTM with its zone and latitude band, `35T 412345 E 5043210 N`. Outside 80°S–84°N there is no
 * UTM reference, and the format says so rather than print one that does not exist.
 */
export const UTM: FormatDefinition<UtmSettings> = {
  id: 'utm',
  ...FORMAT_SETTINGS.utm,
  format: (lon, lat, s) => write(lon, lat, s.zone, s.precisionMetres),
  formatProtected: (lon, lat, s, gridMetres) =>
    write(lon, lat, s.zone, Math.max(s.precisionMetres, protectedGridStepMetres(gridMetres))),
  parse: parseUtm,
};
