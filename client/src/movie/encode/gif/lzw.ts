// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * The variable-width LZW that GIF image data is compressed with.
 *
 * The output is exactly what follows an image descriptor: the minimum code size byte, the code
 * stream cut into data sub-blocks of at most 255 bytes each, and the zero-length block that ends
 * them.
 *
 * <b>How the code width moves, because a decoder has to agree with it bit for bit.</b> A decoder
 * builds its table one step behind the encoder — it cannot add the string a code stands for until
 * it has seen the first pixel of the next one — and widens its codes when its next free entry
 * reaches the next power of two. The encoder therefore widens *after* emitting a code whose next
 * free entry has reached that power, which is also what makes the end-of-information code come
 * out at the width the decoder is expecting. Codes stop at 12 bits: when the table's 4096 entries
 * are used up the encoder emits a clear code (still at 12 bits) and starts again at the minimum
 * width, which is the 4096 reset every decoder implements.
 */

const MAX_CODES = 4096;
const MAX_WIDTH = 12;

// The dictionary is a dense trie: entry (prefix code, next index) holds the code for that string,
// 0 when there is none (every real string code is at least clear + 2, so 0 is never a code). It is
// kept between calls (one per thread) because it would otherwise be a fresh 2 MB allocation per
// frame, and only the entries set since the last reset are cleared, which is at most 4096 of them.
let trie: Int16Array | null = null;
const used = new Int32Array(MAX_CODES);

/**
 * Compresses palette indices (each below `2 ** minCodeSize`) into a GIF image data block.
 *
 * `minCodeSize` is the colour table's bit depth, raised to 2 when the table is smaller than four
 * entries — GIF does not allow a smaller one.
 */
export function lzwEncode(indices: Uint8Array, minCodeSize: number): Uint8Array {
  if (!Number.isInteger(minCodeSize) || minCodeSize < 2 || minCodeSize > 8) {
    throw new RangeError(`LZW minimum code size must be 2 to 8, not ${minCodeSize}`);
  }
  if (indices.length === 0) throw new RangeError('LZW input is empty');
  const dict = (trie ??= new Int16Array(MAX_CODES << 8));
  const clear = 1 << minCodeSize;
  const eoi = clear + 1;
  const n = indices.length;

  // Every pixel emits at most one code, and a clear code is added per 4094 codes; each is at most
  // 12 bits. So this never has to grow.
  const packed = new Uint8Array(Math.ceil((n + (n >> 11) + 4) * 1.5) + 8);
  let pos = 0;
  let acc = 0;
  let bits = 0;
  let width = minCodeSize + 1;
  let next = clear + 2;
  let usedCount = 0;

  const emit = (code: number) => {
    acc |= code << bits;
    bits += width;
    while (bits >= 8) {
      packed[pos++] = acc & 0xff;
      acc >>>= 8;
      bits -= 8;
    }
  };
  // Widening after an emit, never before: see the note at the top of the file.
  const widen = () => {
    if (next >= 1 << width && width < MAX_WIDTH) width++;
  };
  const reset = () => {
    for (let i = 0; i < usedCount; i++) dict[used[i]] = 0;
    usedCount = 0;
  };

  emit(clear);
  let prefix = indices[0];
  for (let i = 1; i < n; i++) {
    const k = indices[i];
    const key = (prefix << 8) | k;
    const code = dict[key];
    if (code !== 0) {
      prefix = code;
      continue;
    }
    emit(prefix);
    widen();
    if (next < MAX_CODES) {
      dict[key] = next++;
      used[usedCount++] = key;
    } else {
      emit(clear);
      reset();
      next = clear + 2;
      width = minCodeSize + 1;
    }
    prefix = k;
  }
  emit(prefix);
  widen();
  emit(eoi);
  if (bits > 0) packed[pos++] = acc & 0xff;
  reset();

  const blocks = Math.ceil(pos / 255);
  const out = new Uint8Array(1 + pos + blocks + 1);
  out[0] = minCodeSize;
  let o = 1;
  for (let start = 0; start < pos; start += 255) {
    const len = Math.min(255, pos - start);
    out[o++] = len;
    out.set(packed.subarray(start, start + len), o);
    o += len;
  }
  out[o] = 0;
  return out;
}
