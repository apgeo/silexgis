// SPDX-License-Identifier: AGPL-3.0-or-later
import type { ThemeConfig } from 'antd';

// All theming through antd tokens — no scattered inline styles.
// Dark mode lands with the workspace polish phase.
export const themeConfig: ThemeConfig = {
  token: {
    colorPrimary: '#146262',
    borderRadius: 4,
  },
};
