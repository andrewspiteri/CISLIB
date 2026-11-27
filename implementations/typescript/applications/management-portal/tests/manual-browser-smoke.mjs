import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { chromium, expect } from '@playwright/test';
import { verifyConfigurationRepairsBrowser } from './native-configuration-repairs-checks.mjs';
const directory = process.argv[2];
const status = JSON.parse(await readFile(join(directory, 'status.json'), 'utf8'));
assert.equal(status.state, 'ready');
let browser;
let original;
let report;
const cases = [];
const formObservations = [];
const headerCaptures = [];
const headerErrors = [];
const contexts = [];
const dialogCaptures = [];
const dialogErrors = [];
try {
  browser = await chromium.launch({ channel: 'msedge', headless: true });
  const context = await ownedContext();
  const page = await context.newPage();
  observeForms(page);
  const entry = await page.goto(status.portalUrl);
  assert.equal(entry.headers()['referrer-policy'], 'same-origin');
  assert.equal(entry.headers()['content-encoding'], undefined);
  assert.ok(entry.headers()['content-security-policy'].includes("form-action 'self' " + status.issuerUrl));
  await page.getByRole('link', {name:'Sign in',exact:true}).first().click();
  await page.getByRole('button', {name:/Management operator/}).click();
  await expect(page.getByRole('heading', {name:'Local test sign-in'})).toBeVisible();
  await page.getByRole('button', {name:'Operator A — Country Services and Billing Worker editor',exact:true}).click();
  await expect(page.getByRole('link', {name:'Country Services',exact:true})).toBeVisible();
  await expect(page.getByRole('link', {name:'Billing Worker',exact:true})).toBeVisible();
  cases.push('real-browser-chooser-pkce-signin-and-two-native-applications');
  const applicationLink = page.getByRole('link', {name:'Country Services',exact:true});
  const applicationA = new URL(await applicationLink.getAttribute('href'), status.portalUrl).searchParams.get('applicationId');
  assert.match(applicationA, /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u);
  await applicationLink.click();
  await expect(page.getByRole('row').filter({has:page.getByRole('button',{name:'country-api-eu-01',exact:true})})).toContainText('1.0.0');
  await expect(page.getByRole('row').filter({has:page.getByRole('button',{name:'country-api-eu-02',exact:true})})).toContainText('2.4.2');
  cases.push('two-reported-instance-versions');
  const application = await get(page, `/api/portal/applications/${applicationA}`);
  assert.equal(application.applicationId, applicationA);
  await verifyDesktopBounds(page, application);
  const metadata = await verifyMetadataNavigation(page, applicationA, application);
  await page.getByRole('tab',{name:'Configuration',exact:true}).click();
  await expect(page.getByRole('button',{name:'shared.settings',exact:true})).toBeVisible();
  await page.getByRole('tab',{name:'Scheduled jobs',exact:true}).click();
  await expect(page.getByRole('button',{name:'Refresh country cache',exact:true})).toBeVisible();
  cases.push('real-configuration-metadata-and-job-report');
  await page.screenshot({path:join(directory,'manual-smoke.png'),fullPage:true});
  const signout = await Promise.allSettled([
    page.waitForURL(url => url.origin === new URL(status.portalUrl).origin && url.pathname === '/api/auth/signout'),
    page.getByRole('link',{name:'Sign out',exact:true}).click()
  ]);
  const signoutErrors = signout.filter(item => item.status === 'rejected').map(item => item.reason);
  if (signoutErrors.length > 0) { throw new AggregateError(signoutErrors, 'The native sign-out destination and click did not both complete.'); }
  await expect(page.getByRole('button',{name:'Sign out',exact:true})).toBeVisible();
  await page.getByRole('button',{name:'Sign out',exact:true}).click();
  await page.goto(status.portalUrl);
  const revoked = await page.request.get(new URL('/api/portal/applications', status.portalUrl).href);
  try {
    assert.equal(revoked.status(), 401);
    const reply = await revoked.json();
    assert.equal(reply.state, 'unauthenticated');
    assert.equal('context' in reply, false);
    assert.equal('value' in reply, false);
  } finally { await revoked.dispose(); }
  await expect(page.getByRole('link',{name:'Country Services',exact:true})).toHaveCount(0);
  await page.getByRole('link',{name:'Sign in',exact:true}).first().click();
  await page.getByRole('button',{name:/Management operator/}).click();
  await page.getByRole('button',{name:'Operator A — Country Services and Billing Worker editor',exact:true}).click();
  await expect(page.getByRole('link',{name:'Country Services',exact:true})).toBeVisible();
  cases.push('ordinary-browser-signout-and-reauthentication');
  const other = await ownedContext();
  const b = await other.newPage();
  observeForms(b);
  await b.goto(status.portalUrl);
  await b.getByRole('link',{name:'Sign in',exact:true}).first().click();
  await b.getByRole('button',{name:/Management operator/}).click();
  await b.getByRole('button',{name:'Operator B — Billing Worker reader',exact:true}).click();
  await expect(b.getByRole('link',{name:'Billing Worker',exact:true})).toBeVisible();
  await expect(b.getByRole('link',{name:'Country Services',exact:true})).toHaveCount(0);
  cases.push('independent-browser-operator-b-native-target-isolation');
  const repairsContext = await ownedContext();
  const repairs = await repairsContext.newPage();
  observeForms(repairs);
  await repairs.goto(status.portalUrl);
  await repairs.getByRole('link', {name:'Sign in',exact:true}).first().click();
  await repairs.getByRole('button', {name:/Management operator/}).click();
  await repairs.getByRole('button', {name:'Operator A — Country Services and Billing Worker editor',exact:true}).click();
  await expect(repairs.getByRole('link', {name:'Country Services',exact:true})).toBeVisible();
  const configuration = await verifyConfigurationRepairsBrowser({page:repairs,origin:new URL(status.portalUrl).origin,applicationA,
    screenshot:async (target,name) => target.screenshot({path:join(directory,name+'.png'),fullPage:true}),cases});
  let dirtyExitAccepted = false;
  repairs.once('dialog', dialog => captureDialog(async () => {
    if (dialog.type() !== 'confirm') { await dialog.dismiss(); assert.fail('Dirty Back must use its explicit confirmation.'); }
    dirtyExitAccepted = true; await dialog.accept();
  }));
  await repairs.getByRole('button', {name:'Back to Configuration list',exact:false}).click();
  await expect(repairs).toHaveURL(url => url.pathname.endsWith('/configuration') && url.searchParams.get('applicationId') === applicationA);
  assert.equal(dirtyExitAccepted, true, 'Leaving the unsubmitted local buffer requires an explicit discard choice.');
  await Promise.all(dialogCaptures);
  if (dialogErrors.length > 0) { throw new AggregateError(dialogErrors,'Browser dialog handling failed.'); }
  cases.push('manual-configuration-dirty-buffer-exit-explicitly-accepted');
  await Promise.all(headerCaptures);
  if (headerErrors.length > 0) { throw new AggregateError(headerErrors, 'Browser form header capture failed.'); }
  assert.ok(formObservations.length >= 4);
  assert.ok(formObservations.every(item => item.origin === new URL(status.portalUrl).origin && item.site !== 'cross-site'));
  assert.ok(formObservations.some(item => item.path === '/api/auth/signout'), 'Real browser signout must submit its maintained CSRF form.');
  report = {passed:true,cases,formObservations,metadataProofHashes:metadata,
    configurationProofHashes:Object.fromEntries(Object.entries(configuration).map(([name,value]) => [name.endsWith('Sha256') ? name : name+'Sha256',name.endsWith('Sha256') ? value : sha(String(value))]))};
} catch (error) {
  original = error;
} finally {
  const contextCleanup = await Promise.allSettled(contexts.map(context => context.close()));
  await Promise.all(headerCaptures);
  await Promise.all(dialogCaptures);
  const settled = await Promise.allSettled([
    writeFile(join(directory,'form-observations.json'),JSON.stringify(formObservations,null,2)),
    browser?.close() ?? Promise.resolve(),
    writeFile(join(directory,'stop.request'),'Manual smoke finished; join owned cleanup.')
  ]);
  const errors = original === undefined ? [] : [original];
  for (const item of contextCleanup) { if (item.status === 'rejected') { errors.push(item.reason); } }
  if (headerErrors.length > 0) { errors.push(...headerErrors); }
  if (dialogErrors.length > 0) { errors.push(...dialogErrors); }
  for (const item of settled) { if (item.status === 'rejected') { errors.push(item.reason); } }
  if (errors.length > 0) { throw new AggregateError(errors, 'The manual browser check or its owned cleanup failed.'); }
}
await writeFile(join(directory,'smoke-report.json'),JSON.stringify(report,null,2));
process.stdout.write(JSON.stringify({passed:true,cases}));
function observeForms(page) {
  page.on('request', request => {
    const url = new URL(request.url());
    if (request.method() === 'POST' && (url.pathname === '/api/auth/signin/cis-management' || url.pathname === '/api/auth/signout')) {
      headerCaptures.push(request.allHeaders().then(headers => {
        formObservations.push({path:url.pathname,origin:headers.origin ?? null,site:headers['sec-fetch-site'] ?? null});
      }).catch(error => { headerErrors.push(error); }));
    }
  });
}

async function ownedContext() {
  // Trust is scoped to this newly owned browser context and the fixture's local test certificates.
  const context = await browser.newContext({ignoreHTTPSErrors:true,viewport:{width:1600,height:1000}});
  contexts.push(context); return context;
}
function sha(value) { return createHash('sha256').update(value,'utf8').digest('hex'); }
function captureDialog(work) { dialogCaptures.push(work().catch(error => {dialogErrors.push(error);})); }
async function get(page,path) {
  const response = await page.request.get(new URL(path,status.portalUrl).href);
  try { assert.equal(response.status(),200); const reply = await response.json(); assert.equal(reply.state,'ready'); return reply.value; }
  finally { await response.dispose(); }
}
async function verifyDesktopBounds(page,application) {
  assert.equal(typeof application.metadata.description,'string'); assert.ok(application.metadata.description.length > 0);
  const title = page.getByRole('heading',{name:application.metadata.displayName,exact:true});
  const description = page.locator('.application-description');
  await expect(description).toHaveText(application.metadata.description);
  try {
    for (const width of [900,1280]) {
      await page.setViewportSize({width,height:900});
      await expect(title).toBeVisible(); await expect(description).toBeVisible();
      const titleBox = await title.boundingBox(), descriptionBox = await description.boundingBox();
      assert.ok(titleBox !== null && descriptionBox !== null && descriptionBox.y >= titleBox.y + titleBox.height - 1);
      const bounds = await page.evaluate(() => {
        const main = document.querySelector('main').getBoundingClientRect();
        return {viewport:document.documentElement.clientWidth,width:document.documentElement.scrollWidth,
          main:{left:main.left,right:main.right,width:main.width},frames:[...document.querySelectorAll('.instance-section .table-frame')].map(frame => {
            const box = frame.getBoundingClientRect(); return {left:box.left,right:box.right,width:box.width};
          })};
      });
      assert.ok(bounds.width <= bounds.viewport + 1,'The actual desktop document must not overflow at this window width.');
      assert.equal(bounds.frames.length,2);
      assert.ok(bounds.frames.every(frame => frame.left >= bounds.main.left - 1 && frame.right <= bounds.main.right + 1 && frame.width <= bounds.main.width + 1));
      await expect(page.getByRole('row').filter({has:page.getByRole('button',{name:'country-api-eu-01',exact:true})})).toContainText('1.0.0');
      await expect(page.getByRole('row').filter({has:page.getByRole('button',{name:'country-api-eu-02',exact:true})})).toContainText('2.4.2');
      await page.screenshot({path:join(directory,`manual-instance-bounds-${width}.png`),fullPage:true});
    }
  } finally { await page.setViewportSize({width:1600,height:1000}); }
  cases.push('manual-native-description-below-title-and-contained-desktop-tables-900-1280');
}

async function verifyMetadataNavigation(page,applicationId,application) {
  const origin = new URL(status.portalUrl).origin, path = `/api/portal/applications/${applicationId}/metadata`;
  const updatedDescription=application.metadata.description+' · Confirmed in the bounded manual browser check';
  let writes = 0, unexpectedDialogs = 0, responseEntered = false, confirmation, responseFailure, settle;
  const joined = new Promise(resolve => {settle=resolve;});
  const count = request => {if (request.method()==='PUT' && request.url()===origin+path) {writes++;}};
  const dismiss = dialog => captureDialog(async () => {unexpectedDialogs++; await dialog.dismiss();});
  page.on('request',count); page.on('dialog',dismiss);
  const routeHandler = async route => {
    if (route.request().method()!=='PUT') {await route.continue(); return;}
    responseEntered=true; let response;
    try {
      response=await route.fetch({timeout:30000}); assert.equal(response.status(),200);
      const body=await response.json(); assert.equal(body.state,'ready');
      confirmation=body.value;
      assert.equal(confirmation.family,'management.metadata.replace'); assert.equal(confirmation.disposition,'applied');
      assert.equal(confirmation.originalCompletion,'committed'); assert.equal(confirmation.target.applicationId,applicationId);
      assert.equal(confirmation.key,route.request().headers()['idempotency-key']);
      assert.equal(confirmation.decision.metadata.displayName,application.metadata.displayName);
      assert.equal(confirmation.decision.metadata.description,updatedDescription);
      await route.fulfill({response});
    } catch (error) {responseFailure=error; try {await route.abort('failed');} catch {/* Preserve the original native/observer fault. */}}
    finally {
      try {if (response!==undefined) {await response.dispose();}}
      catch (error) {responseFailure=responseFailure===undefined ? error : new AggregateError([responseFailure,error],'Metadata response cleanup failed.');}
      finally {settle();}
    }
  };
  try {
    await page.getByRole('link',{name:'Display metadata',exact:true}).click();
    await expect(page.getByRole('heading',{name:'Edit display metadata',exact:true})).toBeVisible();
    await page.getByRole('button',{name:'Back to application',exact:false}).click();
    await expect(page).toHaveURL(url => url.pathname.endsWith('/instances') && url.searchParams.get('applicationId')===applicationId);
    assert.equal(writes,0,'Clean Back performs no metadata write.'); assert.equal(unexpectedDialogs,0);
    await page.getByRole('link',{name:'Display metadata',exact:true}).click();
    await page.getByLabel('Description',{exact:true}).fill(updatedDescription);
    await page.getByRole('button',{name:'Review changes',exact:true}).click();
    await expect(page.getByRole('heading',{name:'Review display metadata',exact:true})).toBeFocused();
    await page.route(origin+path,routeHandler);
    await page.getByRole('button',{name:'Confirm metadata change',exact:true}).click();
    await expect.poll(() => responseEntered).toBe(true); await joined;
    if (responseFailure!==undefined) {throw responseFailure;}
    assert.ok(confirmation);
    await expect(page).toHaveURL(url => url.pathname.endsWith('/instances') && url.searchParams.get('applicationId')===applicationId);
    await expect(page.locator('.application-description')).toHaveText(updatedDescription);
    const titleBox=await page.getByRole('heading',{name:application.metadata.displayName,exact:true}).boundingBox();
    const descriptionBox=await page.locator('.application-description').boundingBox();
    assert.ok(titleBox!==null && descriptionBox!==null && descriptionBox.y>=titleBox.y+titleBox.height-1);
    const actual=await get(page,path);
    assert.equal(actual.applicationId,applicationId); assert.equal(actual.metadata.displayName,application.metadata.displayName);
    assert.equal(actual.metadata.description,updatedDescription); assert.equal(writes,1); assert.equal(unexpectedDialogs,0);
    cases.push('manual-native-clean-metadata-back-with-zero-writes','manual-native-fresh-metadata-confirmation-exact-return-and-description');
    return {applicationIdSha256:sha(applicationId),originalKeySha256:sha(confirmation.key),descriptionSha256:sha(updatedDescription)};
  } finally {
    try {if (responseEntered) {await joined;} await page.unroute(origin+path,routeHandler);}
    finally {page.off('request',count); page.off('dialog',dismiss);}
  }
}
