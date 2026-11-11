// SPDX-License-Identifier: AGPL-3.0-or-later
import { createRunner } from './runner.ts';
import { scenarioLength, scenarioProblems } from './scenario.ts';

// The presentation runner as a script of its own, put into the page by the recorder. It is not
// part of the application's bundle: it only needs the page's 3D recording handle and the page's
// own elements.

declare global {
  interface Window {
    __silexgisPresenterRunner?: {
      createRunner: typeof createRunner;
      scenarioLength: typeof scenarioLength;
      scenarioProblems: typeof scenarioProblems;
    };
  }
}

window.__silexgisPresenterRunner = { createRunner, scenarioLength, scenarioProblems };
