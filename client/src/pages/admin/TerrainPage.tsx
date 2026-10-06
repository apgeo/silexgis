// SPDX-License-Identifier: AGPL-3.0-or-later
import { GlobalOutlined } from '@ant-design/icons';
import { Alert, Flex, Typography } from 'antd';
import { useTranslation } from 'react-i18next';
import { hasAccessAction, useCapabilities } from '../../api/hooks.ts';
import TerrainBuildForm from './terrain/TerrainBuildForm.tsx';
import TerrainBuildList from './terrain/TerrainBuildList.tsx';
import TerrainConfiguredNotice from './terrain/TerrainConfiguredNotice.tsx';
import TerrainCreditNotice from './terrain/TerrainCreditNotice.tsx';
import TerrainDerivativeForm from './terrain/TerrainDerivativeForm.tsx';
import TerrainDerivativeList from './terrain/TerrainDerivativeList.tsx';
import { TerrainWorkerNotice } from './terrain/TerrainWorkerMissing.tsx';

/**
 * The elevation surface an installation has built for itself: where a rectangle becomes terrain.
 *
 * Two bars meet on this page and they are not the same. Seeing the builds needs the terrain right
 * held over the installation; starting one needs the right to execute; and naming a directory on
 * the server for the application to read needs full administration, because that question is about
 * the machine rather than about anything in it. Each control asks the bar it actually needs, so
 * nothing is offered that the server will refuse.
 */
export default function TerrainPage() {
  const { t } = useTranslation();
  const { data: capabilities } = useCapabilities();
  const canRead = hasAccessAction(capabilities?.domains.terrain, 'read');
  const canExecute = hasAccessAction(capabilities?.domains.terrain, 'execute');
  // Which build the scene draws is decided under the execute right — choosing one and stopping it
  // are the same decision made in two directions, and the server asks for execute on both. Only
  // removing a build and what it left on disk is held under delete.
  const canDelete = hasAccessAction(capabilities?.domains.terrain, 'delete');

  // Nothing is rendered until the answer is in: guessing either way makes a control appear and
  // then vanish, or refuse.
  if (!capabilities) {
    return null;
  }

  if (!canRead) {
    return <Alert type="error" showIcon title={t('admin.forbidden')} style={{ margin: 16 }} />;
  }

  return (
    <Flex vertical gap={16} style={{ padding: 16 }}>
      <Typography.Title level={4} style={{ margin: 0 }}>
        <GlobalOutlined /> {t('terrain.title')}
      </Typography.Title>
      <Typography.Paragraph type="secondary" style={{ margin: 0 }}>
        {t('terrain.intro')}
      </Typography.Paragraph>
      {/*
        Above the form rather than beside the build that met it: what is missing is a property of
        the installation, not of one build, and somebody about to draw another rectangle should
        read it before drawing rather than after that build stops in the same place.
      */}
      <TerrainWorkerNotice />
      <TerrainBuildForm canExecute={canExecute} isFullAdmin={capabilities.isFullAdmin} />
      {/*
        Directly above the list whose "being drawn" badge it contradicts: a configured terrain
        address wins over the chosen build without the server refusing anything, so the row would
        otherwise say one thing and the scene do another.
      */}
      <TerrainConfiguredNotice />
      {/*
        Beside it, because both are about the ground the scene is drawing right now rather than
        about any row below: this one says that ground is being shown with nobody credited for it.
      */}
      <TerrainCreditNotice />
      <TerrainBuildList canExecute={canExecute} canDelete={canDelete} />
      {/*
        Below the builds rather than among them: a picture is made from a build that has finished,
        so somebody arrives here having first seen which builds there are. Asking for one is held
        under execute, as starting a build is; removing one under delete, as removing a build is.
      */}
      <TerrainDerivativeForm canExecute={canExecute} />
      <TerrainDerivativeList canDelete={canDelete} />
    </Flex>
  );
}
