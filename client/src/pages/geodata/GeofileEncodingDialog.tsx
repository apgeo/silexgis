// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect, useState } from 'react';
import { Alert, App, Flex, Modal, Radio, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { ApiError } from '../../api/client.ts';
import { useReimportGeofile, type GeofileInfo, type TripCsvEncoding } from '../../api/hooks.ts';

/** "Detect" is the absence of a stated encoding: the server reads the bytes and decides. */
const DETECT = 'detect';

/**
 * The encodings a person is offered. Detection already tells UTF-8 apart from the two Central
 * European code pages, but it never picks Windows-1252, so a Western-European file's à, è, ñ come
 * back as other letters until somebody says what the file is. The server accepts a few more; one
 * of those, if already stored on the file, is still shown so the dialog never hides what is set.
 */
const OFFERED: readonly TripCsvEncoding[] = ['utf8', 'windows1250', 'windows1252'];

type Choice = TripCsvEncoding | typeof DETECT;

interface Props {
  /** The delimited file whose reading is being corrected; null while the dialog is closed. */
  geofile: GeofileInfo | null;
  onClose: () => void;
}

/** The wording of a refused re-read: the codes this route answers, then the status it came with. */
function failureKey(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.code === 'geofile.import_in_progress') {
      return 'geodata.encoding.inProgress';
    }
    if (error.status === 403) {
      return 'geodata.encoding.forbidden';
    }
    if (error.status === 404) {
      return 'geodata.encoding.gone';
    }
  }
  return 'geodata.encoding.failed';
}

/**
 * Re-reads a delimited upload under a stated character encoding. The re-import replaces the file's
 * stored reading options wholesale, so the separator and coordinate columns it already has are sent
 * back unchanged beside the new encoding — otherwise correcting the letters would silently undo a
 * corrected column.
 */
export default function GeofileEncodingDialog({ geofile, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const reimport = useReimportGeofile();
  const stored = geofile?.sourceOptions?.delimited?.encoding ?? null;
  const [choice, setChoice] = useState<Choice>(stored ?? DETECT);
  const [failure, setFailure] = useState<string | null>(null);

  useEffect(() => {
    setChoice(stored ?? DETECT);
    setFailure(null);
  }, [geofile?.id, stored]);

  const choices: readonly Choice[] = [
    DETECT,
    ...OFFERED,
    ...(stored && !OFFERED.includes(stored) ? [stored] : []),
  ];

  const onConfirm = async () => {
    if (!geofile) {
      return;
    }
    setFailure(null);
    try {
      await reimport.mutateAsync({
        id: geofile.id,
        sourceOptions: {
          delimited: {
            ...geofile.sourceOptions?.delimited,
            encoding: choice === DETECT ? null : choice,
          },
        },
      });
      message.success(t('geodata.encoding.queued', { name: geofile.name }));
      onClose();
    } catch (error) {
      setFailure(t(failureKey(error)));
    }
  };

  return (
    <Modal
      title={t('geodata.encoding.title')}
      open={geofile !== null}
      onCancel={onClose}
      onOk={() => void onConfirm()}
      okText={t('geodata.encoding.confirm')}
      okButtonProps={{ disabled: choice === (stored ?? DETECT) }}
      confirmLoading={reimport.isPending}
      destroyOnHidden
    >
      <Flex vertical gap={12} data-testid="geodata-encoding-dialog">
        <Typography.Paragraph type="secondary" style={{ marginBottom: 0 }}>
          {t('geodata.encoding.hint')}
        </Typography.Paragraph>
        <Radio.Group value={choice} onChange={(e) => setChoice(e.target.value as Choice)}>
          <Flex vertical gap={4}>
            {choices.map((value) => (
              <Radio key={value} value={value} data-testid={`geodata-encoding-${value}`}>
                {value === DETECT ? t('geodata.encoding.detect') : t(`tripImport.encodings.${value}`)}
              </Radio>
            ))}
          </Flex>
        </Radio.Group>
        {failure && <Alert type="error" showIcon title={failure} data-testid="geodata-encoding-failure" />}
      </Flex>
    </Modal>
  );
}
