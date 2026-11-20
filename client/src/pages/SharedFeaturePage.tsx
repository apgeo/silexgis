// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Card, Descriptions, Flex, Spin, Tag, Typography, theme } from 'antd';
import Button from '../components/Button.tsx';
import List from '../components/List.tsx';
import Map from 'ol/Map';
import View from 'ol/View';
import Feature from 'ol/Feature';
import GeoJSONFormat from 'ol/format/GeoJSON';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { useLocation, useParams } from 'react-router-dom';
import { ApiError } from '../api/client.ts';
import { useSharedFeature } from '../api/hooks.ts';
import { useAuth } from '../auth/auth.tsx';
import { editorBackgroundLayer } from '../map/editorBackground.ts';
import { translucent } from '../theme.ts';

/**
 * Anonymous read-only page for a shared feature. The share token resolves server-side to a
 * redacted envelope — protected coordinates and internal ownership fields never reach this
 * page, and login-required shares answer 401 until the viewer signs in. Follows the shared
 * map-view page pattern: chrome-less, public base map, no workspace access.
 *
 * A link that needs an account is opened from a message, in a tab that holds no sign-in (the
 * sign-in lives in the memory of the tab that made it). So signing in from here runs the full
 * sign-in and comes back to this same address, where the read is made again with the bearer.
 * The password page would not do: it ends on the home page, and the link would be lost.
 */
export default function SharedFeaturePage() {
  const { t, i18n } = useTranslation();
  const { token: colors } = theme.useToken();
  // The shared feature is drawn in the application's own primary colour, read from the theme so the
  // page follows it.
  const highlight = colors.colorPrimary;
  const { token } = useParams<{ token: string }>();
  const location = useLocation();
  const { signIn } = useAuth();
  const mapTarget = useRef<HTMLDivElement>(null);
  const { data: envelope, error, refetch, isFetching } = useSharedFeature(token);
  // Beginning the sign-in first reads the sign-in server's own description, so it fails when that
  // server cannot be reached; said here, because the button would otherwise seem to do nothing.
  const [signInUnreachable, setSignInUnreachable] = useState(false);

  // The mini map exists only when the shared envelope carries geometry (protected non-point
  // shapes arrive as null); built after the data lands because its target div mounts with it.
  useEffect(() => {
    const geometry = envelope?.feature.geometry;
    if (!geometry || !mapTarget.current) {
      return;
    }
    const geom = new GeoJSONFormat().readGeometry(geometry, {
      dataProjection: 'EPSG:4326',
      featureProjection: 'EPSG:3857',
    });
    const map = new Map({
      target: mapTarget.current,
      layers: [
        // The fallback background every small map draws when the installation names none. The
        // installation's own catalogue is a signed-in read, and the shared envelope carries no
        // background of its own (unlike a shared map view), so a reader holding only the link
        // gets the same plain background the public survey page shows.
        editorBackgroundLayer(null, i18n.language),
        new VectorLayer({
          source: new VectorSource({ features: [new Feature(geom)] }),
          style: new Style({
            stroke: new Stroke({ color: highlight, width: 3 }),
            fill: new Fill({ color: translucent(highlight, 0.15) }),
            image: new CircleStyle({
              radius: 8,
              fill: new Fill({ color: highlight }),
              stroke: new Stroke({ color: '#fff', width: 2 }),
            }),
          }),
        }),
      ],
      view: new View({ center: [0, 0], zoom: 2 }),
    });
    map.getView().fit(geom.getExtent(), { padding: [40, 40, 40, 40], maxZoom: 15 });
    return () => map.setTarget(undefined);
  }, [envelope, i18n.language, highlight]);

  if (error) {
    // Only the two answers the server gives about the link itself are said as such. Anything else —
    // no connection, a server error, too many requests — is a read that failed and may succeed if
    // asked again, never "this link does not exist".
    const status = error instanceof ApiError ? error.status : undefined;
    return (
      <Flex align="center" justify="center" style={{ height: '100vh' }}>
        {status === 401 ? (
          <Alert
            type="info"
            showIcon
            data-testid="shared-feature-login-required"
            title={t('shares.loginRequired')}
            description={signInUnreachable ? t('auth.serverUnreachable') : undefined}
            action={
              <Button
                size="small"
                type="primary"
                data-testid="shared-feature-sign-in"
                onClick={() => {
                  setSignInUnreachable(false);
                  signIn(location.pathname + location.search + location.hash).catch(() =>
                    setSignInUnreachable(true),
                  );
                }}
              >
                {t('shares.signIn')}
              </Button>
            }
          />
        ) : status === 404 ? (
          <Alert
            type="warning"
            showIcon
            data-testid="shared-feature-not-found"
            title={t('shares.sharedNotFound')}
          />
        ) : (
          <Alert
            type="error"
            showIcon
            data-testid="shared-feature-load-failed"
            title={t('common.loadFailed')}
            action={
              <Button size="small" loading={isFetching} onClick={() => void refetch()}>
                {t('common.retry')}
              </Button>
            }
          />
        )}
      </Flex>
    );
  }

  if (!envelope) {
    return (
      <Flex align="center" justify="center" style={{ height: '100vh' }}>
        <Spin size="large" />
      </Flex>
    );
  }

  const { feature, cave, entrance, centerline, children } = envelope;

  const detailItem = (label: string, value: unknown) =>
    value == null || value === '' ? null : (
      <Descriptions.Item key={label} label={label}>
        {String(value)}
      </Descriptions.Item>
    );

  // A named value in words, or nothing: a value this build has no words for — a server that has grown
  // a new one, or sent none — is left out rather than shown as its lookup key.
  const named = (key: string) => (i18n.exists(key) ? t(key) : null);

  // Typed-properties document: only primitive entries render; nested structure is not
  // meaningful without the type's schema and stays hidden rather than dumped as JSON.
  const propertyEntries = Object.entries(
    (feature.properties as Record<string, unknown> | null) ?? {},
  ).filter(([, value]) => ['string', 'number', 'boolean'].includes(typeof value));

  return (
    <Flex vertical style={{ minHeight: '100vh' }}>
      <Flex
        align="center"
        gap={8}
        style={{ padding: '8px 16px', borderBottom: `1px solid ${colors.colorBorderSecondary}` }}
      >
        <Typography.Title level={5} style={{ margin: 0, flex: 1 }}>
          {feature.name ?? t('features.unnamed')}
        </Typography.Title>
        <Tag>{t(`featureKinds.${envelope.kind}`)}</Tag>
        <Typography.Text type="secondary">{t('app.name')}</Typography.Text>
      </Flex>

      {/* Border-box: the padding sits inside the full width, or a phone scrolls sideways by it. */}
      <div style={{ padding: 16, maxWidth: 900, width: '100%', boxSizing: 'border-box', margin: '0 auto' }}>
        <Alert
          type="info"
          showIcon
          title={t('shares.readOnly')}
          style={{ marginBottom: 12 }}
        />

        {feature.approximateLocation && (
          <Alert type="warning" showIcon title={t('map.approximate')} style={{ marginBottom: 12 }} />
        )}

        {feature.geometry && (
          <div ref={mapTarget} data-testid="shared-feature-map" style={{ height: 320, marginBottom: 16, background: colors.colorFillTertiary }} />
        )}

        {feature.description && (
          <Typography.Paragraph>{feature.description}</Typography.Paragraph>
        )}

        {cave && (
          <Card size="small" style={{ marginBottom: 12 }}>
            <Descriptions column={{ xs: 1, sm: 2 }} size="small">
              {detailItem(t('caves.identificationCode'), cave.identificationCode)}
              {detailItem(t('caves.region'), cave.region)}
              {detailItem(t('caves.fields.otherToponyms'), cave.otherToponyms)}
              {detailItem(t('caves.fields.hydrographicBasin'), cave.hydrographicBasin)}
              {detailItem(t('caves.fields.valley'), cave.valley)}
              {detailItem(t('caves.fields.surveyedLength'), cave.surveyedLength)}
              {detailItem(t('caves.depth'), cave.depth)}
              {detailItem(t('caves.fields.altitude'), cave.altitude)}
              {detailItem(t('caves.entrances'), cave.entranceCount)}
              {detailItem(t('caves.fields.discoveryDate'), cave.discoveryDate)}
              {detailItem(t('caves.fields.discoverer'), cave.discoverer)}
              {detailItem(
                t('caves.fields.explorationStatus'),
                named(`caves.explorationValues.${cave.explorationStatus}`),
              )}
            </Descriptions>
          </Card>
        )}

        {entrance && (
          <Card size="small" style={{ marginBottom: 12 }}>
            <Descriptions column={{ xs: 1, sm: 2 }} size="small">
              {detailItem(t('caves.fields.altitude'), entrance.altitude)}
              {entrance.isMain && (
                <Descriptions.Item label={t('entrances.main')}>✓</Descriptions.Item>
              )}
              {detailItem(
                t('entrances.positionQuality'),
                named(`entrances.qualityValues.${entrance.positionQuality}`),
              )}
              {detailItem(t('entrances.surveyedAt'), entrance.surveyedAt)}
            </Descriptions>
          </Card>
        )}

        {centerline && (
          <Card size="small" style={{ marginBottom: 12 }}>
            <Descriptions column={{ xs: 1, sm: 2 }} size="small">
              {detailItem(
                t('centerlines.length'),
                centerline.lengthM == null ? null : `${Number(centerline.lengthM).toLocaleString()} m`,
              )}
              {detailItem(t('centerlines.paths'), centerline.pathCount)}
              {detailItem(t('centerlines.source'), named(`centerlines.sourceValues.${centerline.source}`))}
              {centerline.isDefault && (
                <Descriptions.Item label={t('centerlines.default')}>✓</Descriptions.Item>
              )}
            </Descriptions>
          </Card>
        )}

        {propertyEntries.length > 0 && (
          <Card size="small" title={t('features.typedProperties')} style={{ marginBottom: 12 }}>
            <Descriptions column={{ xs: 1, sm: 2 }} size="small">
              {propertyEntries.map(([key, value]) => (
                <Descriptions.Item key={key} label={key}>
                  {String(value)}
                </Descriptions.Item>
              ))}
            </Descriptions>
          </Card>
        )}

        {children && children.length > 0 && (
          <Card size="small" title={t('shares.childrenTitle')}>
            <List
              size="small"
              dataSource={children}
              renderItem={(child) => (
                <List.Item>
                  <Typography.Text>{child.name ?? t('features.unnamed')}</Typography.Text>
                  <Tag style={{ marginLeft: 8 }}>{t(`featureKinds.${child.kind}`)}</Tag>
                </List.Item>
              )}
            />
          </Card>
        )}
      </div>
    </Flex>
  );
}
