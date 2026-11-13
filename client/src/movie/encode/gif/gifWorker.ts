// SPDX-License-Identifier: AGPL-3.0-or-later
import { handleGifWorkerMessage, type GifWorkerReply, type GifWorkerRequest, type GifWorkerState } from './gifFrame.ts';

// A module worker of the GIF encoder's pool: it is given the colour map once, then compresses
// frames as they arrive and posts each one back with its sequence number. All of the work is in
// the handler, which the encoder also runs on its own thread where workers are unavailable.
//
// The application is type-checked against the window's library, not the worker's, so the worker
// global is described here by the two members this file uses.
const scope = self as unknown as {
  onmessage: ((event: MessageEvent<GifWorkerRequest>) => void) | null;
  postMessage(message: GifWorkerReply, transfer: ArrayBuffer[]): void;
};

const state: GifWorkerState = { map: null, width: 0, height: 0 };

scope.onmessage = (event) => {
  handleGifWorkerMessage(state, event.data, (reply, transfer) => scope.postMessage(reply, transfer));
};
