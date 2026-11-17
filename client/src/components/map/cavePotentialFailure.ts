import type { TFunction } from 'i18next';

/** The parts of a result that say why a run failed. */
export interface CavePotentialFailure {
  errorCode?: string | null;
  message?: string | null;
}

/** A failure's code without the family it belongs to, which is how its wording is looked up. */
function failureKey(code: string | null | undefined): string {
  return (code ?? 'failed').replace(/^cave_potential\./, '');
}

/**
 * Which layers a run could not read and why, as the server writes them (`slope: no_layer, ...`:
 * closed lists of layer and reason codes, saying nothing of a place), in the reader's language.
 * A code outside the lists is left as written rather than dropped.
 */
function unavailableText(t: TFunction, message: string): string {
  return message
    .split(', ')
    .map((pair) => {
      const [layer, reason] = pair.split(': ');
      if (!layer || !reason) {
        return pair;
      }
      return `${t(`cavePotential.evidence.${layer}`, { defaultValue: layer })}: ${t(
        `cavePotential.unavailable.${reason}`,
        { defaultValue: reason },
      )}`;
    })
    .join(', ');
}

/**
 * The words for a failed run, always from the translation files and never the server's English:
 * the one place the layer panel and the page both read, so the two cannot disagree.
 */
export function cavePotentialFailureText(t: TFunction, result: CavePotentialFailure): string {
  const sentence = t(`cavePotential.failures.${failureKey(result.errorCode)}`, {
    defaultValue: t('cavePotential.failures.failed'),
  });
  return result.errorCode === 'cave_potential.evidence_unavailable' && result.message
    ? `${sentence} (${unavailableText(t, result.message)})`
    : sentence;
}
