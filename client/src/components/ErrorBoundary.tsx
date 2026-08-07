// SPDX-License-Identifier: AGPL-3.0-or-later
import { Component, type ErrorInfo, type ReactNode } from 'react';
import { Button, Result, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { reportError } from '../diagnostics/reporter.ts';

/**
 * What is shown when a screen stops rendering.
 *
 * Until this existed, one throw anywhere in the tree left an empty white page: React unmounts the
 * whole tree when nothing catches, so the failure took the navigation, the map and every way of
 * getting somewhere else with it, and said nothing about what had happened. A person could not tell
 * that from a broken installation.
 *
 * Reloading is offered rather than a "try again", because there is no state to retry from — the tree
 * that failed has already gone. The message deliberately does not claim the error was recorded:
 * that is only true while the development server is running, and telling somebody their problem has
 * been filed when it has not is worse than saying nothing.
 */
function ErrorFallback({ detail }: { detail: string }) {
  const { t } = useTranslation();
  return (
    <Result
      status="error"
      title={t('errorBoundary.title')}
      subTitle={t('errorBoundary.description')}
      extra={
        <Button type="primary" onClick={() => window.location.reload()}>
          {t('errorBoundary.reload')}
        </Button>
      }
    >
      {/* The message itself, small and last: useless to most people and the first thing asked for
          when somebody reports this to whoever runs the installation. */}
      {detail && (
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0, textAlign: 'center' }}>
          <Typography.Text code>{detail}</Typography.Text>
        </Typography.Paragraph>
      )}
    </Result>
  );
}

interface Props {
  children: ReactNode;
}

interface State {
  detail: string;
  failed: boolean;
}

export default class ErrorBoundary extends Component<Props, State> {
  state: State = { detail: '', failed: false };

  static getDerivedStateFromError(error: unknown): State {
    return { failed: true, detail: error instanceof Error ? error.message : String(error) };
  }

  /**
   * A boundary is the only observer of this class of error that cannot be a listener: React hands a
   * caught error to a component instead of letting it reach the window, and the component stack that
   * comes with it — which names the part of the tree that failed — exists nowhere else.
   */
  componentDidCatch(error: Error, info: ErrorInfo): void {
    reportError({
      kind: 'uncaught',
      name: error.name,
      message: error.message,
      stack: error.stack,
      componentStack: info.componentStack ?? undefined,
    });
  }

  render(): ReactNode {
    return this.state.failed ? <ErrorFallback detail={this.state.detail} /> : this.props.children;
  }
}
