// SPDX-License-Identifier: AGPL-3.0-or-later
import type Map from 'ol/Map';
import { unByKey } from 'ol/Observable';
import Control from 'ol/control/Control';
import OverviewMap from 'ol/control/OverviewMap';
import type { EventsKey } from 'ol/events';
import TileLayer from 'ol/layer/Tile';
import type TileSource from 'ol/source/Tile';

/** The library's overview control, with its toggle button reachable — the control keeps its element to itself. */
class ToggleableOverviewMap extends OverviewMap {
  toggleButton(): HTMLButtonElement | null {
    return this.element.querySelector('button');
  }
}

export interface OverviewMapOptions {
  /** Whether the inset starts open. */
  open: boolean;
  /** The name of the button that opens and closes it, in the reader's language. */
  label: string;
  /** Told each time the reader opens or closes it, so the choice can be remembered. */
  onToggle: (open: boolean) => void;
}

const CLASS_NAME = 'ol-overviewmap map-overview';

export interface OverviewMapHandle {
  /** Opens or closes the inset, as though its button had been pressed, without reporting it. */
  setOpen: (open: boolean) => void;
  /** Takes the control off the map. */
  detach: () => void;
}

/**
 * A small overview of the map's surroundings in the corner, drawn from the base layer the map is
 * showing.
 *
 * The inset is a second map with layers of its own; each mirrors one of the main map's base
 * layers — the same tile source, so no tile is fetched twice — and is shown exactly while that
 * base layer is. Switching base layer, from the panel or from the chooser, therefore switches the
 * inset with it, and the inset never draws anything the main map is not already drawing.
 *
 * Closed, there is no second map at all, only the button that opens one: the library's own closed
 * inset keeps its whole map in the page, drawing nothing, and a page with two maps in it is two of
 * everything a map is found by. The button is drawn with the library's classes, so closed and open
 * look like the one control.
 *
 * Returns the control's handle: `setOpen` opens or closes it from outside — the account's own
 * choice can arrive after the map is drawn — and `detach` takes it off the map again.
 */
export function attachOverviewMap(
  map: Map,
  baseLayers: TileLayer<TileSource>[],
  { open, label, onToggle }: OverviewMapOptions,
): OverviewMapHandle {
  let current: Control | undefined;
  let isOpen = open;
  let keys: EventsKey[] = [];
  let removeClick: (() => void) | undefined;

  const take = () => {
    removeClick?.();
    removeClick = undefined;
    unByKey(keys);
    keys = [];
    if (current) {
      map.removeControl(current);
      current = undefined;
    }
  };

  const describe = (button: HTMLButtonElement | null, expanded: boolean) => {
    button?.setAttribute('aria-label', label);
    button?.setAttribute('aria-expanded', String(expanded));
  };

  const showClosed = (focus: boolean) => {
    take();
    isOpen = false;
    const element = document.createElement('div');
    element.className = `${CLASS_NAME} ol-collapsed ol-unselectable ol-control`;
    const button = document.createElement('button');
    button.type = 'button';
    button.title = label;
    // The glyph the library's own closed inset shows.
    const glyph = document.createElement('span');
    glyph.setAttribute('aria-hidden', 'true');
    glyph.textContent = '›';
    button.append(glyph);
    describe(button, false);
    element.append(button);
    current = new Control({ element });
    map.addControl(current);
    const onClick = () => {
      onToggle(true);
      showOpen(true);
    };
    button.addEventListener('click', onClick);
    removeClick = () => button.removeEventListener('click', onClick);
    if (focus) {
      button.focus();
    }
  };

  const showOpen = (focus: boolean) => {
    take();
    isOpen = true;
    const mirrors = baseLayers.map(
      (base) => new TileLayer({ source: base.getSource() ?? undefined, visible: base.getVisible() }),
    );
    keys = baseLayers.map((base, index) =>
      base.on('change:visible', () => mirrors[index].setVisible(base.getVisible())),
    );
    const control = new ToggleableOverviewMap({
      className: CLASS_NAME,
      layers: mirrors,
      collapsed: false,
      collapsible: true,
      tipLabel: label,
    });
    current = control;
    map.addControl(control);
    // The library gives its button a title and a glyph for a label, which names it by the glyph.
    const button = control.toggleButton();
    describe(button, true);
    // Added after the library's own listener on the same button, which has collapsed the inset
    // by the time this runs; the collapsed inset is then put away for the plain button.
    const onClick = () => {
      if (control.getCollapsed()) {
        onToggle(false);
        showClosed(true);
      }
    };
    button?.addEventListener('click', onClick);
    removeClick = () => button?.removeEventListener('click', onClick);
    if (focus) {
      button?.focus();
    }
  };

  if (open) {
    showOpen(false);
  } else {
    showClosed(false);
  }
  return {
    setOpen: (wanted) => {
      if (wanted !== isOpen) {
        if (wanted) {
          showOpen(false);
        } else {
          showClosed(false);
        }
      }
    },
    detach: take,
  };
}
