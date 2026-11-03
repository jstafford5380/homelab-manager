import { readFile } from "node:fs/promises";
import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { Drive, Server, StorageData } from "../src/storageModel";
test("001 root-only dataset usage and empty temperature cache explain their results", async ({
  page,
}) => {
  const data = fixture(),
    pool = data.servers[0].pools[0],
    d = pool.drives[0];
  pool.datasets = [{ name: "tank", usedBytes: 36598155851840 }];
  d.temperatureC = null;
  d.temperatureNote = "TrueNAS returned an empty temperature cache.";
  await setup(page, data);
  await page.goto("/storage");
  await page.getByText("What's using space", { exact: true }).click();
  await expect(
    page.getByText("Only the root dataset was returned.", { exact: false }),
  ).toBeVisible();
  await expect(page.locator(".storage-datasets li").first()).toContainText(
    "36.6 TB",
  );
  await page.getByRole("button", { name: d.title, exact: true }).click();
  const panel = page.getByRole("dialog");
  await expect(
    panel.getByText("No cached reading", { exact: true }),
  ).toBeVisible();
  await expect(
    panel.getByText(d.temperatureNote, { exact: true }),
  ).toBeVisible();
  await audit(page, "Root dataset and empty temperature cache");
});
test("001.08 pool drives are contiguous by vdev with alternating group stripes", async ({
  page,
}) => {
  const data = fixture(),
    pool = data.servers[0].pools[0];
  const first = { id: "vdev-0", name: "raidz1-0", role: "data" },
    second = { id: "vdev-1", name: "raidz1-1", role: "data" };
  pool.drives = ["a", "b", "c", "d"].map((id, i) => ({
    ...drive(id, "nas-a"),
    title: "Drive " + id,
    group: i % 2 ? first : second,
  }));
  await setup(page, data);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/storage");
  const groups = page.locator(".storage-table").first().locator("tbody");
  await expect(groups).toHaveCount(2);
  await expect(groups.nth(0)).toContainText("raidz1-0");
  await expect(groups.nth(0).locator("[data-drive-id]")).toHaveText([
    "Drive b",
    "Drive d",
  ]);
  await expect(groups.nth(1).locator("[data-drive-id]")).toHaveText([
    "Drive a",
    "Drive c",
  ]);
  expect(
    await groups.nth(0).evaluate((el) => getComputedStyle(el).backgroundColor),
  ).not.toEqual(
    await groups.nth(1).evaluate((el) => getComputedStyle(el).backgroundColor),
  );
  await audit(page, "Vdev group stripes desktop");
  await page.screenshot({
    path: "test-results/storage-vdev-groups-desktop.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  const cards = page
    .locator(".storage-drive-cards")
    .first()
    .locator(".storage-drive-group");
  await expect(cards).toHaveCount(2);
  await expect(cards.nth(0)).toContainText("raidz1-0");
  await expect(cards.nth(0).locator("button")).toHaveText([
    /Drive b/,
    /Drive d/,
  ]);
  await audit(page, "Vdev group stripes mobile");
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.screenshot({
    path: "test-results/storage-vdev-groups-mobile.png",
    fullPage: true,
  });
});
test("001.10 drawer header contains long drive labels without overlapping Health", async ({
  page,
}) => {
  const data = fixture();
  const d = data.servers[0].pools[0].drives[0];
  d.title = "WD WD101EFBX-68B0AN0 · media pool replacement drive";
  d.disk.model = "WDC_WD101EFBX-68B0AN0";
  d.status = { label: "Healthy", tone: "ok", why: null };
  await setup(page, data);
  await page.setViewportSize({ width: 1440, height: 720 });
  await page.goto("/storage?disk=one");
  for (const width of [1440, 900, 390]) {
    await page.setViewportSize({ width, height: 720 });
    if (width <= 700)
      await expect(page.locator(".storage-panel-mobile")).toBeVisible();
    else await expect(page.getByRole("dialog")).toBeVisible();
    const header = page.locator(".storage-panel-head");
    await expect(
      header.getByRole("heading", { name: d.title, exact: true }),
    ).toBeFocused();
    const bounds = await header.boundingBox();
    expect(bounds!.height).toBeGreaterThan(72);
    for (const child of await header.locator(":scope > *").all()) {
      const rect = await child.boundingBox();
      expect(rect!.y).toBeGreaterThanOrEqual(bounds!.y);
      expect(rect!.y + rect!.height).toBeLessThanOrEqual(
        bounds!.y + bounds!.height + 1,
      );
    }
    const health = await page.locator("#storage-health").boundingBox();
    expect(health!.y).toBeGreaterThanOrEqual(bounds!.y + bounds!.height);
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBeTruthy();
    await page.screenshot({
      path: "test-results/storage-drawer-header-" + width + ".png",
    });
  }
});
test("001 nested response validation, timeout and retry keep previously loaded data", async ({
  page,
}) => {
  const data = fixture();
  await setup(page, data);
  await page.clock.install();
  let mode = "valid",
    requests = 0;
  await page.route("**/api/v1/storage/overview", (r) => {
    requests++;
    if (mode === "hung") return;
    return r.fulfill({
      json:
        mode === "bad"
          ? { ...data, servers: [{ ...data.servers[0], pools: [{}] }] }
          : data,
    });
  });
  await page.goto("/storage");
  await expect(
    page.getByRole("heading", { name: "TrueNAS", exact: true }),
  ).toBeVisible();
  const storage = page.getByRole("region", { name: "Storage", exact: true });
  mode = "bad";
  const before = requests;
  await page.clock.fastForward(30001);
  await expect.poll(() => requests).toBeGreaterThan(before);
  await expect(storage.getByRole("status")).toContainText(
    "could not be read safely",
  );
  await expect(
    page.getByRole("heading", { name: "TrueNAS", exact: true }),
  ).toBeVisible();
  mode = "hung";
  const previous = requests;
  await page.clock.fastForward(30001);
  await expect.poll(() => requests).toBeGreaterThan(previous);
  await page.clock.fastForward(15001);
  await expect(storage.getByRole("status")).toContainText("timed out");
  await expect(
    page.getByRole("heading", { name: "TrueNAS", exact: true }),
  ).toBeVisible();
  mode = "valid";
  await storage.getByRole("button", { name: "Retry", exact: true }).click();
  await expect(storage.getByRole("status")).toHaveCount(0);
});
const now = () => new Date().toISOString();
function drive(id: string, serverId: string, status = "Healthy"): Drive {
  return {
    disk: {
      id,
      connectionId: serverId,
      nickname: "WD Red Plus 8TB",
      model: "WDC WD80EFZZ-68BTXN0",
      serial: "SERIAL-" + id,
      wwn: null,
      alias: "sda",
      mappingConfidence: "inferred",
      sizeBytes: 8000000000000,
      driveType: "HDD",
      rotationRate: 7200,
      enclosure: 1,
      slot: 2,
      bay: "Bay 2",
      purchasedUtc: "2022-03-01T00:00:00Z",
      warrantyEndUtc: "2027-03-01T00:00:00Z",
      notes: "Receipt retained",
      purchaseDateConfidence: "documented",
    },
    title: "WD Red Plus 8TB",
    status: {
      label: status,
      tone: status === "Healthy" ? "ok" : "warn",
      why:
        status === "Healthy"
          ? null
          : "TrueNAS reported unreadable sectors on this drive.",
    },
    poolGuid: "tank",
    poolName: "tank",
    memberState: "ONLINE",
    readErrors: 0,
    writeErrors: 0,
    checksumErrors: 0,
    bayConfirmed: false,
    temperatureC: 33,
    temperatureFetchedUtc: now(),
    acquisition: {
      revision: "original",
      installedUtc: "2022-03-01T00:00:00Z",
      installationConfidence: "documented",
      purchaseCost: 189,
      currency: "USD",
      costConfidence: "documented",
    },
    plan: null,
  };
}
function fixture(): StorageData {
  const a = drive("one", "nas-a", "Watch"),
    spare = drive("spare", "nas-a", "Spare");
  spare.title = "Shelf spare";
  spare.disk.nickname = null;
  spare.poolGuid = null;
  spare.poolName = null;
  spare.disk.enclosure = null;
  spare.disk.slot = null;
  spare.acquisition = null;
  const server: Server = {
    id: "nas-a",
    name: "TrueNAS",
    host: "nas.local",
    freshness: "Reporting",
    lastSuccessUtc: now(),
    poolCount: 1,
    driveCount: 2,
    rawCapacityBytes: 16000000000000,
    attentionCount: 1,
    alerts: [
      {
        id: 1,
        class: "SMART",
        level: "WARNING",
        message: "Drive SERIAL-one has unreadable sectors",
        lastSeenUtc: now(),
      },
    ],
    pools: [
      {
        guid: "tank",
        name: "tank",
        status: { label: "Needs a look", tone: "warn", why: null },
        layout: "RAIDZ2",
        memberCount: 6,
        toleratedFailures: 2,
        usedBytes: 10000000000000,
        sizeBytes: 20000000000000,
        unmappedMembers: 0,
        scan: {
          scanFunction: "SCRUB",
          scanState: "FINISHED",
          scanPercentage: 100,
          scanStartedUtc: "2026-10-28T00:00:00Z",
          scanEndedUtc: "2026-10-29T00:00:00Z",
          scanErrors: 0,
        },
        capacityOutlook: {
          status: "estimated",
          thresholdFraction: 0.8,
          projectedDateAtThreshold: "2028-03-15T00:00:00Z",
        },
        datasets: Array.from({ length: 8 }, (_, i) => ({
          name: "tank/dataset-" + i,
          usedBytes: 800000000000 - i * 1000000000,
        })),
        datasetStatus: "supported",
        drives: [a],
      },
    ],
    looseDrives: [spare],
  };
  const b = structuredClone(server);
  b.id = "nas-b";
  b.name = "Backup NAS";
  b.host = "backup.local";
  b.freshness = "Out of date";
  b.attentionCount = null;
  b.lastSuccessUtc = new Date(Date.now() - 2 * 86400000).toISOString();
  b.alerts = [];
  b.looseDrives = [];
  b.driveCount = 1;
  b.pools[0].name = "archive";
  b.pools[0].guid = "archive";
  b.pools[0].status = {
    label: "Last known: healthy",
    tone: "neutral",
    why: null,
  };
  b.pools[0].drives = [drive("two", "nas-b")];
  b.pools[0].drives[0].title = "Backup drive";
  b.pools[0].drives[0].poolName = "archive";
  b.pools[0].drives[0].status = {
    label: "Last known: healthy",
    tone: "neutral",
    why: "This server is out of date.",
  };
  return { smartEnabled: false, servicePlanYears: 5, servers: [server, b] };
}
const overview = {
  settings: { timezone: "America/Phoenix", capacityUnits: "decimal" },
  connections: [],
  disks: [],
  findings: [],
  plans: [],
  recovery: [],
  activity: [],
  recoveryAssessments: [],
  capabilities: [],
};
async function setup(page: Page, data: StorageData) {
  await page.route("**/api/v1/session", (r) =>
    r.fulfill({ json: { authenticated: true, setupRequired: false } }),
  );
  await page.route("**/api/v1/overview", (r) => r.fulfill({ json: overview }));
  await page.route("**/api/v1/storage/overview", (r) =>
    r.fulfill({ json: data }),
  );
  await page.route("**/api/v1/storage/drives/*/history", (r) =>
    r.fulfill({
      json: [
        {
          atUtc: "2022-03-01T00:00:00Z",
          kind: "installed",
          text: "Installed in Bay 2.",
        },
      ],
    }),
  );
}
async function audit(page: Page, name: string) {
  for (const theme of ["light", "dark"]) {
    await page.evaluate(
      (t) => (document.documentElement.dataset.theme = t),
      theme,
    );
    await page.addStyleTag({
      content:
        "*,*::before,*::after{animation:none!important;transition:none!important}",
    });
    await page.evaluate(
      () =>
        new Promise((resolve) =>
          requestAnimationFrame(() => requestAnimationFrame(resolve)),
        ),
    );
    const result = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect
      .soft(
        result.violations.map((v) => ({
          id: v.id,
          nodes: v.nodes.map((n) => ({
            target: n.target,
            summary: n.failureSummary,
          })),
        })),
        name + " " + theme,
      )
      .toEqual([]);
  }
}
test("001 server switching, pool context, datasets, alert chip, modal focus and responsive cards", async ({
  page,
}) => {
  const data = fixture();
  await setup(page, data);
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.goto("/storage");
  await expect(
    page.getByRole("heading", { name: "TrueNAS", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("RAIDZ2 · 6 drives · survives 2 drive losses"),
  ).toBeVisible();
  await expect(
    page.getByText("At the current rate, 80% full around Mar 2028"),
  ).toBeVisible();
  await page.getByText("What's using space", { exact: true }).click();
  await expect(page.getByText("Other", { exact: true })).toBeVisible();
  await page.getByText("1 NAS alerts", { exact: true }).click();
  await expect(
    page.getByText("Drive SERIAL-one has unreadable sectors"),
  ).toBeVisible();
  await audit(page, "Storage desktop");
  await page.screenshot({
    path: "test-results/storage-001-desktop.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: /Backup NAS/ }).click();
  await expect(
    page.getByRole("heading", { name: "archive", exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "tank", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByText("Can't reach this server right now.", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Forecast paused until this server reports again"),
  ).toBeVisible();
  await expect(
    page.locator(".storage-facts").getByText("Unknown", { exact: true }),
  ).toBeVisible();
  await audit(page, "Storage stale");
  await page.getByRole("button", { name: /TrueNAS/ }).click();
  const opener = page.getByRole("button", {
    name: "WD Red Plus 8TB",
    exact: true,
  });
  await opener.click();
  const panel = page.getByRole("dialog");
  await expect(
    panel.getByRole("heading", { name: "WD Red Plus 8TB", exact: true }),
  ).toBeFocused();
  await expect(
    panel.getByText("Reported model: WDC WD80EFZZ-68BTXN0"),
  ).toBeVisible();
  await expect(
    panel.getByText(
      "Based on your 5-year replacement plan, not a failure prediction.",
    ),
  ).toBeVisible();
  await expect(
    panel.getByRole("button", { name: "Import SMART report" }),
  ).toHaveCount(0);
  await expect(panel.getByText("Firmware:", { exact: false })).toHaveCount(0);
  for (let i = 0; i < 20; i++) {
    await page.keyboard.press("Tab");
    expect(
      await panel.evaluate((el) => el.contains(document.activeElement)),
    ).toBeTruthy();
  }
  await audit(page, "Drive panel");
  await page.screenshot({ path: "test-results/storage-001-panel.png" });
  await page.keyboard.press("Escape");
  await expect(panel).toHaveCount(0);
  await expect(opener).toBeFocused();
  await page.setViewportSize({ width: 900, height: 1000 });
  await audit(page, "Storage tablet");
  await page.screenshot({
    path: "test-results/storage-001-tablet.png",
    fullPage: true,
  });
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator(".storage-table").first()).toBeHidden();
  await expect(page.locator(".storage-drive-cards").first()).toBeVisible();
  await audit(page, "Storage mobile");
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBeTruthy();
  await page.screenshot({
    path: "test-results/storage-001-mobile.png",
    fullPage: true,
  });
  await page.getByRole("button", { name: /WD Red Plus 8TB Watch/ }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "TrueNAS · tank" }),
  ).toBeVisible();
  await audit(page, "Drive mobile");
  await page.getByRole("button", { name: "TrueNAS · tank" }).click();
  await expect(page.locator(".storage-tabs")).toBeVisible();
});
test("001 profile, nickname, bay confirmation, notes and replacement plans save and retain failed drafts", async ({
  page,
}) => {
  const data = fixture();
  await setup(page, data);
  const d = data.servers[0].pools[0].drives[0];
  let fail = true;
  let posted: Record<string, unknown> | null = null;
  await page.route("**/api/v1/storage/drives/one/profile", (r) => {
    posted = r.request().postDataJSON();
    if (fail) {
      fail = false;
      return r.fulfill({
        status: 409,
        json: {
          error: "This drive changed. Reload its details before saving.",
        },
      });
    }
    Object.assign(d.disk, {
      notes: posted!.notes,
      purchasedUtc: posted!.purchasedUtc,
      warrantyEndUtc: posted!.warrantyEndUtc,
      purchaseDateConfidence: posted!.datesEstimated
        ? "estimated"
        : "documented",
    });
    Object.assign(d.acquisition!, {
      revision: "saved",
      purchaseCost: posted!.purchaseCost,
      installedUtc: posted!.installedUtc,
      installationConfidence: posted!.datesEstimated
        ? "estimated"
        : "documented",
    });
    return r.fulfill({ json: {} });
  });
  await page.route("**/api/v1/storage/drives/one/nickname", (r) => {
    const n = r.request().postDataJSON().nickname;
    d.title = n;
    d.disk.nickname = n;
    return r.fulfill({ json: {} });
  });
  await page.route("**/api/v1/storage/drives/one/location", (r) => {
    posted = r.request().postDataJSON();
    d.bayConfirmed = true;
    return r.fulfill({ json: {} });
  });
  await page.route("**/api/v1/storage/drives/one/notes", (r) => {
    posted = r.request().postDataJSON();
    return r.fulfill({ json: {} });
  });
  await page.route("**/api/v1/plans", (r) => {
    posted = r.request().postDataJSON();
    d.plan = {
      id: "plan-one",
      budget: 250,
      currency: "USD",
      status: "planned",
    };
    return r.fulfill({ json: { id: "plan-one" } });
  });
  await page.goto("/storage?disk=one");
  const panel = page.getByRole("dialog");
  await panel.getByRole("button", { name: "Edit", exact: true }).click();
  await panel.getByLabel("Price paid (USD)").fill("199.99");
  await panel.getByLabel("These dates are estimates").check();
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  await expect(panel.getByRole("alert")).toContainText("This drive changed");
  await expect(panel.getByLabel("Price paid (USD)")).toHaveValue("199.99");
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  await expect(panel.getByRole("status")).toHaveText("Saved");
  expect(posted).toMatchObject({ purchaseCost: 199.99, datesEstimated: true });
  await expect(
    panel.getByText("(estimated)", { exact: false }).first(),
  ).toBeVisible();
  await panel.getByRole("button", { name: "Rename", exact: true }).click();
  await panel.getByLabel("Drive name").fill("Media bay two");
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  await expect(
    panel.getByRole("heading", { name: "Media bay two", exact: true }),
  ).toBeVisible();
  await panel
    .getByRole("button", { name: "I've checked it's here", exact: true })
    .click();
  await panel.getByLabel("Retype the physical serial").fill("SERIAL-one");
  await panel
    .getByLabel("What you checked")
    .fill("Physical serial and bay label");
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  await expect(panel.getByText("You confirmed this location.")).toBeVisible();
  await panel.getByRole("button", { name: "Add note", exact: true }).click();
  await panel.getByLabel("Note", { exact: true }).fill("Checked label today.");
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  expect(posted).toMatchObject({ text: "Checked label today." });
  await panel
    .getByRole("button", { name: "Plan a replacement", exact: true })
    .click();
  await panel
    .getByLabel("Reason", { exact: true })
    .fill("Owner replacement plan");
  await panel.getByLabel("Budget (USD)").fill("250");
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  await expect(
    panel.getByRole("button", { name: "Plan a replacement", exact: true }),
  ).toHaveCount(0);
  await expect(panel.getByRole("link", { name: /Open plan/ })).toHaveAttribute(
    "href",
    "/planning?plan=plan-one",
  );
  await page.route("**/api/v1/replacements", (r) => {
    posted = r.request().postDataJSON();
    return r.fulfill({ json: { id: "replacement-one" } });
  });
  await panel
    .getByRole("button", { name: "Mark as replaced", exact: true })
    .click();
  await panel.getByLabel("New drive", { exact: true }).selectOption("spare");
  await panel
    .getByLabel("Retype old drive serial (or WWN)", { exact: true })
    .fill("SERIAL-one");
  await panel.getByLabel(/Retype new drive serial/).fill("SERIAL-spare");
  await panel
    .getByLabel("Pool membership checked")
    .fill("Checked tank membership");
  await panel
    .getByLabel("Reason", { exact: true })
    .fill("Owner replacement plan");
  await panel
    .getByLabel("What you checked", { exact: true })
    .fill("Physical labels");
  await panel.getByRole("button", { name: "Save", exact: true }).click();
  expect(posted).toMatchObject({
    oldDiskId: "one",
    newDiskId: "spare",
    confirmedOldIdentity: "SERIAL-one",
    confirmedNewIdentity: "SERIAL-spare",
  });
});
test("001 loading, empty, unavailable, retry and retained session data", async ({
  page,
}) => {
  const data = fixture();
  await setup(page, data);
  let mode = "loading";
  await page.route("**/api/v1/storage/overview", (r) =>
    mode === "loading"
      ? undefined
      : mode === "failure"
        ? r.fulfill({ status: 503, json: { error: "Fixture outage" } })
        : r.fulfill({
            json: mode === "empty" ? { ...data, servers: [] } : data,
          }),
  );
  await page.goto("/storage");
  await expect(page.getByLabel("Loading storage")).toBeVisible();
  await audit(page, "Storage loading");
  mode = "failure";
  await page.reload();
  await expect(
    page
      .getByRole("region", { name: "Storage", exact: true })
      .getByRole("status"),
  ).toContainText("Nothing has been loaded yet.");
  await audit(page, "Storage unavailable");
  mode = "empty";
  await page.getByRole("button", { name: "Retry", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "No servers yet" }),
  ).toBeVisible();
  await expect(
    page.getByRole("link", { name: "Go to Settings" }),
  ).toHaveAttribute("href", "/settings#connections");
  await audit(page, "Storage empty");
  mode = "normal";
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "TrueNAS", exact: true }),
  ).toBeVisible();
  mode = "failure";
  await page.getByRole("button", { name: "Refresh", exact: true }).click();
  await expect(
    page
      .getByRole("region", { name: "Storage", exact: true })
      .getByRole("status"),
  ).toContainText("Showing what was loaded");
  await expect(
    page.getByRole("heading", { name: "TrueNAS", exact: true }),
  ).toBeVisible();
});
test("001 scan running, unknown, disabled datasets and SMART on states", async ({
  page,
}) => {
  const data = fixture(),
    s = data.servers[0],
    d = s.pools[0].drives[0];
  data.smartEnabled = true;
  s.pools[0].scan!.scanState = "SCANNING";
  s.pools[0].scan!.scanPercentage = 72;
  s.pools[0].datasetStatus = "disabled";
  d.status = {
    label: "Unknown",
    tone: "neutral",
    why: "This drive doesn't provide SMART data.",
  };
  await setup(page, data);
  await page.route("**/api/v1/storage/drives/*/trends", (r) =>
    r.fulfill({
      json: {
        trends: [
          {
            metric: "ata.pending_sectors",
            segments: [
              [
                { atUtc: "2026-10-01T00:00:00Z", value: 0 },
                { atUtc: "2026-10-10T00:00:00Z", value: 8 },
              ],
              [{ atUtc: "2026-10-11T00:00:00Z", value: 0 }],
            ],
            explanation: "A reset is not evidence of repair.",
          },
        ],
      },
    }),
  );
  await page.goto("/storage");
  await expect(page.getByText(/Scrub in progress · 72%/)).toBeVisible();
  await page.getByText("What's using space", { exact: true }).click();
  await expect(
    page.getByText("Dataset usage is turned off.", { exact: false }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "WD Red Plus 8TB", exact: true })
    .click();
  const panel = page.getByRole("dialog");
  await expect(
    panel.getByText("No SMART reading is available yet.", { exact: true }),
  ).toBeVisible();
  await expect(
    panel.getByRole("button", { name: "Import SMART report", exact: true }),
  ).toBeVisible();
  await expect(
    panel.getByRole("img", { name: /ata.pending_sectors:/ }),
  ).toBeVisible();
  await audit(page, "SMART unknown with reset trend");
  await page.keyboard.press("Escape");
  d.smart = {
    passed: true,
    readUtc: now(),
    skippedAsleep: true,
    error: null,
    powerOnHours: 20000,
    temperatureC: 32,
    pendingSectors: 8,
    reallocatedSectors: 0,
    nvmeWearPercent: null,
    firmware: "83.H0A83",
    lastSelfTest: null,
  };
  await page.reload();
  await page
    .getByRole("button", { name: "WD Red Plus 8TB", exact: true })
    .click();
  await expect(
    panel.getByText("Asleep, so it was skipped", { exact: false }),
  ).toBeVisible();
  await expect(panel.getByText("Firmware: 83.H0A83")).toBeVisible();
  await audit(page, "SMART asleep");
  await page.route("**/api/v1/disks/*/smart-reports/history", (r) =>
    r.fulfill({ json: { items: [], asOf: now(), nextOffset: null } }),
  );
  await panel
    .getByRole("button", { name: "Import SMART report", exact: true })
    .click();
  await expect(
    page.getByRole("dialog", {
      name: "Import an existing drive report",
      exact: true,
    }),
  ).toBeVisible();
  await audit(page, "SMART import dialog");
});


test("001.18 API power-on hours distinguish supported, unsupported and unavailable", async ({ page }) => {
  const data = fixture();
  const d = data.servers[0].pools[0].drives[0];
  await setup(page, data);
  const states = [
    { status: "supported", hours: 12345, expected: "12,345 hours", detail: "Reported by ATA SMART through the TrueNAS API." },
    { status: "unsupported", hours: null, expected: "Not supported via API", detail: "This drive is not supported by the TrueNAS ATA SMART attributes API." },
    { status: "denied", hours: null, expected: "Permission denied", detail: "The API credential cannot read SMART attributes for this drive." },
    { status: "not_reported", hours: null, expected: "Unknown", detail: "The drive did not return an unambiguous power-on hours counter." },
    { status: "stale", hours: 12345, expected: "12,345 hours (last known)", detail: "Last known API reading; collection is paused or out of date." },
    { status: "disabled", hours: null, expected: "Collection disabled", detail: "API power-on hours collection is off in Settings." },
  ];
  for (const state of states) {
    d.apiPowerOnHours = { status: state.status, hours: state.hours, fetchedUtc: now(), detail: state.detail };
    await page.goto("/storage?disk=one");
    const tile = page.getByRole("dialog").locator(".storage-tiles > div").filter({ has: page.locator("dt", { hasText: "Powered on" }) });
    await expect(tile.locator("dd").first()).toHaveText(state.expected);
    if (state.hours != null)
      await expect(tile.locator("dd").nth(1)).toHaveText("About 1 year, 4 months");
    else await expect(tile.getByText("About", { exact: false })).toHaveCount(0);
    if (state.hours != null) await expect(tile.locator("dd")).toHaveCount(2);
    else await expect(tile).toContainText(state.detail);
    await audit(page, "API hours " + state.status);
  }
});


test("001.18 Run now requires saved opt-in and reports running, errors and outcomes", async ({ page }) => {
  await setup(page, fixture());
  let saved = {
    id: 1, timezone: "UTC", capacityUnits: "decimal", inventoryMinutes: 15,
    driveAgeReviewYears: 5, warrantyNoticeDays: 30, smartMinutes: 90,
    readCachedTemperatures: false, readApiPowerOnHours: false, readDatasets: false,
    sampleRetentionDays: 90, rawRetentionDays: 14, backupsEnabled: false,
    backupDailyCopies: 7, backupWeeklyCopies: 4,
  };
  await page.route("**/api/v1/settings", r => {
    if (r.request().method() === "PUT") { saved = r.request().postDataJSON(); return r.fulfill({ json: saved }); }
    return r.fulfill({ json: { settings: saved, backupDestinationConfigured: false } });
  });
  await page.route("**/api/v1/backups", r => r.fulfill({ json: [] }));
  await page.route("**/api/v1/connections", r => r.fulfill({ json: [] }));
  await page.route("**/api/v1/notifications", r => r.fulfill({ json: { channel: null, deliveries: [], attempts: [] } }));
  let fail = true, calls = 0;
  let finish: (() => void) | undefined;
  await page.route("**/api/v1/storage/power-on-hours/run", async r => {
    calls++;
    expect(r.request().method()).toBe("POST");
    expect(r.request().headers()["x-homelab-request"]).toBe("1");
    if (fail) return r.fulfill({ status: 409, json: { error: "A power-on hours read is already running." } });
    await new Promise<void>(resolve => { finish = resolve; });
    return r.fulfill({ json: { servers: [
      { name: "TrueNAS", completed: true, hoursAvailable: 4, unsupported: 2, denied: 1, unavailable: 0 },
      { name: "Backup NAS", completed: false, hoursAvailable: 0, unsupported: 0, denied: 0, unavailable: 0 },
    ] } });
  });
  await page.goto("/settings");
  const run = page.getByRole("button", { name: "Run now", exact: true });
  await expect(run).toBeDisabled();
  await page.getByLabel("Read power-on hours through the TrueNAS API", { exact: true }).check();
  await expect(run).toBeDisabled();
  await expect(page.getByText("Save settings to enable Run now.")).toBeVisible();
  await page.getByRole("button", { name: "Save settings", exact: true }).click();
  await expect(run).toBeEnabled();
  await run.click();
  await expect(page.getByText("A power-on hours read is already running.", { exact: true })).toBeVisible();
  await expect(run).toBeEnabled();
  fail = false;
  await page.getByLabel("Disk inventory interval (minutes)").fill("99");
  await run.click();
  await expect(page.getByRole("button", { name: "Running…", exact: true })).toBeDisabled();
  await expect(page.getByText("Reading power-on hours…", { exact: true })).toBeVisible();
  await expect.poll(() => Boolean(finish)).toBe(true);
  finish!();
  await expect(page.getByText("TrueNAS: finished.", { exact: false })).toContainText("4 drives returned hours; 2 unsupported; 1 permission denied");
  await expect(page.getByText("Backup NAS: no new reading was saved.", { exact: false })).toBeVisible();
  await expect(run).toBeEnabled();
  expect(calls).toBe(2);
  expect(saved.inventoryMinutes).toBe(15);
  await expect(page.getByLabel("Disk inventory interval (minutes)")).toHaveValue("99");
  await page.setViewportSize({ width: 390, height: 844 });
  await run.scrollIntoViewIfNeeded();
  await expect(run).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await audit(page, "Run power-on hours now");
});


test("001 storage CSV export downloads all servers independently of selection and handles failure", async ({ page }) => {
  await setup(page, fixture());
  const csv = '\ufeff"record_type","record_id","server_id","drive_id","name","model","serial","alias","pool","capacity_bytes","location","notes","record_json"\r\n"Connections","a","nas-a","","Media NAS","","","","","","","","{}"\r\n"Connections","b","nas-b","","Backup NAS","","","","","","","","{}"\r\n';
  let fail = true;
  let release: (() => void) | undefined;
  await page.route("**/api/v1/storage/export.csv", async r => {
    expect(r.request().method()).toBe("GET");
    expect(new URL(r.request().url()).search).toBe("");
    if (fail) return r.fulfill({ status: 500, json: { error: "Fixture export failure" } });
    await new Promise<void>(resolve => { release = resolve; });
    return r.fulfill({ contentType: "text/csv; charset=utf-8", body: csv });
  });
  await page.goto("/storage?server=nas-b");
  const button = page.getByRole("button", { name: "Export CSV — all storage inventory", exact: true });
  await expect(button).toBeVisible();
  const buttonBox = await button.boundingBox();
  const titleBox = await page.getByRole("heading", { name: "Storage", exact: true }).boundingBox();
  expect(Math.abs(buttonBox!.y - titleBox!.y)).toBeLessThan(100);
  await button.click();
  await expect(page.getByRole("alert")).toContainText("Storage couldn't be exported.");
  fail = false;
  await page.setViewportSize({ width: 390, height: 844 });
  const downloaded = page.waitForEvent("download");
  await button.click();
  await expect(page.getByRole("button", { name: "Exporting… all storage inventory", exact: true })).toBeDisabled();
  await expect.poll(() => Boolean(release)).toBe(true);
  release!();
  const file = await downloaded;
  expect(file.suggestedFilename()).toMatch(/^homelab-storage-.*\.csv$/);
  expect(await readFile((await file.path())!, "utf8")).toBe(csv);
  await expect(button).toBeEnabled();
  await expect(page.getByRole("alert")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "archive", exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await audit(page, "Storage export mobile");
  await page.screenshot({ path: "test-results/storage-export-mobile.png", fullPage: true });
});


test("001.09 Watch explains the actual pending and uncorrectable sector alerts", async ({ page }) => {
  const data = fixture();
  const d = data.servers[0].pools[0].drives[0];
  d.status = { label: "Watch", tone: "warn", why: "TrueNAS reports: Device: /dev/sdk [SAT], 5 Currently unreadable (pending) sectors. Device: /dev/sdk [SAT], 5 Offline uncorrectable sectors. Reallocated-sector counts and the overall SMART result are shown only when separately reported." };
  await setup(page, data);
  await page.goto("/storage?disk=one");
  const reason = page.getByRole("dialog").locator(".storage-panel-body > .storage-banner");
  await expect(reason).toContainText("5 Currently unreadable (pending) sectors");
  await expect(reason).toContainText("5 Offline uncorrectable sectors");
  await expect(reason).not.toContainText("A native NAS alert names this drive");
  await expect(reason).toContainText("overall SMART result");
  await audit(page, "Specific Watch alert reason");
  await page.setViewportSize({ width: 390, height: 844 });
  await expect(page.locator(".storage-panel-mobile .storage-banner")).toContainText("5 Currently unreadable");
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
});
