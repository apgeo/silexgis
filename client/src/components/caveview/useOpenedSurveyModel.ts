// SPDX-License-Identifier: AGPL-3.0-or-later
import { useCallback, useState } from 'react';
import { useSurveyModel, type SurveyModelInfo } from '../../api/hooks.ts';

/**
 * A survey model opened in a dialog, held by its id and read afresh on every render.
 *
 * Never the object it was opened with: a model's answer carries an address signed for ten minutes,
 * which its query asks for again before it lapses, and a dialog left open past that reads the file
 * again — when its second viewer tab is chosen, or on a retry — through whatever address it holds.
 * Reading the model by id shares the query (and its cache) with whatever else shows the same model,
 * so opening the dialog over a pane that already shows the model asks for nothing new.
 *
 * Closed while the answer is not there: a model the reader may no longer see answers 404, and a
 * dialog over nothing has nothing to show.
 */
export function useOpenedSurveyModel(): {
  model: SurveyModelInfo | null;
  open: (model: SurveyModelInfo | null) => void;
  close: () => void;
} {
  const [openedId, setOpenedId] = useState<string | null>(null);
  const { data } = useSurveyModel(openedId ?? undefined);
  const open = useCallback((model: SurveyModelInfo | null) => setOpenedId(model?.id ?? null), []);
  const close = useCallback(() => setOpenedId(null), []);
  return { model: openedId !== null && data?.id === openedId ? data : null, open, close };
}
