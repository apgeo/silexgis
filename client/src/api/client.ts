// SPDX-License-Identifier: AGPL-3.0-or-later
import createClient from 'openapi-fetch';
import type { paths } from './schema';
import { userManager } from '../auth/auth.tsx';

/**
 * Typed API client generated from the server OpenAPI contract.
 * `schema.d.ts` is generated — regenerate with `npm run generate:api`, never hand-edit.
 */
export const api = createClient<paths>({ baseUrl: '/' });

api.use({
  async onRequest({ request }) {
    const user = await userManager.getUser();
    if (user?.access_token) {
      request.headers.set('Authorization', `Bearer ${user.access_token}`);
    }
    return request;
  },
});
