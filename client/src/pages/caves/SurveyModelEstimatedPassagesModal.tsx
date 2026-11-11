// SPDX-License-Identifier: AGPL-3.0-or-later
import { useEffect } from 'react';
import { Alert, App, ColorPicker, Form, InputNumber, Modal, Switch, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  useChangeSurveyModelEstimatedPassages,
  type SurveyModelInfo,
} from '../../api/hooks.ts';
import {
  DEFAULT_ESTIMATED_COLOR,
  DEFAULT_ESTIMATED_DIAMETER_M,
  ESTIMATED_COLOR_PRESETS,
  LARGEST_ESTIMATED_DIAMETER_M,
  SMALLEST_ESTIMATED_DIAMETER_M,
} from '../../scene3d/estimatedPassages.ts';
import { surveyModelProblemMessage } from './surveyModelProblems.ts';
import { formatPassageLength } from './surveyPlacement.ts';

interface EstimatedPassagesForm {
  enabled: boolean;
  diameterM: number;
  color: string;
}

/**
 * Whether a line plot's passages without a measured size are drawn at an estimated one, and at
 * which size and in which colour.
 *
 * The warning is the first thing in the dialog and stays there whichever way the switch is set:
 * every such passage is drawn at the one size chosen here, a crawl and a chamber alike, and once
 * drawn it looks like a passage somebody measured. Saying so is the price of offering it at all.
 */
export default function SurveyModelEstimatedPassagesModal({
  caveId,
  model,
  onClose,
}: {
  caveId: string;
  model: SurveyModelInfo | null;
  onClose: () => void;
}) {
  const { t } = useTranslation();
  const { message } = App.useApp();
  const [form] = Form.useForm<EstimatedPassagesForm>();
  const change = useChangeSurveyModelEstimatedPassages();

  useEffect(() => {
    if (model === null) {
      return;
    }
    form.setFieldsValue({
      // Opened to turn them on, which is nearly always why somebody opens it while they are off.
      enabled: true,
      diameterM:
        typeof model.estimatedPassageDiameterM === 'number'
          ? model.estimatedPassageDiameterM
          : DEFAULT_ESTIMATED_DIAMETER_M,
      color: model.estimatedPassageColor || DEFAULT_ESTIMATED_COLOR,
    });
  }, [model, form]);

  const close = () => {
    form.resetFields();
    onClose();
  };

  const submit = async () => {
    if (model === null) {
      return;
    }
    let values: EstimatedPassagesForm;
    try {
      values = await form.validateFields();
    } catch {
      return;
    }
    try {
      await change.mutateAsync({
        id: model.id,
        caveId,
        estimated: { enabled: values.enabled, diameterM: values.diameterM, color: values.color },
      });
      message.success(t('surveyModels.estimated.started'));
      close();
    } catch (error) {
      message.error(surveyModelProblemMessage(error, t, 'surveyModels.estimated.failed'));
    }
  };

  const unsized =
    model && typeof model.unsizedPassageM === 'number' && typeof model.unsizedLegCount === 'number'
      ? { length: formatPassageLength(model.unsizedPassageM), count: model.unsizedLegCount }
      : null;

  return (
    <Modal
      title={t('surveyModels.estimated.title')}
      open={model !== null}
      onCancel={close}
      onOk={() => void submit()}
      okText={t('surveyModels.estimated.submit')}
      okButtonProps={{ loading: change.isPending }}
      width="min(560px, 95vw)"
      destroyOnHidden
    >
      {model && (
        <Form form={form} layout="vertical" data-testid="survey-model-estimated-form">
          <Typography.Paragraph strong style={{ marginBottom: 4 }}>
            {model.name}
          </Typography.Paragraph>
          {unsized && (
            <Typography.Paragraph type="secondary">
              {t('surveyModels.estimated.unsized', unsized)}
            </Typography.Paragraph>
          )}
          <Alert
            type="warning"
            showIcon
            style={{ marginBottom: 16 }}
            title={t('surveyModels.estimated.warningTitle')}
            description={t('surveyModels.estimated.warning')}
            data-testid="survey-model-estimated-warning"
          />
          <Form.Item name="enabled" valuePropName="checked" label={t('surveyModels.estimated.enabled')}>
            <Switch data-testid="survey-model-estimated-enabled" />
          </Form.Item>
          <Form.Item
            name="diameterM"
            label={t('surveyModels.estimated.diameter')}
            extra={t('surveyModels.estimated.diameterHint')}
            rules={[
              {
                required: true,
                type: 'number',
                min: SMALLEST_ESTIMATED_DIAMETER_M,
                max: LARGEST_ESTIMATED_DIAMETER_M,
                message: t('surveyModels.estimated.diameterRange', {
                  min: SMALLEST_ESTIMATED_DIAMETER_M,
                  max: LARGEST_ESTIMATED_DIAMETER_M,
                }),
              },
            ]}
          >
            <InputNumber
              min={SMALLEST_ESTIMATED_DIAMETER_M}
              max={LARGEST_ESTIMATED_DIAMETER_M}
              step={0.1}
              suffix="m"
              aria-label={t('surveyModels.estimated.diameter')}
              data-testid="survey-model-estimated-diameter"
            />
          </Form.Item>
          {/* Held as the `#rrggbb` text rather than the picker's own colour object, so the value
              the form carries is the one the request is built from. */}
          <Form.Item
            name="color"
            label={t('surveyModels.estimated.color')}
            extra={t('surveyModels.estimated.colorHint')}
            getValueFromEvent={(_colour: unknown, hex: string) => hex}
          >
            <ColorPicker
              format="hex"
              disabledAlpha
              showText
              presets={[
                { label: t('surveyModels.estimated.presets'), colors: [...ESTIMATED_COLOR_PRESETS] },
              ]}
            />
          </Form.Item>
        </Form>
      )}
    </Modal>
  );
}
