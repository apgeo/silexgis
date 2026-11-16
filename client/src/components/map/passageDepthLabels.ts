// SPDX-License-Identifier: AGPL-3.0-or-later
/** The class labels, from the edges the server answered with so the key follows its classes. */
export function passageDepthClassLabels(
  edges: readonly number[],
  t: (key: string, options?: Record<string, unknown>) => string,
): string[] {
  const labels: string[] = [];
  for (let index = 0; index <= edges.length; index += 1) {
    if (index === 0) {
      labels.push(t('map.passageDepth.legendAbove'));
    } else if (index === edges.length) {
      labels.push(t('map.passageDepth.legendOver', { from: edges[index - 1] }));
    } else {
      labels.push(t('map.passageDepth.legendBand', { from: edges[index - 1], to: edges[index] }));
    }
  }
  return labels;
}
