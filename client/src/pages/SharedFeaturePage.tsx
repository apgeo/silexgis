// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useRef, useState } from 'react';
import { Alert, Button, Card, Descriptions, Flex, List, Spin, Tag, Typography } from 'antd';
import Map from 'ol/Map';
import View from 'ol/View';
import Feature from 'ol/Feature';
import GeoJSONFormat from 'ol/format/GeoJSON';
import TileLayer from 'ol/layer/Tile';
import VectorLayer from 'ol/layer/Vector';
import VectorSource from 'ol/source/Vector';
import XYZ from 'ol/source/XYZ';
import { Circle as CircleStyle, Fill, Stroke, Style } from 'ol/style';
import { useTranslation } from 'react-i18next';
import { Link, useParams } from 'react-router-dom';
import { ApiError } from '../api/client.ts';
import { fetchSharedFeature, type SharedFeatureEnvelope } from '../api/hooks.ts';

/**
 * Anonymous read-only page for a shared feature. The share token resolves server-side to a
 * redacted envelope — protected coordinates and internal ownership fields never reach this
 * page, and login-required shares answer 401 until the viewer signs in. Follows the shared
 * map-view page pattern: chrome-less, public base map, no workspace access.
 */
export default function SharedFeaturePage() {
  const { t } = useTranslation();
  const { token } = useParams<{ token: string }>();
  const mapTarget = useRef<HTMLDivElement>(null);
  const [envelope, setEnvelope] = useState<SharedFeatureEnvelope | null>(null);
  const [failure, setFailure] = useState<'notFound' | 'loginRequired' | null>(null);

  useEffect(() => {
    let disposed = false;
    fetchSharedFeature(token!)
      .then((data) => {
        if (!disposed) {
          setEnvelope(data);
        }
      })
      .catch((error: unknown) => {
        if (!disposed) {
          setFailure(error instanceof ApiError && error.status === 401 ? 'loginRequired' : 'notFound');
        }
      });
    return () => {
      disposed = true;
    };
  }, [token]);

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
        new TileLayer({
          source: new XYZ({
            url: 'https://tile.openstreetmap.org/{z}/{x}/{y}.png',
            attributions: '© OpenStreetMap contributors',
          }),
        }),
        new VectorLayer({
          source: new VectorSource({ features: [new Feature(geom)] }),
          style: new Style({
            stroke: new Stroke({ color: '#146262', width: 3 }),
            fill: new Fill({ color: 'rgba(20, 98, 98, 0.15)' }),
            image: new CircleStyle({
              radius: 8,
              fill: new Fill({ color: '#146262' }),
              stroke: new Stroke({ color: '#fff', width: 2 }),
            }),
          }),
        }),
      ],
      view: new View({ center: [0, 0], zoom: 2 }),
    });
    map.getView().fit(geom.getExtent(), { padding: [40, 40, 40, 40], maxZoom: 15 });
    return () => map.setTarget(undefined);
  }, [envelope]);

  if (failure) {
    return (
      <Flex align="center" justify="center" style={{ height: '100vh' }}>
        {failure === 'loginRequired' ? (
          <Alert
            type="info"
            showIcon
            message={t('shares.loginRequired')}
            action={
              <Link to="/login">
                <Button size="small" type="primary">
                  {t('shares.signIn')}
                </Button>
              </Link>
            }
          />
        ) : (
          <Alert type="warning" showIcon message={t('shares.sharedNotFound')} />
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
        style={{ padding: '8px 16px', borderBottom: '1px solid rgba(128,128,128,0.25)' }}
      >
        <Typography.Title level={5} style={{ margin: 0, flex: 1 }}>
          {feature.name ?? t('features.unnamed')}
        </Typography.Title>
        <Tag>{t(`featureKinds.${envelope.kind}`)}</Tag>
        <Typography.Text type="secondary">{t('app.name')}</Typography.Text>
      </Flex>

      <div style={{ padding: 16, maxWidth: 900, width: '100%', margin: '0 auto' }}>
        <Alert
          type="info"
          showIcon
          message={t('shares.readOnly')}
          style={{ marginBottom: 12 }}
        />

        {feature.approximateLocation && (
          <Alert type="warning" showIcon message={t('map.approximate')} style={{ marginBottom: 12 }} />
        )}

        {feature.geometry && (
          <div ref={mapTarget} style={{ height: 320, marginBottom: 16, background: '#e8ecef' }} />
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
                t(`caves.explorationValues.${cave.explorationStatus}`),
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
              {entrance.positionQuality &&
                detailItem(
                  t('entrances.positionQuality'),
                  t(`entrances.qualityValues.${entrance.positionQuality}`),
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
              {detailItem(t('centerlines.source'), t(`centerlines.sourceValues.${centerline.source}`))}
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
