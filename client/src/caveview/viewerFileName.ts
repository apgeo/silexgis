// SPDX-License-Identifier: AGPL-3.0-or-later
import type { SurveyModelInfo } from '../api/hooks.ts';

/**
 * The file name the survey viewer is handed, whose extension is what selects the parser.
 *
 * One home, because it is a rule rather than a line: the viewer reads the format off the name it
 * is given, so a caller that spells it differently gets a model that silently fails to draw. It
 * lived privately in two components that had drifted into agreeing by hand, and a third mount was
 * about to copy one of them.
 *
 * `surveyModelReadableByViewer` in the API hooks is the same kind of rule about the same objects
 * and is already single-homed for the same reason.
 */
export function viewerFileName(model: Pick<SurveyModelInfo, 'name' | 'format'>): string {
  return `${model.name}.${model.format === 'lox' ? 'lox' : '3d'}`;
}

/**
 * The same name for a model that arrives without one.
 *
 * A published trip's envelope carries the format and no name: a model's name is the installation's
 * own vocabulary and is not part of what a follower is handed. So the page invents one, and only
 * the extension was ever load-bearing — which is exactly why this sits here beside the rule it is
 * an instance of, rather than being spelled inline at the one mount that needs it.
 */
export function unnamedViewerFileName(format: SurveyModelInfo['format']): string {
  return `trip.${format === 'lox' ? 'lox' : '3d'}`;
}
