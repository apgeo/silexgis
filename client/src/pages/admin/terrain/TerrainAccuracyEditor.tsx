// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { App, Form, Modal, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { useSetTerrainBuildAccuracy } from '../../../api/hooks.ts';
import type { TerrainBuild } from '../../../api/hooks.ts';
import TerrainAccuracyFields from './TerrainAccuracyFields.tsx';
import {
  accuracyFormValues,
  accuracyRequest,
  type AccuracyFormValues,
} from './terrainAccuracy.ts';
import { terrainProblemMessage } from './terrainProblems.ts';

interface Props {
  build: TerrainBuild | null;
  onClose: () => void;
}

/**
 * Corrects what is recorded about the accuracy of one build's elevation.
 *
 * Saving with every field empty clears the record, which the list then shows as "not recorded":
 * that is the honest state for data nobody has described, and it is reachable here on purpose.
 */
export default function TerrainAccuracyEditor({ build, onClose }: Props) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<AccuracyFormValues>();
  const save = useSetTerrainBuildAccuracy();

  useEffect(() => {
    if (build) {
      form.setFieldsValue(accuracyFormValues(build.accuracy));
    }
  }, [build, form]);

  const submit = async (values: AccuracyFormValues) => {
    if (!build) {
      return;
    }
    try {
      await save.mutateAsync({
        id: build.id,
        // An empty form is a request to forget, so it is sent as all nulls rather than left out.
        request: accuracyRequest(values) ?? {
          sourceResolutionM: null,
          verticalAccuracyM: null,
          verticalAccuracyStatistic: null,
          accuracySource: null,
        },
      });
      message.success(t('terrain.accuracy.saved'));
      onClose();
    } catch (error) {
      message.error(terrainProblemMessage(error, t));
    }
  };

  return (
    <Modal
      open={build !== null}
      title={t('terrain.accuracy.editTitle')}
      okText={t('common.save')}
      confirmLoading={save.isPending}
      onOk={() => form.submit()}
      onCancel={onClose}
      destroyOnHidden
      width={640}
    >
      <Typography.Paragraph type="secondary">{t('terrain.accuracy.editIntro')}</Typography.Paragraph>
      <Form form={form} layout="vertical" onFinish={(values) => void submit(values)}>
        <TerrainAccuracyFields />
      </Form>
    </Modal>
  );
}
