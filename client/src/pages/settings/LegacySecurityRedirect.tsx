// SPDX-License-Identifier: AGPL-3.0-or-later
import { Navigate, useLocation } from 'react-router-dom';

/**
 * Keeps the old security page address working. The query string is carried across on purpose:
 * an external sign-in link round-trip comes back with its result in the query, and dropping it
 * would silently swallow the message the user is waiting for.
 */
export default function LegacySecurityRedirect() {
  const { search } = useLocation();
  return <Navigate to={{ pathname: '/settings/security', search }} replace />;
}
