// SPDX-License-Identifier: AGPL-3.0-or-later
import { useState } from 'react';
import { Alert, Card, Col, Empty, Row, Select, Statistic, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import { useCaves, useClosestApproach } from '../../api/hooks.ts';

interface CaveClosestApproachSectionProps {
  caveId: string;
}

/**
 * How close this cave comes to another one: the shortest line between the two caves' line work in
 * three dimensions, with that distance split into how far apart the two ends are on the ground and
 * how far apart they are vertically, and the bearing from one end to the other.
 *
 * <p>
 * The horizontal figure is the plan separation of the two ends of that shortest line — not the
 * shortest plan distance between the caves, which is a different pair of points and a smaller
 * number. Saying which is which matters: a caver reading "40 m apart" wants to know whether that is
 * across or down.
 * </p>
 *
 * <p>
 * <b>Nothing appears at all unless this reader may place both caves exactly.</b> The server gives a
 * distance only then — never a rounded one and never one snapped to a grid, because a snapped
 * answer is still an answer and the same pair asked from two vantage points would triangulate away
 * whatever the snapping hid. A refusal is spelled as "no such cave", so this section shows the same
 * nothing for a guarded cave as for a cave that was never created.
 * </p>
 *
 * <p>
 * The measurement is made in the installation's working coordinate system. Three-dimensional
 * distance adds the ordinates as it finds them, so over stored longitude, latitude and metres of
 * depth it would return a number that is very nearly just the height difference — two caves a
 * kilometre apart on the ground and forty metres apart vertically would be reported as forty
 * metres from each other.
 * </p>
 */
export default function CaveClosestApproachSection({ caveId }: CaveClosestApproachSectionProps) {
  const { t } = useTranslation();
  const [search, setSearch] = useState('');
  const [other, setOther] = useState<string | undefined>();

  const { data: caves, isFetching } = useCaves({ pageSize: 20, search: search || undefined });
  const { data, isLoading, isError } = useClosestApproach(caveId, other);

  const options = (caves?.items ?? [])
    .filter((cave) => cave.id !== caveId)
    .map((cave) => ({ value: cave.id, label: cave.name }));

  return (
    <Card title={t('closestApproach.title')} style={{ marginBottom: 16 }}>
      <Select
        showSearch
        allowClear
        filterOption={false}
        style={{ width: '100%', maxWidth: 420 }}
        placeholder={t('closestApproach.pickOther')}
        value={other}
        options={options}
        loading={isFetching}
        onSearch={setSearch}
        onChange={(value?: string) => setOther(value ?? undefined)}
        data-testid="closest-approach-other"
      />

      {other && <Body caveId={caveId} data={data} isError={isError} isLoading={isLoading} />}
      {!other && (
        <Empty
          image={Empty.PRESENTED_IMAGE_SIMPLE}
          description={t('closestApproach.pickOtherHint')}
          style={{ marginTop: 16 }}
        />
      )}
    </Card>
  );
}

interface BodyProps {
  caveId: string;
  data: ReturnType<typeof useClosestApproach>['data'];
  isError: boolean;
  isLoading: boolean;
}

function Body({ caveId, data, isError, isLoading }: BodyProps) {
  const { t } = useTranslation();

  // A cave this reader may not place, one they may not read, and one that never existed all arrive
  // here identically, and are said in one sentence for the same reason the server answers them in
  // one way: distinguishing them would say which caves are being kept from whom.
  if (isError) {
    return (
      <Alert
        type="info"
        showIcon
        style={{ marginTop: 16 }}
        message={t('closestApproach.unavailable')}
        description={t('closestApproach.unavailableWhy')}
      />
    );
  }

  if (isLoading || !data) {
    return <Statistic title={t('closestApproach.distance')} value="—" loading style={{ marginTop: 16 }} />;
  }

  if (data.absence !== 'none') {
    // An absence with its reason, never a blank where a number belongs: "no depths were recorded"
    // and "there is no line work" are different answers, and both differ from "not yet compared".
    return (
      <Alert
        type="warning"
        showIcon
        style={{ marginTop: 16 }}
        message={t(`closestApproach.absence.${data.absence}`)}
      />
    );
  }

  // The pair is reported lowest id first however it was asked for, so which end of the bearing is
  // "from" is a property of the answer and not of the question — the name is stated beside it.
  const fromName = data.caveAId === caveId ? data.caveAName : data.caveBName;
  const toName = data.caveAId === caveId ? data.caveBName : data.caveAName;

  return (
    <div data-testid="closest-approach" style={{ marginTop: 16 }}>
      <Row gutter={[16, 16]}>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('closestApproach.distance')}
            value={format(data.distanceM, 1)}
            suffix={t('closestApproach.unitMetres')}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('closestApproach.horizontal')}
            value={format(data.horizontalDistanceM, 1)}
            suffix={t('closestApproach.unitMetres')}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('closestApproach.vertical')}
            value={format(data.verticalDistanceM, 1)}
            suffix={t('closestApproach.unitMetres')}
          />
        </Col>
        <Col xs={12} sm={8} md={6}>
          <Statistic
            title={t('closestApproach.bearing')}
            value={format(data.bearingDegrees, 0)}
            suffix={t('closestApproach.unitDegrees')}
          />
        </Col>
      </Row>
      <Typography.Paragraph type="secondary" style={{ marginTop: 16, marginBottom: 0 }}>
        {t('closestApproach.between', { from: fromName ?? '—', to: toName ?? '—' })}
      </Typography.Paragraph>
      <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
        {t('closestApproach.horizontalIsOfTheSameLine')}
      </Typography.Paragraph>
    </div>
  );
}

/** A figure to the stated number of places, or an em dash where there is not one. */
function format(value: number | null | undefined, places: number): string {
  return value == null ? '—' : value.toFixed(places);
}
