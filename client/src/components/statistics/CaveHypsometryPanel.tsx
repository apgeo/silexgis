// SPDX-License-Identifier: AGPL-3.0-or-later
import { App, Button, Card, Empty, Popconfirm, Space, Table, Typography } from 'antd';
import { useTranslation } from 'react-i18next';

import {
  useCaveHypsometry,
  useCaveLevelBands,
  useClearCaveLevelBands,
  useSaveCaveLevelBands,
  type ElevationBand,
} from '../../api/hooks.ts';
import ElevationHistogram from './ElevationHistogram.tsx';
import SurveyBasisNote from './SurveyBasisNote.tsx';

/**
 * How much of a cave's passage sits at each height, the storeys it appears to have been cut at, and
 * what somebody decided those storeys are.
 *
 * <p>
 * <b>The machine proposes; a person decides.</b> The bands drawn behind the histogram are recomputed
 * from the measurements on every read and are a reading of a shape, never a claim about the cave.
 * They are labelled as proposed and they are separated, in words and in the layout, from the
 * reading a researcher confirmed — which is stored, says who confirmed it and when, and survives a
 * reload. Nothing here saves a reading by itself.
 * </p>
 * <p>
 * <b>The spring lines are the other half of the judgment.</b> A level cut at the height water leaves
 * the massif at means something a level anywhere else does not, so the altitudes of the springs in
 * the cave's area are drawn across the histogram. They arrive already filtered to the ones this
 * reader may place: an altitude is a coordinate, and a line that appeared when a guarded spring was
 * added would be that spring's height read off the chart.
 * </p>
 * <p>
 * <b>Absent rather than empty when there is no answer.</b> A reader who may see the cave but may
 * not be told where it is gets nothing from the server for this question, and a card of blanks
 * would both announce that a guarded cave is here and read as "this cave has no heights".
 * </p>
 */
export default function CaveHypsometryPanel({
  caveId,
  canEdit,
}: {
  caveId: string;
  canEdit: boolean;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const { data, isLoading, isError } = useCaveHypsometry(caveId);
  const { data: saved } = useCaveLevelBands(caveId);
  const save = useSaveCaveLevelBands(caveId);
  const clear = useClearCaveLevelBands(caveId);

  if (isError) return null;

  const proposal = data?.proposal ?? null;
  const proposed: ElevationBand[] = proposal?.bands ?? [];

  const confirmProposal = async () => {
    try {
      await save.mutateAsync({
        bands: proposed.map((band) => ({ fromM: band.fromM, toM: band.toM, label: null })),
        note: null,
      });
      message.success(t('hypsometry.saved'));
    } catch {
      // The one case worth naming: two people recording a reading of the same cave at the same
      // moment. The server refuses the second and the reader has to re-read what stands.
      message.error(t('hypsometry.saveFailed'));
    }
  };

  const body = () => {
    if (isLoading || !data) return <Empty description={t('hypsometry.loading')} />;

    // No line work at all is a different statement from line work carrying no heights, and only
    // this branch may say the cave has nothing to measure.
    if (data.basis === 'unavailable') {
      return <Empty description={t('hypsometry.nothingMeasured')} />;
    }

    // Refused rather than drawn flat. A survey recorded as a plan has no third coordinate, and a
    // histogram piled at zero would say the cave lies on one level at sea level.
    if (!data.hasAltitudes || !proposal) {
      return (
        <div data-testid="hypsometry-refused">
          <Empty description={t('hypsometry.noAltitudesTitle')} />
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('hypsometry.noAltitudesReason')}
          </Typography.Paragraph>
        </div>
      );
    }

    return (
      <Space orientation="vertical" size={12} style={{ width: '100%' }}>
        <ElevationHistogram
          bins={proposal.bins}
          bands={proposal.bands}
          referenceHeightsM={data.springAltitudesM}
          measure="weight"
          testId="chart-cave-hypsometry"
        />

        <Typography.Text type="secondary">
          {t('hypsometry.proposedCount', { count: proposed.length })}
        </Typography.Text>

        <Table<ElevationBand>
          data-testid="hypsometry-proposed"
          size="small"
          pagination={false}
          rowKey={(band) => `${band.fromM}-${band.toM}`}
          dataSource={proposed}
          locale={{ emptyText: t('hypsometry.noProposal') }}
          columns={[
            {
              title: t('hypsometry.from'),
              dataIndex: 'fromM',
              render: (value: number) => value.toFixed(1),
            },
            {
              title: t('hypsometry.to'),
              dataIndex: 'toM',
              render: (value: number) => value.toFixed(1),
            },
            {
              title: t('hypsometry.share'),
              dataIndex: 'weightFraction',
              render: (value: number) => `${(value * 100).toFixed(0)}%`,
            },
          ]}
        />

        <div data-testid="hypsometry-saved">
          <Typography.Text strong>
            {saved?.confirmed
              ? t('hypsometry.confirmedCount', { count: saved.bands.length })
              : t('hypsometry.notConfirmed')}
          </Typography.Text>
        </div>

        {canEdit && (
          <Space wrap>
            <Button
              type="primary"
              data-testid="hypsometry-confirm"
              disabled={proposed.length === 0}
              loading={save.isPending}
              onClick={confirmProposal}
            >
              {t('hypsometry.confirm')}
            </Button>
            {saved?.confirmed && (
              <Popconfirm
                title={t('hypsometry.clearConfirm')}
                onConfirm={() => clear.mutate()}
                okText={t('common.ok')}
                cancelText={t('common.cancel')}
              >
                <Button danger data-testid="hypsometry-clear" loading={clear.isPending}>
                  {t('hypsometry.clear')}
                </Button>
              </Popconfirm>
            )}
          </Space>
        )}

        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t('hypsometry.proposalIsNotAnAssertion')}
        </Typography.Paragraph>
        {data.springAltitudesM.length > 0 && (
          <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
            {t('hypsometry.springLinesExplained')}
          </Typography.Paragraph>
        )}
      </Space>
    );
  };

  return (
    <Card title={t('hypsometry.caveTitle')} style={{ marginBottom: 16 }}>
      {body()}
      {data && <SurveyBasisNote basis={data.basis} />}
    </Card>
  );
}
