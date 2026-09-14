// SPDX-License-Identifier: AGPL-3.0-or-later

/**
 * Which of the vendored viewer's own controls a surface puts over its model.
 *
 * Kept away from the panel that mounts the toolbar, and beside the rest of what this application
 * knows about the viewer, for the reason the neighbouring modules are: none of it imports the
 * viewer, so all of it is a choice over plain values that a test can drive without a drawing
 * context — and a surface that has to take one control out of a set can ask for the set without
 * pulling a React component in behind it.
 *
 * Every measurement quoted below was taken on the live model, on the profile it names.
 */

/**
 * The viewer controls a full-width panel offers: everything the toolbar can hold.
 *
 * <b>The four elevations and the two projections are here because nothing else reaches them.</b>
 * They were left out while the reasoning was that both are "already in the side panel" — but that
 * panel is the vendored viewer's own tree-and-settings drawer, which this application does not
 * mount on any of the four surfaces that draw a model. So a reader had a plan view and no way at
 * all to look at a cave from the side, which is how depth is read; and no way to swap the
 * projection, which is what makes a long horizontal cave legible end-on.
 *
 * Fifteen controls at the size a mouse needs come to 539px, measured over an 826px model, which
 * fits every full-width mount. The toolbar wraps rather than overflowing if one is ever narrower.
 *
 * <b>This is the set for a mouse, and only for a mouse.</b> Under a finger every one of these is
 * grown to forty pixels and the same fifteen no longer fit anywhere — see the coarse set below.
 */
const TOOLBAR_BUTTONS = [
  'stations',
  'stationLabels',
  'splays',
  'walls',
  'scraps',
  'entrances',
  'viewPlan',
  'viewNorth',
  'viewEast',
  'viewSouth',
  'viewWest',
  'cameraPerspective',
  'cameraOrthographic',
  'shadingMode',
  'fullscreen',
] as const;

/**
 * The same toolbar under a finger, cut to what one row will hold.
 *
 * <b>This set exists because the size of a control is not a question about width.</b> Every control
 * in the bar is grown to forty pixels on a coarse pointer, which this panel's own stylesheet asks
 * for, and a set chosen on width alone therefore hands the mouse set at finger sizes to every
 * device that is wide and coarse at once. A phone held sideways is exactly that: 863px across,
 * `pointer: coarse`, and 360px tall.
 *
 * Measured on the live model, where a coarse control costs 44px of bar and the shading chooser 96px:
 * the fifteen come to 732px, the landscape phone gives the model surface 709px, and the bar
 * therefore wrapped — 709x98 over a model surface of 709x216, two rows of finger-sized controls
 * taking 45% of the model. Thirteen fit there at 638px. Twelve is what also fits at the narrowest
 * width this set can be reached at: at 768px, the breakpoint below which the narrow set takes over,
 * the surface measures 614px and twelve come to 594px. So twelve is the number.
 *
 * <b>What the three cut are, and why they are these three.</b> The compass is kept whole — four
 * elevations are how depth is read and half a compass is not a control — and so is the plan that
 * comes back from wherever a gesture has turned the model to. What goes is the splay toggle, which
 * turns *on* a close reading of a survey done at a desk, and the two projections, which change how
 * the same view is drawn rather than what is shown. That is the same order of preference the narrow
 * set below is built on, applied with more room.
 */
const COARSE_TOOLBAR_BUTTONS = [
  'stations',
  'stationLabels',
  'walls',
  'scraps',
  'entrances',
  'viewPlan',
  'viewNorth',
  'viewEast',
  'viewSouth',
  'viewWest',
  'shadingMode',
  'fullscreen',
] as const;

/**
 * What is left on a phone, and why it is this and not more.
 *
 * <b>The limit is one row, and it is a hard one.</b> The toolbar wraps rather than overflowing, so
 * a set that does not fit is not unreachable — it is a second row of finger-sized controls, 44px
 * off the top of a model that is only 320px tall to begin with. Measured on a 412px phone, where
 * the model surface comes to 338px: five controls fit in one row of 54px, six take two rows of 98.
 * So the set stays at five and what earns a place has to displace something.
 *
 * <b>The plan view earns its place, and takes the splays' one.</b> Of the six view controls, the
 * plan is the only one that can be offered alone — the four elevations are a compass, and half a
 * compass is not a control — and it is the way back from wherever the model has been dragged to,
 * which matters far more on a touchscreen than with a mouse because every gesture there turns the
 * scene and nothing else returns it. What it displaces is the splay toggle: splays are drawn off
 * by default, so that button's use is turning them *on*, which is a close reading of a survey and
 * is done at a desk. The two projections stay out on both counts — they change how the same view
 * is drawn rather than what is shown.
 *
 * The other half of making this fit is the shading chooser, which the viewer reserves 160px for on
 * a coarse pointer; this panel's stylesheet narrows it, and that narrowing is what keeps even this
 * five-control set on one row down at 360px.
 */
const NARROW_TOOLBAR_BUTTONS = [
  'stations',
  'stationLabels',
  'viewPlan',
  'shadingMode',
  'fullscreen',
] as const;

/**
 * Which of the three sets a surface gets, from the two axes that decide it.
 *
 * <b>Both axes, because they answer different questions.</b> How much room there is across is a
 * question about the *width*; how big each control has to be before a finger can land on it is a
 * question about the *pointer*. A bar is a row of controls, so how many of them fit is the first
 * answer divided by the second, and reading either one off the other is what put two rows of
 * finger-sized buttons over a 216px model on a phone held sideways.
 *
 * Width is still what decides the narrowest case on its own, and that is not an inconsistency: the
 * narrowest screen this application is designed at is 360px, where the full set does not fit at a
 * mouse's sizes either. Below the breakpoint there is no pointer for which more is safe.
 *
 * Exported so a surface that has to take something out of the set does not have to decide the rest
 * of it again — the choice is made here, and a caller subtracts from what it is given.
 */
export function caveViewToolbarButtons(axes: {
  narrow: boolean;
  coarse: boolean;
}): readonly string[] {
  if (axes.narrow) {
    return NARROW_TOOLBAR_BUTTONS;
  }
  return axes.coarse ? COARSE_TOOLBAR_BUTTONS : TOOLBAR_BUTTONS;
}
