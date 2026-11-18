import type { TFunction } from 'i18next';
import { describe, expect, it } from 'vitest';
import en from '../../i18n/locales/en.json';
import { cavePotentialFailureText } from './cavePotentialFailure.ts';

// A lookup over the real English file, so a key the helper builds that does not exist fails here.
const t = ((key: string, options?: { defaultValue?: string }) => {
  const value = key.split('.').reduce<unknown>((node, part) => (node as Record<string, unknown> | undefined)?.[part], en);
  return typeof value === 'string' ? value : (options?.defaultValue ?? key);
}) as unknown as TFunction;

describe('cavePotentialFailureText', () => {
  it('words a failure from its code and never from the server message', () => {
    expect(cavePotentialFailureText(t, { errorCode: 'cave_potential.access_changed', message: 'English from the server' }))
      .toBe(en.cavePotential.failures.access_changed);
  });

  it('falls back to the general failure for a missing or unknown code', () => {
    expect(cavePotentialFailureText(t, { errorCode: null, message: null })).toBe(en.cavePotential.failures.failed);
    expect(cavePotentialFailureText(t, { errorCode: 'cave_potential.something_new' })).toBe(en.cavePotential.failures.failed);
  });

  it('translates the layer and reason codes of unavailable evidence', () => {
    const text = cavePotentialFailureText(t, {
      errorCode: 'cave_potential.evidence_unavailable',
      message: 'slope: no_layer, elevation: no_terrain',
    });
    expect(text).toContain(en.cavePotential.evidence.slope);
    expect(text).toContain(en.cavePotential.unavailable.no_layer);
    expect(text).toContain(en.cavePotential.evidence.elevation);
    expect(text).toContain(en.cavePotential.unavailable.no_terrain);
    expect(text).not.toContain('no_layer');
  });

  it('words a run that read too many entrances from the translation files', () => {
    const text = cavePotentialFailureText(t, { errorCode: 'cave_potential.too_many_entrances', message: 'raw english' });
    expect(text).toBe(en.cavePotential.failures.too_many_entrances);
  });
});
