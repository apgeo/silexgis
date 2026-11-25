// SPDX-License-Identifier: AGPL-3.0-or-later
import { Alert, Tag } from 'antd';
import { useTranslation } from 'react-i18next';
import { type AdminSettings } from '../../api/hooks.ts';
import { settingsNotice } from './settingsProblems.ts';

/** Stands in for a secret that is already stored; the real one is never sent to the browser. */
export const SECRET_KEPT = '••••••••';

/** What every section form of the installation settings page is given. */
export interface SectionProps {
  settings: AdminSettings;
  onSaved: (next: AdminSettings) => void;
  /** Called when a save was refused because the section was saved over since it was read. */
  onStale?: () => void;
}

/** The notes a save came back with, each said as a warning the administrator can act on. */
export function SaveNotices({ codes }: { codes: string[] }) {
  const { t } = useTranslation();
  if (codes.length === 0) {
    return null;
  }
  return (
    <>
      {codes.map((code) => (
        <Alert key={code} type="warning" showIcon title={settingsNotice(code, t)} />
      ))}
    </>
  );
}

export function StatusBanner({ configured, okText, offText }: { configured: boolean; okText: string; offText: string }) {
  return configured ? <Tag color="green">{okText}</Tag> : <Alert type="warning" showIcon title={offText} />;
}
