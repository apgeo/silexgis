// SPDX-License-Identifier: AGPL-3.0-or-later
import { create } from 'zustand';
import type { FlybyChange, FlybyDocument, FlybyFeatureLayer, FlybyLook, FlybyPresetId, FlybyTexts } from '../../../flyby/flybyDocument.ts';
import type { FlybyCapturedView, FlybyPresetOptions } from '../../../flyby/flybyPresets.ts';

/** What a flyby is about: the selection, caves picked in view, a saved filter, the view itself or a file. */
export type FlybySource = 'selected' | 'inView' | 'filter' | 'view' | 'file';

/** Everything the reader put together in the flyby tool that is not yet kept under a name. */
export interface FlybyDraftState {
  source: FlybySource;
  checked: string[];
  presetChoice: FlybyPresetId | undefined;
  fileId: string | undefined;
  savedFilterId: string | undefined;
  filterLimit: number;
  options: FlybyPresetOptions;
  speed: number;
  loop: boolean;
  name: string;
  edited: FlybyDocument | null;
  look: FlybyLook;
  changes: FlybyChange[];
  texts: FlybyTexts;
  features: FlybyFeatureLayer[];
  views: FlybyCapturedView[];
}

export interface FlybyDraft {
  state: FlybyDraftState;
  /**
   * The draft as it stood when last saved or opened, written the same way as `flybyDraftPrint`, so
   * a reopened tool still knows whether anything is unsaved.
   */
  clean: string;
}

interface FlybyDraftStoreState {
  draft: FlybyDraft | null;
  keep(draft: FlybyDraft): void;
}

/**
 * The flyby being put together, kept while the tool is closed so that opening it again brings the
 * work back. Held in memory only, on purpose: the flybys a reader chose to keep are the saved
 * library in this browser; an unsaved draft lasts until the page is reloaded or closed.
 */
export const useFlybyDraftStore = create<FlybyDraftStoreState>()((set) => ({
  draft: null,
  keep: (draft) => set({ draft }),
}));

/** One comparable text for a draft: two drafts that would build the same flyby print the same. */
export function flybyDraftPrint(state: FlybyDraftState): string {
  return JSON.stringify(state);
}
