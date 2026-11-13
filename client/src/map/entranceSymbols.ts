// SPDX-License-Identifier: AGPL-3.0-or-later

// Which symbols the installation draws its cave entrances with.
//
// An administrator chooses them; the server publishes the choice with the map's configuration (and
// with a shared view, for a link's page). Until that answer arrives the defaults below are drawn,
// which are the server's own defaults — so an installation nobody has configured never shows a
// change of symbol when the configuration lands.
//
// A module of its own, free of any drawing library, for the same reason as the feature-type
// catalog: the flat map and the 3D scene both draw entrances, and the scene is a separate chunk.

export const DEFAULT_ENTRANCE_SYMBOL = 'shape:cave';
export const DEFAULT_APPROXIMATE_ENTRANCE_SYMBOL = 'shape:cave:d46b08';

export interface EntranceSymbols {
  entranceSymbol: string;
  approximateEntranceSymbol: string;
}

let current: EntranceSymbols = {
  entranceSymbol: DEFAULT_ENTRANCE_SYMBOL,
  approximateEntranceSymbol: DEFAULT_APPROXIMATE_ENTRANCE_SYMBOL,
};

const listeners = new Set<() => void>();

/**
 * Takes the installation's choice. Notifies only on a real change: the map's configuration is
 * re-read now and then, and every notification redraws every entrance on the map and in the scene.
 */
export function setEntranceSymbols(next: Partial<EntranceSymbols> | null | undefined): void {
  const resolved: EntranceSymbols = {
    entranceSymbol: next?.entranceSymbol || DEFAULT_ENTRANCE_SYMBOL,
    approximateEntranceSymbol: next?.approximateEntranceSymbol || DEFAULT_APPROXIMATE_ENTRANCE_SYMBOL,
  };
  if (
    resolved.entranceSymbol === current.entranceSymbol &&
    resolved.approximateEntranceSymbol === current.approximateEntranceSymbol
  ) {
    return;
  }
  current = resolved;
  for (const listener of [...listeners]) {
    listener();
  }
}

/** The reference an entrance is drawn with, by whether its position is shown approximately. */
export function entranceSymbolReference(approximate: boolean): string {
  return approximate ? current.approximateEntranceSymbol : current.entranceSymbol;
}

/** Subscribes to a change of choice; returns an unsubscribe function. */
export function onEntranceSymbolsChanged(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}
