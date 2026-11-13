// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { FLYBY_VERSION, type FlybyDocument } from './flybyDocument.ts';
import {
  clearFlybyTexts,
  drawFlybyTexts,
  flybyTextPlan,
  flybyTextPlanIsEmpty,
  flybyTextsAt,
  sameFlybyTexts,
  type FlybyTextCanvas,
} from './flybyTexts.ts';

/** An approach of 10 s and a circle of 20 s: 30 s in all. */
function flyby(texts: FlybyDocument['texts'], speed = 1): FlybyDocument {
  return {
    version: FLYBY_VERSION,
    name: '',
    speed,
    loop: false,
    subjects: [{ kind: 'cave', caveId: 'c1' }],
    start: { subject: 0, pose: { heading: 0, pitch: -20, range: 6, lift: 0, clearance: 60 } },
    sections: [
      { id: 'approach-1', kind: 'approach', subject: 0, seconds: 10, to: { range: 3 } },
      { id: 'orbit-2', kind: 'orbit', subject: 0, seconds: 20, to: { headingBy: 360 } },
    ],
    texts,
  };
}

/** A canvas that writes down what it was asked to draw, with text 0.5 of the font size per letter. */
function recorder() {
  const drawn: { text: string; x: number; y: number; alpha: number; font: string }[] = [];
  const boxes: { fill: string; alpha: number }[] = [];
  const turns: { x: number; y: number; angle: number }[] = [];
  const context = {
    font: '10px sans-serif',
    fillStyle: '#000',
    globalAlpha: 1,
    textAlign: 'start',
    textBaseline: 'alphabetic',
    shadowColor: '',
    shadowBlur: 0,
    stack: [] as number[],
    save() {
      this.stack.push(this.globalAlpha);
    },
    restore() {
      this.globalAlpha = this.stack.pop() ?? 1;
    },
    clearRect() {
      drawn.length = 0;
      boxes.length = 0;
    },
    fillRect() {
      boxes.push({ fill: String(this.fillStyle), alpha: this.globalAlpha });
    },
    fill() {
      boxes.push({ fill: String(this.fillStyle), alpha: this.globalAlpha });
    },
    fillText(text: string, x: number, y: number) {
      drawn.push({ text, x, y, alpha: this.globalAlpha, font: this.font });
    },
    measureText(text: string) {
      const size = Number(/(\d+(\.\d+)?)px/.exec(this.font)?.[1] ?? 10);
      return { width: text.length * size * 0.5 } as TextMetrics;
    },
    beginPath() {},
    moveTo() {},
    arcTo() {},
    closePath() {},
    lineTo() {},
    arc() {},
    stroke() {},
    translate(x: number, y: number) {
      turns.push({ x, y, angle: 0 });
    },
    rotate(angle: number) {
      turns[turns.length - 1].angle = angle;
    },
  };
  // What the texts are drawn with, and what the owner of a texts-only canvas wipes it with.
  return {
    context: context as unknown as FlybyTextCanvas & Pick<CanvasRenderingContext2D, 'clearRect'>,
    drawn,
    boxes,
    turns,
  };
}

describe('what a flyby writes over the scene', () => {
  it('shows the title from the first frame and takes it away before it ends', () => {
    const plan = flybyTextPlan(flyby({ title: { text: 'Peștera', subtitle: 'Bihor', seconds: 4 } }));
    expect(flybyTextsAt(plan, 0).title).toEqual({ text: 'Peștera', subtitle: 'Bihor', alpha: 1 });
    expect(flybyTextsAt(plan, 3.6).title!.alpha).toBeGreaterThan(0);
    expect(flybyTextsAt(plan, 3.6).title!.alpha).toBeLessThan(1);
    expect(flybyTextsAt(plan, 4.5).title).toBeUndefined();
  });

  it('brings the end card in and keeps it to the last frame', () => {
    const plan = flybyTextPlan(flyby({ end: { text: 'Club', seconds: 5 } }));
    expect(flybyTextsAt(plan, 24).end).toBeUndefined();
    expect(flybyTextsAt(plan, 25.2).end!.alpha).toBeLessThan(1);
    expect(flybyTextsAt(plan, 30).end).toEqual({ text: 'Club', alpha: 1 });
  });

  it('places a caption in its section, moving and stretching with it', () => {
    const texts = { captions: [{ section: 'orbit-2', text: 'Around the entrance', at: 2, seconds: 6 }] };
    const plan = flybyTextPlan(flyby(texts));
    expect(plan.captions).toEqual([{ text: 'Around the entrance', start: 12, end: 18 }]);
    expect(flybyTextsAt(plan, 15).caption).toEqual({ text: 'Around the entrance', alpha: 1 });
    expect(flybyTextsAt(plan, 11).caption).toBeUndefined();
    // At twice the speed the circle starts at 5 and the caption is at 1 s into it, for 3 s.
    expect(flybyTextPlan(flyby(texts, 2)).captions).toEqual([{ text: 'Around the entrance', start: 6, end: 9 }]);
  });

  it('rises out of black and sinks back into it', () => {
    const plan = flybyTextPlan(flyby({ fadeIn: 2, fadeOut: 3 }));
    expect(flybyTextsAt(plan, 0).dim).toBe(1);
    expect(flybyTextsAt(plan, 1).dim).toBeCloseTo(0.5, 6);
    expect(flybyTextsAt(plan, 10).dim).toBe(0);
    expect(flybyTextsAt(plan, 28.5).dim).toBeCloseTo(0.5, 6);
    expect(flybyTextsAt(plan, 30).dim).toBe(1);
    // Backwards as forwards.
    expect(flybyTextsAt(plan, 1)).toEqual(flybyTextsAt(plan, 1));
  });

  it('knows when there is nothing to draw', () => {
    expect(flybyTextPlanIsEmpty(flybyTextPlan(flyby(undefined)))).toBe(true);
    expect(flybyTextPlanIsEmpty(flybyTextPlan(flyby({ fadeOut: 1 })))).toBe(false);
    expect(sameFlybyTexts({ dim: 0 }, { dim: 0 })).toBe(true);
    expect(sameFlybyTexts({ dim: 0 }, { dim: 0.1 })).toBe(false);
  });
});

describe('drawing the texts', () => {
  it('draws a title in the middle, sized by the frame rather than in fixed pixels', () => {
    const state = { dim: 0, title: { text: 'Peștera Urșilor', subtitle: 'Chișcău', alpha: 1 } };
    const small = recorder();
    drawFlybyTexts(small.context, state, 640, 360);
    const large = recorder();
    drawFlybyTexts(large.context, state, 1920, 1080);
    expect(small.drawn.map((entry) => entry.text)).toEqual(['Peștera Urșilor', 'Chișcău']);
    // Three times the frame, three times the text and three times as far from the edge.
    const fontSize = (font: string) => Number(/(\d+(\.\d+)?)px/.exec(font)![1]);
    expect(fontSize(large.drawn[0].font)).toBeCloseTo(fontSize(small.drawn[0].font) * 3, 6);
    expect(large.drawn[0].x).toBeCloseTo(small.drawn[0].x * 3, 6);
    expect(large.drawn[0].y).toBeCloseTo(small.drawn[0].y * 3, 6);
    expect(small.drawn[0].x).toBe(320);
  });

  it('darkens the whole frame by the fade and draws a caption on a band near the bottom', () => {
    const { context, drawn, boxes } = recorder();
    drawFlybyTexts(context, { dim: 0.4, caption: { text: 'A long caption', alpha: 0.5 } }, 800, 600);
    expect(boxes[0]).toEqual({ fill: 'rgba(0, 0, 0, 0.4)', alpha: 1 });
    expect(boxes[1]).toEqual({ fill: 'rgba(0, 0, 0, 0.55)', alpha: 0.5 });
    expect(drawn).toHaveLength(1);
    expect(drawn[0]).toMatchObject({ text: 'A long caption', alpha: 0.5, x: 400 });
    expect(drawn[0].y).toBeGreaterThan(600 * 0.75);
  });

  it('wraps a line too long for the frame at its spaces, and cuts what will not fit', () => {
    const { context, drawn } = recorder();
    const words = Array.from({ length: 60 }, (_, index) => `word${index}`).join(' ');
    drawFlybyTexts(context, { dim: 0, caption: { text: words, alpha: 1 } }, 400, 400);
    expect(drawn.length).toBe(3);
    expect(drawn[2].text.endsWith('…')).toBe(true);
    // Nothing clears the frame's width.
    for (const line of drawn) {
      expect(line.text.length * 400 * 0.042 * 0.5).toBeLessThanOrEqual(400 * 0.8 + 400 * 0.042);
    }
  });

  it('draws over what the canvas holds, and leaves wiping it to whoever owns the canvas', () => {
    // A movie's frame is the scene with the texts over it, on one canvas: texts that cleared
    // first would leave a film of captions on nothing.
    const { context, drawn } = recorder();
    drawFlybyTexts(context, { dim: 0, title: { text: 'One', alpha: 1 } }, 100, 100);
    drawFlybyTexts(context, { dim: 0 }, 100, 100);
    expect(drawn.map((line) => line.text)).toEqual(['One']);
    // The canvas that holds only texts is emptied between moments by its owner.
    clearFlybyTexts(context, 100, 100);
    expect(drawn).toEqual([]);
  });
});

describe('the furniture of the frame', () => {
  it('draws a north arrow that turns against the camera, and a line in the corner, for the whole flyby', () => {
    const plan = flybyTextPlan(flyby({ northArrow: true, corner: 'Clubul de speologie' }));
    expect(flybyTextPlanIsEmpty(plan)).toBe(false);
    plan.heading = (t) => t * 3;
    expect(flybyTextsAt(plan, 0)).toEqual({ dim: 0, north: 0, corner: 'Clubul de speologie' });
    expect(flybyTextsAt(plan, 30)).toEqual({ dim: 0, north: 90, corner: 'Clubul de speologie' });

    const { context, drawn, turns } = recorder();
    drawFlybyTexts(context, flybyTextsAt(plan, 30), 1000, 600);
    // In the top right corner, turned a quarter turn back: north is to the left when facing east.
    expect(turns).toHaveLength(1);
    expect(turns[0].x).toBeGreaterThan(900);
    expect(turns[0].y).toBeLessThan(100);
    expect(turns[0].angle).toBeCloseTo(-Math.PI / 2);
    expect(drawn.map((entry) => entry.text)).toEqual(['N', 'Clubul de speologie']);
    // The corner line is at the top left, clear of the credits along the bottom.
    expect(drawn[1].x).toBeLessThan(50);
    expect(drawn[1].y).toBeLessThan(50);
  });

  it('draws neither unless asked, and points the arrow up when nobody says which way the camera faces', () => {
    expect(flybyTextsAt(flybyTextPlan(flyby({})), 3)).toEqual({ dim: 0 });
    expect(flybyTextsAt(flybyTextPlan(flyby({ northArrow: true })), 3).north).toBe(0);
  });
});

describe('the mark of a stretched relief', () => {
  it('says the relief in a corner only while the factor is not 1, rounded to a tenth', () => {
    const plan = flybyTextPlan(flyby({}));
    // A ramp from true height to ×3 over the first 10 s, and back to true height at the end.
    plan.relief = (t) => (t <= 10 ? 1 + 0.2 * t : t >= 29 ? 1 : 3);
    plan.reliefWords = (factor) => `Relief ×${factor}`;
    expect(flybyTextPlanIsEmpty(plan)).toBe(false);
    expect(flybyTextsAt(plan, 0)).toEqual({ dim: 0 });
    // A factor a hair from true is true height to the tenth, and says nothing.
    expect(flybyTextsAt(plan, 0.2).relief).toBeUndefined();
    expect(flybyTextsAt(plan, 5).relief).toBe('Relief ×2');
    expect(flybyTextsAt(plan, 20).relief).toBe('Relief ×3');
    expect(flybyTextsAt(plan, 30).relief).toBeUndefined();
    // Scrubbed back, each moment says what it said going forwards.
    expect(flybyTextsAt(plan, 5)).toEqual(flybyTextsAt(plan, 5));
    // Two moments of the ramp within the same tenth draw the same picture, so the canvas is not drawn again.
    expect(sameFlybyTexts(flybyTextsAt(plan, 5), flybyTextsAt(plan, 5.2))).toBe(true);

    const { context, drawn } = recorder();
    drawFlybyTexts(context, flybyTextsAt(plan, 20), 1000, 600);
    expect(drawn.map((entry) => entry.text)).toEqual(['Relief ×3']);
    expect(drawn[0].x).toBeLessThan(50);
    expect(drawn[0].y).toBeLessThan(50);
  });

  it('goes under the corner line, and is not drawn when the flyby switches it off', () => {
    const plan = flybyTextPlan(flyby({ corner: 'Club' }));
    plan.relief = () => 2;
    const { context, drawn } = recorder();
    drawFlybyTexts(context, flybyTextsAt(plan, 3), 1000, 600);
    expect(drawn.map((entry) => entry.text)).toEqual(['Club', '×2']);
    expect(drawn[1].y).toBeGreaterThan(drawn[0].y);

    const off = flybyTextPlan(flyby({ reliefMark: false }));
    off.relief = () => 2;
    expect(flybyTextsAt(off, 3)).toEqual({ dim: 0 });
    expect(flybyTextPlanIsEmpty(off)).toBe(true);
  });

  it('is written to a file only when switched off, and read back so', async () => {
    const { normalizeFlybyDocument } = await import('./flybyDocument.ts');
    const off = flyby({ reliefMark: false });
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(off)))).toEqual(off);
    expect(normalizeFlybyDocument(JSON.parse(JSON.stringify(flyby({ reliefMark: true }))))?.texts).toEqual({});
  });
});
