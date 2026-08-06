// SPDX-License-Identifier: AGPL-3.0-or-later
import { getFeatureTypeName, getFeatureTypeNameByCode } from './featureTypeCatalog.ts';

// What a map feature is called when it is named in passing — a tooltip, a callout, a one-line
// label over the thing itself.
//
// Both views ask this question and they must answer it the same way, or the same cave reads as two
// different places depending on which view a viewer happens to be in. The rule is small enough
// that writing it twice would look harmless, which is exactly why it is written once: it is a
// product decision about how a place is named, not a formatting detail.
//
// Deliberately free of any drawing library so both views can import it, and deliberately taking a
// bag of properties rather than a typed feature — the flat map reads them off a vector feature and
// the scene reads them off the raw response, and the two disagree about everything except the
// property names.

/** Joins a thing to the context that identifies it, the way both views write it. */
function joined(name: string | undefined, context: string | undefined): string | undefined {
  if (name && context && name !== context) {
    return `${name} — ${context}`;
  }
  return name ?? context;
}

function text(value: unknown): string | undefined {
  return typeof value === 'string' && value ? value : undefined;
}

/**
 * How a cave entrance is named: its own name, and its cave's when that adds anything.
 *
 * The entrance's name already falls back to the cave's name server-side, so the two are equal for
 * an unnamed entrance and repeating it would read as a stutter.
 */
export function entranceLabel(properties: Record<string, unknown>): string | undefined {
  return joined(text(properties.name), text(properties.caveName));
}

/**
 * How a surface feature is named: its own name, and what kind of thing it is.
 *
 * An unnamed feature is labelled with its kind alone, which is the useful half — "Sinkhole" says
 * more about an unnamed dot than nothing does. Server-loaded rows carry the type's code; one drawn
 * on screen and not yet saved knows only which type the palette was armed with, so both are tried.
 */
export function surfaceFeatureLabel(properties: Record<string, unknown>): string | undefined {
  const typeName =
    getFeatureTypeNameByCode(properties.typeCode) ?? getFeatureTypeName(properties.featureTypeId);
  return joined(text(properties.name), typeName);
}
