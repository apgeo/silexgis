// SPDX-License-Identifier: AGPL-3.0-or-later
import { describe, expect, it } from 'vitest';
import { FLYBY_VERSION, type FlybyDocument } from './flybyDocument.ts';
import {
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
  };
  return { context: context as unknown as FlybyTextCanvas, drawn, boxes };
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

  it('clears what was drawn before', () => {
    const { context, drawn } = recorder();
    drawFlybyTexts(context, { dim: 0, title: { text: 'One', alpha: 1 } }, 100, 100);
    drawFlybyTexts(context, { dim: 0 }, 100, 100);
    expect(drawn).toEqual([]);
  });
});
