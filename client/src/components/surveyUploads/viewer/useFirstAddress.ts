// SPDX-License-Identifier: AGPL-3.0-or-later
import { useRef } from 'react';
import type { ViewerModel } from '../../../api/hooks.ts';

/**
 * The model as first seen, kept while it is the same model: a read that only renewed its signed
 * address hands back the earlier one, so the viewers keep what they loaded.
 */
export function useFirstAddress(model: ViewerModel | null): ViewerModel | null {
  const kept = useRef<ViewerModel | null>(null);
  if (model === null) {
    kept.current = null;
  } else if (kept.current === null || kept.current.name !== model.name || kept.current.format !== model.format) {
    kept.current = model;
  }
  return kept.current;
}
