// SPDX-License-Identifier: AGPL-3.0-or-later
import { ease, type Ease, type ScenarioItem } from './scenario.ts';

// The things a presentation does through the interface, written as the steps a person's hand
// takes: move to something, press it, pause, drag. Each action is a generator so that the next
// step is decided only when the previous one has finished — a group of basemaps has to be open
// before the pointer can travel to a row inside it, and where that row is is not known before.

export type PointerStep =
  | { kind: 'move'; target: () => Element; duration: number }
  | { kind: 'click'; target: () => Element }
  | { kind: 'pause'; duration: number }
  /** Holds until `ready` answers true — a popover finishing opening, a group of rows unfolding. */
  | { kind: 'wait'; ready: () => boolean; what: string }
  | { kind: 'drag'; handle: () => Element; rail: () => Element; values: number[]; duration: number; ease?: Ease };

export const MOVE_SECONDS = 0.6;
export const PAUSE_SECONDS = 0.35;

/** A step's target is not on screen yet — a popover still opening, a row not drawn yet. */
export class NotReadyError extends Error {}

function need<T extends Element>(found: T | null | undefined, what: string): T {
  if (!found) {
    throw new NotReadyError(`presenter: ${what} is not on the page`);
  }
  return found;
}

function visible(node: Element | null): boolean {
  if (!(node instanceof HTMLElement)) {
    return false;
  }
  const box = node.getBoundingClientRect();
  return box.width > 0 && box.height > 0 && !node.closest('.ant-popover-hidden');
}

function layerPanel(): HTMLElement | null {
  const panel = document.querySelector<HTMLElement>('[data-testid="scene3d-layer-panel"]');
  return visible(panel) ? panel : null;
}

function panelTrigger(): Element {
  return need(document.querySelector('[data-testid="scene3d-layers-trigger"]'), 'the 3D layer button');
}

function normalise(text: string | null | undefined): string {
  return (text ?? '').replace(/\s+/g, ' ').trim().toLowerCase();
}

function basemapRadio(name: string): HTMLElement | undefined {
  const panel = layerPanel();
  if (!panel) {
    return undefined;
  }
  return Array.from(panel.querySelectorAll<HTMLElement>('.ant-radio-wrapper')).find(
    (row) => normalise(row.textContent) === normalise(name),
  );
}

/** The header of the basemap group called `group`, whose label is "<group> (<count>)". */
function groupHeader(group: string): HTMLElement | undefined {
  const panel = layerPanel();
  if (!panel) {
    return undefined;
  }
  const collapse = panel.querySelector('.layer-group-collapse');
  return Array.from(collapse?.querySelectorAll<HTMLElement>('.ant-collapse-header') ?? []).find((header) =>
    normalise(header.textContent).startsWith(`${normalise(group)} (`),
  );
}

function isOpen(header: HTMLElement): boolean {
  return header.getAttribute('aria-expanded') === 'true';
}

const panelShown: PointerStep = { kind: 'wait', ready: () => Boolean(layerPanel()), what: 'the 3D layer panel' };

function* openPanel(): Generator<PointerStep> {
  if (!layerPanel()) {
    yield { kind: 'move', target: panelTrigger, duration: MOVE_SECONDS };
    yield { kind: 'click', target: panelTrigger };
    yield panelShown;
    yield { kind: 'pause', duration: PAUSE_SECONDS };
  }
}

/** The opacity slider on the row of the basemap that is chosen. */
function activeBasemapSlider(): { handle: Element; rail: Element } {
  const panel = need(layerPanel(), 'the 3D layer panel');
  const checked = need(panel.querySelector('.ant-radio-wrapper-checked'), 'a chosen basemap');
  const row = need(checked.closest('.scene3d-layer-row'), 'the chosen basemap’s row');
  return {
    handle: need(row.querySelector('.ant-slider-handle'), 'the opacity slider’s handle'),
    rail: need(row.querySelector('.ant-slider-rail'), 'the opacity slider'),
  };
}

export function* stepsFor(action: ScenarioItem): Generator<PointerStep> {
  switch (action.kind) {
    case 'layerPanel': {
      if (Boolean(layerPanel()) !== action.open) {
        yield { kind: 'move', target: panelTrigger, duration: MOVE_SECONDS };
        yield { kind: 'click', target: panelTrigger };
        yield { kind: 'wait', ready: () => Boolean(layerPanel()) === action.open, what: 'the 3D layer panel to change' };
        yield { kind: 'pause', duration: PAUSE_SECONDS };
      }
      return;
    }
    case 'basemap': {
      yield* openPanel();
      if (!basemapRadio(action.name) || !visible(basemapRadio(action.name) ?? null)) {
        // Fold away whichever group is open, as a person tidying the list would, then open the
        // one the basemap is in.
        const target = action.group;
        if (!target) {
          throw new Error(`presenter: basemap "${action.name}" is in a closed group and the scenario names no group`);
        }
        const panel = need(layerPanel(), 'the 3D layer panel');
        for (const header of Array.from(panel.querySelectorAll<HTMLElement>('.layer-group-collapse .ant-collapse-header'))) {
          if (isOpen(header) && !normalise(header.textContent).startsWith(`${normalise(target)} (`)) {
            yield { kind: 'move', target: () => header, duration: MOVE_SECONDS };
            yield { kind: 'click', target: () => header };
          }
        }
        const header = () => need(groupHeader(target), `the basemap group "${target}"`);
        if (!isOpen(header() as HTMLElement)) {
          yield { kind: 'move', target: header, duration: MOVE_SECONDS };
          yield { kind: 'click', target: header };
          yield { kind: 'pause', duration: PAUSE_SECONDS };
        }
        yield {
          kind: 'wait',
          ready: () => visible(basemapRadio(action.name) ?? null),
          what: `the basemap "${action.name}"`,
        };
      }
      const radio = () => need(basemapRadio(action.name), `the basemap "${action.name}"`);
      yield { kind: 'move', target: radio, duration: MOVE_SECONDS };
      yield { kind: 'click', target: () => need(radio().querySelector('input'), 'the basemap’s button') };
      return;
    }
    case 'layer': {
      yield* openPanel();
      const row = () =>
        need(
          Array.from(need(layerPanel(), 'the 3D layer panel').querySelectorAll<HTMLElement>('.ant-checkbox-wrapper')).find(
            (candidate) => normalise(candidate.textContent).includes(normalise(action.label)),
          ),
          `the layer "${action.label}"`,
        );
      const checked = row().classList.contains('ant-checkbox-wrapper-checked');
      yield { kind: 'move', target: row, duration: MOVE_SECONDS };
      if (checked !== action.on) {
        yield { kind: 'click', target: () => need(row().querySelector('input'), 'the layer’s box') };
      }
      return;
    }
    case 'basemapOpacity': {
      yield* openPanel();
      yield { kind: 'move', target: () => activeBasemapSlider().handle, duration: MOVE_SECONDS };
      yield {
        kind: 'drag',
        handle: () => activeBasemapSlider().handle,
        rail: () => activeBasemapSlider().rail,
        values: action.values,
        duration: Math.max(0.5, (action.duration ?? 2) - MOVE_SECONDS),
        ease: action.ease,
      };
      return;
    }
    default:
      return;
  }
}

/** The slider value the drag has reached at progress `p`, through every listed value in turn. */
export function dragValue(values: number[], p: number, kind?: Ease): number {
  const x = ease(kind, p) * (values.length - 1);
  const index = Math.min(values.length - 2, Math.floor(x));
  const local = x - index;
  return values[index] + (values[index + 1] - values[index]) * local;
}

export function centreOf(target: Element): { x: number; y: number } {
  target.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  const box = target.getBoundingClientRect();
  return { x: box.left + box.width / 2, y: box.top + box.height / 2 };
}

export function mouse(type: 'mousedown' | 'mousemove' | 'mouseup', target: EventTarget, x: number, y: number): void {
  target.dispatchEvent(
    new MouseEvent(type, {
      bubbles: true,
      cancelable: true,
      clientX: x,
      clientY: y,
      button: 0,
      buttons: type === 'mouseup' ? 0 : 1,
      view: window,
    }),
  );
}
