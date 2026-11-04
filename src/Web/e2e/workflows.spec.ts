import { test, expect } from '@playwright/test';
import AxeBuilder from '@axe-core/playwright';
test('owner setup, planning, inventory, recovery and settings are usable', async ({ page }) => {
 test.setTimeout(120000);
 await page.emulateMedia({reducedMotion:"reduce"});
 const audit = async (screen: string) => {
  const originalTheme = await page.locator('html').getAttribute('data-theme');
  for (const theme of ['light', 'dark']) {
   await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
   await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
   const result = await new AxeBuilder({ page }).withTags(['wcag2a', 'wcag2aa', 'wcag21a', 'wcag21aa']).analyze();
   await test.info().attach(`accessibility-${screen}-${theme}`, { body: JSON.stringify(result), contentType: 'application/json' });
   expect.soft(result.violations.map(v => ({ rule: v.id, nodes: v.nodes.map(n => ({ target: n.target, reason: n.failureSummary })) })), `${screen} (${theme})`).toEqual([]);
   if (theme === 'dark' && ['Overview', 'New plan dialog', 'Dataset usage', 'Drive details'].includes(screen))
    await page.screenshot({path:`test-results/dark-${screen.toLowerCase().replaceAll(' ','-')}.png`,animations:'disabled'});
  }
  await page.evaluate(theme => { if (theme) document.documentElement.dataset.theme = theme; else delete document.documentElement.dataset.theme; }, originalTheme);
  await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
  const fieldColors = await page.locator('input[data-slot="input"]:visible:not(:disabled)').evaluateAll(inputs => inputs.map(input => ({id:input.id,actual:getComputedStyle(input).color,expected:getComputedStyle(document.body).color})).filter(row=>row.actual!==row.expected));
  expect(fieldColors, `${screen}: form values use the restored theme foreground`).toEqual([]);
 };
 const errors: string[] = []; page.on('pageerror', error => errors.push(error.message));
 await page.goto('/');
 await expect(page.getByRole('heading', { name: 'Create your owner account' })).toBeVisible();
 await audit('Owner setup');
 await page.getByLabel('Username', { exact: true }).fill('fixture-owner');
 await page.getByLabel('Password', { exact: true }).fill('fixture-password-long');
 await page.getByLabel('Server setup token').fill('browser-test-setup-only');
 await page.getByRole('button', { name: 'Create account' }).click();
 await expect(page.getByRole('heading', { name: 'Your homelab, at a glance.' })).toBeVisible();
 await page.getByRole('link', { name: 'Skip to main content' }).focus();
 await page.keyboard.press('Enter');
 await expect(page.getByRole('main')).toBeFocused();
 await audit('Overview');
 await page.getByRole('link', { name: 'Planning', exact: true }).click();
 await page.getByRole('button', { name: 'Add plan' }).click();
 await audit('New plan dialog');
 await page.getByLabel('What are you planning?').fill('Buy compatible spare');
 await page.getByLabel('Evidence or reason').fill('No spare available; confirm capacity and interface');
 await page.getByLabel('Budget (USD)').fill('250');
 await page.getByRole('button', { name: 'Save', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Buy compatible spare' })).toBeVisible();
 await audit('Planning');
 await page.getByRole('button', { name: 'Edit plan Buy compatible spare', exact: true }).click();
 const planEditor = page.getByRole('dialog', { name: 'Edit plan', exact: true });
 await planEditor.getByLabel('Plan owner', { exact: true }).fill('Home owner');
 await planEditor.getByLabel('Estimated budget', { exact: true }).fill('325');
 await planEditor.getByLabel('Review by (UTC)', { exact: true }).fill('2027-01-15T12:00');
 await planEditor.getByLabel('Evidence & planning notes', { exact: true }).fill('Confirmed interface; awaiting a final quote.');
 await planEditor.getByRole('button', { name: 'Save plan changes' }).click();
 await expect(planEditor).not.toBeVisible();
 await expect(page.getByText('Owner: Home owner', { exact: true })).toBeVisible();
 await page.getByRole('button', { name: 'Edit plan Buy compatible spare', exact: true }).click();
 await planEditor.getByText('Plan change history', { exact: true }).focus();
 await page.keyboard.press('Enter');
 await expect(planEditor.getByText('Budget: 250 USD → 325 USD', { exact: true })).toBeVisible();
 await expect(planEditor.getByText('Previous evidence: No spare available; confirm capacity and interface', { exact: true })).toBeVisible();
 const editorViewport = page.viewportSize()!;
 await planEditor.getByRole('button', { name: 'Cancel', exact: true }).click();
 await page.setViewportSize({ width: 390, height: 844 });
 await page.getByRole('button', { name: 'Edit plan Buy compatible spare', exact: true }).click();
 await expect(planEditor.getByRole('heading', { name: 'Edit plan', exact: true })).toBeVisible();
 await audit('Mobile plan editor');
 await page.screenshot({ path: 'test-results/plan-editor-mobile.png', animations: 'disabled' });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.setViewportSize(editorViewport);
 const latestPlan = (await (await page.request.get('/api/v1/overview')).json()).plans.find((plan: { title: string }) => plan.title === 'Buy compatible spare');
 const concurrentEdit = await page.request.put(`/api/v1/plans/${latestPlan.id}`, { headers: { 'X-Homelab-Request': '1' }, data: { ...latestPlan, budget: 330 } });
 expect(concurrentEdit.status()).toBe(200);
 await planEditor.getByRole('button', { name: 'Save plan changes' }).click();
 await expect(planEditor.getByRole('alert')).toContainText('This plan changed since you opened it');
 await expect(planEditor.getByLabel('Estimated budget', { exact: true })).toHaveValue('325');
 await planEditor.getByRole('button', { name: 'Cancel', exact: true }).click();
 await page.getByRole('button', { name: 'Edit plan Buy compatible spare', exact: true }).click();
 await expect(planEditor.getByLabel('Estimated budget', { exact: true })).toHaveValue('330');
 await planEditor.getByRole('button', { name: 'Cancel', exact: true }).click();


 await page.getByRole('link', { name: 'Inventory', exact: true }).click();
 await page.getByRole('button', { name: 'Add asset' }).click();
 await audit('New asset dialog');
 await page.getByLabel('Name', { exact: true }).fill('Cold spare shelf');
 await page.getByLabel('Type', { exact: true }).click(); await page.locator('[role="option"][data-value="spare"]').click();
 await page.getByLabel('Quantity / stock on hand').fill('2');
 await page.getByLabel('Spare compatibility / assumptions').fill('Verify SATA and sector format');
 await page.getByRole('button', { name: 'Save', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Cold spare shelf' })).toBeVisible();
 await expect(page.getByText('2 on hand')).toBeVisible();
 await audit('Inventory');
 await page.getByRole('link', { name: 'Recovery', exact: true }).click();
 await page.getByRole('button', { name: 'Add protection policy' }).click();
 await audit('Protection policy dialog');
 await page.getByLabel('Dataset or service').fill('Family photos');
 await page.getByLabel('Recovery importance').click(); await page.locator('[role="option"][data-value="critical"]').click();
 await page.getByLabel('Destination', { exact: true }).fill('Offsite backup');
 await page.getByLabel('Protected scope').fill('photos dataset');
 await page.getByLabel('Destination capacity notes (optional)').fill('8 TiB total, 3 TiB free; manual check 2026-01-01');
 await page.getByLabel('Restore prerequisites (optional)').fill('Restore app configuration and file permissions; custody reference in runbook');
 const recoveryPlanningViewport = page.viewportSize()!;
 await page.setViewportSize({ width: 390, height: 844 });
 await page.getByLabel('Restore prerequisites (optional)').scrollIntoViewIfNeeded();
 await audit('Recovery prerequisites mobile');
 await page.screenshot({ path: 'test-results/recovery-prerequisites-mobile.png', animations: 'disabled' });
 await page.setViewportSize(recoveryPlanningViewport);
 await page.getByRole('button', { name: 'Save evidence' }).click();
 await expect(page.getByRole('heading', { name: 'Family photos' })).toBeVisible();
 await expect(page.getByText('Importance: critical', { exact: false })).toBeVisible();
 await page.getByRole('button', { name: 'Edit policy', exact: true }).click();
 await expect(page.getByLabel('Recovery importance')).toContainText('critical');
 await page.getByLabel('Retention', { exact: true }).fill('Keep monthly copies for one year');
 await page.getByRole('button', { name: 'Save evidence' }).click();
 await expect(page.getByRole('dialog')).toHaveCount(0);

 // An open evidence form must never silently adopt a changed protection scope.
 await page.getByRole('button', { name: 'Record backup outcome', exact: true }).click();
 const recoveryDialog = page.getByRole('dialog');
 await recoveryDialog.getByLabel('Attempt finished at (browser timezone)').fill('2026-01-01T12:00');
 await recoveryDialog.getByLabel('Evidence / reference', { exact: true }).fill('Keep my destination inspection notes');
 const futureReview = new Date(Date.now() + 86400000 * 7).toISOString().slice(0,16);
 await recoveryDialog.getByLabel('Review this manual evidence by').fill(futureReview);
 const recoveryPolicy = (await (await page.request.get('/api/v1/recovery')).json())[0].policy;
 expect(recoveryPolicy.importance).toBe('critical');
 expect(recoveryPolicy.retention).toBe('Keep monthly copies for one year');
 expect(recoveryPolicy.destinationCapacity).toBe('8 TiB total, 3 TiB free; manual check 2026-01-01');
 expect(recoveryPolicy.restoreRequirements).toContain('Restore app configuration and file permissions');
 const changedPolicy = await page.request.put(`/api/v1/recovery/${recoveryPolicy.id}`, { headers: { 'X-Homelab-Request': '1' }, data: { ...recoveryPolicy, scope: 'photos and documents' } });
 expect(changedPolicy.status()).toBe(200);
 await recoveryDialog.getByRole('button', { name: 'Save evidence' }).click();
 await expect(recoveryDialog.getByRole('alert')).toContainText('This protection policy changed since you opened it');
 await expect(recoveryDialog.getByLabel('Evidence / reference', { exact: true })).toHaveValue('Keep my destination inspection notes');
 expect((await (await page.request.get('/api/v1/recovery')).json())[0].runs).toHaveLength(0);
 const recoveryViewport = page.viewportSize()!;
 await page.setViewportSize({ width: 390, height: 844 });
 await audit('Recovery revision conflict');
 await page.screenshot({ path: 'test-results/recovery-conflict-mobile.png', animations: 'disabled' });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.setViewportSize(recoveryViewport);
 await page.keyboard.press('Escape');
 await page.getByRole('button', { name: 'Record restore exercise', exact: true }).click();
 await expect(recoveryDialog.getByLabel('Scope actually restored')).toHaveValue('photos and documents');
 await expect(recoveryDialog.getByText('Recording evidence for Family photos: photos and documents → Offsite backup.', { exact: false })).toBeVisible();
 await expect(recoveryDialog.getByRole('alert')).toHaveCount(0);
 await page.keyboard.press('Escape');

 await expect(page.getByText('independence unverified', { exact: true })).toBeVisible();
 await page.getByRole('link',{name:'Activity',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Activity history',exact:true})).toBeVisible();
 let activityFails=true;
 await page.route('**/api/v1/activity/history?*',route=>{
  const query=new URL(route.request().url()).searchParams;
  if(query.has('before')&&activityFails){activityFails=false;return route.fulfill({status:503,json:{error:'fixture failure'}});}
  if(query.has('before')){expect(query.get('snapshotId')).toBe('151');expect(query.get('before')).toBe('102');}
  return route.fulfill({json:{snapshotId:151,items:[{id:query.has('before')?1:151,action:query.has('before')?'Oldest retained action':'Recent retained action',actor:'owner',atUtc:'2026-01-01T12:00:00Z',detail:'Audited local change'}],nextBefore:query.has('before')?null:102}});
 });
 await page.getByRole('button',{name:'Refresh latest activity'}).click();
 await expect(page.getByRole('heading',{name:'Recent retained action',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Older activity'}).click();
 await expect(page.getByRole('alert')).toContainText('Previously loaded events remain visible');
 await expect(page.getByRole('heading',{name:'Recent retained action',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Older activity'}).click();
 await expect(page.getByRole('heading',{name:'Oldest retained action',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Older activity'})).toHaveCount(0);
 await page.setViewportSize({width:390,height:844});
 await audit('Retained activity history');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await page.screenshot({path:'test-results/activity-history-mobile.png',animations:'disabled'});
 await page.setViewportSize(recoveryViewport);
 await page.unroute('**/api/v1/activity/history?*');
 await page.getByRole('link',{name:'Recovery',exact:true}).click();
 await audit('Recovery');
 await page.getByText('Runbook & evidence history', {exact:true}).click();
 let historyFailure=true;
 const historyItem={id:'older-record',policyRevision:recoveryPolicy.revision,occurredUtc:'2026-01-01T12:00:00Z',recordedUtc:'2026-01-02T12:00:00Z',author:'owner',outcome:'success',reviewByUtc:'2026-02-01T12:00:00Z',recoveryPointUtc:'2026-01-01T11:00:00Z',destinationConfirmed:true,scope:null,checks:null,durationMinutes:null,evidence:'Retained destination manifest',failureReason:null};
 await page.route('**/api/v1/recovery/*/history?*', route=>{
  const url=new URL(route.request().url());const older=url.searchParams.has('beforeId');
  if(older&&historyFailure){historyFailure=false;return route.fulfill({status:503,json:{error:'fixture outage'}});}
  return route.fulfill({json:{asOfUtc:'2026-02-01T12:00:00Z',kind:url.searchParams.get('kind'),items:[{...historyItem,evidence:older?'Oldest retained evidence':'Retained destination manifest',...(url.searchParams.get('kind')==='exercises'?{scope:'photos',checks:'opened files',durationMinutes:15,destinationConfirmed:null,outcome:'passed'}:{})}],next:older?null:{atUtc:historyItem.occurredUtc,id:historyItem.id}}});
 });
 await page.getByRole('button',{name:'Browse all recovery evidence'}).click();
 const recoveryHistory=page.getByRole('region',{name:'Recovery history for Family photos'});
 await expect(recoveryHistory.getByText('Retained destination manifest',{exact:true})).toBeVisible();
 await expect(recoveryHistory.getByText('Earlier policy revision — current scope may differ.',{exact:true})).toBeVisible();
 await recoveryHistory.getByRole('button',{name:'Older evidence',exact:true}).click();
 await expect(recoveryHistory.getByRole('alert')).toContainText('Previously loaded records remain visible');
 await expect(recoveryHistory.getByText('Retained destination manifest',{exact:true})).toBeVisible();
 await recoveryHistory.getByRole('button',{name:'Older evidence',exact:true}).click();
 await expect(recoveryHistory.getByText('Oldest retained evidence',{exact:true})).toBeVisible();
 await expect(recoveryHistory.getByRole('button',{name:'Older evidence',exact:true})).toHaveCount(0);
 await recoveryHistory.getByLabel('Evidence type for Family photos').click(); await page.locator('[role="option"][data-value="exercises"]').click();
 await expect(recoveryHistory.getByText('Restored scope: photos',{exact:true})).toBeVisible();
 await page.setViewportSize({width:390,height:844});
 await audit('Recovery retained history');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await recoveryHistory.scrollIntoViewIfNeeded();
 await page.screenshot({path:'test-results/recovery-history-mobile.png',animations:'disabled'});
 await page.setViewportSize(recoveryViewport);
 await page.unroute('**/api/v1/recovery/*/history?*');

 await page.route('**/api/v1/backups', route => route.fulfill({json:[{id:'fixture-backup',createdUtc:'2026-01-01T12:00:00Z',fileName:'fixture-only.zip',status:'completed',sizeBytes:1073741824}]}));
 await page.getByRole('link', { name: 'Settings', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Connections', exact: true })).toBeVisible();
 await expect(page.getByRole('heading', { name: 'No systems connected yet' })).toBeVisible();
 await audit('Settings');
 await page.getByRole('button', { name: 'Connect NAS', exact: true }).click();
 const enrollmentDialog = page.getByRole('dialog');
 await expect(enrollmentDialog.getByLabel('Trusted public CA certificate (optional)')).toBeVisible();
 await enrollmentDialog.getByLabel('Trusted public CA certificate (optional)').fill('fixture public certificate');
 await audit('NAS enrollment certificate');
 await page.keyboard.press('Escape');
 await expect(enrollmentDialog).not.toBeVisible();
 await expect(page.getByRole('button', {name:'Connect NAS',exact:true})).toBeFocused();

 const settingsNav = page.getByRole('navigation', { name: 'Settings sections' });
 const openSettings = (name: string) => settingsNav.getByRole('link', { name, exact: true }).click();
 await openSettings('Notifications');
 await expect(page.getByRole('button', { name: 'Send a test', exact: true })).toBeDisabled();
 await expect(page.getByRole('button', { name: 'Turn on delivery', exact: true })).toBeDisabled();
 await audit('Notifications first run');
 await openSettings('Display');
 const themeSelect = page.getByRole('group',{name:'Color theme',exact:true});
 await expect(themeSelect.getByRole('radio', {name:'System',exact:true})).toBeChecked();
 const settingsBeforeTheme = await (await page.request.get('/api/v1/settings')).json();
 await themeSelect.getByRole('radio', {name:'System',exact:true}).focus();
 await themeSelect.getByRole('radio', {name:'Dark',exact:true}).check();
 await expect(themeSelect.getByRole('radio', {name:'Dark',exact:true})).toBeChecked();
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await expect(page.getByText('Appearance saved for this browser.',{exact:true})).toBeVisible();
 await page.reload();
 await expect(themeSelect.getByRole('radio', {name:'Dark',exact:true})).toBeChecked();
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await page.emulateMedia({colorScheme:'light'});
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 const secondTab = await page.context().newPage(); await secondTab.goto('/settings/display');
 await expect(secondTab.getByRole('radio', {name:'Dark',exact:true})).toBeChecked();
 await themeSelect.getByRole('radio', {name:'Light',exact:true}).check();
 await expect(secondTab.locator('html')).toHaveAttribute('data-theme','light');
 await expect(secondTab.getByRole('radio', {name:'Light',exact:true})).toBeChecked();
 await secondTab.close();
 await themeSelect.getByRole('radio', {name:'System',exact:true}).check();
 await expect(page.locator('html')).toHaveAttribute('data-theme','light');
 await page.emulateMedia({colorScheme:'dark'});
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await page.emulateMedia({colorScheme:'light'});
 await expect(page.locator('html')).toHaveAttribute('data-theme','light');
 await page.evaluate(() => { Object.defineProperty(Storage.prototype,'setItem',{value:() => { throw new DOMException('Storage disabled','SecurityError'); },configurable:true}); });
 await themeSelect.getByRole('radio', {name:'Dark',exact:true}).check();
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await expect(page.getByText('Appearance applied for this session. Browser storage is unavailable.',{exact:true})).toBeVisible();
 await page.reload();
 await expect(themeSelect.getByRole('radio', {name:'System',exact:true})).toBeChecked();
 await expect(page.locator('html')).toHaveAttribute('data-theme','light');
 expect(await (await page.request.get('/api/v1/settings')).json()).toEqual(settingsBeforeTheme);

 const saveSettings = () => page.getByRole('button', { name: 'Save changes', exact: true }).click();
 await openSettings('Data & backups');
 const backupRow = page.locator('.settings-row').filter({hasText:'fixture-only.zip'});
 await expect(backupRow).toContainText('1 GiB');
 await expect(page.getByRole('button', { name: 'Back up now' })).toHaveCount(0);
 await audit('Data & backups');
 await page.goBack();
 await expect(page).toHaveURL(/settings\/display$/);
 await expect(page.getByRole('heading', { name: 'Display', exact: true })).toBeFocused();
 await page.goForward();
 await expect(page).toHaveURL(/settings\/data$/);
 await openSettings('Display');
 await page.getByRole('radio', { name: 'Decimal (TB)', exact: true }).check();
 await page.getByLabel('Display timezone').fill('UTC');
 await saveSettings();
 await expect(page.getByText('Settings saved.',{exact:true})).toBeVisible();
 await audit('Display');
 await openSettings('Data & backups');
 await expect(backupRow).toContainText('1.07 GB');
 const utcTime = await page.evaluate(() => new Date('2026-01-01T12:00:00Z').toLocaleString(undefined,{timeZone:'UTC',timeZoneName:'short'}));
 await expect(backupRow).toContainText(utcTime);

 await openSettings('Review levels');
 const policy = page.getByRole('switch', { name: 'Local temperature policy' });
 const limit = page.getByLabel('Temperature review limit',{exact:true});
 await expect(limit).toHaveCount(0);
 await policy.check();
 await limit.fill('45');
 await saveSettings();
 await expect(page.getByText('Record the basis for your local temperature policy before enabling it.',{exact:true})).toBeVisible();
 await expect(page.getByLabel('Temperature policy basis',{exact:true})).toBeFocused();
 await expect(page.getByLabel('Temperature policy basis',{exact:true})).toHaveAttribute('aria-invalid', 'true');
 await audit('Settings validation failure');
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.temperatureReviewCelsius).toBeNull();
 await page.getByLabel('Temperature policy basis',{exact:true}).fill('Fixture local cooling review policy');
 await saveSettings();
 await expect(page.getByText('Settings saved.',{exact:true})).toBeVisible();
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.temperatureReviewCelsius).toBe(45);
 await audit('Local temperature policy');
 const policyViewport = page.viewportSize()!;
 await page.setViewportSize({width:390,height:844});
 await page.locator('.settings-content').screenshot({path:'test-results/temperature-policy-mobile.png'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.setViewportSize(policyViewport);
 await policy.uncheck();
 await saveSettings();
 await expect(page.getByText('Settings saved.',{exact:true})).toBeVisible();

 await openSettings('Monitoring');
 const inventory = page.getByLabel('Disk inventory interval', { exact: true });
 await inventory.fill('31');
 await expect(page.getByText('Was 15 minutes')).toBeVisible();
 await audit('Unsaved Monitoring');
 const stay = page.getByRole('dialog', { name: 'Discard unsaved changes?' }).getByRole('button', { name: 'Stay', exact: true });
 await openSettings('Display');
 await stay.click();
 await expect(page).toHaveURL(/settings\/monitoring$/);
 await page.goBack();
 await stay.click();
 await expect(page).toHaveURL(/settings\/monitoring$/);
 await expect(inventory).toHaveValue('31');
 await page.getByRole('button', { name: 'Discard', exact: true }).click();
 await expect(inventory).toHaveValue('15');
 await inventory.fill('30');
 const concurrent = await (await page.request.get('/api/v1/settings')).json();
 expect((await page.request.patch('/api/v1/settings/monitoring', {
   headers: { 'X-Homelab-Request': '1' },
   data: { revision: concurrent.revisions.monitoring, changes: { inventoryMinutes: 20 } },
 })).status()).toBe(200);
 await saveSettings();
 await expect(page.getByRole('alert')).toContainText('This section changed');
 await expect(inventory).toHaveValue('30');
 await page.getByRole('button', { name: 'Reload saved settings' }).click();
 await expect(page.getByText('Was 20 minutes')).toBeVisible();
 await page.getByLabel('Read existing NAS temperature cache').check();
 await saveSettings();
 await expect(page.getByText('Settings saved.', { exact: true })).toBeVisible();
 const updated = (await (await page.request.get('/api/v1/settings')).json()).settings;
 expect(updated).toMatchObject({ inventoryMinutes: 30, readCachedTemperatures: true, capacityUnits: 'decimal', timezone: 'UTC' });

 const settingsViewport = page.viewportSize()!;
 for (const width of [1440, 900, 390]) {
   await page.setViewportSize({width, height: 900});
   await openSettings('Connections');
   await openSettings('Display');
   await expect(page.getByRole('heading', {name: 'Display', exact: true})).toBeFocused();
   expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
   if (width === 390) {
     const scrollable = await settingsNav.evaluate(nav => nav.scrollWidth > nav.clientWidth);
     expect(scrollable).toBeTruthy();
   }
   await audit(`Settings at ${width}px`);
   await page.screenshot({path:`test-results/settings-display-${width}.png`, animations:'disabled'});
 }
 await page.setViewportSize(settingsViewport);
 let finishSettingsLoad: (() => void) | undefined;
 let failSettings = true;
 const settingsUnavailable = async (route: import('@playwright/test').Route) => {
   if (!failSettings) return route.fallback();
   await new Promise<void>(resolve => { finishSettingsLoad = resolve; });
   return route.fulfill({ status: 503 });
 };
 await page.route('**/api/v1/settings', settingsUnavailable);
 await page.reload();
 await expect(page.getByRole('status').filter({ hasText: 'Loading settings…' })).toBeVisible();
 await expect(settingsNav.getByRole('link')).toHaveCount(0);
 await audit('Settings loading');
 finishSettingsLoad!();
 await expect(page.getByRole('alert')).toContainText('Settings couldn’t be loaded');
 await expect(page.getByRole('radio', { name: 'Dark' })).toBeVisible();
 await audit('Settings unavailable');
 failSettings = false;
 await page.getByRole('button', { name: 'Try again', exact: true }).click();
 await expect(page.getByLabel('Display timezone')).toHaveValue('UTC');
 await page.unroute('**/api/v1/settings', settingsUnavailable);
 await page.getByRole('link', { name: /^Overview(?: \d+ open findings?)?$/ }).click();
 await expect(page.getByRole('list', { name: 'Condition summary' }).getByText('1 gap', { exact: true })).toBeVisible();
 await page.screenshot({ path: 'test-results/overview-desktop.png', fullPage: true });
 await page.setViewportSize({ width: 390, height: 844 });
 const navigationLinks = page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link');
 await expect(navigationLinks).toHaveCount(8);
 for (const link of await navigationLinks.all()) {
  const bounds = await link.boundingBox();
  expect(bounds).not.toBeNull();
  expect(bounds!.x).toBeGreaterThanOrEqual(0);
  expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(390);
  expect(bounds!.y + bounds!.height).toBeLessThan(844);
 }
 await audit('Mobile overview');
 await page.screenshot({ path: 'test-results/overview-mobile.png', fullPage: true });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 // Storage redesign (001) is covered in storage.spec.ts. Legacy evidence screens are unlinked.
 const connectionId = "11111111-1111-1111-1111-111111111111";
 await page.clock.install();
 let planningMode = 'ready';
 let planningHung = false;
 await page.route('**/api/v1/planning/review', route => {
  if (planningMode === 'hung') { planningHung = true; return; }
  if (planningMode === 'malformed') return route.fulfill({json:{reviews:null}});
  return route.fulfill({ json: {
  generatedUtc: '2026-10-31T12:00:00Z', ruleVersion: 'drive-planning-v1', trackedDriveCount: 1, driveAgeReviewYears: 5, warrantyNoticeDays: 90,
  reviews: [{ diskId: '22222222-2222-2222-2222-222222222222', model: 'Fixture drive', serial: 'FIXTURE-01', priority: 'Review media evidence', nextStep: 'Review current evidence and recovery readiness before replacement.', lastSeenUtc: '2026-10-31T12:00:00Z', present: true, mappingConfidence: 'inferred', reasons: [{ kind: 'finding', detail: 'Pending sectors observed: 2. Source fixture, observed 2026-10-31.', findingId: null }], openPlanIds: [] }],
  spares: [{ id: 'spare', name: 'Shelf spare', quantity: 1, updatedUtc: '2026-10-31T12:00:00Z', compatibilityNotes: 'Capacity and sector format require verification.', verification: 'unverified', estimatedUnitCost: null, currency: 'USD' }],
  budgets: [{ currency: 'USD', total: 250, withoutEstimate: 1, count: 2 }]
 } }); });
 let savedReview: Record<string, unknown> | null = null;
 await page.route('**/api/v1/plans', async route => { savedReview = route.request().postDataJSON(); await route.fulfill({ json: { id: 'fixture-plan' } }); });
 await page.getByRole('link', { name: 'Planning', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Fixture drive', exact: true })).toBeVisible();
 const planningRegion = page.getByRole('region', {name:'Evidence-based planning review',exact:true});
 planningMode = 'hung';
 await page.clock.fastForward(30001);
 await expect.poll(() => planningHung).toBe(true);
 await page.clock.fastForward(15001);
 await expect(planningRegion.getByRole('alert')).toContainText('request timed out');
 await expect(planningRegion.getByRole('heading',{name:'Fixture drive',exact:true})).toBeVisible();
 planningMode = 'malformed';
 await planningRegion.getByRole('button',{name:'Retry planning review',exact:true}).click();
 await expect(planningRegion.getByRole('alert')).toContainText('invalid response');
 await expect(planningRegion.getByText('Showing previously loaded evidence.',{exact:false})).toBeVisible();
 planningMode = 'ready';
 await planningRegion.getByRole('button',{name:'Retry planning review',exact:true}).click();
 await expect(planningRegion.getByRole('alert')).toHaveCount(0);
 await expect(planningRegion.getByText('Review generated',{exact:false})).toBeVisible();

 await page.getByText('Why this drive is listed (1)', { exact: true }).click();
 await expect(page.getByText('Pending sectors observed: 2. Source fixture, observed 2026-10-31.', { exact: true })).toBeVisible();
 await page.screenshot({ path: 'test-results/planning-mobile.png', fullPage: true });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.setViewportSize({ width: 1440, height: 1000 });
 await page.screenshot({ path: 'test-results/planning-desktop.png', fullPage: true });
 let spareReview: Record<string,unknown> | null = null;
 await page.route('**/api/v1/disks/*/spare-reviews', route => {
  if (route.request().method() === 'POST') {
   const input = route.request().postDataJSON();
   if (input.confirmedIdentity !== 'FIXTURE-01') return route.fulfill({status:400,json:{error:'Confirm the exact drive serial or WWN.'}});
   spareReview = {...input,id:1,assetName:'Shelf spare',author:'fixture-owner',recordedUtc:new Date().toISOString()};
   return route.fulfill({json:spareReview});
  }
  return route.fulfill({json:{generatedUtc:new Date().toISOString(),items:spareReview ? [{review:spareReview,status:'reviewed compatible'}] : [],nextBefore:null}});
 });
 await page.getByRole('button',{name:'Review spare compatibility',exact:true}).click();
 await page.getByRole('button',{name:'Record compatibility review',exact:true}).click();
 await page.getByLabel('Compatibility assessment',{exact:true}).click(); await page.locator('[role="option"][data-value="compatible"]').click();
 await page.getByLabel('Compatibility evidence',{exact:true}).fill('Capacity, interface, sector format and fit checked against fixture manufacturer and enclosure sheets.');
 await page.getByLabel('Review again by (00:00 UTC)',{exact:true}).fill(new Date(Date.now()+30*86400000).toISOString().slice(0,10));
 await page.getByLabel('Confirm drive serial or WWN',{exact:true}).fill('wrong');
 await page.getByRole('button',{name:'Save compatibility review',exact:true}).click();
 await expect(page.getByRole('dialog').getByRole('status')).toBeFocused();
 await expect(page.getByRole('dialog').getByRole('status')).toContainText('Confirm the exact drive serial');
 await page.getByLabel('Confirm drive serial or WWN',{exact:true}).fill('FIXTURE-01');
 await page.setViewportSize({width:390,height:844});
 await audit('Spare compatibility form');
 await page.screenshot({path:'test-results/spare-review-mobile.png',fullPage:true});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.getByRole('button',{name:'Save compatibility review',exact:true}).click();
 await expect(page.getByText('Compatibility review saved. No stock was reserved.',{exact:true})).toBeVisible();
 await expect(page.getByText('reviewed compatible',{exact:true})).toBeVisible();
 expect(spareReview).toMatchObject({assetId:'spare',assetUpdatedUtc:'2026-10-31T12:00:00Z',verdict:'compatible',confirmedIdentity:'FIXTURE-01'});
 await page.getByText('Review evidence and author',{exact:true}).click();
 await audit('Spare compatibility evidence');
 await page.setViewportSize({width:1440,height:1000});
 await page.getByRole('button', { name: 'Create review plan', exact: true }).click();
 await page.getByLabel('Estimated budget (optional)').fill('300');
 await page.getByRole('button', { name: 'Save review plan', exact: true }).click();
 await expect(page.getByText('Review plan saved. Evidence has been attached to the task.', { exact: true })).toBeVisible();
 expect(savedReview).toMatchObject({ diskId: '22222222-2222-2222-2222-222222222222', budget: 300, currency: 'USD' });
 expect(String(savedReview?.['reason'])).toContain('Pending sectors observed: 2');
 const oldId = '33333333-3333-3333-3333-333333333333';
 const findingId = '55555555-5555-5555-5555-555555555555';
 await page.route(`**/api/v1/findings/${findingId}`, route => route.fulfill({ json: {
  id: findingId, diskId: oldId, title: 'Historical media evidence', evidence: 'Pending sectors were observed in an earlier sample.', nextStep: 'Review recovery readiness.', rule: 'media-present-v1:ata.pending_sectors', severity: 'urgent', state: 'resolved', firstSeenUtc: '2026-01-01T12:00:00Z', lastSeenUtc: '2026-01-02T12:00:00Z', evidenceObservedUtc: '2026-01-01T12:00:00Z', evidenceSource: 'fixture', evidenceSchema: 'fixture-v1', evidenceEpoch: 'one', resolvedUtc: '2026-01-02T12:00:00Z', resolutionEvidence: 'Two later qualifying observations were zero.', assignedOwner: null, reviewUtc: null, snoozedUntilUtc: null
 } }));
 await page.goto(`/?finding=${findingId}`);
 await expect(page.getByRole('heading', { name: 'Historical media evidence', exact: true })).toBeVisible();
 await expect(page.getByRole('heading', { name: 'Resolution evidence', exact: true })).toBeVisible();
 await expect(page.getByRole('button', { name: 'Acknowledge for 7 days', exact: true })).toHaveCount(0);
 await page.screenshot({ path: 'test-results/finding-evidence-mobile.png', fullPage: true });
 const poolFindingId = '77777777-7777-7777-7777-777777777777';
 await page.route(`**/api/v1/findings/${poolFindingId}`,route=>route.fulfill({json:{id:poolFindingId,diskId:null,title:'Pool tank member error counters increased',evidence:'Pool GUID pool-one. Member 18446744073709551615: checksum 0 → 7 (+7). These are observed net differences, not guaranteed new errors or physical-drive failures.',nextStep:'Review member history, independent recovery and native NAS evidence before planning replacement.',rule:'vdev-counter-increase-v1:pool-one',severity:'review',state:'open',firstSeenUtc:'2026-01-01T12:00:00Z',lastSeenUtc:'2026-01-01T12:00:00Z',evidenceObservedUtc:'2026-01-01T12:00:00Z',evidenceSource:'TrueNAS pool.query + system.info',evidenceSchema:'truenas-24.10-pool-v4',evidenceEpoch:'Matching reported context; unobserved clears remain possible',evidenceUnit:'observed counter differences',resolvedUtc:null,resolutionEvidence:null,assignedOwner:null,reviewUtc:null,snoozedUntilUtc:null}}));
 await page.goto(`/?finding=${poolFindingId}`);
 await expect(page.getByRole('heading',{name:'Pool tank member error counters increased',exact:true})).toBeVisible();
 await expect(page.getByRole('link',{name:'View drive evidence',exact:true})).toHaveCount(0);
 await audit('Pool counter finding');
 await page.locator('.finding-detail').screenshot({path:'test-results/pool-counter-finding-mobile.png'});
 await page.getByRole('link',{name:'View pool evidence',exact:true}).click();
 await expect(page).toHaveURL(/\/storage$/);
 const resilverFindingId = '88888888-8888-8888-8888-888888888888';
 await page.route(`**/api/v1/findings/${resilverFindingId}`,route=>route.fulfill({json:{id:resilverFindingId,diskId:oldId,title:'Drive evidence flagged during resilver of tank',evidence:'Pool tank reported RESILVER / SCANNING at 2026-01-02T12:00:00Z. Inferred member mapping: member-one (data). Original drive observation time 2026-01-01T12:00:00Z. Pending observation: 8 sectors. The unresolved evidence may predate this scan; this does not establish simultaneous deterioration or predict resilver success. Mapping does not verify a physical bay.',nextStep:'Review original drive evidence, current native scan and independent recovery readiness. Do not remove a drive, interrupt the scan or clear errors based on this correlation.',rule:'resilver-drive-evidence-v1:pool-one:fixture',severity:'urgent',state:'open',firstSeenUtc:'2026-01-02T12:00:00Z',lastSeenUtc:'2026-01-02T12:00:00Z',evidenceObservedUtc:'2026-01-01T12:00:00Z',evidenceSource:'TrueNAS pool.query + same-cycle disk.query mapping + retained local finding',evidenceSchema:'resilver-drive-evidence-v1',evidenceEpoch:'Independent scan read and historical drive evidence; not a counter comparison',evidenceUnit:'contextual review',resolvedUtc:null,resolutionEvidence:null,assignedOwner:null,reviewUtc:null,snoozedUntilUtc:null}}));
 await page.goto(`/?finding=${resilverFindingId}`);
 await expect(page.getByRole('heading',{name:'Drive evidence flagged during resilver of tank',exact:true})).toBeVisible();
 await expect(page.getByRole('link',{name:'View pool evidence',exact:true})).toHaveAttribute('href','/storage');
 await expect(page.getByRole('link',{name:'View drive evidence',exact:true})).toHaveAttribute('href',`/storage?disk=${oldId}`);
 await audit('Resilver evidence finding');
 await page.locator('.finding-detail').screenshot({path:'test-results/resilver-finding-mobile.png'});
 await page.goto(`/?finding=${findingId}`);
 const manualFinding = {id:findingId,diskId:oldId,title:'Historical self-test evidence',evidence:'A prior self-test reported a read failure.',nextStep:'Review retained report and independent recovery.',rule:'ata-self-test-history-v1',severity:'review',state:'open',resolutionToken:'fixture-revision',firstSeenUtc:'2026-01-01T12:00:00Z',lastSeenUtc:'2026-01-02T12:00:00Z',evidenceObservedUtc:'2026-01-02T12:00:00Z',evidenceSource:'fixture',evidenceSchema:'fixture-v1',evidenceEpoch:null,resolvedUtc:null as string|null,resolutionEvidence:null as string|null,assignedOwner:null,reviewUtc:null,snoozedUntilUtc:null};
 await page.route(`**/api/v1/findings/${findingId}`,route=>route.fulfill({json:manualFinding}));
 await page.route(`**/api/v1/findings/${findingId}/resolve`,route=>{
  const body=route.request().postDataJSON();expect(body.resolutionToken).toBe('fixture-revision');expect(body.reviewedCurrentEvidence).toBe(true);
  Object.assign(manualFinding,{state:'resolved',resolutionEvidence:body.evidence,resolvedUtc:new Date().toISOString()});return route.fulfill({json:manualFinding});
 });
 await page.reload();
 await page.getByRole('button',{name:'Record resolution',exact:true}).click();
 await page.getByLabel('Resolution evidence',{exact:true}).fill('Reviewed failed-test history, current native details and verified independent recovery copy.');
 await page.getByRole('checkbox',{name:'I reviewed current evidence and independent recovery readiness.',exact:true}).check();
 await audit('Manual finding resolution');
 await page.screenshot({path:'test-results/finding-resolution-mobile.png',fullPage:true});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await page.getByRole('button',{name:'Save resolution',exact:true}).click();
 await expect(page.locator('.finding-detail').getByRole('status')).toContainText('Resolution recorded locally');
 await expect(page.locator('.finding-detail').getByRole('status')).toBeFocused();
 await expect(page.getByRole('heading',{name:'Resolution evidence',exact:true})).toBeVisible();
 await expect(page.getByRole('button',{name:'Record resolution',exact:true})).toHaveCount(0);
 expect((await page.request.get('/api/v1/not-a-real-endpoint')).status()).toBe(404);
 await page.goto('/storage?disk=not-a-real-drive');
 await expect(page.getByRole('region',{name:'Storage',exact:true}).getByRole('status')).toContainText('This drive could not be found');
 await page.goto('/not-a-real-page');
 await expect(page.getByRole('heading', { name: 'This page does not exist', exact: true })).toBeVisible();
 await page.goto('/?finding=66666666-6666-6666-6666-666666666666');
 await expect(page.getByRole('alert')).toContainText('This finding could not be found');
 await page.getByRole('link', { name: 'Planning', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Buy compatible spare', exact: true })).toBeVisible();
 let overviewMode = 'http';
 let releaseOverview: (() => Promise<void>) | null = null;
 await page.route('**/api/v1/overview', route => {
  if (overviewMode === 'normal') return route.fallback();
  if (overviewMode === 'invalid') return route.fulfill({ json: { plans: null } });
  if (overviewMode === 'hang') return new Promise<void>(resolve => { releaseOverview = async () => { await route.abort().catch(() => {}); resolve(); }; });
  return route.fulfill({ status: 503, json: { error: 'Fixture service unavailable' } });
 });
 await page.getByRole('button', { name: 'Refresh', exact: true }).click();
 const unavailable = page.getByRole('alert').filter({ hasText: 'Overview refresh unavailable' });
 await expect(unavailable).toContainText('Showing last-known overview data loaded');
 await expect(page.getByRole('status', { name: 'Overview refresh status' })).toContainText('Overview unavailable');
 await expect(page.getByRole('heading', { name: 'Buy compatible spare', exact: true })).toBeVisible();
 await page.screenshot({ path: 'test-results/overview-unavailable-mobile.png', fullPage: true });
 overviewMode = 'invalid';
 await page.getByRole('button', { name: 'Retry overview', exact: true }).click();
 await expect(unavailable).toContainText('Overview data could not be read.');

 overviewMode = 'hang';
 await page.getByRole('button', { name: 'Retry overview', exact: true }).click();
 await expect.poll(() => releaseOverview !== null).toBe(true);
 await page.clock.fastForward(15001);
 await expect(unavailable).toContainText('The refresh timed out');
 await (releaseOverview as (() => Promise<void>) | null)?.();
 overviewMode = 'normal';
 await page.getByRole('button', { name: 'Retry overview', exact: true }).click();
 await expect(unavailable).toHaveCount(0);
 await expect(page.getByRole('status', { name: 'Overview refresh status' })).toContainText('Overview refreshed');
 await expect(page.getByRole('heading', { name: 'Buy compatible spare', exact: true })).toBeVisible();
 // A superseded request must not overwrite a newer successful refresh.
 releaseOverview = null; overviewMode = 'hang';
 await page.getByRole('button', { name: 'Refresh', exact: true }).click();
 await expect.poll(() => releaseOverview !== null).toBe(true);
 overviewMode = 'normal';
 await page.getByRole('button', { name: 'Refresh', exact: true }).click();
 await expect(page.getByRole('status', { name: 'Overview refresh status' })).toContainText('Overview refreshed');
 await (releaseOverview as (() => Promise<void>) | null)?.();
 await expect(unavailable).toHaveCount(0);
 // Initial overview failure must not look like an empty, healthy installation.
 overviewMode = 'http';
 await page.goto('/');
 await expect(page.getByRole('alert')).toContainText('No overview data has been loaded in this session.');
 await expect(page.getByRole('heading', { name: 'Overview not available yet', exact: true })).toBeVisible();
 await expect(page.getByRole('heading', { name: 'Meet your NAS', exact: true })).toHaveCount(0);
 overviewMode = 'normal';
 await page.getByRole('button', { name: 'Retry overview', exact: true }).click();
 await expect(page.getByRole('status', { name: 'Overview refresh status' })).toContainText('Overview refreshed');
 let sessionUnavailable = true;
 await page.route('**/api/v1/session', route => sessionUnavailable ? route.fulfill({ status: 503, json: { error: 'Fixture session unavailable' } }) : route.fallback());
 await page.reload();
 await expect(page.getByRole('button', { name: 'Retry connection', exact: true })).toBeVisible();
 sessionUnavailable = false;
 await page.getByRole('button', { name: 'Retry connection', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Your homelab, at a glance.', exact: true })).toBeVisible();
 await page.setViewportSize({width:390,height:844});
 const identityConnection = { id: connectionId, configurationRevision:'00000000-0000-0000-0000-000000000001', name: 'Identity fixture NAS', apiUri: 'wss://nas.fixture/websocket', connectHost: null, connectPort: null, pollSeconds: 60, enabled: false, trustedCaPem: null, certificateSha256: null, status: 'System identity changed or missing — collection paused', systemSerial: 'NAS-A', observedSystemSerial: 'NAS-B', systemIdentityState: 'changed', systemIdentityCheckedUtc: '2026-10-31T12:00:00Z' };
 const connections = [
  identityConnection,
  { ...identityConnection, id: 'stale-fixture', name: 'Stale fixture NAS', enabled: true, status: 'Partial', lastSuccessUtc: '2020-01-01T00:00:00Z', systemSerial: null, observedSystemSerial: null, systemIdentityState: 'unavailable' },
  { ...identityConnection, id: 'paused-fixture', name: 'Paused fixture NAS', status: 'Collection paused by owner', systemSerial: null, observedSystemSerial: null, systemIdentityState: 'unavailable' },
  { ...identityConnection, id: 'unsupported-fixture', name: 'Unsupported fixture NAS', status: 'Unsupported release — collection paused', systemSerial: null, observedSystemSerial: null, systemIdentityState: 'unavailable' },
 ];
 await page.route('**/api/v1/connections', route => route.fulfill({ json: connections }));
 let acceptedIdentity: Record<string, unknown> | null = null;
 await page.route(`**/api/v1/connections/${connectionId}/identity`, route => {
  const input = route.request().postDataJSON();
  if (input.configurationRevision !== identityConnection.configurationRevision) return route.fulfill({status:409,json:{error:'This connection changed since the identity review was loaded. Reload it and verify the current endpoint, certificate and serials.'}});
  if (input.previousSerial !== 'NAS-A' || input.newSerial !== 'NAS-B') return route.fulfill({ status: 400, json: { error: 'Confirm the exact saved and newly reported serials.' } });
  acceptedIdentity = input;
  identityConnection.systemSerial = 'NAS-B'; identityConnection.systemIdentityState = 'awaiting validation'; identityConnection.enabled = true;
  return route.fulfill({ json: {} });
 });
 await page.getByRole('link', { name: 'Settings', exact: true }).click();
 await expect(page.getByText('Out of date', {exact:true})).toBeVisible();
 await expect(page.getByText('Paused', {exact:true})).toBeVisible();
 await expect(page.getByText('Unsupported release', {exact:true})).toBeVisible();
 await page.getByRole('link', { name: 'Review Identity fixture NAS', exact: true }).click();
 await page.getByLabel('Retype saved system serial').fill('wrong');
 await page.getByLabel('Retype newly reported system serial').fill('NAS-B');
 await page.getByLabel('Independent verification evidence').fill('Motherboard replaced; physical label, endpoint and certificate verified');
 await page.getByRole('button', { name: 'Accept new baseline & resume', exact: true }).click();
 await expect(page.getByRole('alert')).toContainText('Confirm the exact saved and newly reported serials.');
 await page.getByLabel('Retype saved system serial').fill('NAS-A');
 identityConnection.configurationRevision = '00000000-0000-0000-0000-000000000004';
 await page.getByRole('button',{name:'Accept new baseline & resume',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('This connection changed since the identity review was loaded');
 expect(acceptedIdentity).toBeNull();
 await expect(page.getByRole('alert')).toBeFocused();
 await page.getByRole('button',{name:'Reload saved connection',exact:true}).click();
 await expect(page.getByLabel('Retype saved system serial')).toHaveValue('');
 await page.getByLabel('Retype saved system serial').fill('NAS-A');
 await page.getByLabel('Retype newly reported system serial').fill('NAS-B');
 await page.getByLabel('Independent verification evidence').fill('Motherboard replaced; physical label, endpoint and certificate verified');

 await page.getByRole('heading', { name: 'This system reported a different serial', exact: true }).scrollIntoViewIfNeeded();
 await audit('Connection detail');
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
 await page.screenshot({ path: 'test-results/identity-review-mobile.png' });
 await page.getByRole('button', { name: 'Accept new baseline & resume', exact: true }).click();
 await expect(page.getByText('System identity: awaiting validation', { exact: true })).toBeVisible();
 expect(acceptedIdentity).toMatchObject({ previousSerial: 'NAS-A', newSerial: 'NAS-B', evidence: 'Motherboard replaced; physical label, endpoint and certificate verified' });
 await page.route(`**/api/v1/connections/${connectionId}`, route => {
  const input = route.request().postDataJSON();
  if (input.configurationRevision !== identityConnection.configurationRevision)
   return route.fulfill({status:409,json:{error:'This connection changed since it was loaded. Reload the saved connection and review it before saving.'}});
  Object.assign(identityConnection,input,{configurationRevision:'00000000-0000-0000-0000-000000000003'});
  return route.fulfill({json:{}});
 });
 await page.getByLabel('Name',{exact:true}).fill('Unsaved stale name');
 await page.getByLabel('New NAS API key',{exact:true}).fill('discard-this-fixture-key');
 identityConnection.name = 'Updated by another tab'; identityConnection.configurationRevision = '00000000-0000-0000-0000-000000000002';
 await page.getByRole('button',{name:'Save changes',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('This connection changed since it was loaded');
 await expect(page.getByLabel('Name',{exact:true})).toHaveValue('Unsaved stale name');
 await expect(page.getByRole('alert')).toBeVisible();
 await audit('Connection edit conflict');
 await page.locator('.settings-content').screenshot({path:'test-results/connection-conflict-mobile.png',animations:'disabled'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.getByRole('button',{name:'Reload saved settings',exact:true}).click();
 await expect(page.getByLabel('Name',{exact:true})).toHaveValue('Updated by another tab');
 await expect(page.getByLabel('New NAS API key',{exact:true})).toHaveValue('');
 await page.getByLabel('Name',{exact:true}).fill('Reviewed connection');
 await page.getByRole('button',{name:'Save changes',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Reviewed connection',exact:true})).toBeVisible();
 await expect(page.getByText('Last replaced')).toHaveCount(0);

 const notificationFixture = {
  channel: {host:'notifier.example',enabled:true,maskIdentifiers:true,testedUtc:'2026-11-01T00:00:00Z',maintenanceUntilUtc:null},
  deliveries:[{id:'77777777-7777-7777-7777-777777777777',kind:'opened',status:'failed',attempts:5,createdUtc:'2026-11-01T00:00:00Z'}],
  attempts:[{id:1,deliveryId:'77777777-7777-7777-7777-777777777777',atUtc:'2026-11-01T00:05:00Z',result:'outcome-unknown',httpStatus:null}]
 };
 await page.route('**/api/v1/notifications', route => route.fulfill({json:notificationFixture}));
 // Leave and return so the settings view fetches the newly installed fixture.
 await page.getByRole('link',{name: /^Overview(?: \d+ open findings?)?$/}).click();
 await expect(page.getByRole('heading',{name:'Your homelab, at a glance.',exact:true})).toBeVisible();
 let requestedRetry = false;
 await page.route('**/api/v1/notifications/deliveries/*/retry', route => {
  requestedRetry = true; notificationFixture.deliveries[0].status = 'pending'; notificationFixture.deliveries[0].attempts = 0;
  return route.fulfill({status:200,json:{}});
 });
 await page.getByRole('link',{name:'Settings',exact:true}).click();
 await openSettings('Notifications');
 const attemptDetails = page.getByText('Recent attempt details',{exact:true});
 await attemptDetails.focus(); await page.keyboard.press('Enter');
 await expect(page.getByText('Receiver outcome unknown',{exact:false})).toBeVisible();
 await expect(page.getByText('An interrupted send may have reached the receiver.',{exact:false})).toBeVisible();
 await audit('Notification attempt history');
 await attemptDetails.scrollIntoViewIfNeeded();
 const historyTextBounds = await page.getByText('An interrupted send may have reached the receiver.',{exact:false}).boundingBox();
 expect(historyTextBounds!.width).toBeGreaterThan(250);
 await page.screenshot({path:'test-results/notification-attempt-mobile.png',animations:'disabled'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.getByRole('button',{name:'Retry delivery',exact:true}).click();
 await expect(page.getByRole('button',{name:'Retry delivery',exact:true})).toHaveCount(0);
 expect(requestedRetry).toBe(true);
 await expect(page.getByText('Receiver outcome unknown',{exact:false})).toBeVisible();
 let telegramSaved: Record<string, unknown> | null = null;
 const telegramChannel = {kind:'telegram',host:'api.telegram.org',enabled:false,maskIdentifiers:true,testedUtc:null as string|null,maintenanceUntilUtc:null,quietStart:null as string|null,quietEnd:null as string|null};
 await page.route('**/api/v1/notifications', route => {
   if(route.request().method()==='PUT') { telegramSaved=route.request().postDataJSON(); telegramChannel.enabled=Boolean(telegramSaved!.enabled); telegramChannel.quietStart=telegramSaved!.quietStart as string|null; telegramChannel.quietEnd=telegramSaved!.quietEnd as string|null; return route.fulfill({json:{}}); }
   return route.fulfill({json:{channel:telegramChannel,deliveries:[],attempts:[]}});
 });
 await page.route('**/api/v1/notifications/test', route => {telegramChannel.testedUtc='2026-11-01T12:00:00Z';return route.fulfill({json:{}});});
 await page.getByRole('button', { name: 'Change', exact: true }).click();
 await page.getByLabel('Notification service',{exact:true}).click(); await page.locator('[role="option"][data-value="telegram"]').click();
 await page.getByLabel('Telegram bot token',{exact:true}).fill('123456:abcdefghijklmnopqrstuvwxyz');
 await page.getByLabel('Telegram chat ID',{exact:true}).fill('-100123456');
 await expect(page.getByRole('button',{name:'Send a test',exact:true})).toBeDisabled();
 await audit('Telegram notification setup');
 await page.getByRole('button',{name:'Save destination',exact:true}).click();
 await expect.poll(()=>telegramSaved).toMatchObject({kind:'telegram',botToken:'123456:abcdefghijklmnopqrstuvwxyz',chatId:'-100123456',enabled:false});
 await expect(page.getByLabel('Telegram bot token',{exact:true})).toHaveCount(0);
 await expect(page.getByLabel('Telegram chat ID',{exact:true})).toHaveCount(0);
 await expect(page.getByRole('button',{name:'Turn on delivery',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Send a test',exact:true}).click();
 await expect(page.getByRole('button',{name:'Turn on delivery',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Turn on delivery',exact:true}).click();
 await expect(page.getByRole('switch',{name:'Delivery enabled',exact:true})).toBeChecked();
 await expect(page.getByText(/Accepted means/)).toHaveCount(0);
 await page.getByLabel('Quiet period starts',{exact:true}).fill('22:00');
 await page.getByLabel('Quiet period ends',{exact:true}).fill('07:00');
 await page.getByRole('button',{name:'Save changes',exact:true}).click();
 await expect.poll(()=>telegramSaved).toMatchObject({quietStart:'22:00',quietEnd:'07:00',maintenanceUntilUtc:null});
 await expect(page.getByLabel('Quiet period starts',{exact:true})).toHaveValue('22:00');
 await expect(page.getByLabel('Quiet period ends',{exact:true})).toHaveValue('07:00');
 await audit('Daily quiet hours');
 await page.screenshot({path:'test-results/telegram-settings-mobile.png',animations:'disabled'});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth)).toBeTruthy();
 let scanMode = 'active';
 const scanTime = await page.evaluate(() => Date.now());
 await page.route('**/api/v1/overview', async route => {
  if (scanMode === 'offline') return route.fulfill({status:503,json:{error:'Fixture outage'}});
  const response = await route.fetch();
  return route.fulfill({json:{...await response.json(),scanReports:scanMode === 'complete' ? [] : [{
   connectionId,nasName:'Fixture NAS',poolGuid:'pool-one',poolName:'tank',function:'RESILVER',state:'SCANNING',
   fetchedUtc:new Date(scanTime).toISOString(),validUntilUtc:new Date(scanTime+30000).toISOString(),freshness:'fresh',
   explanation:'Latest pool read reported this scan state. No completion time is inferred.',percentage:45.5,errors:null,
   scanStartedUtc:new Date(scanTime-7200000).toISOString(),scanPausedUtc:new Date(scanTime-60000).toISOString(),scanEndedUtc:null,scanRemainingSeconds:7200
  }]}});
 });
 await page.getByRole('link',{name: /^Overview(?: \d+ open findings?)?$/}).click();
 await page.getByRole('button',{name:'Refresh',exact:true}).click();
 const scanRegion = page.getByRole('region',{name:'Scrub and resilver reports'});
 await expect(scanRegion.getByText('Recent source report',{exact:true})).toBeVisible();
 await expect(scanRegion.getByText('Reported progress: 45.5% · Scan errors unknown',{exact:true})).toBeVisible();
 await expect(scanRegion.getByText('RESILVER · PAUSED (source report)',{exact:true})).toBeVisible();
 await scanRegion.getByText('Source scan timing',{exact:true}).focus();
 await page.keyboard.press('Enter');
 await expect(scanRegion.getByText('about 2.0 hours',{exact:true})).toBeVisible();
 await expect(scanRegion.getByText('NAS-reported times.',{exact:false})).toBeVisible();
 await audit('Overview scan report');
 scanMode = 'offline';
 await page.clock.fastForward(40000);
 await expect(scanRegion.getByText('Last-known report',{exact:true})).toBeVisible();
 await expect(scanRegion.getByText('Current operation state is unverified.',{exact:false})).toBeVisible();
 await scanRegion.scrollIntoViewIfNeeded();
 await page.screenshot({path:'test-results/overview-scan-mobile.png',animations:'disabled'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 scanMode = 'complete';
 await page.getByRole('button',{name:'Refresh',exact:true}).click();
 await expect(scanRegion.getByText('RESILVER · PAUSED (source report)',{exact:true})).toHaveCount(0);
 await expect(scanRegion).toHaveCount(0);
 await expect(page.getByRole('heading', { name: 'Meet your NAS' })).toBeVisible();
 await expect(page.getByRole('region', { name: 'Readiness' })).toHaveCount(0);
 expect(errors).toEqual([]);
});
