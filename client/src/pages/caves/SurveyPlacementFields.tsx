// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Form, InputNumber, Radio, Typography, type FormInstance } from 'antd';
import { useTranslation } from 'react-i18next';
import {
  HIGHEST_HEIGHT_M,
  LOWEST_HEIGHT_M,
  type OriginProblem,
  type SurveyPlacementForm,
} from './surveyPlacement.ts';

/**
 * The questions a survey file's placement is answered by: local metres about a point, or a
 * projected grid, and the altitude of the file's zero level. Rendered inside the caller's form.
 */
export function SurveyPlacementFields({
  form,
  demanded,
  exactOrigin,
  originProblem,
}: {
  form: FormInstance<SurveyPlacementForm>;
  /** Whether the answers are required, rather than offered for a file that may answer itself. */
  demanded: boolean;
  exactOrigin: { longitude: number; latitude: number } | undefined;
  originProblem: OriginProblem | undefined;
}) {
  const { t } = useTranslation();
  const coordinates = Form.useWatch('coordinates', form) ?? 'local';

  return (
    <>
      <Form.Item name="coordinates">
        <Radio.Group>
          <Radio value="local">{t('surveyModels.coordinatesLocal')}</Radio>
          <Radio value="projected">{t('surveyModels.coordinatesProjected')}</Radio>
        </Radio.Group>
      </Form.Item>

      {coordinates === 'projected' ? (
        <>
          <Typography.Paragraph type="secondary">
            {t('surveyModels.coordinatesProjectedHint')}
          </Typography.Paragraph>
          <Form.Item
            name="sourceEpsg"
            label={t('surveyModels.sourceEpsg')}
            rules={[{ required: demanded, message: t('surveyModels.problems.crsInvalid') }]}
          >
            <InputNumber min={1} step={1} style={{ width: 200 }} />
          </Form.Item>
        </>
      ) : (
        <>
          <Typography.Paragraph type="secondary">
            {t('surveyModels.coordinatesLocalHint')}
          </Typography.Paragraph>
          {exactOrigin && (
            <Alert
              type="info"
              showIcon
              style={{ marginBottom: 12 }}
              title={t('surveyModels.originFromEntrance')}
            />
          )}
          {originProblem && (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              title={
                originProblem === 'obfuscated'
                  ? t('surveyModels.originObfuscated')
                  : t('surveyModels.originNoEntrance')
              }
            />
          )}
          <Form.Item
            name="originLongitude"
            label={t('surveyModels.originLongitude')}
            rules={[{ required: demanded, message: t('surveyModels.problems.originInvalid') }]}
          >
            <InputNumber min={-180} max={180} step={0.00001} style={{ width: 200 }} />
          </Form.Item>
          <Form.Item
            name="originLatitude"
            label={t('surveyModels.originLatitude')}
            rules={[{ required: demanded, message: t('surveyModels.problems.originInvalid') }]}
          >
            <InputNumber min={-90} max={90} step={0.00001} style={{ width: 200 }} />
          </Form.Item>
        </>
      )}

      <Form.Item
        name="originHeightM"
        label={t('surveyModels.originHeightM')}
        extra={t('surveyModels.originHeightHint')}
        rules={[{ required: demanded, message: t('surveyModels.problems.heightInvalid') }]}
      >
        <InputNumber min={LOWEST_HEIGHT_M} max={HIGHEST_HEIGHT_M} step={1} style={{ width: 200 }} />
      </Form.Item>
    </>
  );
}
