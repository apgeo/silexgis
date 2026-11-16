// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CabinetInfo, CabinetWrite } from '../../api/hooks.ts';

/**
 * Cabinets as select options, each named by its whole path. Names are unique only among
 * siblings — "1987" sits under many archives — so a bare name is ambiguous anywhere away from
 * the tree that gives it context, and filing on the wrong shelf changes who can read a document.
 */
export function cabinetPathOptions(cabinets: readonly CabinetInfo[]): { value: string; label: string }[] {
  const names = new Map(cabinets.map((cabinet) => [cabinet.id, cabinet.name]));
  return cabinets
    .map((cabinet) => ({
      value: cabinet.id,
      label: cabinet.ancestorIds.map((id) => names.get(id) ?? '…').join(' / '),
    }))
    .sort((a, b) => a.label.localeCompare(b.label));
}

/**
 * The whole write body for a cabinet as it stands, with the given fields changed.
 *
 * The cabinet route replaces everything it is sent: a default left out of the body is a default
 * cleared. So a write that means to change one thing — a move in the tree, say — has to send this
 * cabinet's own defaults back unchanged (its own, not the inherited `effective*` ones, or saving
 * would copy its parent's answers onto it and freeze them there).
 */
export function cabinetWriteOf(cabinet: CabinetInfo, changes: Partial<CabinetWrite> = {}): CabinetWrite {
  return {
    name: cabinet.name,
    description: cabinet.description,
    parentId: cabinet.parentId,
    defaultDocumentTypeId: cabinet.defaults.documentTypeId,
    defaultVisibility: cabinet.defaults.visibility,
    defaultTagIds: [...cabinet.defaults.tagIds],
    requiredMetadataKeys: [...cabinet.defaults.requiredMetadataKeys],
    ...changes,
  };
}
