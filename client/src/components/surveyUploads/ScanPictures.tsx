// SPDX-License-Identifier: AGPL-3.0-or-later
import { Flex, Image, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import type { SurveyUploadPointCloudPictures } from '../../api/hooks.ts';

/**
 * The colour ramp the worker paints the pictures with, low to high, so the legend reads the
 * pictures' own colours. It has to match the worker's ramp stop for stop; a legend in other
 * colours would explain a picture nobody drew.
 */
const SCAN_PICTURE_RAMP = ['#440154', '#3b528b', '#21918c', '#5ec962', '#fde725'] as const;

/**
 * A scan's top view and extended profile, as the run drew them, with a legend that says what the
 * colours are and how fine a pixel is. Drawn in the scan's own frame — heights above its lowest
 * point, no coordinate — so they are shown to whoever may read the card, like the tiles.
 */
export default function ScanPictures({ pictures }: { pictures: SurveyUploadPointCloudPictures }) {
  const { t, i18n } = useTranslation();
  const number = (value: number, digits = 1) =>
    new Intl.NumberFormat(i18n.language, { maximumFractionDigits: digits }).format(value);
  const centimetres = (metres: number) => number(metres * 100, 1);
  const byHeight = pictures.colourBy !== 'intensity';
  const [planWidth, planHeight] = pictures.planSize;

  return (
    <Flex vertical gap={8} data-testid="scan-pictures">
      <Typography.Text strong>{t('surveyUploads.pictures.title')}</Typography.Text>
      <Flex gap={12} wrap>
        <figure style={{ margin: 0, flex: '1 1 260px', minWidth: 0 }}>
          <Image
            src={pictures.planUrl}
            alt={t('surveyUploads.pictures.plan')}
            style={{ maxHeight: 320, objectFit: 'contain', background: '#f5f5f5' }}
            data-testid="scan-picture-plan"
          />
          <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="scan-picture-plan-caption">
            {t('surveyUploads.pictures.planCaption', {
              pixelCm: centimetres(pictures.pixelM),
              width: planWidth ?? 0,
              height: planHeight ?? 0,
            })}
          </Typography.Text>
        </figure>
        {pictures.profileUrl !== null && (
          <figure style={{ margin: 0, flex: '1 1 260px', minWidth: 0 }}>
            <Image
              src={pictures.profileUrl}
              alt={t('surveyUploads.pictures.profile')}
              style={{ maxHeight: 320, objectFit: 'contain', background: '#f5f5f5' }}
              data-testid="scan-picture-profile"
            />
            <Typography.Text type="secondary" style={{ fontSize: 12 }} data-testid="scan-picture-profile-caption">
              {t('surveyUploads.pictures.profileCaption', {
                pixelCm: centimetres(pictures.profilePixelM ?? pictures.pixelM),
                lengthM: number(pictures.profileLengthM ?? 0),
              })}
            </Typography.Text>
          </figure>
        )}
      </Flex>
      {pictures.profileUrl === null && (
        <Typography.Text type="secondary" data-testid="scan-picture-no-profile">
          {t('surveyUploads.pictures.noProfile')}
        </Typography.Text>
      )}
      <Flex gap={8} align="center" wrap data-testid="scan-pictures-legend">
        <Typography.Text type="secondary" style={{ fontSize: 12 }}>
          {t(byHeight ? 'surveyUploads.pictures.byHeight' : 'surveyUploads.pictures.byIntensity')}
        </Typography.Text>
        <Typography.Text style={{ fontSize: 12 }}>
          {byHeight ? t('surveyUploads.pictures.metres', { value: number(pictures.valueLow) }) : number(pictures.valueLow, 0)}
        </Typography.Text>
        <span
          aria-hidden
          style={{
            display: 'inline-block',
            width: 120,
            height: 10,
            borderRadius: 2,
            background: `linear-gradient(to right, ${SCAN_PICTURE_RAMP.join(', ')})`,
          }}
        />
        <Typography.Text style={{ fontSize: 12 }}>
          {byHeight ? t('surveyUploads.pictures.metres', { value: number(pictures.valueHigh) }) : number(pictures.valueHigh, 0)}
        </Typography.Text>
      </Flex>
    </Flex>
  );
}
