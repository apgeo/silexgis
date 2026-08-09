// SPDX-License-Identifier: AGPL-3.0-or-later
import { depth, nodeCount } from './document.ts';
import type { FilterDocument, FilterLimits, FilterNode, WorldVocabulary } from './types.ts';

/**
 * Whether a filter is worth sending — a courtesy to the person building it, never the enforcement.
 *
 * The server checks all of this again and refuses on its own account. This exists so somebody
 * assembling a large filter is told while they are assembling it, rather than after pressing the
 * button. The numbers are the server's own, published with the vocabulary, so there is one set of
 * limits and this side never has an opinion about what they should be.
 *
 * What it deliberately does not check is whether a field exists or admits an operator. That answer
 * depends on who is asking, the server gives it, and a second opinion here would be wrong for
 * anybody whose vocabulary differs from the one that happened to be loaded.
 */

export interface PreflightProblem {
  /** A translation key, so the message reads in the reader's language rather than the author's. */
  messageKey: string;
  values?: Record<string, string | number>;
}

export function preflight(
  document: FilterDocument,
  limits: FilterLimits,
  vocabularies: WorldVocabulary[],
): PreflightProblem[] {
  const problems: PreflightProblem[] = [];

  if (document.scope.length === 0) {
    problems.push({ messageKey: 'filters.problems.noWorlds' });
  }

  if (document.scope.length > limits.maxWorlds) {
    problems.push({ messageKey: 'filters.problems.tooManyWorlds', values: { max: limits.maxWorlds } });
  }

  const named = document.scope.map((s) => s.world);
  if (new Set(named).size !== named.length) {
    problems.push({ messageKey: 'filters.problems.worldTwice' });
  }

  if (nodeCount(document) > limits.maxNodes) {
    problems.push({ messageKey: 'filters.problems.tooLarge', values: { max: limits.maxNodes } });
  }

  if (depth(document) > limits.maxDepth) {
    problems.push({ messageKey: 'filters.problems.tooDeep', values: { max: limits.maxDepth } });
  }

  // No world sorts by what was asked. Checked here because the alternative is a list that comes
  // back in an order nobody chose, with nothing on screen to say the sort was ignored.
  const sortable = document.scope.some((scope) =>
    vocabularies.find((v) => v.world === scope.world)?.sorts.includes(document.sort));
  if (document.scope.length > 0 && !sortable) {
    problems.push({ messageKey: 'filters.problems.sortUnavailable' });
  }

  for (const scope of document.scope) {
    walk(scope.where, (node) => {
      if (node.node !== 'condition') {
        return;
      }

      if (node.values.length > limits.maxValuesPerCondition) {
        problems.push({
          messageKey: 'filters.problems.tooManyValues',
          values: { max: limits.maxValuesPerCondition },
        });
      }

      for (const value of node.values) {
        if (value.kind === 'text' && value.value.length > limits.maxTextValueLength) {
          problems.push({
            messageKey: 'filters.problems.textTooLong',
            values: { max: limits.maxTextValueLength },
          });
        }
      }
    });
  }

  return problems;
}

/**
 * Whether every condition has what its operator needs.
 *
 * Kept apart from the limits above because it is not a refusal: a half-written row is what a filter
 * looks like while somebody is writing it. The editor uses this to leave a row out of the request
 * rather than to complain about it.
 */
export function isComplete(node: FilterNode): boolean {
  if (node.node === 'condition') {
    if (node.op === 'isEmpty' || node.op === 'isNotEmpty') {
      return true;
    }

    if (node.op === 'between') {
      return node.values.length === 2;
    }

    return node.values.length >= 1 && node.values.every((v) => v.value !== '' && v.value !== null);
  }

  const children = node.node === 'not' ? [node.of] : node.of;
  return children.every(isComplete);
}

function walk(node: FilterNode | null, visit: (node: FilterNode) => void): void {
  if (node === null) {
    return;
  }

  visit(node);
  if (node.node === 'condition') {
    return;
  }

  const children = node.node === 'not' ? [node.of] : node.of;
  for (const child of children) {
    walk(child, visit);
  }
}
