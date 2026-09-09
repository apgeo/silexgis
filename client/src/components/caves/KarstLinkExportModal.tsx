// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Checkbox, Modal, Radio, Skeleton, Space, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { DownloadError } from '../../api/download.ts';
import { useKarstLinkExportPreview, type KarstLinkExportRequest } from '../../api/hooks.ts';
import { useUiPrefsStore, type ProtectedPositionTreatment } from '../../stores/uiPrefsStore.ts';
import { exportKarstLink } from './karstLinkExport.ts';

/**
 * The three answers, in the order they are offered. Least withheld first, so the list reads
 * from "the recipient gets the most this installation will give" downwards, and the one that
 * makes the file disagree with the registry is last rather than first.
 */
const treatments: ProtectedPositionTreatment[] = ['grid_position', 'no_position', 'omit'];

interface Props {
  open: boolean;
  onClose: () => void;
  /** What would be exported: the same filters the list is showing. */
  request: KarstLinkExportRequest;
}

/**
 * Asks what an interchange export should do about the caves whose position this installation
 * protects, and then takes the file.
 *
 * The question is asked once for the whole export rather than once per cave. That is not a
 * simplification of the server's contract — the server still takes a decision per cave and
 * refuses a request that leaves one uncovered — it is what "apply to all" means, and it is the
 * only shape in which a decision about two thousand nine hundred caves is a decision somebody
 * can actually make. Which is why the count is shown before the choice and not after it:
 * "3 caves" and "2,900 caves" are different decisions, and a chooser that hid the difference
 * would be collecting a click rather than an answer.
 *
 * The surveyed position is deliberately not offered, and its absence here is a convenience
 * rather than the protection: the server's vocabulary has no member naming it, so a client that
 * offered a fourth button would be refused. Nothing on this screen is what keeps a protected
 * coordinate out of the file.
 *
 * Somebody who has settled on an answer never reaches this dialog at all — the export starts
 * from the menu and the stored answer goes with it — so this is the screen for the first time
 * and for whenever the answer is being changed. That is why the remembered value is re-read on
 * every open rather than once per mount: it is the answer being revisited.
 */
export default function KarstLinkExportModal({ open, onClose, request }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const remembered = useUiPrefsStore((s) => s.karstLinkTreatment);
  const setRemembered = useUiPrefsStore((s) => s.setKarstLinkTreatment);

  const [treatment, setTreatment] = useState<ProtectedPositionTreatment>(
    remembered ?? 'grid_position',
  );
  const [remember, setRemember] = useState(remembered !== undefined);
  const [busy, setBusy] = useState(false);

  // The remembered answer is adopted each time the dialog opens, not once per mount: somebody
  // who settled on one answer and exported twice should see their own answer the second time.
  useEffect(() => {
    if (open) {
      setTreatment(remembered ?? 'grid_position');
      setRemember(remembered !== undefined);
    }
  }, [open, remembered]);

  const { data: preview, isFetching, isError } = useKarstLinkExportPreview(request, open);
  const protectedCount = preview?.protectedCaveCount ?? undefined;

  const onOk = async () => {
    setBusy(true);
    try {
      // One answer for every cave that needs one.
      await exportKarstLink(request, treatment);
      setRemembered(remember ? treatment : undefined);
      onClose();
    } catch (error) {
      const code = error instanceof DownloadError ? error.code : undefined;
      message.error(
        code === 'export.too_many_caves'
          ? t('karstlinkExport.errors.tooManyCaves', { count: preview?.maxCaveCount ?? 0 })
          : t('common.saveFailed'),
      );
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open={open}
      title={t('karstlinkExport.title')}
      okText={t('karstlinkExport.download')}
      okButtonProps={{
        loading: busy,
        disabled: preview?.exceedsLimit === true,
        'data-testid': 'karstlink-export-download',
      }}
      onOk={() => void onOk()}
      onCancel={onClose}
      destroyOnHidden
    >
      <Space orientation="vertical" size="middle" style={{ width: '100%' }}>
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t('karstlinkExport.intro')}
        </Typography.Paragraph>

        {isFetching && !preview && <Skeleton active paragraph={{ rows: 1 }} />}
        {isError && <Alert type="error" showIcon title={t('common.loadFailed')} />}

        {preview?.exceedsLimit && (
          <Alert
            type="warning"
            showIcon
            data-testid="karstlink-export-too-many"
            title={t('karstlinkExport.tooMany', {
              count: preview.caveCount,
              limit: preview.maxCaveCount,
            })}
          />
        )}

        {preview && !preview.exceedsLimit && (
          <Alert
            type="info"
            showIcon
            data-testid="karstlink-export-count"
            title={t('karstlinkExport.caveCount', { count: preview.caveCount })}
            description={
              protectedCount === 0
                ? t('karstlinkExport.noneProtected')
                : t('karstlinkExport.protectedCount', { count: protectedCount ?? 0 })
            }
          />
        )}

        {/* The choice is offered even when nothing is protected, because it costs nothing and
            the alternative is a dialog whose contents change shape between two exports of
            almost the same set. The server ignores an answer for a cave that needs none. */}
        <Radio.Group
          data-testid="karstlink-export-treatment"
          value={treatment}
          onChange={(e) => setTreatment(e.target.value as ProtectedPositionTreatment)}
        >
          <Space orientation="vertical">
            {treatments.map((code) => (
              <Radio key={code} value={code} data-testid={`karstlink-export-treatment-${code}`}>
                <Typography.Text>{t(`karstlinkExport.treatments.${code}.label`)}</Typography.Text>
                <br />
                <Typography.Text type="secondary">
                  {t(`karstlinkExport.treatments.${code}.help`)}
                </Typography.Text>
              </Radio>
            ))}
          </Space>
        </Radio.Group>

        <Checkbox
          data-testid="karstlink-export-remember"
          checked={remember}
          onChange={(e) => setRemember(e.target.checked)}
        >
          {t('karstlinkExport.remember')}
        </Checkbox>
      </Space>
    </Modal>
  );
}
