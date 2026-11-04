import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import type { HwDetail, HwMetric, HwComponent } from "../src/features/hardware/hardwareModel";
const now = () => new Date().toISOString();
const before = (days: number) =>
  new Date(Date.now() - days * 86400000).toISOString();
function metric(overrides: Partial<HwMetric> = {}): HwMetric {
  return {
    source: "collected",
    average: 20,
    latest: 18,
    peak: 43,
    p95: 40,
    previousAverage: null,
    daysAboveLevel: 0,
    readingsAboveLevel: 0,
    readingCount: 0,
    partial: false,
    reviewLevel: 80,
    status: null,
    series: [3, 2, 1].map((days, i) => ({
      atUtc: before(days),
      value: 20 + i * 5,
      source: "collected",
      readingId: null,
    })),
    cacheAverage: null,
    freeAverage: null,
    ...overrides,
  };
}
function component(kind = "processor", id = "cpu"): HwComponent {
  return {
    record: {
      id,
      serverId: "a",
      revision: "rev",
      kind,
      detectedKey: kind,
      detectedName: kind === "memory" ? "Memory modules" : "Intel Xeon",
      detectedSpec:
        kind === "memory"
          ? "64 GiB physical memory"
          : "6 physical cores · 12 logical cores",
      detectedTotalBytes: kind === "memory" ? 64 * 1073741824 : null,
      name: "",
      model: "",
      spec: "",
      notes: "",
      pricePaid: 289,
      purchasedUtc: before(1500),
      installedUtc: before(1400),
      warrantyEndUtc: before(400),
      datesEstimated: true,
      solderedBytes: null,
      maxSupportedBytes: kind === "memory" ? 128 * 1073741824 : null,
      retiredUtc: null,
    },
    title: kind === "memory" ? "Memory modules" : "Intel Xeon",
    source: "Detected",
    group: "Compute",
    status: {
      label: "Watch",
      tone: "warn",
      why: "Temperature exceeded your 80 °C review level on 3 days. Check cooling and airflow.",
    },
    enteredTotalBytes: kind === "memory" ? 64 * 1073741824 : null,
    slots:
      kind === "memory"
        ? [
            { name: "DIMM 1", sizeBytes: 32 * 1073741824 },
            { name: "DIMM 2", sizeBytes: 32 * 1073741824 },
            { name: "DIMM 3", sizeBytes: null },
            { name: "DIMM 4", sizeBytes: null },
          ]
        : [],
    history: [
      {
        atUtc: before(3),
        kind: "status",
        text: "Watch: CPU temperature exceeded your review level.",
      },
    ],
  };
}
function detail(): HwDetail {
  return {
    server: {
      id: "a",
      name: "media-nas",
      host: "truenas.lan",
      platform: "truenas",
      version: "24.10.2.4",
      freshness: "Reporting",
      lastSuccessUtc: now(),
      uptimeSeconds: 41 * 86400,
      componentCount: 2,
      watchCount: 1,
      nearLimitCount: 0,
    },
    coverage: {
      samples: 8604,
      expectedSamples: 8640,
      firstSampleUtc: before(30),
      collectionStartedUtc: before(60),
      days: 30,
      coveredDays: 30,
      gaps: [{ startUtc: before(3), endUtc: before(2.9) }],
      downsampled: false,
    },
    cpu: metric(),
    memory: metric({
      average: 18.4 * 1073741824,
      latest: 18 * 1073741824,
      peak: 25 * 1073741824,
      reviewLevel: 80,
      cacheAverage: 40.1 * 1073741824,
      freeAverage: 5.5 * 1073741824,
      series: metric().series.map((p) => ({ ...p, value: 18.4 * 1073741824 })),
    }),
    temperature: metric({
      average: 52,
      latest: 50,
      peak: 86,
      daysAboveLevel: 3,
      status: { label: "Watch", tone: "warn", why: "Hot" },
    }),
    power: metric({
      source: "none",
      average: null,
      latest: null,
      peak: null,
      p95: null,
      reviewLevel: null,
      series: [],
    }),
    totalBytes: 64 * 1073741824,
    components: [component(), component("memory", "memory")],
    readings: [],
    cpuLevel: 80,
    memoryLevel: 80,
    temperatureLevel: 80,
    sustainedMinutes: 60,
  };
}
async function setup(
  page: Page,
  data: HwDetail,
  options: {
    empty?: boolean;
    failed?: boolean;
    loading?: boolean;
    flags?: boolean;
  } = {},
) {
  await page.route("**/api/v1/session", (r) =>
    r.fulfill({ json: { authenticated: true, setupRequired: false } }),
  );
  await page.route("**/api/v1/overview", (r) =>
    r.fulfill({
      json: {
        connections: [],
        disks: [],
        findings: [],
        plans: [],
        recovery: [],
        activity: [],
        capabilities: [],
        recoveryAssessments: [],
        settings: { timezone: "America/Phoenix", capacityUnits: "binary" },
      },
    }),
  );
  await page.route("**/api/v1/hardware/servers", (r) =>
    options.loading
      ? new Promise(() => {})
      : options.failed
        ? r.fulfill({ status: 503, json: { error: "Service unavailable" } })
        : r.fulfill({
            json: {
              hwmetrics: options.flags !== false,
              synology: options.flags !== false,
              servers: options.empty
                ? []
                : [
                    data.server,
                    {
                      ...data.server,
                      id: "b",
                      name: "backup-box",
                      componentCount: 0,
                      watchCount: 0,
                    },
                  ],
            },
          }),
  );
  await page.route("**/api/v1/hardware/servers/*?range=*", async (r) => {
    expect(r.request().headers()["content-type"]).toBeUndefined();
    const url = new URL(r.request().url()),
      id = url.pathname.split("/").at(-1),
      days =
        url.searchParams.get("range") === "24h"
          ? 1
          : Number(url.searchParams.get("range")!.slice(0, -1));
    await r.fulfill({
      json: {
        ...data,
        server: {
          ...data.server,
          id,
          name: id === "b" ? "backup-box" : data.server.name,
        },
        components: id === "b" ? [] : data.components,
        coverage: { ...data.coverage, days },
      },
    });
  });
}
async function audit(page: Page) {
  for (const theme of ["light", "dark"]) {
    await page.evaluate(
      (t) => (document.documentElement.dataset.theme = t),
      theme,
    );
    await page.addStyleTag({
      content:
        "*,*::before,*::after{animation:none!important;transition:none!important}",
    });
    const result = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
      .analyze();
    expect(
      result.violations.map((v) => ({
        id: v.id,
        nodes: v.nodes.map((n) => ({
          target: n.target,
          summary: n.failureSummary,
        })),
      })),
    ).toEqual([]);
  }
}
for (const width of [1440, 900, 390])
  test(`002 normal, panel, slots, source and accessibility at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 1000 });
    await setup(page, { ...detail(), investment: [{ currency: "USD", amount: 289.5 }] });
    await page.goto("/hardware");
    await expect(page.locator(".hw-facts").getByText("Total Investment")).toBeVisible();
    await expect(page.locator(".hw-facts").getByText("$289.50")).toBeVisible();
    await expect(
      page.getByRole("heading", { name: "How it’s running", exact: true }),
    ).toBeVisible();
    await expect(
      page.getByText("ZFS cache 40.1 GiB", { exact: false }),
    ).toBeVisible();
    await audit(page);
    expect(
      await page.evaluate(
        () => document.documentElement.scrollWidth <= innerWidth,
      ),
    ).toBeTruthy();
    await page.screenshot({
      path: `test-results/hardware-${width}.png`,
      fullPage: true,
    });
    const opener = page.getByRole("button", {
      name: "Intel Xeon",
      exact: true,
    });
    await opener.click();
    const panel =
      width <= 700 ? page.locator(".hw-fullpage") : page.getByRole("dialog");
    await expect(
      panel.getByRole("heading", { name: "Intel Xeon", exact: true }),
    ).toBeFocused();
    await expect(panel.getByText(/Why “Watch”/)).toBeVisible();
    await audit(page);
    await page.screenshot({
      path: `test-results/hardware-panel-${width}.png`,
      fullPage: true,
    });
    await panel
      .getByRole("button", {
        name: width <= 700 ? "← Back · Close" : "Close",
        exact: true,
      })
      .click();
    await expect(opener).toBeFocused();
    await page
      .getByRole("button", { name: "Memory modules", exact: true })
      .click();
    await expect(
      page.getByRole("heading", { name: "Memory slots" }),
    ).toBeVisible();
    await expect(
      page.getByText("The detected total matches the modules you entered."),
    ).toBeVisible();
    await audit(page);
  });
for (const state of [
  "stale",
  "collecting",
  "manual",
  "near-limit",
  "flags-off",
  "empty",
  "loading",
  "unavailable",
])
  test(`002 state ${state} in both themes`, async ({ page }) => {
    const d = detail();
    if (state === "stale") {
      d.server.freshness = "Out of date";
      d.server.lastSuccessUtc = before(2);
      d.server.watchCount = null;
      d.server.nearLimitCount = null;
      d.components.forEach((c) => {
        c.status.label = "Last known: watch";
        c.status.tone = "neutral";
      });
    }
    if (state === "collecting") {
      for (const m of [d.cpu, d.memory, d.temperature]) {
        m.partial = true;
        m.average = null;
      }
      d.coverage.collectionStartedUtc = before(0.25);
      d.coverage.coveredDays = 1;
      d.coverage.samples = 72;
    }
    if (state === "manual" || state === "near-limit") {
      d.server.platform = "synology";
      d.server.name = "ds920";
      if (state === "manual") d.server.freshness = "Not connected";
      d.cpu = metric({
        source: "entered",
        readingsAboveLevel: 2,
        readingCount: 4,
        average: 73,
        series: metric().series.map((p) => ({ ...p, source: "entered" })),
        status: {
          label: "Near its limit",
          tone: "info",
          why: "Nothing points to a fault.",
        },
      });
      d.memory.cacheAverage = null;
      d.memory.freeAverage = null;
    }
    if (state === "flags-off") {
      d.cpu = metric({ source: "none", series: [] });
      d.memory = metric({ source: "none", series: [] });
      d.temperature = metric({ source: "none", series: [] });
      d.components.forEach((c) => {
        c.status = {
          label: "Not monitored",
          tone: "neutral",
          why: "No measurements",
        };
      });
      d.coverage.samples = 0;
    }
    await setup(page, d, {
      empty: state === "empty",
      failed: state === "unavailable",
      loading: state === "loading",
      flags: state !== "flags-off",
    });
    await page.goto("/hardware");
    const expectation =
      state === "empty"
        ? page.getByRole("heading", { name: "No servers yet" })
        : state === "unavailable"
          ? page.getByText("Hardware couldn’t be loaded.")
          : state === "loading"
            ? page.getByLabel("Loading hardware")
            : page.getByRole("heading", {
                name: "How it’s running",
                exact: true,
              });
    await expect(expectation).toBeVisible();
    if (state === "flags-off") {
      await expect(page.getByText(/inventory checked/)).toBeVisible();
      await expect(
        page.getByText("Inventory detected", { exact: true }),
      ).toBeVisible();
    }
    if (state === "stale")
      await expect(page.locator(".hw-facts > div").filter({ hasText: "Needs a look" }).locator("dd")).toContainText(
        "Unknown",
      );
    if (state === "manual")
      await expect(page.getByText(/This server isn’t connected/)).toBeVisible();
    if (state === "flags-off")
      await expect(
        page.locator(".hw-unmeasured-title").filter({ hasText: "No readings" }),
      ).toHaveCount(3);
    await audit(page);
  });
for (const width of [1440, 390])
  test(`002 forms save reading, edit component, note, replace, and prefill existing Planning at ${width}px`, async ({
    page,
  }) => {
    await page.setViewportSize({ width, height: 1000 });
    const d = detail();
    await setup(page, d);
    const writes: { path: string; body: Record<string, unknown> }[] = [];
    await page.route("**/api/v1/hardware/servers/a/**", async (r) => {
      if (r.request().method() === "GET") return r.fallback();
      writes.push({
        path: r.request().url(),
        body: r.request().postDataJSON(),
      });
      await r.fulfill({ json: { id: "cpu" } });
    });
    await page.goto("/hardware?server=a");
    const cpu = page.locator("tbody tr").filter({ has: page.getByRole("button", { name: /^Edit component Intel Xeon/i }) });
    await cpu.getByRole("button", { name: /^Edit component / }).click();
    await expect(
      page.getByText("Detected values stay unchanged.", { exact: false }),
    ).toBeVisible();
    await page.getByLabel("Key specs (you entered)").fill("Socket LGA1151");
    await audit(page);
    await page
      .getByRole("button", { name: "Save details", exact: true })
      .click();
    await expect(
      page.getByRole("status").filter({ hasText: "Saved" }),
    ).toBeVisible();
    expect(writes.at(-1)!.body.spec).toEqual("Socket LGA1151");
    expect(writes.at(-1)!.body).not.toHaveProperty("detectedName");
    await page.goto("/hardware?server=a&component=cpu");
    await page.getByRole("button", { name: "Add note", exact: true }).click();
    await page.getByLabel("Note", { exact: true }).fill("Cleaned case filter");
    await page.getByRole("button", { name: "Save note" }).click();
    expect(writes.at(-1)!.body.text).toEqual("Cleaned case filter");
    await page
      .getByRole("button", { name: "Mark as replaced", exact: true })
      .click();
    await page.getByLabel("Name", { exact: true }).fill("Successor CPU");
    await audit(page);
    await page.getByRole("button", { name: "Record replacement" }).click();
    await expect(
      page.getByRole("heading", { name: "Replace Intel Xeon", exact: true }),
    ).toHaveCount(0);
    expect(writes.at(-1)!.path).toContain("/replace");
    expect(writes.at(-1)!.body).toHaveProperty("successor");
    await page
      .getByRole("button", {
        name: width <= 700 ? "← Back · Close" : "Close",
        exact: true,
      })
      .click();
    await page
      .getByRole("button", { name: "Add reading", exact: true })
      .click();
    await page.getByLabel("Power draw (W)").fill("36");
    await page.getByText("An average over a period", { exact: true }).click();
    await page.getByLabel("Period (minutes)").fill("60");
    await audit(page);
    await page
      .getByRole("button", { name: "Save reading", exact: true })
      .click();
    expect(writes.at(-1)!.body.powerWatts).toBe(36);
    expect(writes.at(-1)!.body.cpuPercent).toBeNull();
    await page
      .getByRole("button", { name: "Add component", exact: true })
      .click();
    await page.getByLabel("Name", { exact: true }).fill("Power supply");
    await audit(page);
    await page
      .getByRole("button", { name: "Add component", exact: true })
      .last()
      .click();
    await page.getByRole("button", { name: "Intel Xeon", exact: true }).click();
    await page.getByRole("link", { name: "Plan an upgrade" }).click();
    await expect(page.getByRole("dialog").getByLabel("Title", {exact:true})).toHaveValue(
      "Upgrade Intel Xeon",
    );
    await expect(page.getByRole("dialog").getByLabel("Notes", {exact:true})).toHaveValue(
      /media-nas/,
    );
  });
test("002 server switches and failed reload keep data scoped and last known", async ({
  page,
}) => {
  const d = detail();
  await setup(page, d);
  await page.goto("/hardware");
  await expect(
    page.getByRole("button", { name: "Intel Xeon", exact: true }),
  ).toBeVisible();
  let release!: () => void;
  const pending = new Promise<void>((resolve) => (release = resolve));
  await page.route("**/api/v1/hardware/servers/b?range=*", async (r) => {
    await pending;
    await r.fulfill({
      json: {
        ...d,
        server: { ...d.server, id: "b", name: "backup-box" },
        components: [],
        cpu: metric({ average: 2 }),
      },
    });
  });
  await page.getByRole("button", { name: /backup-box/ }).click();
  await expect(
    page.getByRole("button", { name: "Intel Xeon", exact: true }),
  ).toHaveCount(0);
  release();
  await expect(
    page.getByRole("heading", { name: "backup-box", exact: true }),
  ).toBeVisible();
  await page.route("**/api/v1/hardware/servers/*?range=*", (r) =>
    r.fulfill({ status: 503, json: { error: "Unavailable" } }),
  );
  await page.getByRole("button", { name: "7 days", exact: true }).click();
  await expect(page.getByText("Hardware couldn’t be loaded.")).toBeVisible();
  await audit(page);
});

test("002 range coverage and entered dots stay separate from collected averages", async ({
  page,
}) => {
  const d = detail();
  d.coverage.downsampled = true;
  d.lastCollectionUtc = "2026-11-03T20:18:44Z";
  d.cpu.readingCount = 1;
  d.cpu.series.push({
    atUtc: before(1),
    value: 99,
    source: "entered",
    readingId: "spot-reading",
  });
  await setup(page, d);
  await page.goto("/hardware");
  await expect(page.locator(".hw-metrics .hw-dot")).toHaveCount(1);
  await expect(
    page.getByText(
      "Also shown: 1 dots entered by you; excluded from collected averages.",
    ),
  ).toBeVisible();
  await page.getByRole("button", { name: "90 days", exact: true }).click();
  await expect(page.getByText("90-day average", { exact: true })).toHaveCount(
    3,
  );
  await expect(
    page.getByText(/Last collection run.*1:18:44 PM MST/),
  ).toBeVisible();
  await expect(page.getByText(/Previous 90/)).toHaveCount(0);
  await audit(page);
});

test("Hardware Refresh polls the selected server and reloads its readings", async ({ page }) => {
  const d = detail();
  await setup(page, d);
  await page.route("**/api/v1/connections/a/refresh", async (route) => {
    expect(route.request().method()).toBe("POST");
    expect(route.request().postDataJSON()).toEqual({ target: "hardware" });
    await route.fulfill({ status: 204 });
  });
  await page.goto("/hardware?server=a");
  await expect(page.getByRole("heading", { name: d.server.name, exact: true })).toBeVisible();
  await expect(page.getByRole("button", { name: "Refresh now", exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Refresh", exact: true })).toBeVisible();
  const reloaded = page.waitForRequest("**/api/v1/hardware/servers/a?range=30d");
  await page.getByRole("button", { name: "Refresh", exact: true }).click();
  await reloaded;
  await expect(page.getByText("NAS collection complete.")).toBeVisible();
});


test("UPS readings stay separate from wattage and label unknown and stale samples", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  const data = detail();
  data.ups = {
    status: "reporting",
    load: { value: 24, observedUtc: now() },
    charge: { value: 98, observedUtc: before(1) },
    runtime: { value: 1260, observedUtc: now() },
    nominalVa: 1500,
  };
  await setup(page, data);
  await page.goto("/hardware");
  const ups = page.getByRole("region", { name: "UPS readings" });
  await expect(ups).toContainText("24%");
  await expect(ups).toContainText("98%");
  await expect(ups).toContainText("Last known");
  await expect(ups).not.toContainText("MST");
  await expect(ups).toContainText("About 21 min");
  await expect(ups).toContainText("1,500 VA");
  await expect(page.getByText("Wattage not measured", { exact: true })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBeTruthy();
  await audit(page);
});

test("UPS setup binds an estimate to one detected device", async ({ page }) => {
  const data = detail();
  const key = "A".repeat(64);
  const first = { key, name: "ups", model: "Tripp Lite UPS", hasSerial: false,
    nominalVa: 1500, load: { value: 29, observedUtc: now() }, charge: null, runtime: null };
  data.ups = { status: "reporting", devices: [
    first, { ...first, key: "B".repeat(64), name: "other-ups", hasSerial: true },
  ] };
  await setup(page, data);
  await page.route("**/api/v1/hardware/servers/a/components", route => {
    const input = route.request().postDataJSON();
    expect(input).toMatchObject({ kind: "ups", upsDeviceKey: key, upsPowerFactor: 0.6, confirmUpsWithoutSerial: true });
    const linked = component("ups", "linked-ups");
    Object.assign(linked.record, input);
    linked.title = input.name;
    data.electricityRateUsdPerKwh = 0.15;
    data.upsMonthlyPower = [{ deviceKey: key, medianWatts: 270, sampleCount: 10, daysWithSamples: 2, estimatedCostUsd: 29.16 }];
    data.components.push(linked);
    return route.fulfill({ json: { id: linked.record.id } });
  });
  await page.goto("/hardware");
  await page.getByRole("region", { name: "UPS readings for ups", exact: true }).getByRole("button", { name: "Set up this UPS" }).click();
  const form = page.getByRole("dialog", { name: "Set up this UPS" });
  await form.getByLabel("Name", { exact: true }).fill("Media UPS");
  await form.getByLabel("Power factor for estimates").fill("0.6");
  await form.getByRole("checkbox", { name: /Confirm this is my UPS/ }).check();
  await expect(form.getByText(/261 W estimated/)).toBeVisible();
  await form.getByRole("button", { name: "Add component", exact: true }).click();
  await expect(page.getByRole("region", { name: "UPS readings for Media UPS" })).toContainText("261 W estimated");
  const linkedUps = page.getByRole("region", { name: "UPS readings for Media UPS" });
  await expect(linkedUps).toContainText("$29.16 per 30 days");
  await expect(linkedUps).not.toContainText("270 W median");
  await linkedUps.getByRole("button", { name: "How monthly cost is calculated" }).focus();
  await expect(page.getByRole("tooltip")).toContainText("270 W median");
  await page.keyboard.press("Escape");
  await expect(page.getByRole("tooltip")).toHaveCount(0);
  await expect(linkedUps).toHaveText(/Nominal rating[\s\S]*261 W estimated/);
  await expect(page.getByText("Wattage not measured", { exact: true })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Add a reading", exact: true })).toHaveCount(0);
  const powerHeading = page.locator(".hw-card-heading").filter({ has: page.getByRole("heading", { name: "Power draw", exact: true }) });
  await powerHeading.getByRole("button", { name: "Edit UPS component Media UPS" }).click();
  const edit = page.getByRole("dialog", { name: "Edit Media UPS" });
  await expect(edit.getByLabel("Power factor for estimates")).toHaveValue("0.6");
  await edit.getByRole("button", { name: "Cancel" }).click();
  await expect(page.getByRole("region", { name: "UPS readings for other-ups" })).not.toContainText("W estimated");
  await audit(page);
});
