// SPDX-License-Identifier: AGPL-3.0-or-later
import { afterEach, describe, expect, it, vi } from 'vitest';
import { MemoryMovieSink } from '../memoryMovieSink.ts';
import { openMovieEncoder, probeMovieFormats } from '../movieEncoder.ts';
import { isMovieSinkMismatch, type MovieSink } from '../movieSink.ts';
import { h264Level, videoBitrate, videoCodecCandidates, type VideoMovieEncoder } from './videoEncoder.ts';

// ---- A stand-in for the browser's WebCodecs encoder: it records what it was asked, answers with
// ---- small fake chunks, and lets a test hold its queue full to exercise back-pressure.

interface Encoded {
  timestamp: number;
  duration: number | null;
  keyFrame: boolean;
}

class FakeVideoFrame {
  static open = 0;
  static made = 0;
  closed = false;
  readonly timestamp: number;
  readonly duration: number;
  constructor(_source: unknown, init: { timestamp: number; duration: number }) {
    this.timestamp = init.timestamp;
    this.duration = init.duration;
    FakeVideoFrame.open++;
    FakeVideoFrame.made++;
  }
  close() {
    if (!this.closed) FakeVideoFrame.open--;
    this.closed = true;
  }
}

class FakeVideoEncoder extends EventTarget {
  static supported: (codec: string) => boolean = () => true;
  static last: FakeVideoEncoder | null = null;
  /** Frames, by index, that come back as keyframes although nobody asked for one. */
  static unaskedKeys = new Set<number>();
  static async isConfigSupported(config: VideoEncoderConfig) {
    return { supported: FakeVideoEncoder.supported(config.codec), config };
  }

  state: 'unconfigured' | 'configured' | 'closed' = 'unconfigured';
  config: VideoEncoderConfig | null = null;
  encoded: Encoded[] = [];
  queue: Encoded[] = [];
  /** While true, frames stay queued until release() is called. */
  hold = false;
  ondequeue: (() => void) | null = null;
  private readonly init: { output: (c: unknown, m?: unknown) => void; error: (e: unknown) => void };

  constructor(init: { output: (c: unknown, m?: unknown) => void; error: (e: unknown) => void }) {
    super();
    this.init = init;
    FakeVideoEncoder.last = this;
  }
  get encodeQueueSize() {
    return this.queue.length;
  }
  configure(config: VideoEncoderConfig) {
    this.config = config;
    this.state = 'configured';
  }
  encode(frame: FakeVideoFrame, options: { keyFrame: boolean }) {
    if (this.state !== 'configured') throw new DOMException('not configured', 'InvalidStateError');
    if (frame.closed) throw new TypeError('the frame is closed');
    const keyFrame = options.keyFrame || FakeVideoEncoder.unaskedKeys.has(this.encoded.length);
    const e = { timestamp: frame.timestamp, duration: frame.duration, keyFrame };
    this.encoded.push(e);
    this.queue.push(e);
    if (!this.hold) queueMicrotask(() => this.release());
  }
  release() {
    while (this.queue.length > 0) {
      const e = this.queue.shift()!;
      const first = this.encoded[0] === e;
      const bytes = Uint8Array.from([e.keyFrame ? 1 : 0, e.timestamp & 0xff, (e.timestamp >> 8) & 0xff]);
      this.init.output(
        {
          type: e.keyFrame ? 'key' : 'delta',
          timestamp: e.timestamp,
          duration: e.duration,
          byteLength: bytes.length,
          copyTo: (dest: Uint8Array) => dest.set(bytes),
        },
        first ? { decoderConfig: { codec: this.config!.codec, description: Uint8Array.from([1, 0x42, 0, 0x1f, 0xff, 0xe1, 0, 0]) } } : undefined,
      );
      this.dispatchEvent(new Event('dequeue'));
    }
  }
  async flush() {
    if (this.state === 'closed') throw new DOMException('closed', 'AbortError');
    this.release();
  }
  close() {
    if (this.state === 'closed') throw new DOMException('closed', 'InvalidStateError');
    this.state = 'closed';
  }
  failWith(error: unknown) {
    this.state = 'closed';
    this.init.error(error);
  }
}

function installWebCodecs() {
  FakeVideoFrame.open = 0;
  FakeVideoFrame.made = 0;
  FakeVideoEncoder.supported = () => true;
  FakeVideoEncoder.last = null;
  FakeVideoEncoder.unaskedKeys = new Set();
  vi.stubGlobal('VideoEncoder', FakeVideoEncoder);
  vi.stubGlobal('VideoFrame', FakeVideoFrame);
}

function canvas(): HTMLCanvasElement {
  return Object.assign(document.createElement('canvas'), { width: 64, height: 36 });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe('video codec choice', () => {
  it('picks the lowest H.264 level that holds the size and rate, and names Baseline at that level', () => {
    expect(h264Level(640, 360, 25)).toBe(31);
    expect(h264Level(1280, 720, 30)).toBe(31);
    expect(h264Level(1280, 720, 60)).toBe(32);
    expect(h264Level(1920, 1080, 30)).toBe(40);
    expect(h264Level(1920, 1080, 60)).toBe(42);
    expect(h264Level(20000, 16, 25)).toBeNull();
    expect(videoCodecCandidates('mp4', 1920, 1080, 25)).toEqual(['avc1.420028', 'avc1.42e028']);
    expect(videoCodecCandidates('webm', 1920, 1080, 25)).toEqual(['vp09.00.10.08', 'vp8']);
  });

  it('sizes the bitrate from the picture, the rate and the quality', () => {
    expect(videoBitrate(1280, 720, 25, 'low')).toBe(1_152_000);
    expect(videoBitrate(1280, 720, 25, 'medium')).toBe(2_304_000);
    expect(videoBitrate(1280, 720, 25, 'high')).toBe(4_608_000);
  });

  it('offers only GIF where there is no video encoder, or where it supports nothing', async () => {
    const none = await probeMovieFormats({ width: 640, height: 360 }, 25);
    expect(none.filter((f) => f.supported).map((f) => f.format)).toEqual(['gif']);
    await expect(openMovieEncoder('webm', { width: 640, height: 360, fps: 25, quality: 'medium' })).rejects.toThrow(
      /^WEBM encoder, open: this browser cannot encode it at 640×360/,
    );

    installWebCodecs();
    FakeVideoEncoder.supported = () => false;
    const nothing = await probeMovieFormats({ width: 640, height: 360 }, 25);
    expect(nothing.filter((f) => f.supported).map((f) => f.format)).toEqual(['gif']);
  });

  it('falls back from VP9 to VP8, reports the codec it chose, and refuses odd sides', async () => {
    installWebCodecs();
    FakeVideoEncoder.supported = (codec) => codec === 'vp8' || codec.startsWith('avc1.4200');
    const formats = await probeMovieFormats({ width: 640, height: 360 }, 25);
    expect(formats).toEqual([
      { format: 'gif', supported: true, codec: null },
      { format: 'webm', supported: true, codec: 'vp8' },
      { format: 'mp4', supported: true, codec: 'avc1.42001f' },
    ]);
    const odd = await probeMovieFormats({ width: 641, height: 360 }, 25);
    expect(odd.filter((f) => f.supported).map((f) => f.format)).toEqual(['gif']);
    await expect(openMovieEncoder('mp4', { width: 641, height: 360, fps: 25, quality: 'low' })).rejects.toThrow(
      /^MP4 encoder, open: .*even/,
    );
  });
});

describe('video movie encoder', () => {
  it('stamps frames by index, asks for a keyframe every two seconds, closes every frame, and writes a WebM', async () => {
    installWebCodecs();
    const encoder = (await openMovieEncoder('webm', { width: 64, height: 36, fps: 10, quality: 'medium' })) as VideoMovieEncoder;
    expect(encoder.samplesWanted).toBe(0);
    const fake = FakeVideoEncoder.last!;
    expect(fake.config).toMatchObject({ codec: 'vp09.00.10.08', width: 64, height: 36, framerate: 10, bitrate: 2304 });
    for (let i = 0; i < 45; i++) await encoder.addFrame(canvas(), i);
    const blob = await encoder.finish();
    expect(fake.encoded.map((e) => e.timestamp)).toEqual(Array.from({ length: 45 }, (_, i) => i * 100_000));
    expect(fake.encoded.filter((e) => e.keyFrame).map((e) => e.timestamp)).toEqual([0, 2_000_000, 4_000_000]);
    expect(FakeVideoFrame.made).toBe(45);
    expect(FakeVideoFrame.open).toBe(0);
    expect(blob.type).toBe('video/webm');
    const head = new Uint8Array(await blob.arrayBuffer()).subarray(0, 4);
    expect([...head]).toEqual([0x1a, 0x45, 0xdf, 0xa3]);
    expect(fake.state).toBe('closed');
  });

  it('writes an MP4 with the decoder configuration the encoder reported', async () => {
    installWebCodecs();
    const encoder = await openMovieEncoder('mp4', { width: 64, height: 36, fps: 25, quality: 'high' });
    expect(FakeVideoEncoder.last!.config).toMatchObject({ codec: 'avc1.42001f', avc: { format: 'avc' } });
    for (let i = 0; i < 5; i++) await encoder.addFrame(canvas(), i);
    const bytes = new Uint8Array(await (await encoder.finish()).arrayBuffer());
    expect(String.fromCharCode(...bytes.subarray(4, 8))).toBe('ftyp');
    const text = String.fromCharCode(...bytes);
    expect(text.indexOf('moov')).toBeLessThan(text.indexOf('mdat'));
    expect(text).toContain('avcC');
  });

  it('holds addFrame back while the browser’s queue is full, and lets it go as frames leave', async () => {
    installWebCodecs();
    const encoder = await openMovieEncoder('webm', { width: 64, height: 36, fps: 10, quality: 'low' });
    const fake = FakeVideoEncoder.last!;
    fake.hold = true;
    for (let i = 0; i < 4; i++) await encoder.addFrame(canvas(), i);
    let settled = false;
    const fifth = encoder.addFrame(canvas(), 4).then(() => (settled = true));
    await new Promise((r) => setTimeout(r, 20));
    expect(settled).toBe(false);
    expect(fake.encodeQueueSize).toBe(5);
    fake.release();
    await fifth;
    expect(settled).toBe(true);
    encoder.close();
  });

  it('surfaces an encoder error with the format and the stage, and rejects what follows', async () => {
    installWebCodecs();
    const encoder = await openMovieEncoder('mp4', { width: 64, height: 36, fps: 10, quality: 'low' });
    await encoder.addFrame(canvas(), 0);
    FakeVideoEncoder.last!.failWith(new DOMException('the codec gave up', 'EncodingError'));
    await expect(encoder.addFrame(canvas(), 1)).rejects.toThrow(/^MP4 encoder, encode: the codec gave up/);
    await expect(encoder.finish()).rejects.toThrow(/^MP4 encoder, encode: the codec gave up/);
    expect(() => encoder.close()).not.toThrow();
  });

  it('closes mid-run without an error: a waiting addFrame resolves, the browser encoder is closed, closing again is harmless', async () => {
    installWebCodecs();
    const encoder = await openMovieEncoder('webm', { width: 64, height: 36, fps: 10, quality: 'low' });
    const fake = FakeVideoEncoder.last!;
    fake.hold = true;
    for (let i = 0; i < 4; i++) await encoder.addFrame(canvas(), i);
    const waiting = encoder.addFrame(canvas(), 4);
    encoder.close();
    await expect(waiting).resolves.toBeUndefined();
    expect(fake.state).toBe('closed');
    expect(() => encoder.close()).not.toThrow();
    await expect(encoder.addFrame(canvas(), 5)).rejects.toThrow(/^WEBM encoder, addFrame: the encoder is closed/);
    await expect(encoder.finish()).rejects.toThrow(/^WEBM encoder, finish: the encoder is closed/);
  });

  it('refuses a frame out of order, naming the stage', async () => {
    installWebCodecs();
    const encoder = await openMovieEncoder('webm', { width: 64, height: 36, fps: 10, quality: 'low' });
    await expect(encoder.addFrame(canvas(), 3)).rejects.toThrow(/^WEBM encoder, addFrame: frame 3 was given where frame 0/);
    encoder.close();
  });
});

describe('video movie encoder, writing to a sink', () => {
  const OPTIONS = { width: 64, height: 36, fps: 10, quality: 'medium' as const };

  async function inMemory(format: 'webm' | 'mp4', count: number): Promise<Uint8Array> {
    const encoder = await openMovieEncoder(format, OPTIONS);
    for (let i = 0; i < count; i++) await encoder.addFrame(canvas(), i);
    return new Uint8Array(await (await encoder.finish()).arrayBuffer());
  }

  it.each(['webm', 'mp4'] as const)('puts in the sink the very %s it would have answered from memory', async (format) => {
    installWebCodecs();
    const expected = await inMemory(format, 45);
    const sink = new MemoryMovieSink();
    const encoder = await openMovieEncoder(format, { ...OPTIONS, sink, frameCount: 45 });
    for (let i = 0; i < 45; i++) await encoder.addFrame(canvas(), i);
    await encoder.finishInSink!();
    expect([...sink.bytes()]).toEqual([...expected]);
    // The sink is whoever opened it's to end: the encoder wrote to it and left it open.
    expect(sink.state).toBe('open');
    expect(FakeVideoEncoder.last!.state).toBe('closed');
    expect(FakeVideoFrame.open).toBe(0);
  });

  it('keeps no frame once it is written, and answers no file of its own', async () => {
    installWebCodecs();
    const sink = new MemoryMovieSink();
    const encoder = (await openMovieEncoder('mp4', { ...OPTIONS, sink, frameCount: 5 })) as VideoMovieEncoder;
    for (let i = 0; i < 5; i++) await encoder.addFrame(canvas(), i);
    expect(encoder.encodedFrames).toBe(5);
    await expect(encoder.finish()).rejects.toThrow(/^MP4 encoder, finish: the encoder writes to a sink/);
    await encoder.finishInSink();
    expect(String.fromCharCode(...sink.bytes().subarray(4, 8))).toBe('ftyp');
  });

  it('needs the number of frames beforehand, and refuses to open without it', async () => {
    installWebCodecs();
    await expect(openMovieEncoder('webm', { ...OPTIONS, sink: new MemoryMovieSink() })).rejects.toThrow(
      /^WEBM encoder, open: a file written as it is encoded needs the number of frames/,
    );
  });

  it('passes on, as itself, the refusal of a file that could not be laid out as planned', async () => {
    installWebCodecs();
    FakeVideoEncoder.unaskedKeys = new Set([3]);
    const encoder = await openMovieEncoder('mp4', { ...OPTIONS, sink: new MemoryMovieSink(), frameCount: 8 });
    const outcome = await (async () => {
      for (let i = 0; i < 8; i++) await encoder.addFrame(canvas(), i);
      await encoder.finishInSink!();
    })().catch((error: unknown) => error);
    expect(isMovieSinkMismatch(outcome)).toBe(true);
    encoder.close();
  });

  it('fails with the stage named when the sink cannot be written to, and waits for a slow one', async () => {
    installWebCodecs();
    let release!: () => void;
    const gate = new Promise<void>((resolve) => (release = resolve));
    const slow: MovieSink = {
      write: async () => {
        await gate;
        throw new Error('the disk is full');
      },
      close: async () => {},
      abort: async () => {},
    };
    // One frame is the whole movie, so its only write — the file, at the end — is the one that fails.
    const encoder = await openMovieEncoder('mp4', { ...OPTIONS, sink: slow, frameCount: 1 });
    await encoder.addFrame(canvas(), 0);
    let settled = false;
    const finishing = encoder.finishInSink!().catch((error: unknown) => {
      settled = true;
      return error;
    });
    await new Promise((r) => setTimeout(r, 20));
    expect(settled).toBe(false);
    release();
    const error = await finishing;
    expect((error as Error).message).toMatch(/^MP4 encoder, write: the disk is full/);
    expect(FakeVideoEncoder.last!.state).toBe('closed');
  });

  it('closes mid-run without writing a file: the sink holds no front, and is left for its owner to abort', async () => {
    installWebCodecs();
    const sink = new MemoryMovieSink();
    const encoder = await openMovieEncoder('mp4', { ...OPTIONS, sink, frameCount: 40 });
    for (let i = 0; i < 10; i++) await encoder.addFrame(canvas(), i);
    encoder.close();
    await expect(encoder.finishInSink!()).rejects.toThrow(/^MP4 encoder, finish: the encoder is closed/);
    expect(sink.writes.every((w) => w.position > 0)).toBe(true);
    expect(sink.state).toBe('open');
  });
});
