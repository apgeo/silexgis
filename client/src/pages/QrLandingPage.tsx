// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { CheckCircleOutlined, QuestionCircleOutlined } from '@ant-design/icons';
import { Card, Flex, Result, Spin, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useParams } from 'react-router-dom';
import { fetchPublicQr } from '../api/hooks.ts';

/**
 * What somebody standing at a cave with a phone gets when they point it at a printed label.
 *
 * A page rather than a payload: the address in the square is opened by a camera app, which hands
 * it to a browser, and a browser showing raw JSON to a caver in a field is a failure however
 * correct the JSON is.
 *
 * It says which installation the code is registered with and nothing else — no name, no position,
 * not even for a code that resolves. That is not this page being cautious with what it was given;
 * the server's answer has no field for either, so there is nothing here to leak and nothing to
 * remember not to render. A code that does not resolve, one nobody ever issued, one on a cave
 * nobody published and one on a cave somebody stopped publishing are the same answer from the
 * server and are the same page here, deliberately: telling them apart is telling somebody which
 * codes exist.
 */
export default function QrLandingPage() {
  const { t } = useTranslation();
  const { code } = useParams<{ code: string }>();
  const [state, setState] = useState<{ status: 'loading' } | { status: 'found'; instanceName: string } | { status: 'unknown' }>({
    status: 'loading',
  });

  useEffect(() => {
    let disposed = false;
    setState({ status: 'loading' });
    fetchPublicQr(code ?? '')
      .then((data) => {
        if (!disposed) {
          setState({ status: 'found', instanceName: data.instanceName });
        }
      })
      .catch(() => {
        // Every reason a code fails to resolve reaches here as the same failure, because the
        // server gave them all the same answer. Nothing is inspected to find out which.
        if (!disposed) {
          setState({ status: 'unknown' });
        }
      });
    return () => {
      disposed = true;
    };
  }, [code]);

  return (
    <Flex align="center" justify="center" style={{ minHeight: '100vh', padding: 16 }}>
      <Card style={{ maxWidth: 520, width: '100%' }}>
        {state.status === 'loading' && (
          <Flex align="center" justify="center" style={{ padding: 48 }}>
            <Spin size="large" />
          </Flex>
        )}

        {state.status === 'found' && (
          <Result
            icon={<CheckCircleOutlined />}
            status="success"
            title={t('qr.landingFoundTitle')}
            subTitle={t('qr.landingFoundBody', { instance: state.instanceName })}
            extra={
              <Typography.Text code data-testid="qr-landing-code">
                {code}
              </Typography.Text>
            }
          />
        )}

        {state.status === 'unknown' && (
          <Result
            icon={<QuestionCircleOutlined />}
            status="warning"
            title={t('qr.landingUnknownTitle')}
            subTitle={t('qr.landingUnknownBody')}
            extra={
              <Typography.Text code data-testid="qr-landing-code">
                {code}
              </Typography.Text>
            }
          />
        )}
      </Card>
    </Flex>
  );
}
