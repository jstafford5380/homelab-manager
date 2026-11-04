import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import calm from "../../../docs/features/004-dashboard-layout/fixtures/calm.json" with { type: "json" };
import empty from "../../../docs/features/004-dashboard-layout/fixtures/empty.json" with { type: "json" };
import findings from "../../../docs/features/004-dashboard-layout/fixtures/findings.json" with { type: "json" };
import fleet from "../../../docs/features/004-dashboard-layout/fixtures/fleet.json" with { type: "json" };
import loading from "../../../docs/features/004-dashboard-layout/fixtures/loading.json" with { type: "json" };
import partial from "../../../docs/features/004-dashboard-layout/fixtures/partial.json" with { type: "json" };

const cases = { calm, empty, findings, fleet, loading, partial };

for (const state of Object.keys(cases) as (keyof typeof cases)[]) {
  test(`004 dashboard layout · ${state}`, async ({ page }) => {
    const fixture = cases[state];
    const { _mock, planning, ...overview } = fixture;
    await page.clock.setFixedTime(new Date(_mock.now));
    await page.emulateMedia({ reducedMotion: "reduce" });
    await page.route("**/api/v1/session", (route) =>
      route.fulfill({ json: { authenticated: true, setupRequired: false } }),
    );
    let overviewUnavailable = false;
    let scanReports: unknown = overview.scanReports;
    await page.route("**/api/v1/overview", (route) =>
      overviewUnavailable
        ? route.fulfill({ status: 503, json: { error: "Fixture outage" } })
        : route.fulfill({
            json: {
              ...overview,
              scanReports,
              settings: { timezone: "UTC" },
              disks: [],
              plans: [],
              recovery: [],
              recoveryAssessments: [],
              activity: [],
            },
          }),
    );
    await page.route("**/api/v1/planning/review", (route) => {
      if (state === "loading") return;
      if (state === "partial") return route.fulfill({ status: 503 });
      const reviews = [
        ...Array.from({ length: 5 }, () => ({
          reasons: [{ kind: "age-review" }],
        })),
        ...Array.from({ length: 3 }, () => ({
          reasons: [{ kind: "age-unknown" }],
        })),
      ];
      return route.fulfill({
        json: state === "empty" ? planning : { ...planning, reviews },
      });
    });
    await page.setViewportSize({ width: 1400, height: 950 });
    await page.goto("/");

    const signals = page.getByRole("list", { name: "Condition summary" });
    const attention = page.getByRole("region", {
      name: "Needs your attention",
    });
    const scans = page.getByRole("region", {
      name: "Scrub and resilver reports",
    });
    const systems = page.getByRole("region", { name: "Systems" });
    const upcoming = page.getByRole("region", { name: "Coming up" });
    const readiness = page.getByRole("region", { name: "Readiness" });

    if (state === "empty") {
      await expect(
        page.getByRole("heading", { name: "Meet your NAS" }),
      ).toBeVisible();
      await expect(systems).toHaveCount(0);
      await expect(readiness).toHaveCount(0);
    } else {
      await expect(systems).toBeVisible();
      await expect(readiness).toBeVisible();
    }
    if (state === "findings" || state === "fleet") {
      await expect(attention).toBeVisible();
      await expect(
        signals.getByRole("link", { name: /Findings/ }),
      ).toBeVisible();
      expect((await attention.boundingBox())!.y).toBeLessThan(
        (await systems.boundingBox())!.y,
      );
    } else {
      await expect(attention).toHaveCount(0);
      await expect(signals.getByRole("link", { name: /Findings/ })).toHaveCount(
        0,
      );
    }
    if (state === "fleet") {
      await expect(scans.getByText("Recent source report")).toBeVisible();
      expect((await scans.boundingBox())!.y).toBeLessThan(
        (await systems.boundingBox())!.y,
      );
      await expect(readiness).toContainText("1 scan report in progress");
      const table = systems.getByRole("table");
      await expect(table.getByRole("row")).toHaveCount(4);
      await expect(
        table.getByRole("row").filter({ hasText: "Backup NAS" }),
      ).toContainText("Out of date");
      await expect(
        table.getByRole("link", { name: "Backup NAS" }),
      ).toHaveAttribute(
        "href",
        `/settings/connections/${fleet.connections[1].id}`,
      );
    } else {
      await expect(scans).toHaveCount(0);
    }
    if (state === "calm") {
      const sources = systems
        .getByRole("list", { name: "Sources for Media NAS" })
        .getByRole("listitem");
      await expect(sources).toHaveCount(calm.capabilities.length);
      await expect(sources.nth(0)).toContainText("Turned off");
      await expect(sources.nth(1)).toContainText("Unsupported");
      await expect(sources.nth(2)).toContainText("Unsupported");
      await expect(sources.nth(3)).toContainText("Reporting");
      await expect(
        systems.getByText("Source gaps").locator(".."),
      ).toContainText("3");
      await expect(
        systems.getByRole("link", { name: "Review coverage" }),
      ).toHaveAttribute("href", "/storage");
      await expect(readiness).toContainText("No scrub or resilver in progress");
      await expect(readiness).toContainText(
        "This does not establish that all pools were checked.",
      );
      const main = (await systems.boundingBox())!;
      const rail = (await readiness.boundingBox())!;
      expect(
        Math.abs(main.y + main.height - rail.y - rail.height),
      ).toBeLessThanOrEqual(120);
    }
    if (state === "loading") {
      await expect(upcoming).toContainText("Checking planning reminders…");
      await expect(readiness).toContainText("Spares unknown");
    }
    if (state === "partial") {
      await expect(upcoming).toContainText(
        "Drive age reminders are unavailable right now.",
      );
      await expect(readiness).toContainText("Spares unknown");
      await expect(readiness).toContainText("Scan summary unavailable");
      overviewUnavailable = true;
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await expect(page.getByRole("alert")).toContainText(
        "Showing last-known overview data",
      );
    }

    for (const width of [1400, 900, 390]) {
      await page.setViewportSize({ width, height: 950 });
      const tiles = signals.getByRole("listitem");
      const first = (await tiles.first().boundingBox())!;
      const last = (await tiles.last().boundingBox())!;
      if (width === 390) {
        if (await tiles.count() === 5) {
          expect(last.width).toBeGreaterThan(first.width * 2);
        } else {
          expect(last.width).toBeCloseTo(first.width, 0);
        }
        expect(last.y).toBeGreaterThan(first.y);
        expect(
          await page.evaluate(() => document.documentElement.scrollWidth),
        ).toBeLessThanOrEqual(width);
        if (state === "fleet") {
          await expect(
            systems.getByRole("row").nth(1).getByText("Last collected"),
          ).toBeVisible();
        }
      } else {
        expect(last.y).toBe(first.y);
      }
      if (width === 900 && state !== "empty") {
        expect((await upcoming.boundingBox())!.y).toBe(
          (await readiness.boundingBox())!.y,
        );
      }

      for (const theme of ["light", "dark"]) {
        await page.evaluate((theme) => {
          document.documentElement.dataset.theme = theme;
        }, theme);
        const audit = await new AxeBuilder({ page })
          .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
          .analyze();
        expect
          .soft(
            audit.violations.map(({ id, nodes }) => ({
              id,
              targets: nodes.map(({ target }) => target),
            })),
            `${width}px, ${theme}`,
          )
          .toEqual([]);
      }
    }

    if (state === "empty") {
      scanReports = fleet.scanReports;
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await expect(scans.getByText("Recent source report")).toBeVisible();
      expect((await scans.boundingBox())!.y).toBeGreaterThan(
        (await page
          .getByRole("heading", { name: "Meet your NAS" })
          .boundingBox())!.y,
      );
    }
    if (state === "calm") {
      scanReports = [{ percentage: "unknown" }];
      await page.getByRole("button", { name: "Refresh", exact: true }).click();
      await expect(readiness).toContainText("Scan summary unavailable");
      await expect(systems).toBeVisible();
    }
  });
}


test("Drive age icon warns at five years and turns red past seven", async ({ page }) => {
  await page.clock.setFixedTime(new Date("2026-11-04T12:00:00Z"));
  await page.route("**/api/v1/session", r => r.fulfill({ json: { authenticated: true, setupRequired: false } }));
  let purchasedUtc = "2021-11-04T12:00:00Z";
  await page.route("**/api/v1/overview", r => r.fulfill({ json: {
    ...calm, settings: { timezone: "UTC" }, plans: [], findings: [], recovery: [], recoveryAssessments: [], activity: [],
    disks: [{ id: "age-drive", purchasedUtc, lifecycle: "active", present: true }]
  }}));
  await page.route("**/api/v1/planning/review", r => r.fulfill({ json: {
    driveAgeReviewYears: 5, reviews: [{ diskId: "age-drive", reasons: [{ kind: "age-review" }] }], spares: [], budgets: []
  }}));
  const icon = page.getByRole("link", { name: "1 drive past your 5-year review age" }).locator("xpath=../..").locator("svg");
  await page.goto("/");
  await expect(icon).toHaveAttribute("style", /--tone-warn/);
  purchasedUtc = "2019-11-04T12:00:00Z";
  await page.reload();
  await expect(icon).toHaveAttribute("style", /--tone-warn/);
  purchasedUtc = "2019-11-04T11:59:59Z";
  await page.reload();
  await expect(icon).toHaveAttribute("style", /--tone-bad/);
});
