// SPDX-License-Identifier: AGPL-3.0-or-later
import { MOVIE_EXTENSION, MOVIE_MIME, type MovieFormat } from './encode/movieFormats.ts';
import type { MovieSink } from './encode/movieSink.ts';

/**
 * From what estimated size a video is written to a file the reader chooses, instead of being held
 * in memory until it is finished and then handed to the browser to save: 256 MiB.
 *
 * Below it, the way every movie was saved before is kept as it was: one file in memory is well
 * within what a browser holds, the download needs no question answered, and it works in every
 * browser. Above it the whole movie in memory is what makes a long export fail.
 * The figure itself is where writing as it goes starts to save anything: a WebM's first 256 MiB
 * are held before any of it can be placed in the file, so a smaller one would be asked where to
 * go and then be assembled in memory all the same.
 */
export const MOVIE_DISK_BYTES = 2 ** 28;

/** The part of a file handle a movie is written through, with the removal not every browser has. */
export type MovieFileHandle = Pick<FileSystemFileHandle, 'name' | 'createWritable' | 'getFile'> & {
  remove?: () => Promise<void>;
};

interface SaveFilePickerOptions {
  suggestedName?: string;
  types?: { description?: string; accept: Record<string, string[]> }[];
}
type SaveFilePicker = (options?: SaveFilePickerOptions) => Promise<MovieFileHandle>;

// Asked of the window each time, never remembered: only Chromium browsers have it, and only on a
// page served securely.
function saveFilePicker(): SaveFilePicker | null {
  if (typeof window === 'undefined') return null;
  const picker = (window as unknown as { showSaveFilePicker?: unknown }).showSaveFilePicker;
  return typeof picker === 'function' ? (picker as SaveFilePicker).bind(window) : null;
}

/**
 * Whether a movie of this format and estimated size is written to a file as it is encoded. Never a
 * GIF, whose encoder assembles the picture in memory; never where the browser cannot write a file
 * it was pointed at; never a movie small enough to hold.
 */
export function movieGoesToDisk(format: MovieFormat, estimatedBytes: number): boolean {
  return format !== 'gif' && estimatedBytes >= MOVIE_DISK_BYTES && saveFilePicker() !== null;
}

export type MovieFileChoice =
  /** The reader chose a file; `name` is what they called it. */
  | { kind: 'file'; name: string; sink: MovieSink }
  /** The reader closed the question without choosing. Nothing was started and nothing is. */
  | { kind: 'dismissed' }
  /** The browser would not ask or would not open the file: the movie is made in memory after all. */
  | { kind: 'memory' };

/**
 * Asks the reader where the movie is to be saved. A browser only asks in answer to a press, so
 * this is called from the press itself, before anything is awaited.
 */
export async function chooseMovieFile(suggestedName: string, format: MovieFormat): Promise<MovieFileChoice> {
  const picker = saveFilePicker();
  if (picker === null) return { kind: 'memory' };
  let handle: MovieFileHandle;
  try {
    handle = await picker({
      suggestedName,
      types: [{ accept: { [MOVIE_MIME[format]]: [`.${MOVIE_EXTENSION[format]}`] } }],
    });
  } catch (error) {
    // Closing the question is reported as an abort; anything else is the browser refusing to ask.
    return (error as { name?: unknown } | null)?.name === 'AbortError' ? { kind: 'dismissed' } : { kind: 'memory' };
  }
  try {
    return { kind: 'file', name: handle.name, sink: await openMovieFileSink(handle) };
  } catch {
    // A file that cannot be opened for writing: the sink has already taken away what the question
    // made, and the movie is made in memory.
    return { kind: 'memory' };
  }
}

/**
 * A sink over a file the reader chose.
 *
 * The browser writes to a temporary copy and only puts it in the file's place when the sink is
 * closed, so an export that stops half-way never leaves half a movie under the name that was
 * chosen: dropping the temporary copy is all that undoing the writing takes.
 *
 * What is left then depends on what the reader pointed at. A new name leaves the empty file the
 * save question created, which is removed — as is the finished movie, when the abort comes after
 * the close. A file that was already there and had something in it is the reader's earlier file,
 * never written over until the close: it is left exactly as it was, and once the close has
 * replaced it there is no earlier file to give back, so the finished one stays.
 */
export async function openMovieFileSink(handle: MovieFileHandle): Promise<MovieSink> {
  const ours = await madeByTheQuestion(handle);
  let stream: FileSystemWritableFileStream;
  try {
    stream = await handle.createWritable();
  } catch (error) {
    if (ours) await removeFile(handle);
    throw error;
  }
  let state: 'open' | 'closed' | 'aborted' = 'open';
  return {
    write: (position, data) => stream.write({ type: 'write', position, data: data as BufferSource }),
    close: async () => {
      await stream.close();
      state = 'closed';
    },
    abort: async () => {
      if (state === 'aborted') return;
      const was = state;
      state = 'aborted';
      if (was === 'open') {
        try {
          await stream.abort();
        } catch {
          // A stream that failed is already gone, and its temporary copy with it.
        }
      }
      if (ours) await removeFile(handle);
    },
  };
}

/**
 * Whether the file is the empty one the save question made, and so this export's to take away
 * again. Read before anything is opened for writing. A file with something in it was there before;
 * so, for this purpose, is one that will not say how large it is — a file is only ever removed on
 * the knowledge that nothing of the reader's is in it.
 */
async function madeByTheQuestion(handle: MovieFileHandle): Promise<boolean> {
  try {
    return (await handle.getFile()).size === 0;
  } catch {
    return false;
  }
}

async function removeFile(handle: MovieFileHandle): Promise<void> {
  try {
    if (typeof handle.remove === 'function') {
      await handle.remove();
      return;
    }
    // A browser that can write a file but not remove it: the file is left, with nothing in it.
    const stream = await handle.createWritable();
    await stream.close();
  } catch {
    // The reader may have moved or deleted it meanwhile, or taken the permission back.
  }
}
