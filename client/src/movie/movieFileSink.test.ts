// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  chooseMovieFile,
  MOVIE_DISK_BYTES,
  movieGoesToDisk,
  openMovieFileSink,
  type MovieFileHandle,
} from './movieFileSink.ts';

/** A file as the browser hands one out: what was written only becomes the file when it is closed. */
function fakeFile(
  name = 'chosen.webm',
  options: { removable?: boolean; writable?: boolean; earlier?: number[]; sized?: boolean } = {},
) {
  const { removable = true, writable = true, earlier, sized = true } = options;
  const log: string[] = [];
  // `earlier` is what a file the reader chose to replace already holds; without it the file is the
  // empty one the save question makes.
  const state = { exists: true, content: earlier ?? (null as number[] | null) };
  const handle: MovieFileHandle = {
    name,
    getFile: (async () => {
      if (!sized) throw new DOMException('not allowed', 'NotAllowedError');
      return { size: state.content?.length ?? 0 };
    }) as unknown as MovieFileHandle['getFile'],
    createWritable: (async () => {
      if (!writable) throw new DOMException('not allowed', 'NotAllowedError');
      log.push('open');
      const swap: number[] = [];
      return {
        write: async (chunk: { type: string; position: number; data: Uint8Array }) => {
          log.push(`write:${chunk.type}@${chunk.position}+${chunk.data.byteLength}`);
          chunk.data.forEach((byte, i) => (swap[chunk.position + i] = byte));
        },
        close: async () => {
          log.push('close');
          state.content = Array.from(swap, (byte) => byte ?? 0);
        },
        abort: async () => {
          log.push('abort');
        },
      };
    }) as unknown as MovieFileHandle['createWritable'],
    ...(removable
      ? {
          remove: async () => {
            log.push('remove');
            state.exists = false;
            state.content = null;
          },
        }
      : {}),
  };
  return { handle, log, state };
}

function installPicker(picker: (options: unknown) => Promise<MovieFileHandle>) {
  const asked = vi.fn(picker);
  vi.stubGlobal('showSaveFilePicker', asked);
  return asked;
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('movieGoesToDisk', () => {
  it('is never so where the browser cannot be asked where to save', () => {
    expect(movieGoesToDisk('webm', MOVIE_DISK_BYTES * 4)).toBe(false);
    expect(movieGoesToDisk('mp4', MOVIE_DISK_BYTES * 4)).toBe(false);
  });

  it('is so for a video from 256 MiB up, and never for a GIF or a smaller video', () => {
    installPicker(async () => fakeFile().handle);
    expect(MOVIE_DISK_BYTES).toBe(256 * 1024 * 1024);
    expect(movieGoesToDisk('webm', MOVIE_DISK_BYTES)).toBe(true);
    expect(movieGoesToDisk('mp4', MOVIE_DISK_BYTES)).toBe(true);
    expect(movieGoesToDisk('webm', MOVIE_DISK_BYTES - 1)).toBe(false);
    expect(movieGoesToDisk('mp4', MOVIE_DISK_BYTES - 1)).toBe(false);
    expect(movieGoesToDisk('gif', MOVIE_DISK_BYTES * 4)).toBe(false);
  });
});

describe('chooseMovieFile', () => {
  it('asks under the name and type of the movie, and answers the file under the name the reader gave it', async () => {
    const file = fakeFile('my caving weekend.mp4');
    const asked = installPicker(async () => file.handle);
    const choice = await chooseMovieFile('silexgis-movie-2026-10-08.mp4', 'mp4');
    expect(asked).toHaveBeenCalledWith({
      suggestedName: 'silexgis-movie-2026-10-08.mp4',
      types: [{ accept: { 'video/mp4': ['.mp4'] } }],
    });
    expect(choice.kind).toBe('file');
    expect(choice.kind === 'file' && choice.name).toBe('my caving weekend.mp4');
    expect(file.log).toEqual(['open']);
  });

  it('answers that the question was closed, and touches nothing', async () => {
    installPicker(async () => {
      throw new DOMException('The user aborted a request.', 'AbortError');
    });
    expect(await chooseMovieFile('movie.webm', 'webm')).toEqual({ kind: 'dismissed' });
  });

  it('answers that the movie is made in memory where the browser will not ask, or has no such question', async () => {
    expect(await chooseMovieFile('movie.webm', 'webm')).toEqual({ kind: 'memory' });
    installPicker(async () => {
      throw new DOMException('Must be handling a user gesture.', 'SecurityError');
    });
    expect(await chooseMovieFile('movie.webm', 'webm')).toEqual({ kind: 'memory' });
  });

  it('takes away the file the question made when it cannot be opened for writing, and makes the movie in memory', async () => {
    const file = fakeFile('chosen.webm', { writable: false });
    installPicker(async () => file.handle);
    expect(await chooseMovieFile('movie.webm', 'webm')).toEqual({ kind: 'memory' });
    expect(file.log).toEqual(['remove']);
    expect(file.state.exists).toBe(false);
  });

  it('leaves an earlier file as it was when it cannot be opened for writing', async () => {
    const file = fakeFile('last month.webm', { writable: false, earlier: [7, 8, 9] });
    installPicker(async () => file.handle);
    expect(await chooseMovieFile('movie.webm', 'webm')).toEqual({ kind: 'memory' });
    expect(file.log).toEqual([]);
    expect(file.state).toEqual({ exists: true, content: [7, 8, 9] });
  });
});

describe('a sink over a chosen file', () => {
  it('writes at the positions it is given, and the file is what was written once it is closed', async () => {
    const file = fakeFile();
    const sink = await openMovieFileSink(file.handle);
    await sink.write(4, Uint8Array.from([5, 6]));
    await sink.write(0, Uint8Array.from([1, 2, 3]));
    expect(file.state.content).toBeNull();
    await sink.close();
    expect(file.log).toEqual(['open', 'write:write@4+2', 'write:write@0+3', 'close']);
    expect(file.state.content).toEqual([1, 2, 3, 0, 5, 6]);
  });

  it('leaves nothing behind when aborted: what was written is dropped and the file is removed', async () => {
    const file = fakeFile();
    const sink = await openMovieFileSink(file.handle);
    await sink.write(10, Uint8Array.from([1]));
    await sink.abort();
    await sink.abort();
    expect(file.log).toEqual(['open', 'write:write@10+1', 'abort', 'remove']);
    expect(file.state).toEqual({ exists: false, content: null });
  });

  it('removes the finished file when the abort comes after the close', async () => {
    const file = fakeFile();
    const sink = await openMovieFileSink(file.handle);
    await sink.write(0, Uint8Array.from([1]));
    await sink.close();
    await sink.abort();
    expect(file.log.slice(-2)).toEqual(['close', 'remove']);
    expect(file.state.exists).toBe(false);
  });

  it('empties the file where the browser cannot remove one', async () => {
    const file = fakeFile('chosen.webm', { removable: false });
    const sink = await openMovieFileSink(file.handle);
    await sink.write(0, Uint8Array.from([1, 2, 3]));
    await sink.abort();
    expect(file.log).toEqual(['open', 'write:write@0+3', 'abort', 'open', 'close']);
    expect(file.state.content).toEqual([]);
  });

  it('leaves an earlier file the reader chose to replace as it was when aborted', async () => {
    const file = fakeFile('last month.webm', { earlier: [7, 8, 9] });
    const sink = await openMovieFileSink(file.handle);
    await sink.write(0, Uint8Array.from([1, 2, 3, 4]));
    await sink.abort();
    await sink.abort();
    expect(file.log).toEqual(['open', 'write:write@0+4', 'abort']);
    expect(file.state).toEqual({ exists: true, content: [7, 8, 9] });
  });

  it('never empties an earlier file where the browser cannot remove one', async () => {
    const file = fakeFile('last month.webm', { removable: false, earlier: [7, 8, 9] });
    const sink = await openMovieFileSink(file.handle);
    await sink.write(0, Uint8Array.from([1]));
    await sink.abort();
    expect(file.log).toEqual(['open', 'write:write@0+1', 'abort']);
    expect(file.state.content).toEqual([7, 8, 9]);
  });

  it('keeps the finished movie when the abort comes after it has replaced an earlier file', async () => {
    const file = fakeFile('last month.webm', { earlier: [7, 8, 9] });
    const sink = await openMovieFileSink(file.handle);
    await sink.write(0, Uint8Array.from([1, 2]));
    await sink.close();
    await sink.abort();
    expect(file.log).toEqual(['open', 'write:write@0+2', 'close']);
    expect(file.state).toEqual({ exists: true, content: [1, 2] });
  });

  it('removes nothing of a file that will not say how large it is', async () => {
    const file = fakeFile('chosen.webm', { sized: false });
    const sink = await openMovieFileSink(file.handle);
    await sink.abort();
    expect(file.log).toEqual(['open', 'abort']);
    expect(file.state.exists).toBe(true);
  });
});
