// SPDX-License-Identifier: AGPL-3.0-or-later
import type { components } from '../schema';

// A flyby of the 3D scene asked for as a render bundle for Blender: the shapes of the request.
// The answer is a file, fetched with the download helpers, so there is no hook here — only the
// words the request is written in.

/** Everything a bundle is asked for with: the camera at every frame, the caves, the look and the texts. */
export type FlybyBundleRequest = components['schemas']['FlybyBlenderBundleRequest'];

/** Something the flyby shows that a bundle does not carry, by the name the server knows it by. */
export type FlybyBundleOmission = components['schemas']['FlybyBundleOmission'];

export type FlybyBundleLook = components['schemas']['FlybyBundleLookRequest'];
export type FlybyBundleTexts = components['schemas']['FlybyBundleTextsRequest'];
export type FlybyBundleCave = components['schemas']['FlybyBundleCaveRequest'];
