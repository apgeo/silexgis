// SPDX-License-Identifier: AGPL-3.0-or-later
import { isRouteErrorResponse, useRouteError } from 'react-router-dom';
import { ErrorFallback } from './ErrorBoundary.tsx';
import NotFoundPage from '../pages/NotFoundPage.tsx';

/**
 * What the router shows in place of a route that failed, instead of its own bare error screen.
 *
 * Not reported from here: the router already writes what it caught to the console, and the
 * diagnostics reporter and the browser suite's guard both read that, so a report from this
 * component as well would record every such failure twice.
 */
export default function RouteErrorPage() {
  const error = useRouteError();
  if (isRouteErrorResponse(error) && error.status === 404) {
    return <NotFoundPage />;
  }
  const detail =
    error instanceof Error
      ? error.message
      : isRouteErrorResponse(error)
        ? `${error.status} ${error.statusText}`
        : String(error);
  return <ErrorFallback detail={detail} />;
}
