import { defineConfig } from '@playwright/test';
export default defineConfig({
 testDir: './e2e', workers: 1, retries: 0,
 use: { baseURL: 'http://127.0.0.1:5099', browserName: 'chromium', trace: 'retain-on-failure' },
 webServer: { command: 'python3 ../../scripts/run-e2e.py', url: 'http://127.0.0.1:5099/health/ready', reuseExistingServer: false, timeout: 60000 },
});
