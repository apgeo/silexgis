// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Card, Empty, Typography } from 'antd';
import { useState } from 'react';
import { useTranslation } from 'react-i18next';

import {
  useCaveAltitudeDisagreement,
  useFillEntranceAltitude,
} from '../../api/hooks/entranceAltitudes.ts';
import EntranceAltitudeTable from './EntranceAltitudeTable.tsx';

/**
 * One cave's entrance altitudes against the elevation model, with a fill action for an entrance
 * that has no altitude recorded.
 *
 * <p>
 * <b>Absent rather than empty when the server has no answer.</b> The report is withheld whole from a
 * reader who may see the cave but may not place it, and from one without the right to read
 * elevation; a card of blanks would announce that a guarded cave is here, so on a refusal this
 * renders nothing.
 * </p>
 * <p>
 * <b>Filling never replaces.</b> Only an entrance with no altitude offers the action, and the server
 * refuses it for one that has any. A gap between a recorded altitude and the model is a reason to
 * look at the coordinate, not to overwrite what somebody measured.
 * </p>
 */
export default function CaveAltitudePanel({ caveId, canEdit }: { caveId: string; canEdit: boolean }) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading, isError } = useCaveAltitudeDisagreement(caveId);
  const fill = useFillEntranceAltitude();
  const [filling, setFilling] = useState<string | null>(null);

  if (isError) return null;

  const onFill = async (entranceId: string) => {
    setFilling(entranceId);
    try {
      await fill.mutateAsync(entranceId);
      message.success(t('altitudeCheck.filled'));
    } catch {
      message.error(t('altitudeCheck.fillFailed'));
    } finally {
      setFilling(null);
    }
  };

  const body = () => {
    if (isLoading || !data) return <Empty description={t('altitudeCheck.loading')} />;
    if (!data.hasTerrain) return <Empty description={t('altitudeCheck.noTerrainBuilt')} />;
    if (data.entranceCount === 0) return <Empty description={t('altitudeCheck.noEntrances')} />;
    return (
      <>
        {data.disagreeingCount > 0 && (
          <Typography.Paragraph data-testid="altitude-disagreement-note">
            {t('altitudeCheck.disagreeing', {
              count: data.disagreeingCount,
              tolerance: data.toleranceM,
            })}
          </Typography.Paragraph>
        )}
        <EntranceAltitudeTable
          rows={data.entrances}
          canFill={canEdit}
          filling={filling}
          onFill={(id) => void onFill(id)}
          linkCaves={false}
        />
        <Typography.Paragraph type="secondary" style={{ marginTop: 12, marginBottom: 0 }}>
          {t('altitudeCheck.explained', { tolerance: data.toleranceM })}
        </Typography.Paragraph>
      </>
    );
  };

  return (
    <Card title={t('altitudeCheck.title')} style={{ marginBottom: 16 }} data-testid="cave-altitude-panel">
      {body()}
    </Card>
  );
}
