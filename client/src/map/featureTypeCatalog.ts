// SPDX-License-Identifier: AGPL-3.0-or-later
import type { FeatureType } from '../api/hooks.ts';

// What a feature type is called and which symbol stands for it, looked up by whatever a drawn
// feature happens to carry.
//
// This is a catalog rather than a lookup written where it is used, because two things need it and
// they arrive at it from opposite directions: a row loaded from the server carries the type's code
// and its symbol file, while one just drawn on screen and not yet saved knows only which type the
// palette was armed with. Both have to end up with the same name.
//
// It lives apart from any drawing library on purpose. The flat map's overlays and the 3D scene
// both want it, and the scene is loaded as a separate chunk precisely so that a session which
// never opens it downloads none of it — pulling the catalog out of a map layer is what keeps a
// mapping library out of that chunk.

let symbolByTypeId = new Map<number, string>();
let nameByTypeId = new Map<number, string>();
let nameByTypeCode = new Map<string, string>();

const listeners = new Set<() => void>();

/**
 * Replaces the catalog with what the server published, and tells whoever is drawing from it.
 *
 * The notification is not a nicety: features are drawn before the catalog arrives, so anything
 * already on screen is showing a blank where a type name or a symbol belongs and has no other
 * reason to redraw.
 */
export function setFeatureTypeCatalog(types: FeatureType[]): void {
  symbolByTypeId = new Map(
    types.filter((type) => type.symbolFile).map((type) => [Number(type.id), type.symbolFile!]),
  );
  nameByTypeId = new Map(types.map((type) => [Number(type.id), type.name]));
  nameByTypeCode = new Map(types.map((type) => [type.code, type.name]));
  for (const listener of [...listeners]) {
    listener();
  }
}

/** Subscribes to catalog replacements; returns an unsubscribe function. */
export function onFeatureTypeCatalogChanged(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/** The name of the type a locally drawn feature was armed with, by its numeric id. */
export function getFeatureTypeName(featureTypeId: unknown): string | undefined {
  return nameByTypeId.get(Number(featureTypeId));
}

/** The name of the type a server-loaded feature carries, by its stable code. */
export function getFeatureTypeNameByCode(typeCode: unknown): string | undefined {
  return typeof typeCode === 'string' ? nameByTypeCode.get(typeCode) : undefined;
}

/** The symbol file for a locally drawn feature, which carries no symbol of its own yet. */
export function getFeatureTypeSymbol(featureTypeId: unknown): string | undefined {
  return symbolByTypeId.get(Number(featureTypeId));
}
