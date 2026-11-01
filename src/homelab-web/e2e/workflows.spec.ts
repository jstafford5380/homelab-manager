import { test, expect, type Route } from '@playwright/test';
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
 await expect(page.getByRole('heading', { name: 'Your storage, at a glance.' })).toBeVisible();
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
 await page.getByLabel('Type', { exact: true }).selectOption('spare');
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
 await page.getByLabel('Recovery importance').selectOption('critical');
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
 await expect(page.getByLabel('Recovery importance')).toHaveValue('critical');
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
 await recoveryHistory.getByLabel('Evidence type for Family photos').selectOption('exercises');
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
 await expect(page.getByRole('heading', { name: 'Notifications', exact: true })).toBeVisible();
 await expect(page.getByRole('button', { name: 'Enable delivery' })).toBeDisabled();
 await audit('Settings');
 await page.getByRole('button', { name: 'Connect NAS', exact: true }).click();
 const enrollmentDialog = page.getByRole('dialog');
 await expect(enrollmentDialog.getByLabel('Trusted public CA certificate (optional)')).toBeVisible();
 await enrollmentDialog.getByLabel('Trusted public CA certificate (optional)').fill('fixture public certificate');
 await audit('NAS enrollment certificate');
 await page.keyboard.press('Escape');
 await expect(enrollmentDialog).not.toBeVisible();
 await expect(page.getByRole('button', {name:'Connect NAS',exact:true})).toBeFocused();

 const themeSelect = page.getByLabel('Color theme',{exact:true});
 await expect(themeSelect).toHaveValue('system');
 const settingsBeforeTheme = await (await page.request.get('/api/v1/settings')).json();
 await themeSelect.focus(); await expect(themeSelect).toBeFocused();
 await themeSelect.selectOption('dark');
 await expect(themeSelect).toHaveValue('dark');
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await expect(page.getByText('Appearance saved for this browser.',{exact:true})).toBeVisible();
 await page.reload();
 await expect(themeSelect).toHaveValue('dark');
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await page.emulateMedia({colorScheme:'light'});
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 const secondTab = await page.context().newPage(); await secondTab.goto('/settings');
 await expect(secondTab.getByLabel('Color theme',{exact:true})).toHaveValue('dark');
 await themeSelect.selectOption('light');
 await expect(secondTab.locator('html')).toHaveAttribute('data-theme','light');
 await expect(secondTab.getByLabel('Color theme',{exact:true})).toHaveValue('light');
 await secondTab.close();
 await themeSelect.selectOption('system');
 await expect(page.locator('html')).toHaveAttribute('data-theme','light');
 await page.emulateMedia({colorScheme:'dark'});
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await page.emulateMedia({colorScheme:'light'});
 await expect(page.locator('html')).toHaveAttribute('data-theme','light');
 await page.evaluate(() => { Object.defineProperty(Storage.prototype,'setItem',{value:() => { throw new DOMException('Storage disabled','SecurityError'); },configurable:true}); });
 await themeSelect.selectOption('dark');
 await expect(page.locator('html')).toHaveAttribute('data-theme','dark');
 await expect(page.getByText('Appearance applied for this session. Browser storage is unavailable.',{exact:true})).toBeVisible();
 await page.reload();
 await expect(themeSelect).toHaveValue('system');
 await expect(page.locator('html')).toHaveAttribute('data-theme','light');
 expect(await (await page.request.get('/api/v1/settings')).json()).toEqual(settingsBeforeTheme);

 const backupRow = page.locator('.row').filter({hasText:'fixture-only.zip'});
 await expect(backupRow.getByText('1 GiB',{exact:true})).toBeVisible();
 await page.getByLabel('Capacity units').selectOption('decimal');
 await page.getByLabel('Display timezone').fill('America/Phoenix');
 await page.getByRole('button',{name:'Save settings',exact:true}).click();
 await expect(page.getByText('Settings saved.',{exact:true})).toBeVisible();
 await expect(backupRow.getByText('1.07 GB',{exact:true})).toBeVisible();
 const phoenixTime = await page.evaluate(() => new Date('2026-01-01T12:00:00Z').toLocaleString(undefined,{timeZone:'America/Phoenix',timeZoneName:'short'}));
 await expect(backupRow.getByRole('heading')).toHaveText(phoenixTime);
 await expect(page.getByLabel('Temperature review limit (°C)',{exact:true})).toHaveValue('');
 await page.getByLabel('Temperature review limit (°C)',{exact:true}).fill('45');
 await page.getByRole('button',{name:'Save settings',exact:true}).click();
 await expect(page.getByText('Record the basis for your local temperature policy before enabling it.',{exact:true})).toBeVisible();
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.temperatureReviewCelsius).toBeNull();
 await page.getByLabel('Temperature policy basis',{exact:true}).fill('Fixture local cooling review policy');
 await page.getByRole('button',{name:'Save settings',exact:true}).click();
 await expect(page.getByText('Settings saved.',{exact:true})).toBeVisible();
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.temperatureReviewCelsius).toBe(45);
 await audit('Local temperature policy');
 const policyViewport = page.viewportSize()!;
 await page.setViewportSize({width:390,height:844});
 await page.getByRole('group',{name:'Optional local temperature policy',exact:true}).screenshot({path:'test-results/temperature-policy-mobile.png'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.setViewportSize(policyViewport);
 await page.getByLabel('Temperature review limit (°C)',{exact:true}).fill('');
 await page.getByRole('button',{name:'Save settings',exact:true}).click();
 await expect(page.getByText('Settings saved.',{exact:true})).toBeVisible();
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.temperatureReviewCelsius).toBeNull();
 await page.getByLabel('Display timezone').fill('Invalid/Timezone');
 await expect(backupRow.getByRole('heading')).toHaveText(phoenixTime);
 await page.getByRole('button',{name:'Save settings',exact:true}).click();
 await expect(page.getByText('Choose a valid timezone.',{exact:true})).toBeVisible();
 await expect(backupRow.getByRole('heading')).toHaveText(phoenixTime);
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.timezone).toBe('America/Phoenix');
 await page.getByLabel('Display timezone').fill('UTC');
 await page.getByLabel('Disk inventory interval (minutes)').fill('30');
 await expect(page.getByLabel('Read existing NAS temperature cache')).not.toBeChecked();
 await page.getByLabel('Read existing NAS temperature cache').check();
 await page.getByRole('button', { name: 'Save settings', exact: true }).click();
 await expect(page.getByText('Settings saved.', { exact: true })).toBeVisible();
 await expect(page.getByLabel('Disk inventory interval (minutes)')).toHaveValue('30');
 await expect(page.getByLabel('Read existing NAS temperature cache')).toBeChecked();
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.readCachedTemperatures).toBe(true);
 expect((await (await page.request.get('/api/v1/settings')).json()).settings.capacityUnits).toBe('decimal');
 const utcTime = await page.evaluate(() => new Date('2026-01-01T12:00:00Z').toLocaleString(undefined,{timeZone:'UTC',timeZoneName:'short'}));
 await expect(backupRow.getByRole('heading')).toHaveText(utcTime);
 await page.getByRole('link', { name: 'Overview', exact: true }).click();
 await expect(page.getByText('1 recovery gap(s)')).toBeVisible();
 await page.screenshot({ path: 'test-results/overview-desktop.png', fullPage: true });
 await page.setViewportSize({ width: 390, height: 844 });
 const navigationLinks = page.getByRole('navigation', { name: 'Main navigation' }).getByRole('link');
 await expect(navigationLinks).toHaveCount(7);
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
 await page.getByRole('link', { name: 'Storage', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'No disks discovered yet' })).toBeVisible();
 // Isolated browser fixtures exercise unknown values and history controls without NAS access.
 const connectionId = '11111111-1111-1111-1111-111111111111';
 const storageFixture = {
  systems: [{ id: connectionId, name: 'Fixture NAS', status: 'Partial — SMART unverified', lastSuccessUtc: '2026-01-01T12:00:00Z', topologyJson: JSON.stringify([{ name: 'tank', guid: 'pool-one', status: 'ONLINE', topology: null }]), redundancy: [{ poolGuid:'pool-one',status:'unavailable',explanation:'Pool evidence is stale or collection is paused.',groups:[] as {role:string;guid:string;layout:string;members:number;configuredTolerance:number|null;state:string}[] }] }],
  capabilities: [{ connectionId, name: 'pools', status: 'stale', detail: 'Last-known snapshot', lastSuccessUtc: '2026-01-01T12:00:00Z' }, {connectionId,name:'events:alert.list',status:'supported',detail:'Subscription confirmed; snapshots remain authoritative.',lastSuccessUtc:'2026-01-01T12:00:00Z'}, {connectionId,name:'events:disk.query',status:'denied',detail:'Disk updates denied; periodic inventory remains independent.',lastSuccessUtc:null}], nativeAlerts: []
 };
 let storageMode = 'valid';
 let delayedStorage: Route | undefined;
 await page.route('**/api/v1/storage', async route => {
  if (storageMode === 'failure') return route.fulfill({status:503,json:{error:'fixture outage'}});
  if (storageMode === 'malformed') return route.fulfill({json:{systems:{},capabilities:[],nativeAlerts:[]}});
  if (storageMode === 'bad-topology') return route.fulfill({json:{...storageFixture,systems:[{...storageFixture.systems[0],topologyJson:'{}'}]}});
  if (storageMode === 'delayed') { delayedStorage = route; return; }
  return route.fulfill({json:storageFixture});
 });
 await page.route('**/pools/pool-one/capacity?*', route => route.fulfill({ json: {
  status: route.request().url().includes('days=7') ? 'suppressed' : 'estimated',
  explanation: route.request().url().includes('days=7') ? 'Pool size, layout or source format changed within this window.' : 'Approximate days to 80% allocated if measured linear growth continues.',
  samples: 43200, days: 30, firstUtc: '2026-01-01T00:00:00Z', lastUtc: '2026-01-31T00:00:00Z', sizeBytes: 1099511627776, allocatedBytes: 549755813888,
  growthBytesPerDay: 1073741824, daysTo80Percent: route.request().url().includes('days=7') ? null : 40, fit: .95
 } }));
 const member = {driveMapping:{status:'inferred',diskId:'22222222-2222-2222-2222-222222222222',serial:'FIXTURE-01',wwn:'5000c50012345678',model:'Fixture drive',explanation:'Same-cycle inventory match; inferred historical evidence, not bay verification.'},guid:'18446744073709551615',parentGuid:null,role:'data',type:'DISK',state:'ONLINE',path:'/dev/disk/by-partuuid/fixture',device:'sda1',diskAlias:'sda',readErrors:0,writeErrors:null,checksumErrors:7};
 const memberEvidence = JSON.stringify({schema:'truenas-24.10-vdev-evidence-v1',status:'reported',explanation:'Source-reported counters; no reset or repair inferred.',nodes:[member]});
 const poolPage = {
  items: [{ id: 2, vdevEvidenceJson: memberEvidence, vdevComparison: {status:'compared',explanation:'Observed differences; clears between polls can go undetected.',previousFetchedUtc:'2026-01-01T11:59:00Z',members:[{guid:member.guid,read:{status:'unchanged',difference:0},write:{status:'unknown',difference:null},checksum:{status:'increased',difference:7}}]}, fetchedUtc: '2026-01-01T12:00:00Z', status: 'ONLINE', statusCode: 'CORRUPT_DATA', sizeBytes: 107374182400, allocatedBytes: 53687091200, freeBytes: 53687091200, scanFunction: 'SCRUB', scanState: 'FINISHED', scanPercentage: 100, scanErrors: 0 },
   { id: 1, fetchedUtc: '2026-01-01T11:00:00Z', status: 'UNKNOWN', sizeBytes: null, allocatedBytes: null, freeBytes: null, scanFunction: null, scanState: null, scanPercentage: null, scanErrors: null }], nextBefore: 1
 };
 let delayedPool: Route | undefined;
 await page.route('**/pools/pool-one/history*', route => {
  if (route.request().url().includes('before=1')) { delayedPool = route; return; }
  return route.fulfill({json:poolPage});
 });
 await page.getByRole('link', { name: 'Overview', exact: true }).click();
 await page.getByRole('link', { name: 'Storage', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Fixture NAS', exact: true })).toBeVisible();
 await expect(page.getByText('Last successful source read:', { exact: false })).toBeVisible();
 const storageView = page.getByRole('region', {name:'Storage monitoring view'});
 const redundancy = page.getByText('Redundancy context: unavailable',{exact:true});
 await redundancy.focus(); await page.keyboard.press('Enter');
 await expect(page.getByText('Pool evidence is stale or collection is paused.',{exact:true})).toBeVisible();
 storageFixture.systems[0].redundancy[0] = {
  poolGuid:'pool-one',status:'nonredundant data role',explanation:'A special group has no configured disk redundancy. Group tolerances cannot be added across vdevs.',
  groups:[{role:'data',guid:'mirror-group',layout:'MIRROR',members:2,configuredTolerance:1,state:'reported online'},
          {role:'special',guid:'special-disk',layout:'DISK',members:1,configuredTolerance:0,state:'reported online'},
          {role:'spare',guid:'spare-disk',layout:'DISK',members:1,configuredTolerance:null,state:'reported available'}]
 };
 storageFixture.capabilities[0].status = 'supported';
 storageFixture.capabilities[0].lastSuccessUtc = new Date().toISOString();
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect(page.getByText('Redundancy context: nonredundant data role',{exact:true})).toBeVisible();
 await expect(page.getByText('special · DISK',{exact:true})).toBeVisible();
 await expect(page.getByText('Configured group tolerance: 0 disk failure(s).',{exact:false})).toBeVisible();
 await expect(page.getByText('Cache and spare roles do not add data redundancy.',{exact:true})).toBeVisible();
 await expect(page.getByText('Passive alert updates',{exact:true})).toBeVisible();
 await expect(page.getByText('Passive disk updates',{exact:true})).toBeVisible();
 const memberHistory = page.locator('.vdev-evidence').first();
 await memberHistory.getByText('Member error history',{exact:true}).focus(); await page.keyboard.press('Enter');
 await expect(memberHistory.getByText('18446744073709551615',{exact:true})).toBeVisible();
 await expect(memberHistory.getByText('Unknown',{exact:true})).toBeVisible();
 await expect(memberHistory.getByText('Observed differences: read +0 · write unknown · checksum +7',{exact:true})).toBeVisible();
 await expect(memberHistory.getByText('Drive match: inferred',{exact:true})).toBeVisible();
 await expect(memberHistory.getByRole('link',{name:'View matched drive evidence',exact:true})).toHaveAttribute('href','/storage?disk=22222222-2222-2222-2222-222222222222');
 await audit('Member error history');
 await memberHistory.screenshot({path:'test-results/member-history-mobile.png'});
 await memberHistory.getByLabel('Saved snapshot').selectOption('1');
 await expect(memberHistory.getByText('Member counters were not retained for this snapshot.')).toBeVisible();
 await memberHistory.getByLabel('Saved snapshot').selectOption('2');
 await memberHistory.getByText('Member error history',{exact:true}).click();
 const discoveryAssignments: Record<string,unknown>[]=[];
 const discoveryWwn='5000c50012345678';
 await page.route(`**/api/v1/connections/${connectionId}/identity-review`,route=>{
  if(route.request().method()==='POST'){const body=route.request().postDataJSON();expect(body.confirmedWwn).toBe(discoveryWwn);expect(body.confirmedPhysicalIdentity).toBe(true);const row={...body,id:'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',diskId:'22222222-2222-2222-2222-222222222222',serial:'FIXTURE-01',model:'Fixture drive',author:'fixture-owner',recordedUtc:new Date().toISOString(),revokedUtc:null,revocationEvidence:null};discoveryAssignments.unshift(row);return route.fulfill({json:row});}
  return route.fulfill({json:{usable:true,fetchedUtc:new Date().toISOString(),reviewToken:'discovery-review-token',groups:[{wwn:discoveryWwn,serial:'FIXTURE-01',model:'Fixture drive',aliases:['sda'],eligible:true,records:[]}],assignments:discoveryAssignments,historyTruncated:false}});
 });
 await page.route(`**/api/v1/connections/${connectionId}/identity-review/*/revoke`,route=>{const body=route.request().postDataJSON();Object.assign(discoveryAssignments[0],{revokedUtc:new Date().toISOString(),revocationEvidence:body.evidence});return route.fulfill({json:discoveryAssignments[0]});});
 const discoveryView=page.getByRole('region',{name:'Discovery identity review',exact:true});
 await discoveryView.getByRole('button',{name:'Review discovery identities',exact:true}).click();
 await discoveryView.getByRole('button',{name:'Review WWN assignment',exact:true}).click();
 const discoveryDialog=page.getByRole('dialog',{name:'Review stable WWN assignment',exact:true});
 await discoveryDialog.getByLabel('Confirm exact reported WWN',{exact:true}).fill(discoveryWwn);
 await discoveryDialog.getByLabel('Physical identity evidence',{exact:true}).fill('Synthetic physical label and source WWN review.');
 await discoveryDialog.getByLabel('Assignment review expiry (UTC)',{exact:true}).fill(new Date(Date.now()+30*86400000).toISOString().slice(0,10));
 await discoveryDialog.getByRole('checkbox',{name:'I independently verified this physical identity and reviewed the selected original record.',exact:true}).check();
 await audit('Discovery assignment review');
 await page.screenshot({path:'test-results/discovery-review-mobile.png'});
 await discoveryDialog.getByRole('button',{name:'Record reviewed assignment',exact:true}).click();
 await expect(discoveryView.getByRole('status')).toContainText('Assignment recorded locally');
 await discoveryView.getByText('Revoke this assignment',{exact:true}).click();
 await discoveryView.getByLabel('Revocation reason',{exact:true}).fill('Independent verification needs repeating.');
 await discoveryView.getByRole('button',{name:'Save revocation',exact:true}).click();
 await expect(discoveryView.getByText('Independent verification needs repeating.',{exact:false})).toBeVisible();
 await audit('Discovery assignment history');
 await discoveryView.getByRole('button',{name:'Hide discovery identity review',exact:true}).click();
 await audit('Pool redundancy context');
 await page.getByText('Redundancy context: nonredundant data role',{exact:true}).scrollIntoViewIfNeeded();
 await page.screenshot({path:'test-results/redundancy-mobile.png',animations:'disabled'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 storageFixture.capabilities[0].status = 'stale';
 storageFixture.systems[0].redundancy[0] = {poolGuid:'pool-one',status:'unavailable',explanation:'Pool evidence is stale or collection is paused.',groups:[]};
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect(page.getByText('special · DISK',{exact:true})).toHaveCount(0);
 await page.getByText('Redundancy context: unavailable',{exact:true}).click();

 storageMode = 'failure';
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect(storageView.getByRole('alert')).toContainText('last successfully loaded storage evidence');
 await expect(storageView.getByRole('heading',{name:'Fixture NAS',exact:true})).toBeVisible();
 for (const mode of ['malformed','bad-topology']) {
  storageMode = mode;
  await storageView.getByRole('button',{name:'Refresh storage view'}).click();
  await expect(storageView.getByRole('alert')).toContainText('could not be read safely');
  await expect(storageView.getByRole('heading',{name:'Fixture NAS',exact:true})).toBeVisible();
 }
 storageMode = 'delayed';
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect.poll(() => delayedStorage !== undefined).toBe(true);
 await expect(storageView).toHaveAttribute('aria-busy','true');
 storageFixture.systems[0].name = 'Newer storage evidence';
 storageMode = 'valid';
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect(storageView.getByRole('heading',{name:'Newer storage evidence',exact:true})).toBeVisible();
 await delayedStorage!.fulfill({json:{...storageFixture,systems:[{...storageFixture.systems[0],name:'Obsolete delayed evidence'}]}}).catch(() => {});
 await expect(storageView.getByRole('heading',{name:'Obsolete delayed evidence',exact:true})).toHaveCount(0);
 await expect(storageView.getByRole('alert')).toHaveCount(0);
 delayedStorage = undefined; storageMode = 'delayed';
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect.poll(() => delayedStorage !== undefined).toBe(true);
 await expect(storageView.getByRole('alert')).toContainText('timed out after 15 seconds',{timeout:20000});
 await expect(storageView.getByRole('heading',{name:'Newer storage evidence',exact:true})).toBeVisible();
 await delayedStorage!.abort().catch(() => {});
 storageFixture.systems[0].name = 'Fixture NAS'; storageMode = 'valid';
 await storageView.getByRole('button',{name:'Refresh storage view'}).click();
 await expect(storageView.getByRole('alert')).toHaveCount(0);
 await expect(storageView.getByRole('heading',{name:'Fixture NAS',exact:true})).toBeVisible();

 await page.route('**/collection-history*', route => route.fulfill({ json: {
   items: [{ id: 1, startedUtc: '2026-01-01T12:00:00Z', result: 'partial', durationMs: null, coverage: { known: false, items: [] } }], nextId: null
 } }));
 await page.route('**/collection-daily-history*', route => route.fulfill({ json: {
   items: [{ id: 2, dayUtc: '2026-01-01T00:00:00Z', result: 'denied', attemptCount: 3, firstStartedUtc: '2026-01-01T01:00:00Z', lastStartedUtc: '2026-01-01T23:00:00Z',
    durationKnownCount: 2, durationMinMs: 2000, durationMaxMs: 20000, coverage: { known: true, items: [{ name: 'pools', status: 'denied', expectedSeconds: 60 }] } }],
   nextId: null, nextDay: null, detailDays: 90, summaryYears: 2, backupConfigured: false, pendingAttempts: 12
 } }));
 let datasetMode: 'valid' | 'failed' | 'empty' = 'valid';
 const datasetRows = Array.from({length:22}, (_,i) => ({name:`tank/photos-${i.toString().padStart(2,'0')}`,pool:'tank',type:'FILESYSTEM',
   usedBytes:i === 0 ? 0 : 1073741824,availableBytes:null,snapshotUsedBytes:0,quotaBytes:0,refQuotaBytes:null}));
 await page.route('**/datasets', route => datasetMode === 'failed' ? route.fulfill({status:503,json:{error:'unavailable'}}) : route.fulfill({json:{
   enabled:true,status:'supported',validUntilUtc:'2026-01-01T12:03:00Z',snapshot:{fetchedUtc:'2026-01-01T12:00:00Z',sourceSchema:'truenas-24.10-datasets-v1',items:datasetMode === 'empty' ? [] : datasetRows}
 }}));
 await page.getByRole('button',{name:'View dataset usage',exact:true}).click();
 const datasets = page.getByRole('region',{name:'Dataset space usage',exact:true});
 await expect(datasets.getByRole('status')).toHaveText('Showing 20 of 22 matching entries · 22 saved');
 await expect(datasets.getByText('Last-known usage',{exact:true})).toBeVisible();
 await datasets.getByRole('button',{name:'Show more datasets'}).click();
 await expect(datasets.getByRole('status')).toHaveText('Showing 22 of 22 matching entries · 22 saved');
 await datasets.getByLabel('Find a dataset or volume').fill('photos-00');
 await expect(datasets.getByRole('heading',{name:'tank/photos-00',exact:true})).toBeVisible();
 await expect(datasets.getByRole('heading',{name:'tank/photos-01',exact:true})).toHaveCount(0);
 await expect(datasets.getByText('Unknown',{exact:true}).filter({visible:true})).toHaveCount(1);
 await datasets.getByText('Quota details',{exact:true}).focus(); await page.keyboard.press('Enter');
 await expect(datasets.getByText('No quota set',{exact:true})).toBeVisible();
 await expect(datasets.getByText('Unknown',{exact:true}).filter({visible:true})).toHaveCount(2);
 await audit('Dataset usage');
 await datasets.screenshot({path:'test-results/dataset-usage-mobile.png'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 datasetMode = 'failed'; await datasets.getByRole('button',{name:'Refresh dataset view'}).click();
 await expect(datasets.getByRole('alert')).toContainText('Could not refresh dataset usage');
 await expect(datasets.getByRole('heading',{name:'tank/photos-00',exact:true})).toBeVisible();
 datasetMode = 'empty'; await datasets.getByRole('button',{name:'Refresh dataset view'}).click();
 await expect(datasets.getByText('The API returned no visible datasets.',{exact:false})).toBeVisible();
 let datasetHistoryFails=true;
 const datasetHistorySample={id:12,configurationRevision:'fixture-context',fetchedUtc:'2026-01-01T12:00:00Z',systemSerial:'FIXTURE-NAS',version:'24.10.2',snapshot:{fetchedUtc:'2026-01-01T12:00:00Z',sourceSchema:'truenas-24.10-datasets-v1',items:datasetRows}};
 await page.route('**/dataset-history?*',route=>{
  const query=new URL(route.request().url()).searchParams;
  if(query.has('before')){expect(query.get('snapshotId')).toBe('12');expect(query.get('before')).toBe('11');if(datasetHistoryFails){datasetHistoryFails=false;return route.fulfill({status:503,json:{error:'fixture outage'}});}}
  return route.fulfill({json:{snapshotId:12,items:query.has('before')?[{...datasetHistorySample,id:1,fetchedUtc:'2025-12-31T12:00:00Z',snapshot:{...datasetHistorySample.snapshot,fetchedUtc:'2025-12-31T12:00:00Z',items:[{...datasetRows[0],name:'tank/older-name'}]}}]:[datasetHistorySample,{...datasetHistorySample,id:11,snapshot:{...datasetHistorySample.snapshot,items:[]}}],nextBefore:query.has('before')?null:11,retentionDays:90,sampling:'First successful inventory per UTC hour and connection revision.'}});
 });
 await datasets.getByRole('button',{name:'Browse sampled dataset history',exact:true}).click();
 const datasetHistory=page.getByRole('region',{name:'Sampled dataset inventory history',exact:true});
 await expect(datasetHistory.getByRole('status')).toContainText('2 saved inventories');
 await datasetHistory.getByLabel('Find a name in this saved inventory').fill('photos-00');
 await expect(datasetHistory.getByRole('heading',{name:'tank/photos-00',exact:true})).toBeVisible();
 await expect(datasetHistory.getByText('Unknown',{exact:true}).filter({visible:true})).toHaveCount(1);
 await datasetHistory.getByText('Historical quota details',{exact:true}).focus();await page.keyboard.press('Enter');
 await expect(datasetHistory.getByText('No quota set',{exact:true})).toBeVisible();
 await datasetHistory.getByLabel('Saved inventory',{exact:true}).selectOption('1');
 await expect(datasetHistory.getByText('This successful sample contained no visible datasets.',{exact:false})).toBeVisible();
 await datasetHistory.getByLabel('Saved inventory',{exact:true}).selectOption('0');
 await datasetHistory.getByRole('button',{name:'Older dataset samples',exact:true}).click();
 await expect(datasetHistory.getByRole('alert')).toContainText('Previously loaded history remains visible');
 await expect(datasetHistory.getByRole('heading',{name:'tank/photos-00',exact:true})).toBeVisible();
 await datasetHistory.getByRole('button',{name:'Older dataset samples',exact:true}).click();
 await datasetHistory.getByRole('button',{name:'Clear history filter',exact:true}).click();
 await expect(datasetHistory.getByRole('heading',{name:'tank/older-name',exact:true})).toBeVisible();
 await expect(datasetHistory.getByRole('button',{name:'Older dataset samples',exact:true})).toHaveCount(0);
 await datasetHistory.getByText('Recorded source context',{exact:true}).focus();await page.keyboard.press('Enter');
 await expect(datasetHistory.getByText('System identity: FIXTURE-NAS',{exact:false})).toBeVisible();
 await audit('Sampled dataset history');
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await datasetHistory.screenshot({path:'test-results/dataset-history-mobile.png',animations:'disabled'});

 let renameRecord:any=null;let renameConflict=true;
 const earlierRename={...datasetHistorySample,id:1,fetchedUtc:'2025-12-31T12:00:00Z',snapshot:{...datasetHistorySample.snapshot,items:[{...datasetRows[0],name:'tank/older-name'}]}};
 await page.route('**/dataset-renames**',async route=>{
  const url=new URL(route.request().url());
  if(url.pathname.endsWith('/review'))return route.fulfill({json:{reviewToken:'fixture-review-token',from:earlierRename,to:datasetHistorySample,warning:'Local evidence only'}});
  if(route.request().method()==='POST'){
   const data=route.request().postDataJSON();
   if(url.pathname.endsWith('/reverse')){renameRecord={...renameRecord,reversedUtc:'2026-01-02T12:00:00Z',reversalEvidence:data.evidence,reversedBy:'owner'};return route.fulfill({json:renameRecord});}
   if(renameConflict){renameConflict=false;return route.fulfill({status:409,json:{error:'The reviewed inventories changed. Reload both before submitting.'}});}
   expect(data.confirmedSameDataset).toBe(true);expect(data.fromSampleId).toBe(1);expect(data.toSampleId).toBe(12);
   renameRecord={...data,id:1,author:'owner',recordedUtc:'2026-01-02T11:00:00Z',reversedUtc:null,reversalEvidence:null,reversedBy:null};return route.fulfill({json:renameRecord});
  }
  return route.fulfill({json:{items:renameRecord?[renameRecord]:[],nextBefore:null}});
 });
 await datasetHistory.getByRole('button',{name:'Use this as earlier inventory',exact:true}).click();
 await datasetHistory.getByRole('button',{name:'Refresh latest samples',exact:true}).click();
 await datasetHistory.getByRole('button',{name:'Review rename to this inventory',exact:true}).click();
 const renameDialog=page.getByRole('dialog');
 await renameDialog.getByLabel('Earlier dataset name',{exact:true}).selectOption('tank/older-name');
 await renameDialog.getByLabel('Later dataset name',{exact:true}).selectOption('tank/photos-00');
 await renameDialog.getByLabel('Type the earlier name exactly').fill('tank/older-name');
 await renameDialog.getByLabel('Type the later name exactly').fill('tank/photos-00');
 await renameDialog.getByLabel('I independently verified these names refer to the same dataset.').check();
 await renameDialog.getByLabel('Rename evidence / reference').fill('Independent change log confirms rename');
 await renameDialog.getByRole('button',{name:'Record rename review',exact:true}).click();
 await expect(renameDialog.getByRole('alert')).toContainText('inventories changed');
 await expect(renameDialog.getByLabel('Rename evidence / reference')).toHaveValue('Independent change log confirms rename');
 await renameDialog.getByRole('button',{name:'Reload reviewed inventories',exact:true}).click();
 await expect(renameDialog.getByLabel('Earlier dataset name',{exact:true})).toHaveValue('');
 await expect(renameDialog.getByLabel('Type the earlier name exactly')).toHaveValue('');
 await expect(renameDialog.getByLabel('I independently verified these names refer to the same dataset.')).not.toBeChecked();
 await expect(renameDialog.getByLabel('Rename evidence / reference')).toHaveValue('Independent change log confirms rename');
 await renameDialog.getByLabel('Earlier dataset name',{exact:true}).selectOption('tank/older-name');
 await renameDialog.getByLabel('Later dataset name',{exact:true}).selectOption('tank/photos-00');
 await renameDialog.getByLabel('Type the earlier name exactly').fill('tank/older-name');
 await renameDialog.getByLabel('Type the later name exactly').fill('tank/photos-00');
 await renameDialog.getByLabel('I independently verified these names refer to the same dataset.').check();
 await audit('Dataset rename review');
 await renameDialog.screenshot({path:'test-results/dataset-rename-mobile.png',animations:'disabled'});
 expect(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth)).toBeTruthy();
 await renameDialog.getByRole('button',{name:'Record rename review',exact:true}).click();
 await expect(datasetHistory.getByText('Operator-reviewed rename',{exact:false})).toBeVisible();
 await datasetHistory.getByRole('button',{name:'Reverse this rename review',exact:true}).click();
 await renameDialog.getByLabel('Reason for reversing this review').fill('Independent review found an incorrect link');
 await audit('Dataset rename reversal');
 await renameDialog.getByRole('button',{name:'Record review reversal',exact:true}).click();
 await expect(datasetHistory.getByText('Reversed review',{exact:false})).toBeVisible();
 await expect(datasetHistory.getByText('Independent change log confirms rename',{exact:true})).toBeVisible();
 await page.unroute('**/dataset-renames**');
 await page.unroute('**/dataset-history?*');
 await datasets.getByRole('button',{name:'Hide sampled dataset history',exact:true}).click();

 await expect(datasets.getByRole('alert')).toHaveCount(0);
 await expect(datasets.getByRole('heading',{name:'tank/photos-00',exact:true})).toHaveCount(0);
 await page.getByRole('button',{name:'Hide dataset usage',exact:true}).click();
 let temperatureCacheEmpty = false;
 await page.route('**/cached-temperatures', route => route.fulfill({ json: {
   enabled: true, status: 'supported', lastAttemptUtc: '2026-01-01T12:00:00Z', lastSuccessUtc: '2026-01-01T12:00:00Z',
   snapshot: { fetchedUtc: '2026-01-01T12:00:00Z', sourceSchema: 'truenas-24.10-temperature-cache-v1', readings: temperatureCacheEmpty ? [] : [
     { deviceName: 'sda', celsius: 32, missingReason: null }, { deviceName: 'sdb', celsius: null, missingReason: 'No temperature in the cache.' }
   ] }
 } }));
 await page.getByRole('button', { name: 'View cached temperatures' }).click();
 const temperatures = page.getByRole('region', { name: 'Cached drive temperatures' });
 await expect(temperatures.getByText('32 °C', { exact: true })).toBeVisible();
 await expect(temperatures.getByText('Unknown temperature', { exact: true })).toBeVisible();
 await expect(temperatures.getByText('Physical drive match unverified', { exact: true })).toHaveCount(2);
 await page.screenshot({ path: 'test-results/cached-temperatures-mobile.png', fullPage: true, animations: 'disabled' });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 temperatureCacheEmpty = true;
 await temperatures.getByRole('button', { name: 'Refresh cache view' }).click();
 await expect(temperatures.getByText('The returned cache was empty.', { exact: false })).toBeVisible();
 await expect(temperatures.getByText('32 °C', { exact: true })).toHaveCount(0);
 await page.getByRole('button', { name: 'Hide cached temperatures' }).click();
 await page.getByRole('button', { name: 'View collection history' }).click();
 const collection = page.getByRole('region', { name: 'Collection attempt history' });
 await expect(collection.getByText('Attempt duration: Unknown')).toBeVisible();
 await collection.getByRole('button', { name: 'Recent attempts', exact: true }).click();
 await expect(collection.getByText('Attempt duration: Unknown')).toBeVisible();
 await collection.getByText('Recorded capability states', { exact: true }).click();
 await expect(collection.getByText('Capability evidence is unknown', { exact: false })).toBeVisible();
 await collection.getByRole('button', { name: 'Daily attempt summaries' }).click();
 await expect(collection.getByText('3 attempts · Duration: 2 s – 20 s')).toBeVisible();
 await expect(collection.getByRole('button', { name: 'Daily attempt summaries' })).toHaveAttribute('aria-pressed', 'true');
 await expect(collection.getByRole('button', { name: 'Recent attempts', exact: true })).toHaveAttribute('aria-pressed', 'false');
 await collection.getByText('Recorded capability states', { exact: true }).focus();
 await page.keyboard.press('Enter');
 await expect(collection.getByText('expected interval 60 s', { exact: false })).toBeVisible();
 await page.screenshot({ path: 'test-results/collection-history-mobile.png', fullPage: true, animations: 'disabled' });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.getByRole('button', { name: 'Hide collection history' }).click();
 const capacity = page.getByRole('region', { name: 'Pool capacity planning' });
 await expect(capacity.getByText('About 40 days', { exact: true })).toBeVisible();
 await expect(capacity.getByText('1.07 GB / day', { exact: true })).toBeVisible();
 await capacity.getByText('Evidence and assumptions', { exact: true }).focus();
 await page.keyboard.press('Enter');
 await expect(capacity.getByText('Linear fit: 0.95.', { exact: false })).toBeVisible();
 await capacity.getByLabel('Capacity growth window').selectOption('7');
 await expect(capacity.getByText('Estimate withheld', { exact: true })).toBeVisible();
 await expect(capacity.getByText('No estimate', { exact: true })).toBeVisible();
 await capacity.scrollIntoViewIfNeeded();
 await audit('Mobile storage capacity');
 await capacity.screenshot({path:'test-results/capacity-mobile.png'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.getByRole('button', { name: 'View capacity & scan history' }).click();
 const history = page.getByRole('region', { name: 'Pool snapshot history' });
 await expect(history.getByRole('cell', { name: 'Unknown', exact: true })).toHaveCount(4);
 await expect(history.getByRole('cell', { name: '0', exact: true })).toBeVisible();
 await expect(history.getByText('Status code: CORRUPT_DATA',{exact:true})).toBeVisible();
 await page.getByRole('button', {name:'Older snapshots',exact:true}).click();
 await expect.poll(() => delayedPool !== undefined).toBe(true);
 await expect(page.getByText('Latest saved snapshot:',{exact:false})).toHaveCount(0);
 await expect(page.getByRole('region',{name:'Pool snapshot history'})).toHaveCount(0);
 await expect(page.getByText('Loading capacity history…',{exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Back to latest',exact:true}).click();
 await expect(history.getByRole('cell',{name:'ONLINE Status code: CORRUPT_DATA',exact:true})).toBeVisible();
 await delayedPool!.fulfill({json:{items:[{...poolPage.items[0],status:'OBSOLETE HISTORY'}],nextBefore:null}}).catch(() => {});
 await expect(history.getByRole('cell',{name:'OBSOLETE HISTORY',exact:true})).toHaveCount(0);
 await page.screenshot({ path: 'test-results/storage-history-mobile.png', fullPage: true });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.route('**/pools/pool-one/scans*', route => route.fulfill({json:{items:[{
  id:1,scanFunction:'SCRUB',scanState:'FINISHED',scanStartedUtc:'2026-01-01T01:00:00Z',scanEndedUtc:'2026-01-01T03:00:00Z',
  scanPausedUtc:null,scanRemainingSeconds:null,scanErrors:0,scanErrorsPeak:2,scanPercentage:100,firstFetchedUtc:'2026-01-01T01:05:00Z',
  lastFetchedUtc:'2026-01-01T03:05:00Z',sourceSchema:'fixture'
 }],nextBefore:null}}));
 await page.getByRole('button',{name:'View scrub & resilver history',exact:true}).click();
 const scans = page.getByRole('region',{name:'Recorded scan history',exact:true});
 await expect(scans.getByRole('heading',{name:'SCRUB · FINISHED',exact:true})).toBeVisible();
 await expect(scans.getByText('Last reported progress: 100.0% · Scan errors: 0',{exact:true})).toBeVisible();
 await expect(scans.getByText('Highest retained reported errors: 2',{exact:true})).toBeVisible();
 await expect(scans.getByText('Earlier errors remain evidence',{exact:false})).toBeVisible();
 await scans.getByText('Source scan timing',{exact:true}).focus(); await page.keyboard.press('Enter');
 await audit('Recorded scan history');
 await scans.screenshot({path:'test-results/scan-history-mobile.png'});
 await page.getByRole('button',{name:'Hide scan history',exact:true}).click();
 await page.route('**/pools/pool-one/daily-history*', route => route.fulfill({ json: {
   items: [{ id: 1, vdevEvidenceJson: JSON.stringify({schema:'truenas-24.10-vdev-daily-v1',status:'reported',explanation:'Ranges are not error deltas.',samples:3,nodes:[{node:member,read:{knownCount:2,min:0,max:9},write:{knownCount:0,min:null,max:null},checksum:{knownCount:3,min:7,max:7}}]}), dayUtc: '2026-01-01T00:00:00Z', status: 'ONLINE', statusCode: 'CORRUPT_DATA', sourceSchema: 'fixture-v1', scanFunction: 'SCRUB', scanState: 'FINISHED', sampleCount: 3,
     firstFetchedUtc: '2026-01-01T01:00:00Z', lastFetchedUtc: '2026-01-01T23:00:00Z', freeBytesMin: 1073741824, freeBytesMax: 2147483648, freeBytesKnownCount: 2,
     allocatedBytesMin: null, allocatedBytesMax: null, allocatedBytesKnownCount: 0, sizeBytesMin: null, sizeBytesMax: null, sizeBytesKnownCount: 0,
     scanErrorsMax: 9, scanErrorsKnownCount: 2, scanPercentageMin: null, scanPercentageMax: null, scanPercentageKnownCount: 0 }],
   nextDay: null, nextId: null, detailDays: 90, summaryYears: 2, backupConfigured: false, pendingSnapshots: 12
 } }));
 await page.getByRole('button', { name: 'View older daily summaries' }).click();
 const daily = page.getByRole('region', { name: 'Daily pool summaries', exact: true });
 await expect(daily.getByText('History cleanup is paused', { exact: false })).toBeVisible();
 await expect(daily.getByText('1.07 GB – 2.15 GB', { exact: false })).toBeVisible();
 await expect(daily.getByText('3 unknown readings')).toBeVisible();
 await expect(daily.getByText('Status code: CORRUPT_DATA',{exact:true})).toBeVisible();
 await expect(daily.getByText('Historical corruption report.',{exact:false})).toBeVisible();
 await daily.getByText('Member error history',{exact:true}).click();
 await expect(daily.getByText('2 known · 1 unknown readings',{exact:true})).toBeVisible();
 await audit('Daily integrity history');
 await daily.getByText('Reading details', { exact: true }).focus();
 await page.keyboard.press('Enter');
 await expect(daily.getByText('Source: fixture-v1.', { exact: false })).toBeVisible();
 await page.screenshot({ path: 'test-results/daily-history-mobile.png', fullPage: true });
 const dailyViewport = page.viewportSize()!;
 await page.setViewportSize({ width: 1440, height: 1000 });
 await page.screenshot({ path: 'test-results/daily-history-desktop.png', fullPage: true });
 await page.setViewportSize(dailyViewport);
 await expect.poll(() => page.evaluate(() => document.documentElement.scrollWidth - window.innerWidth)).toBeLessThanOrEqual(0);
 await page.getByRole('button', { name: 'Hide daily summaries' }).click();
 await page.getByRole('button', { name: 'Hide history' }).click();
 await expect(history).not.toBeVisible();
 await page.clock.install();
 let planningMode = 'ready';
 await page.route('**/api/v1/planning/review', route => {
  if (planningMode === 'hung') return;
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
 await page.getByLabel('Compatibility assessment',{exact:true}).selectOption('compatible');
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
 let maintenanceRecords: Record<string,unknown>[] = []; let maintenanceUnavailable = false;
 await page.route('**/api/v1/disks/*/maintenance*', route => {
  const url = new URL(route.request().url()); const diskId = url.pathname.split('/')[4];
  if (route.request().method() === 'GET') return maintenanceUnavailable ? route.fulfill({status:503,json:{error:'fixture outage'}}) : route.fulfill({json:{items:maintenanceRecords.filter(r => r.diskId === diskId),nextBefore:null}});
  const input = route.request().postDataJSON();
  if (input.confirmedIdentity !== 'OLD-FIXTURE') return route.fulfill({status:400,json:{error:'Confirm the exact drive identity.'}});
  const row = {...input,id:1,diskId,author:'fixture-owner',recordedUtc:new Date().toISOString(),voidedUtc:null,voidReason:null,voidedBy:null};
  maintenanceRecords = [row]; return route.fulfill({json:row});
 });
 await page.route('**/api/v1/disks/*/maintenance/*/void', route => {
  Object.assign(maintenanceRecords[0],{voidedUtc:new Date().toISOString(),voidReason:route.request().postDataJSON().reason,voidedBy:'fixture-owner'});
  return route.fulfill({json:maintenanceRecords[0]});
 });

 let acquisitionRecord: Record<string, unknown> | null = null;
 let acquisitionConflict = true;
 await page.route('**/api/v1/disks/*/acquisition', route => {
  if (route.request().method() === 'GET') return route.fulfill({json:{record:route.request().url().includes(oldId) ? acquisitionRecord : null}});
  if (acquisitionConflict) { acquisitionConflict = false; return route.fulfill({status:409,json:{error:'These acquisition notes changed. Your draft was not saved.'}}); }
  const input = route.request().postDataJSON();
  acquisitionRecord = {...input,diskId:oldId,revision:crypto.randomUUID(),author:'fixture-owner',recordedUtc:new Date().toISOString()};
  return route.fulfill({json:acquisitionRecord});
 });
 const newId = '44444444-4444-4444-4444-444444444444';
 const drive = (id: string, serial: string) => ({ id, connectionId, lastSeenUtc: '2026-10-31T12:00:00Z', model: 'Fixture replacement drive', serial, wwn: null, mappingConfidence: 'inferred', alias: 'fixture', bay: null, bayVerified: false, bayEvidence: null, present: id === newId, purchasedUtc: null, purchaseDateConfidence: 'unknown', warrantyEndUtc: null, notes: '', lifecycle: 'installed' });
 const oldDrive = drive(oldId, 'OLD-FIXTURE'); const newDrive = drive(newId, 'NEW-FIXTURE');
 const oldEvidence = {id:1,value:3,unit:'sectors',observedUtc:'2026-01-01T12:00:00Z',receivedUtc:'2026-10-31T12:00:00Z',source:'Historical fixture report',sourceSchema:'fixture-v1',quality:'observed',missingReason:null,recordedBy:'fixture-owner'};
 await page.route('**/api/v1/disks/*/evidence-coverage', route => route.fulfill({json:{evaluatedUtc:'2026-11-01T12:00:00Z',staleAfterMinutes:270,items:[
  {metric:'ata.pending_sectors',label:'ATA pending sectors',freshness:'stale',explanation:'Old measurement; uploading it did not renew its observation time.',latestSubmission:oldEvidence,latestObserved:oldEvidence},
  {metric:'temperature',label:'Temperature',freshness:'unknown',explanation:'No valid observed measurement is available.',latestSubmission:null,latestObserved:null},
  {metric:'ata.crc_errors',label:'ATA transport CRC errors',freshness:'unverified',explanation:'Undated readings do not establish freshness.',latestSubmission:{...oldEvidence,id:2,value:4,unit:'errors',observedUtc:null,quality:'unverified'},latestObserved:null},
  {metric:'smart.overall_failed',label:'Overall SMART assessment',freshness:'stale',explanation:'A pass does not establish repair.',latestSubmission:{...oldEvidence,value:1,unit:'flag'},latestObserved:{...oldEvidence,value:1,unit:'flag'}}
 ]}}));

 await page.route('**/api/v1/overview', async route => { const response = await route.fetch(); await route.fulfill({ json: { ...await response.json(), disks: [oldDrive, newDrive] } }); });
 for (const disk of [oldDrive, newDrive]) {
  await page.route(`**/api/v1/disks/${disk.id}`, route => route.fulfill({ json: { disk, aliases: [], findings: [], plans: [] } }));
  await page.route(`**/api/v1/disks/${disk.id}/observations`, route => route.fulfill({ json: [] }));
  await page.route(`**/api/v1/disks/${disk.id}/observation-history?*`, route => route.fulfill({json:{items:[],asOfUtc:new Date().toISOString(),snapshotId:0,nextBefore:null}}));
 }
 let replacementRecords: Record<string, unknown>[] = [];
 await page.route('**/api/v1/disks/*/replacements', route => route.fulfill({ json: replacementRecords }));
 await page.route('**/api/v1/replacements', route => {
  const input = route.request().postDataJSON();
  replacementRecords = [{ ...input, id: 'replacement-fixture', oldIdentity: 'OLD-FIXTURE', newIdentity: 'NEW-FIXTURE', author: 'fixture-owner', recordedUtc: '2026-10-31T12:00:00Z', voidedUtc: null, voidReason: null, voidedBy: null }];
  return route.fulfill({ json: replacementRecords[0] });
 });
 await page.route('**/api/v1/replacements/replacement-fixture/void', route => {
  replacementRecords[0] = { ...replacementRecords[0], voidedUtc: '2026-10-31T13:00:00Z', voidReason: route.request().postDataJSON().reason, voidedBy: 'fixture-owner' };
  return route.fulfill({ json: replacementRecords[0] });
 });
 Object.assign(oldDrive,{mappingConfidence:'ambiguous',bay:'Bay 7',bayVerified:true,bayEvidence:'Historical physical label check'});
 await page.getByRole('button', { name: 'Refresh', exact: true }).click();
 await page.getByRole('link', { name: 'Storage', exact: true }).click();
 await expect(page.getByText('Bay needs identity review',{exact:true})).toBeVisible();
 await page.getByRole('button', { name: 'View drive', exact: true }).first().click();
 await expect(page.getByText('Bay needs identity review',{exact:true})).toBeVisible();
 await expect(page.getByText('Verified bay: Bay 7',{exact:true})).toHaveCount(0);
 await audit('Ambiguous bay mapping');
 Object.assign(oldDrive,{mappingConfidence:'inferred',bay:null,bayVerified:false,bayEvidence:null});
 await page.getByRole('button',{name:'Refresh',exact:true}).click();
 await expect(page.getByText('Bay unverified',{exact:true})).toBeVisible();
 const acquisitionRegion = page.getByRole('region',{name:'Drive acquisition notes'});
 await expect(acquisitionRegion.getByText('Purchase cost: Unknown',{exact:true})).toBeVisible();
 await acquisitionRegion.getByRole('button',{name:'Edit purchase & installation',exact:true}).click();
 const acquisitionDialog = page.getByRole('dialog');
 await acquisitionDialog.getByLabel('Installation date (optional)').fill('2025-01-01');
 await acquisitionDialog.getByLabel('Installation-date confidence').selectOption('estimated');
 await acquisitionDialog.getByLabel('Installation evidence or estimate basis').fill('Owner installation log');
 await acquisitionDialog.getByLabel('Original purchase cost (optional)').fill('0');
 await acquisitionDialog.getByLabel('Purchase currency (three letters)').fill('USD');
 await acquisitionDialog.getByLabel('Purchase-cost confidence').selectOption('documented');
 await acquisitionDialog.getByLabel('Receipt reference or purchase evidence').fill('Gift receipt reference');
 await acquisitionDialog.getByRole('button',{name:'Save acquisition notes',exact:true}).click();
 await expect(acquisitionDialog.getByRole('alert')).toContainText('Your draft was not saved');
 await expect(acquisitionDialog.getByLabel('Receipt reference or purchase evidence')).toHaveValue('Gift receipt reference');
 const acquisitionViewport = page.viewportSize()!;
 await page.setViewportSize({width:390,height:844});
 await audit('Drive acquisition conflict');
 await page.screenshot({path:'test-results/drive-acquisition-mobile.png',animations:'disabled'});
 await page.setViewportSize(acquisitionViewport);
 await acquisitionDialog.getByRole('button',{name:'Reload saved notes & discard draft',exact:true}).click();
 await acquisitionRegion.getByRole('button',{name:'Edit purchase & installation',exact:true}).click();
 await expect(acquisitionDialog.getByLabel('Receipt reference or purchase evidence')).toHaveValue('');
 await acquisitionDialog.getByLabel('Original purchase cost (optional)').fill('0');
 await acquisitionDialog.getByLabel('Purchase currency (three letters)').fill('USD');
 await acquisitionDialog.getByLabel('Purchase-cost confidence').selectOption('documented');
 await acquisitionDialog.getByLabel('Receipt reference or purchase evidence').fill('Gift receipt reference');
 await acquisitionDialog.getByRole('button',{name:'Save acquisition notes',exact:true}).click();
 await expect(acquisitionRegion.getByText('Purchase cost: 0 USD (documented)',{exact:true})).toBeVisible();
 await expect(acquisitionRegion.getByText('Installed: Unknown',{exact:true})).toBeVisible();
 await acquisitionRegion.getByRole('button',{name:'Refresh acquisition notes',exact:true}).click();
 await expect(acquisitionRegion.getByText('Purchase cost: 0 USD (documented)',{exact:true})).toBeVisible();
 await audit('Drive acquisition saved');

 const driveCoverage = page.getByRole('region',{name:'Drive evidence freshness'});
 await expect(driveCoverage).toHaveCount(1);
 await expect(driveCoverage.getByText('3 metrics with submitted evidence',{exact:true})).toBeVisible();
 await expect(driveCoverage.getByRole('heading',{name:'Temperature',exact:true})).toHaveCount(0);
 const missingToggle = driveCoverage.getByRole('button',{name:'Show 1 metric without submissions',exact:true});
 await missingToggle.focus(); await page.keyboard.press('Enter');
 await expect(driveCoverage.getByRole('heading',{name:'Temperature',exact:true})).toBeVisible();
 await expect(driveCoverage.getByRole('button',{name:'Hide metrics without submissions',exact:true})).toHaveAttribute('aria-expanded','true');
 await audit('Expanded evidence coverage');
 await driveCoverage.getByRole('button',{name:'Hide metrics without submissions',exact:true}).click();
 await expect(driveCoverage.getByRole('heading',{name:'Temperature',exact:true})).toHaveCount(0);
 await expect(driveCoverage.getByText('stale',{exact:true})).toHaveCount(2);
 await expect(driveCoverage.getByText('Reported failed',{exact:true})).toBeVisible();
 await expect(driveCoverage.getByText('3 sectors',{exact:true})).toBeVisible();
 await expect(driveCoverage.getByText('unverified',{exact:true})).toBeVisible();
 await driveCoverage.getByText('Evidence & limitations',{exact:true}).first().focus();
 await page.keyboard.press('Enter');
 await expect(driveCoverage.getByText('Old measurement; uploading it did not renew its observation time.',{exact:true})).toBeVisible();
 const evidenceViewport = page.viewportSize()!;
 await page.setViewportSize({width:390,height:844});
 await driveCoverage.screenshot({path:'test-results/drive-evidence-mobile.png'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.setViewportSize(evidenceViewport);
 await page.getByRole('button', { name: 'Record a past replacement', exact: true }).click();
 await page.getByLabel('New physical drive').selectOption(newId);
 await page.getByLabel('Retype previous serial / WWN').fill('OLD-FIXTURE');
 await page.getByLabel('Retype new serial / WWN').fill('NEW-FIXTURE');
 await page.getByLabel('Replacement time (browser timezone)').fill('2026-01-01T12:00');
 await page.getByLabel('Pool / vdev membership evidence').fill('Pool tank, data vdev 1, maintenance log');
 await page.getByLabel('Reason for replacement').fill('Rising media observations');
 await page.getByLabel('Operator evidence / reference').fill('Operator log and receipt');
 await page.setViewportSize({ width: 390, height: 844 });
 await page.getByRole('dialog').evaluate(element => element.scrollTop = 0);
 await expect(page.getByRole('dialog')).toBeVisible();
 await expect.poll(async () => (await page.getByRole('dialog').boundingBox())!.height).toBeLessThanOrEqual(844);
 await expect.poll(async () => (await page.getByRole('dialog').boundingBox())!.width).toBeLessThanOrEqual(390);
 await page.screenshot({ path: 'test-results/replacement-dialog-mobile.png' });
 await page.getByRole('button', { name: 'Save replacement history', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'OLD-FIXTURE → NEW-FIXTURE', exact: true })).toBeVisible();
 expect(replacementRecords[0]).toMatchObject({ oldDiskId: oldId, newDiskId: newId, confirmedOldIdentity: 'OLD-FIXTURE', confirmedNewIdentity: 'NEW-FIXTURE' });
 await page.getByRole('button', { name: 'View new drive', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Fixture replacement drive · NEW-FIXTURE', exact: true })).toBeVisible();
 await page.getByRole('button', { name: 'Mark record incorrect', exact: true }).click();
 await page.getByLabel('Correction reason').fill('Wrong membership noted; retain original evidence');
 await page.getByRole('button', { name: 'Save correction', exact: true }).click();
 await expect(page.getByText('Marked incorrect', { exact: true })).toBeVisible();
 await page.getByRole('heading', { name: 'Replacement history', exact: true }).scrollIntoViewIfNeeded();
 await page.screenshot({ path: 'test-results/replacement-history-mobile.png' });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await expect(page).toHaveURL(new RegExp(`/storage\\?disk=${newId}$`));
 await page.reload();
 await expect(page.getByRole('heading', { name: 'Fixture replacement drive · NEW-FIXTURE', exact: true })).toBeVisible();
 await page.goBack();
 await expect(page).toHaveURL(new RegExp(`/storage\\?disk=${oldId}$`));
 await expect(page.getByRole('heading', { name: 'Fixture replacement drive · OLD-FIXTURE', exact: true })).toBeVisible();
 await page.goForward();
 await expect(page.getByRole('heading', { name: 'Fixture replacement drive · NEW-FIXTURE', exact: true })).toBeVisible();
 newDrive.model = 'Updated fixture model';
 await page.getByRole('button', { name: 'Refresh', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Updated fixture model · NEW-FIXTURE', exact: true })).toBeVisible();
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
 await page.getByRole('link', { name: 'View drive evidence', exact: true }).click();
 await expect(page.getByRole('heading', { name: 'Fixture replacement drive · OLD-FIXTURE', exact: true })).toBeVisible();
 await page.getByRole('button', { name: 'Sign out', exact: true }).click();
 await page.getByLabel('Username', { exact: true }).fill('fixture-owner');
 await page.getByLabel('Password', { exact: true }).fill('fixture-password-long');
 await page.getByRole('button', { name: 'Sign in', exact: true }).click();
 await expect(page).toHaveURL(new RegExp(`/storage\\?disk=${oldId}$`));
 await expect(page.getByRole('heading', { name: 'Fixture replacement drive · OLD-FIXTURE', exact: true })).toBeVisible();
 expect((await page.request.get('/api/v1/not-a-real-endpoint')).status()).toBe(404);
 await page.goto('/storage?disk=not-a-real-drive');
 await expect(page.getByRole('alert')).toContainText('This drive could not be found');
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
 await expect(page.getByRole('heading', { name: 'Your storage, at a glance.', exact: true })).toBeVisible();
 const identityConnection = { id: connectionId, configurationRevision:'00000000-0000-0000-0000-000000000001', name: 'Identity fixture NAS', apiUri: 'wss://nas.fixture/websocket', connectHost: null, connectPort: null, pollSeconds: 60, enabled: false, trustedCaPem: null, certificateSha256: null, status: 'System identity changed or missing — collection paused', systemSerial: 'NAS-A', observedSystemSerial: 'NAS-B', systemIdentityState: 'changed', systemIdentityCheckedUtc: '2026-10-31T12:00:00Z' };
 await page.route('**/api/v1/connections', route => route.fulfill({ json: [identityConnection] }));
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
 await page.getByRole('button', { name: 'Edit connection', exact: true }).click();
 await page.getByLabel('Retype saved system serial').fill('wrong');
 await page.getByLabel('Retype newly reported system serial').fill('NAS-B');
 await page.getByLabel('Independent verification evidence').fill('Motherboard replaced; physical label, endpoint and certificate verified');
 await page.getByRole('button', { name: 'Accept identity baseline & resume', exact: true }).click();
 await expect(page.getByRole('alert')).toContainText('Confirm the exact saved and newly reported serials.');
 await page.getByLabel('Retype saved system serial').fill('NAS-A');
 identityConnection.configurationRevision = '00000000-0000-0000-0000-000000000004';
 await page.getByRole('button',{name:'Accept identity baseline & resume',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('This connection changed since the identity review was loaded');
 expect(acceptedIdentity).toBeNull();
 await expect(page.getByRole('alert')).toBeFocused();
 await page.getByRole('button',{name:'Reload saved connection',exact:true}).click();
 await expect(page.getByLabel('Retype saved system serial')).toHaveValue('');
 await page.getByLabel('Retype saved system serial').fill('NAS-A');
 await page.getByLabel('Retype newly reported system serial').fill('NAS-B');
 await page.getByLabel('Independent verification evidence').fill('Motherboard replaced; physical label, endpoint and certificate verified');

 await page.getByRole('heading', { name: 'Review a changed system identity', exact: true }).scrollIntoViewIfNeeded();
 await expect.poll(async () => (await page.getByRole('dialog').boundingBox())!.width).toBeLessThanOrEqual(358);
 await page.screenshot({ path: 'test-results/identity-review-mobile.png' });
 await page.getByRole('button', { name: 'Accept identity baseline & resume', exact: true }).click();
 await expect(page.getByText('System identity: awaiting validation', { exact: true })).toBeVisible();
 expect(acceptedIdentity).toMatchObject({ previousSerial: 'NAS-A', newSerial: 'NAS-B', evidence: 'Motherboard replaced; physical label, endpoint and certificate verified' });
 await page.route(`**/api/v1/connections/${connectionId}`, route => {
  const input = route.request().postDataJSON();
  if (input.configurationRevision !== identityConnection.configurationRevision)
   return route.fulfill({status:409,json:{error:'This connection changed since it was loaded. Reload the saved connection and review it before saving.'}});
  Object.assign(identityConnection,input,{configurationRevision:'00000000-0000-0000-0000-000000000003'});
  return route.fulfill({json:{}});
 });
 await page.getByRole('button',{name:'Edit connection',exact:true}).click();
 await page.getByLabel('Name',{exact:true}).fill('Unsaved stale name');
 await page.getByLabel('New NAS API key',{exact:true}).fill('discard-this-fixture-key');
 identityConnection.name = 'Updated by another tab'; identityConnection.configurationRevision = '00000000-0000-0000-0000-000000000002';
 await page.getByRole('button',{name:'Save connection',exact:true}).click();
 await expect(page.getByRole('alert')).toContainText('This connection changed since it was loaded');
 await expect(page.getByLabel('Name',{exact:true})).toHaveValue('Unsaved stale name');
 await expect(page.getByRole('alert')).toBeFocused();
 await expect(page.getByRole('alert')).toBeVisible();
 await audit('Connection edit conflict');
 await page.getByRole('dialog').screenshot({path:'test-results/connection-conflict-mobile.png',animations:'disabled'});
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await page.getByRole('button',{name:'Reload saved connection',exact:true}).click();
 await expect(page.getByLabel('Name',{exact:true})).toHaveValue('Updated by another tab');
 await expect(page.getByLabel('New NAS API key',{exact:true})).toHaveValue('');
 await page.getByLabel('Name',{exact:true}).fill('Reviewed connection');
 await page.getByRole('button',{name:'Save connection',exact:true}).click();
 await expect(page.getByRole('heading',{name:'Reviewed connection',exact:true})).toBeVisible();

 let importedObservation: Record<string, unknown> | null = null;
 await page.route(`**/api/v1/disks/${oldId}/observation-history?*`,route=>route.fulfill({json:{items:importedObservation?[{...importedObservation,id:1,recordedBy:'fixture-owner',receivedUtc:'2026-10-31T12:00:00Z'}]:[],asOfUtc:new Date().toISOString(),snapshotId:1,nextBefore:null}}));
 await page.route(`**/api/v1/disks/${oldId}/observations`, route => {
  if (route.request().method() !== 'POST') return importedObservation ? route.fulfill({ json: [{ ...importedObservation, id: 1, recordedBy: 'fixture-owner', receivedUtc: '2026-10-31T12:00:00Z' }] }) : route.fallback();
  importedObservation = route.request().postDataJSON();
  return route.fulfill({ json: { ...importedObservation, id: 1 } });
 });
 await page.getByRole('link', { name: 'Storage', exact: true }).click();
 await page.getByRole('button', { name: 'View drive', exact: true }).first().click();
 await page.getByLabel('Observation metric', { exact: true }).selectOption('temperature');
 await expect(page.getByLabel('Unit', { exact: true })).toHaveValue('°C');
 await expect(page.getByLabel('Unit', { exact: true })).toHaveAttribute('readonly', '');
 await page.getByLabel('Value (leave empty if unknown)', { exact: true }).fill('32.5');
 await page.getByLabel('Evidence source / reference', { exact: true }).fill('Existing operator temperature export');
 await page.getByRole('button', { name: 'Import evidence', exact: true }).click();
 await expect.poll(() => importedObservation).toMatchObject({ metric: 'temperature', unit: '°C', value: 32.5, observedUtc: null, quality: 'unverified' });
 await page.getByText('Evidence without observation time (1)', { exact: true }).click();
 await expect(page.getByText('32.5 °C', { exact: true })).toBeVisible();
 await expect(page.getByText(/Observation time unknown · Imported.*by fixture-owner/)).toBeVisible();
 await page.getByLabel('Observation metric', { exact: true }).selectOption('ata.pending_sectors');
 await expect(page.getByLabel('Unit', { exact: true })).toHaveValue('sectors');
 let importedReport: Record<string, unknown> | null = null;
 let failOlderReport = true;
 let historyFixtureComplete = false;
 await page.route(`**/api/v1/disks/${oldId}/smart-reports/history*`, route => {
   const url = new URL(route.request().url());
   const older = url.searchParams.has('offset');
   if (historyFixtureComplete) return route.fulfill({json:{asOf:'2026-11-01T12:00:00Z',nextOffset:null,items:[]}});
   if (older) {
     expect(url.searchParams.get('offset')).toBe('50');
     expect(url.searchParams.get('asOf')).toBe('2026-11-01T12:00:00Z');
     if (failOlderReport) return route.fulfill({status:503,json:{}});
   }
   return route.fulfill({json:{asOf:'2026-11-01T12:00:00Z',nextOffset:older?null:50,items:[{
     id:older?'older-report':'recent-report',actor:older?'historical-owner':'recent-owner',importedUtc:'2026-10-31T12:00:00Z',
     report:{reportHash:'history-fixture',protocol:'NVMe',toolVersion:'7.4',serial:'OLD-FIXTURE',model:'Fixture replacement drive',observedUtc:null,sourceSchema:'fixture',readings:[],attributes:[],warnings:[],continuityGroup:''}
   }]}});
 });
 await page.route(`**/api/v1/disks/${oldId}/smart-reports`, route => {
   if (route.request().method() === 'GET') return route.fulfill({ json: [] });
   importedReport = route.request().postDataJSON(); return route.fulfill({ json: { id: 'report-fixture', count: 2 } });
 });
 await page.route(`**/api/v1/disks/${oldId}/smart-reports/preview`, route => route.fulfill({ json: {
   ataSelfTests: [{source:'ata_smart_self_test_log.standard',coverage:'reported entries',quality:'observed',revision:1,reportedCount:null,entries:[{position:1,typeCode:2,typeText:'Extended offline',statusCode:112,statusText:'Completed: read failure',result:'failed',remainingPercent:null,lifetimeHours:12000,firstErrorLba:1234,reportedPassed:false}]}],
   deviceInfo: {firmware:'FW01',firmwareField:'firmware_version',interface:'SATA 3.3',interfaceField:'sata_version.string',quality:'observed'},
   reportHash: 'fixture-reviewed-hash', protocol: 'ATA', toolVersion: '7.4', serial: 'OLD-FIXTURE', model: 'Fixture replacement drive', observedUtc: '2026-10-31T12:00:00Z', sourceSchema: 'smartctl-json-1.0-ata-tool-7.4-v1', continuityGroup: '',
   readings: [{ metric: 'ata.pending_sectors', value: 4, unit: 'sectors', quality: 'observed', missingReason: null }, { metric: 'ata.offline_uncorrectable', value: null, unit: 'sectors', quality: 'unknown', missingReason: 'Attribute absent.' }],
   attributes: [{ id: 197, name: 'Current_Pending_Sector', rawNumeric: '4', rawText: '4', normalizedValue: 100, worst: 99, threshold: 0, whenFailed: '' }], warnings: ['Imported evidence is not a complete health assessment.']
 } }));
 await page.getByRole('button', { name: 'Import existing SMART report', exact: true }).click();
 const reportDialog = page.getByRole('dialog', { name: 'Import an existing drive report', exact: true });
 await reportDialog.getByText('Saved report history', {exact:true}).click();
 await expect(reportDialog.getByText(/Imported by recent-owner/)).toBeVisible();
 await reportDialog.getByRole('button',{name:'Older reports',exact:true}).click();
 await expect(reportDialog.getByRole('alert')).toContainText('Any displayed reports are retained');
 await expect(reportDialog.getByText(/Imported by recent-owner/)).toBeVisible();
 failOlderReport = false;
 await reportDialog.getByRole('button',{name:'Older reports',exact:true}).click();
 await expect(reportDialog.getByText(/Imported by historical-owner/)).toBeVisible();
 await expect(reportDialog.getByText('End of saved report history.',{exact:true})).toBeVisible();
 await reportDialog.getByRole('button',{name:'Refresh report history',exact:true}).click();
 await expect(reportDialog.getByText(/Imported by recent-owner/)).toBeVisible();
 historyFixtureComplete = true;
 await reportDialog.getByRole('button',{name:'Refresh report history',exact:true}).click();
 await expect(reportDialog.getByText('No reports loaded.',{exact:false})).toBeVisible();
 await reportDialog.getByText('Saved report history', {exact:true}).click();
 const existingReport = JSON.stringify({ json_format_version: [1, 0], smartctl: { version: [7, 4], exit_status: 0 }, device: { protocol: 'ATA' }, model_name: 'Fixture replacement drive', serial_number: 'OLD-FIXTURE' });
 await reportDialog.getByLabel('Existing JSON file (up to 1 MiB)', { exact: true }).setInputFiles({ name: 'existing-report.json', mimeType: 'application/json', buffer: Buffer.from(existingReport) });
 await expect(reportDialog.getByLabel('Report JSON', { exact: true })).toHaveValue(existingReport);
 await reportDialog.getByRole('checkbox', { name: 'ATA reports:', exact: false }).check();
 await reportDialog.getByRole('button', { name: 'Preview report', exact: true }).click();
 await expect(reportDialog.getByText('4 sectors', { exact: true })).toBeVisible();
 await expect(reportDialog.locator('.report-reading').filter({hasText:'Offline uncorrectable'}).getByText('Unknown', { exact: true })).toBeVisible();
 await expect(reportDialog.locator('.report-reading').filter({hasText:'Overall SMART assessment'}).getByText('Unknown', { exact: true })).toBeVisible();
 await expect(reportDialog.getByRole('button', { name: 'Import reviewed report' })).toBeDisabled();
 await reportDialog.getByText('Raw attribute and source details', { exact: true }).focus(); await page.keyboard.press('Enter');
 await expect(reportDialog.getByText('Normalized: 100;', { exact: false })).toBeVisible();
 await reportDialog.getByLabel('Retype selected drive serial').fill('wrong');
 await expect(reportDialog.getByRole('button', { name: 'Import reviewed report' })).toBeDisabled();
 await reportDialog.getByLabel('Retype selected drive serial').fill('OLD-FIXTURE');
 await reportDialog.getByText('Firmware and interface evidence',{exact:true}).focus(); await page.keyboard.press('Enter');
 await expect(reportDialog.getByText('Firmware / revision: FW01',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('Reported interface / standard: SATA 3.3',{exact:true})).toBeVisible();
 await reportDialog.getByText('Recorded ATA self-tests',{exact:true}).focus(); await page.keyboard.press('Enter');
 await expect(reportDialog.getByText('Entry 1 · failed',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('Extended offline · Lifetime hours: 12,000',{exact:true})).toBeVisible();
 await audit('SMART report preview');
 await page.screenshot({ path: 'test-results/smart-report-mobile.png', animations: 'disabled' });
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await reportDialog.getByRole('button', { name: 'Import reviewed report' }).click();
 await expect(reportDialog).not.toBeVisible();
 await expect(page.getByText('Imported 2 readings.', { exact: false })).toBeVisible();
 expect(importedReport).toMatchObject({ json: existingReport, confirmAtaCounterUnits: true, confirmedSerial: 'OLD-FIXTURE', expectedHash: 'fixture-reviewed-hash' });
 await page.route(`**/api/v1/disks/${oldId}/smart-reports/preview`, route => route.fulfill({json:{
  reportHash:'nvme-preview-hash',protocol:'NVMe',toolVersion:'7.4',serial:'OLD-FIXTURE',model:'Fixture replacement drive',observedUtc:'2026-10-31T12:00:00Z',sourceSchema:'smartctl-json-1.0-nvme-tool-7.4-v4',overallSmart:{passed:false,quality:'observed'},continuityGroup:'',attributes:[],
  readings:[{metric:'power_cycles',value:42,unit:'cycles',quality:'observed',missingReason:null},{metric:'nvme.critical_warning',value:8,unit:'bitmask',quality:'observed',missingReason:null},{metric:'nvme.available_spare',value:97,unit:'%',quality:'observed',missingReason:null},{metric:'nvme.available_spare_threshold',value:null,unit:'%',quality:'unknown',missingReason:'Not reported'},{metric:'nvme.percentage_used',value:130,unit:'% rated endurance',quality:'observed',missingReason:null}],
  warnings:["NVMe percentage used is the manufacturer's endurance estimate, not a failure probability or deadline."]
 }}));
 await page.getByRole('button',{name:'Import existing SMART report',exact:true}).click();
 await reportDialog.getByLabel('Report JSON',{exact:true}).fill('{"fixture":"NVMe"}');
 await reportDialog.getByRole('button',{name:'Preview report',exact:true}).click();
 await expect(reportDialog.getByText('130 % rated endurance',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('8 bitmask',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('Power cycles',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('42 cycles',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('NVMe spare warning threshold',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('Reported failed',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('A passed result does not override adverse readings',{exact:false})).toBeVisible();
 await expect(reportDialog.getByText('not a failure probability or deadline.',{exact:false})).toBeVisible();
 await expect(reportDialog.getByRole('heading',{name:'Review the supported evidence'})).toBeFocused();
 await reportDialog.getByText('42 cycles',{exact:true}).scrollIntoViewIfNeeded();
 await page.screenshot({path:'test-results/nvme-preview-mobile.png',animations:'disabled'});
 await page.route(`**/api/v1/disks/${oldId}/smart-reports/preview`, route => route.fulfill({json:{
  reportHash:'scsi-preview-hash',protocol:'SCSI',toolVersion:'7.4',serial:'OLD-FIXTURE',model:'Fixture replacement drive',observedUtc:'2026-10-31T12:00:00Z',sourceSchema:'smartctl-json-1.0-scsi-tool-7.4-v2',continuityGroup:'',attributes:[],
  readings:[{metric:'scsi.grown_defects',value:12,unit:'defects',quality:'observed',missingReason:null},{metric:'scsi.read_uncorrected_errors',value:5,unit:'errors',quality:'observed',missingReason:null},{metric:'scsi.write_uncorrected_errors',value:0,unit:'errors',quality:'observed',missingReason:null},{metric:'scsi.verify_uncorrected_errors',value:null,unit:'errors',quality:'unknown',missingReason:'Not reported'}],
  warnings:['SCSI grown defects count defect-list elements, not ATA reallocated sectors.']
 }}));
 await reportDialog.getByLabel('Report JSON',{exact:true}).fill('{"fixture":"SCSI"}');
 await reportDialog.getByRole('button',{name:'Preview report',exact:true}).click();
 await expect(reportDialog.getByText('SCSI grown defects',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('12 defects',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('SCSI uncorrected read errors',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('5 errors',{exact:true})).toBeVisible();
 await expect(reportDialog.getByText('0 errors',{exact:true})).toBeVisible();
 await expect(reportDialog.locator('.report-reading').filter({hasText:'SCSI uncorrected verify errors'}).getByText('Unknown',{exact:true})).toBeVisible();
 await audit('SCSI report preview');
 await reportDialog.getByText('SCSI grown defects',{exact:true}).scrollIntoViewIfNeeded();
 await page.screenshot({path:'test-results/scsi-preview-mobile.png',animations:'disabled'});

 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();
 await reportDialog.getByRole('button',{name:'Close',exact:true}).click();
 let historyPageFailed=false;
 let historyRequestCount=0;
 const historyAnchor=new Date().toISOString();
 await page.route(`**/api/v1/disks/${oldId}/observation-history?*`,route=>{
  historyRequestCount++;
  const query=new URL(route.request().url()).searchParams;
  if(query.get('metric')!=='temperature')return route.fulfill({json:{items:[],asOfUtc:historyAnchor,snapshotId:0,nextBefore:null}});
  const older=query.has('before');
  if(older){expect(query.get('before')).toBe('100');expect(query.get('snapshotId')).toBe('200');expect(query.get('asOfUtc')).toBe(historyAnchor);if(!historyPageFailed){historyPageFailed=true;return route.fulfill({status:503,json:{error:'Fixture unavailable'}});}}
  return route.fulfill({json:{asOfUtc:historyAnchor,snapshotId:200,nextBefore:older?null:100,comparisons:older?{'99':{previousId:null,previousObservedUtc:null,status:'baseline',difference:null,unit:'°C',explanation:'No earlier timestamped record exists. This is not a zero baseline.'}}:{'200':{previousId:99,previousObservedUtc:new Date(Date.parse(historyAnchor)-2*86400000).toISOString(),status:'compared',difference:3,unit:'°C',explanation:'Observed net difference, not a rate or continuous coverage. This does not predict failure.'}},items:[{id:older?99:200,metric:'temperature',value:older?-5:-2,unit:'°C',quality:'observed',source:older?'Older temperature evidence':'Latest temperature evidence',observedUtc:new Date(Date.parse(historyAnchor)-(older?2:1)*86400000).toISOString(),receivedUtc:historyAnchor,recordedBy:'fixture-owner',missingReason:null}]}});
 });
 await page.getByLabel('Metric',{exact:true}).selectOption('ata.pending_sectors');
 await page.getByLabel('Metric',{exact:true}).selectOption('temperature');
 await page.getByLabel('History window',{exact:true}).selectOption('0');
 await expect(page.getByText(/1 records loaded for this metric/)).toBeVisible();
 await page.getByRole('button',{name:'Load more observations',exact:true}).click();
 await expect(page.getByRole('alert').filter({hasText:'Observation history could not be loaded'})).toBeVisible();
 await expect(page.getByRole('heading',{name:'-2 °C',exact:true})).toBeVisible();
 await page.getByRole('button',{name:'Load more observations',exact:true}).click();
 await expect(page.getByText(/2 records loaded for this metric.*End of retained history/)).toBeVisible();
 await expect(page.getByRole('heading',{name:'-5 °C',exact:true})).toBeVisible();
 const trendChart=page.getByRole('img',{name:/temperature observations/});
 expect(await trendChart.locator('circle').evaluateAll(circles=>circles.every(circle=>Number(circle.getAttribute('cy'))>=25&&Number(circle.getAttribute('cy'))<=160))).toBeTruthy();
 const loadedHistoryRequestCount=historyRequestCount;
 const refreshedDrive=page.waitForResponse(response=>response.url().endsWith(`/api/v1/disks/${oldId}`)&&response.request().method()==='GET');
 await page.getByRole('button',{name:'Refresh',exact:true}).click();
 await refreshedDrive;
 await page.evaluate(()=>new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve))));
 await expect(page.getByText(/2 records loaded for this metric.*End of retained history/)).toBeVisible();
 expect(historyRequestCount).toBe(loadedHistoryRequestCount);
 await page.getByRole('heading',{name:'-2 °C',exact:true}).locator('..').getByText('Change evidence',{exact:true}).click();
 await expect(page.getByText(/^\+3 °C since/)).toBeVisible();
 await page.getByRole('heading',{name:'-5 °C',exact:true}).locator('..').getByText('Change evidence',{exact:true}).click();
 await expect(page.getByText('No earlier timestamped record exists. This is not a zero baseline.',{exact:true})).toBeVisible();
 await audit('Paged temperature history');
 await page.getByRole('heading',{name:'Observed trends',exact:true}).locator('..').screenshot({path:'test-results/paged-trends-mobile.png'});
 await audit('Drive details');
 let identityMerged=false;let identityReversed=false;
 const identityMembers=[{id:oldId,serial:'OLD-FIXTURE',model:'Original identity record',wwn:null,mappingConfidence:'ambiguous',present:true,bay:null,bayVerified:false},{id:newId,serial:'NEW-FIXTURE',model:'Other identity record',wwn:null,mappingConfidence:'ambiguous',present:false,bay:null,bayVerified:false}];
 const identityLink={id:'99999999-9999-9999-9999-999999999999',firstDiskId:oldId,secondDiskId:newId,evidence:'Synthetic reviewed identity history',author:'fixture-owner',recordedUtc:new Date().toISOString(),reversedUtc:null as string|null,reversalEvidence:null as string|null};
 await page.route(`**/api/v1/disks/${oldId}/identity-history`,route=>route.fulfill({json:{reviewToken:identityMerged?'group-token':'single-token',members:identityMerged?identityMembers:[identityMembers[0]],candidates:identityMerged?[]:[identityMembers[1]],links:identityMerged||identityReversed?[identityLink]:[],observations:[{id:1,diskId:oldId,metric:'temperature',value:30,unit:'°C',quality:'observed',observedUtc:'2026-01-01T12:00:00Z',receivedUtc:'2026-01-01T12:00:01Z'}],nextBefore:null,findings:[]}}));
 await page.route(`**/api/v1/disks/${oldId}/identity-history/review?*`,route=>route.fulfill({json:{reviewToken:'pair-token',firstIdentity:'OLD-FIXTURE',secondIdentity:'NEW-FIXTURE',records:identityMembers,explanation:'Review both original records. No counter rates or bay verification are combined.'}}));
 await page.route(`**/api/v1/disks/${oldId}/identity-history/merge`,route=>{const body=route.request().postDataJSON();expect(body.firstIdentity).toBe('OLD-FIXTURE');expect(body.secondIdentity).toBe('NEW-FIXTURE');expect(body.confirmedSamePhysicalDrive).toBe(true);expect(body.reviewToken).toBe('pair-token');identityMerged=true;return route.fulfill({json:identityLink});});
 await page.route(`**/api/v1/disks/${oldId}/identity-history/${identityLink.id}/reverse`,route=>{const body=route.request().postDataJSON();expect(body.reviewToken).toBe('group-token');identityMerged=false;identityReversed=true;identityLink.reversedUtc=new Date().toISOString();identityLink.reversalEvidence=body.evidence;return route.fulfill({json:identityLink});});
 const identityView=page.getByRole('region',{name:'Identity history review',exact:true});
 await identityView.getByRole('button',{name:'Review identity & merge history',exact:true}).click();
 await expect(identityView.getByRole('heading',{name:'1 original record(s) in this history group',exact:true})).toBeVisible();
 await identityView.getByRole('button',{name:'Review a merge',exact:true}).click();
 const identityDialog=page.getByRole('dialog',{name:'Review identity history merge',exact:true});
 await identityDialog.getByLabel('Other original record',{exact:true}).selectOption(newId);
 await identityDialog.getByRole('button',{name:'Load records for review',exact:true}).click();
 await identityDialog.getByLabel('Confirm current record identity',{exact:true}).fill('OLD-FIXTURE');
 await identityDialog.getByLabel('Confirm other record identity',{exact:true}).fill('NEW-FIXTURE');
 await identityDialog.getByRole('checkbox',{name:'I reviewed all records and confirm they describe the same physical drive.',exact:true}).check();
 await identityDialog.getByLabel('Identity review evidence',{exact:true}).fill('Synthetic physical-label comparison; original observations retain provenance.');
 await audit('Identity merge review');
 await page.screenshot({path:'test-results/identity-merge-mobile.png'});
 await identityDialog.getByRole('button',{name:'Merge reviewed histories',exact:true}).click();
 await expect(identityView.getByRole('heading',{name:'2 original record(s) in this history group',exact:true})).toBeVisible();
 await identityView.getByRole('button',{name:'Review split of this merge',exact:true}).click();
 const splitDialog=page.getByRole('dialog',{name:'Review identity history split',exact:true});
 await expect(splitDialog.getByRole('heading',{name:'Resulting group 1',exact:true})).toBeVisible();
 await splitDialog.getByLabel('Reason to split histories',{exact:true}).fill('Separate the synthetic original histories after review.');
 await audit('Identity split review');
 await splitDialog.getByRole('button',{name:'Reverse merge & split histories',exact:true}).click();
 await expect(identityView.getByRole('heading',{name:'1 original record(s) in this history group',exact:true})).toBeVisible();
 await expect(identityView.getByText('Separate the synthetic original histories after review.',{exact:false})).toBeVisible();
 await identityView.getByRole('button',{name:'Hide identity history',exact:true}).click();
 const hardwareAsset={id:'88888888-8888-8888-8888-888888888888',name:'Shared controller',type:'controller',updatedUtc:new Date().toISOString(),archived:false};
 await page.route('**/api/v1/inventory',route=>route.fulfill({json:{assets:[hardwareAsset],dependencies:[]}}));
 let hardwareHead: {assetId:string;id:number;attached:boolean;evidence:string;recordedUtc:string}|null=null;
 const hardwareItems: {record:Record<string,unknown>;status:string}[]=[];
 await page.route(`**/api/v1/disks/${oldId}/hardware-paths*`,route=>{
  if(route.request().method()==='POST'){const body=route.request().postDataJSON();expect(body.confirmedIdentity).toBe('OLD-FIXTURE');if(!hardwareHead){hardwareHead={assetId:hardwareAsset.id,id:9,attached:true,evidence:'Another reviewer recorded this connection.',recordedUtc:new Date().toISOString()};return route.fulfill({status:409,json:{error:'This relationship changed. Reload the latest evidence and review it before saving.'}});}expect(body.previousReviewId).toBe(9);const record={...body,id:1,assetName:hardwareAsset.name,assetType:'controller',recordedUtc:new Date().toISOString(),author:'fixture-owner'};hardwareItems.unshift({record,status:'recorded relationship'});return route.fulfill({json:record});}
  return route.fulfill({json:{generatedUtc:new Date().toISOString(),currentHeads:hardwareHead?[hardwareHead]:[],items:hardwareItems,nextBefore:null,peersTruncated:false,peers:hardwareItems.length?[{diskId:newId,serial:'NEW-FIXTURE',model:'Related drive',components:[{assetId:hardwareAsset.id,assetName:hardwareAsset.name,assetType:'controller',validUntilUtc:new Date(Date.now()+86400000).toISOString()}],findings:[{id:findingId,title:'Related link errors',evidence:'A different drive reported link errors; a common cause is not established.',evidenceObservedUtc:'2026-01-01T12:00:00Z',lastSeenUtc:'2026-01-01T12:00:00Z',state:'open'}]}]:[]}});
 });
 const hardwareView=page.getByRole('region',{name:'Shared hardware evidence',exact:true});
 await hardwareView.getByRole('button',{name:'Shared hardware & related findings',exact:true}).click();
 await hardwareView.getByRole('button',{name:'Record hardware relationship',exact:true}).click();
 const hardwareDialog=page.getByRole('dialog',{name:'Record hardware relationship',exact:true});
 await hardwareDialog.getByLabel('Component',{exact:true}).selectOption(hardwareAsset.id);
 await hardwareDialog.getByLabel('Confirm drive serial or WWN',{exact:true}).fill('OLD-FIXTURE');
 await hardwareDialog.getByLabel('Relationship evidence',{exact:true}).fill('Matched serial label and traced cable to controller port.');
 await hardwareDialog.getByLabel('Review expiry (UTC)',{exact:true}).fill(new Date(Date.now()+30*86400000).toISOString().slice(0,10));
 await audit('Hardware relationship form');
 await page.screenshot({path:'test-results/hardware-relationship-form-mobile.png'});
 await hardwareDialog.getByRole('button',{name:'Save relationship evidence',exact:true}).click();
 await expect(hardwareDialog.getByRole('alert')).toContainText('This relationship changed');
 await expect(hardwareDialog.getByRole('alert')).toBeFocused();
 await hardwareDialog.getByRole('button',{name:'Reload latest evidence',exact:true}).click();
 await expect(hardwareDialog.getByText('Another reviewer recorded this connection.',{exact:true})).toBeVisible();
 await expect(hardwareDialog.getByLabel('Relationship evidence',{exact:true})).toHaveValue('Matched serial label and traced cable to controller port.');
 await hardwareDialog.getByRole('button',{name:'Save relationship evidence',exact:true}).click();
 await expect(hardwareView.getByRole('status')).toContainText('Hardware evidence recorded locally');
 await expect(hardwareView.getByRole('link',{name:'Related link errors',exact:true})).toBeVisible();
 await audit('Shared hardware findings');
 await hardwareView.screenshot({path:'test-results/shared-hardware-mobile.png'});
 await hardwareView.getByRole('button',{name:'Hide shared hardware',exact:true}).click();
 const maintenance = page.getByRole('region',{name:'Drive maintenance history',exact:true});
 await expect(maintenance.getByText('No maintenance records in this window.',{exact:false})).toBeVisible();
 await maintenance.getByRole('button',{name:'Record past work',exact:true}).click();
 await maintenance.getByLabel('Work category',{exact:true}).selectOption('cabling');
 await maintenance.getByLabel('Work time (UTC)',{exact:true}).fill(new Date(Date.now()-3600000).toISOString().slice(0,16));
 await maintenance.getByLabel('Date confidence',{exact:true}).selectOption('estimated');
 await maintenance.getByLabel('Work outcome',{exact:true}).selectOption('completed');
 await maintenance.getByLabel('Work summary',{exact:true}).fill('Inspected cable connection');
 await maintenance.getByLabel('Maintenance evidence',{exact:true}).fill('Operator log; observed connector seating without a health conclusion.');
 await maintenance.getByLabel('Confirm drive serial or WWN',{exact:true}).fill('wrong');
 await maintenance.getByRole('button',{name:'Save maintenance record',exact:true}).click();
 await expect(maintenance.getByRole('status')).toContainText('Confirm the exact drive identity');
 await expect(maintenance.getByRole('status')).toBeFocused();
 await maintenance.getByLabel('Confirm drive serial or WWN',{exact:true}).fill('OLD-FIXTURE');
 await audit('Maintenance record form');
 await maintenance.screenshot({path:'test-results/maintenance-form-mobile.png',animations:'disabled'});
 await maintenance.getByRole('button',{name:'Save maintenance record',exact:true}).click();
 await expect(maintenance.getByRole('heading',{name:'Inspected cable connection',exact:true})).toBeVisible();
 expect(maintenanceRecords[0]).toMatchObject({dateConfidence:'estimated',kind:'cabling',outcome:'completed',confirmedIdentity:'OLD-FIXTURE'});
 expect(String(maintenanceRecords[0].requestId)).toMatch(/^[a-f0-9-]{36}$/);
 await maintenance.getByText('Correct this record',{exact:true}).focus(); await page.keyboard.press('Enter');
 await maintenance.getByLabel('Reason this record is incorrect',{exact:true}).fill('Recorded on the wrong date; retained for correction.');
 await maintenance.getByRole('button',{name:'Mark record incorrect',exact:true}).click();
 await expect(maintenance.getByText('Marked incorrect',{exact:true})).toBeVisible();
 await expect(maintenance.getByText('Original evidence retained.',{exact:false})).toBeVisible();
 await audit('Maintenance correction');
 await maintenance.screenshot({path:'test-results/maintenance-history-mobile.png',animations:'disabled'});
 maintenanceUnavailable = true; await maintenance.getByRole('button',{name:'Refresh maintenance history',exact:true}).click();
 await expect(maintenance.getByRole('alert')).toContainText('Could not refresh maintenance history');
 await expect(maintenance.getByRole('heading',{name:'Inspected cable connection',exact:true})).toBeVisible();
 maintenanceUnavailable = false; await maintenance.getByRole('button',{name:'Refresh maintenance history',exact:true}).click();
 await expect(maintenance.getByRole('alert')).toHaveCount(0);
 expect(await page.evaluate(() => document.documentElement.scrollWidth <= window.innerWidth)).toBeTruthy();

 const notificationFixture = {
  channel: {host:'notifier.example',enabled:true,maskIdentifiers:true,testedUtc:'2026-11-01T00:00:00Z',maintenanceUntilUtc:null},
  deliveries:[{id:'77777777-7777-7777-7777-777777777777',kind:'opened',status:'failed',attempts:5,createdUtc:'2026-11-01T00:00:00Z'}],
  attempts:[{id:1,deliveryId:'77777777-7777-7777-7777-777777777777',atUtc:'2026-11-01T00:05:00Z',result:'outcome-unknown',httpStatus:null}]
 };
 await page.route('**/api/v1/notifications', route => route.fulfill({json:notificationFixture}));
 let requestedRetry = false;
 await page.route('**/api/v1/notifications/deliveries/*/retry', route => {
  requestedRetry = true; notificationFixture.deliveries[0].status = 'pending'; notificationFixture.deliveries[0].attempts = 0;
  return route.fulfill({status:200,json:{}});
 });
 await page.getByRole('link',{name:'Settings',exact:true}).click();
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
 const telegramChannel = {kind:'telegram',host:'api.telegram.org',enabled:false,maskIdentifiers:true,testedUtc:null as string|null,maintenanceUntilUtc:null};
 await page.route('**/api/v1/notifications', route => {
   if(route.request().method()==='PUT') { telegramSaved=route.request().postDataJSON(); telegramChannel.enabled=Boolean(telegramSaved!.enabled); return route.fulfill({json:{}}); }
   return route.fulfill({json:{channel:telegramChannel,deliveries:[],attempts:[]}});
 });
 await page.route('**/api/v1/notifications/test', route => {telegramChannel.testedUtc='2026-11-01T12:00:00Z';return route.fulfill({json:{}});});
 await page.getByLabel('Notification service',{exact:true}).selectOption('telegram');
 await page.getByLabel('Telegram bot token',{exact:true}).fill('123456:abcdefghijklmnopqrstuvwxyz');
 await page.getByLabel('Telegram chat ID',{exact:true}).fill('-100123456');
 await expect(page.getByRole('button',{name:'Send test',exact:true})).toBeDisabled();
 await audit('Telegram notification setup');
 await page.getByRole('button',{name:'Save destination/settings',exact:true}).click();
 await expect.poll(()=>telegramSaved).toMatchObject({kind:'telegram',botToken:'123456:abcdefghijklmnopqrstuvwxyz',chatId:'-100123456',enabled:false});
 await expect(page.getByLabel('Telegram bot token',{exact:true})).toHaveValue('');
 await expect(page.getByLabel('Telegram chat ID',{exact:true})).toHaveValue('');
 await expect(page.getByRole('button',{name:'Enable delivery',exact:true})).toBeDisabled();
 await page.getByRole('button',{name:'Send test',exact:true}).click();
 await expect(page.getByRole('button',{name:'Enable delivery',exact:true})).toBeEnabled();
 await page.getByRole('button',{name:'Enable delivery',exact:true}).click();
 await expect(page.getByRole('button',{name:'Disable delivery',exact:true})).toBeVisible();
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
 await page.getByRole('link',{name:'Overview',exact:true}).click();
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
 await expect(scanRegion.getByText('No in-progress scan is recorded',{exact:false})).toBeVisible();
 expect(errors).toEqual([]);
});
