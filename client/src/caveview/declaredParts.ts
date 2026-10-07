// SPDX-License-Identifier: AGPL-3.0-or-later
import type { CaveViewTreeNode } from './loadCaveView.ts';

/**
 * What a drawing makes of the parts of the cave a tracking watch declared.
 *
 * The declaration is a list of starts of names — of a survey, or of a station — and whether a
 * reported station lies inside it is decided on the server, where the stations are. This answers a
 * different and smaller question for the picture: which whole surveys of the drawing hold nothing
 * declared, so that they can be taken off the screen.
 */
export interface DeclaredPartsView {
  /**
   * The surveys that hold nothing declared, each as the names on its path — a reference the viewer
   * takes. Topmost only: a survey listed here is hidden with everything inside it.
   */
  hide: readonly (readonly string[])[];
  /** The entries no survey of this drawing answers to, as they were written. */
  unmatched: readonly string[];
}

/** A drawing that hides nothing and misses nothing: what there is to say with nothing declared. */
export const NOTHING_DECLARED: DeclaredPartsView = { hide: [], unmatched: [] };

const SEPARATOR = '.';

/** The survey is inside the entry: its own name starts with it, so every station in it does. */
const inside = (survey: string, entry: string) => survey.startsWith(entry);

/** The entry names something within the survey, so the survey cannot be hidden whole. */
const holds = (survey: string, entry: string) => entry.startsWith(survey + SEPARATOR);

function surveyNames(top: CaveViewTreeNode): string[] {
  const names: string[] = [];
  const walk = (node: CaveViewTreeNode, path: readonly string[]) => {
    for (const child of node.children) {
      if (child.isStation()) {
        continue;
      }
      const own = [...path, child.name];
      names.push(own.join(SEPARATOR));
      walk(child, own);
    }
  };
  walk(top, []);
  return names;
}

/**
 * Which surveys of a drawing lie wholly outside the declared parts.
 *
 * <b>The same start-of-name test the server applies to a station, asked of a survey.</b> A survey
 * whose dotted name starts with an entry is declared with everything in it. A survey an entry
 * reaches into — the entry names a survey or a station inside it — stays, and its own surveys are
 * asked in turn. Any other survey holds nothing declared and is hidden whole. Stations cannot be
 * hidden one by one, so a survey kept for one station of it is drawn entire: the picture may show
 * more than was declared and never less.
 *
 * <b>An entry may be written in a spelling the drawing does not use.</b> For one of the survey
 * formats the rows the server keeps carry the name of the outermost survey in front of every
 * name, and the viewer drops it; a declaration can be typed either way. So an entry that answers
 * to no survey as written is tried once more without its first name. That second reading is used
 * only for an entry that would otherwise match nothing at all, and it can only add to what stays
 * on screen. An entry that still answers to nothing is reported back, and the caller should then
 * hide nothing: narrowing the picture by half a declaration would take declared passage off it.
 */
export function declaredPartsView(
  top: CaveViewTreeNode | null | undefined,
  declared: readonly string[],
): DeclaredPartsView {
  if (top == null || declared.length === 0) {
    return NOTHING_DECLARED;
  }
  const names = surveyNames(top);
  const answers = (entry: string) => names.some((name) => inside(name, entry) || holds(name, entry));

  const read: string[] = [];
  const unmatched: string[] = [];
  for (const entry of declared) {
    const cut = entry.indexOf(SEPARATOR);
    const withoutFirst = cut < 0 ? null : entry.slice(cut + 1);
    if (answers(entry)) {
      read.push(entry);
    } else if (withoutFirst !== null && withoutFirst !== '' && answers(withoutFirst)) {
      read.push(withoutFirst);
    } else {
      unmatched.push(entry);
    }
  }

  const hide: string[][] = [];
  const walk = (node: CaveViewTreeNode, path: readonly string[]) => {
    for (const child of node.children) {
      if (child.isStation()) {
        continue;
      }
      const own = [...path, child.name];
      const name = own.join(SEPARATOR);
      if (read.some((entry) => inside(name, entry))) {
        continue;
      }
      if (read.some((entry) => holds(name, entry))) {
        walk(child, own);
        continue;
      }
      hide.push(own);
    }
  };
  walk(top, []);
  return { hide, unmatched };
}
