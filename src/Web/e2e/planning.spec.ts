import { test, expect, type Page } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";
import fs from "node:fs";
import type {Plan} from "../src/features/planning/planning";
import type { DriveReview } from "../src/features/planning/planning";

type Fixture = {
  review: {
    generatedUtc: string;
    trackedDriveCount: number;
    reviews: DriveReview[];
    spares: {
      id: string;
      name: string;
      quantity: number;
      estimatedUnitCost: number | null;
      currency: string;
      compatibleDriveCount: number;
      updatedUtc?: string;
    }[];
  };
  plans: (Plan & { diskSerial?: string | null })[];
  _mock: { now: string };
};
const fixture = (name: string): Fixture =>
  JSON.parse(
    fs.readFileSync(
      new URL(
        `../../../docs/features/005-planning-redesign/fixtures/${name}.json`,
        import.meta.url,
      ),
      "utf8",
    ),
  );
async function arrange(
  page: Page,
  state = "normal",
  customize?: (data: Fixture) => void,
) {
  const data = fixture(state);
  customize?.(data);
  await page.clock.install({ time: new Date("2026-11-04T17:00:00Z") });
  await page.emulateMedia({ reducedMotion: "reduce" });
  const reviewData = data.review ?? fixture("normal").review;
  const reviews = reviewData.reviews;
  let plans = (data.plans ?? []).map((p) => ({
    ...p,
    revision: "fixture-revision",
    reason: p.notes ?? "",
    diskId: reviews.find((r) => r.openPlan?.id === p.id)?.diskId ?? null,
  }));
  let overviewFailed = false;
  let fail = state === "unavailable";
  let held = state === "loading";
  let sent: Record<string, unknown> | null = null;
  let writes = 0;
  await page.route("**/api/v1/session", (r) =>
    r.fulfill({ json: { authenticated: true, setupRequired: false } }),
  );
  await page.route("**/api/v1/overview", (r) =>
    overviewFailed
      ? r.fulfill({ status: 503 })
      : r.fulfill({
          json: {
            settings: { timezone: "America/Phoenix" },
            connections: [],
            capabilities: [],
            disks: reviews.map((r) => ({ id: r.diskId, serial: r.serial })),
            findings: [],
            plans,
            recovery: [],
            scanReports: [],
            capacityOutlook: [],
            recoveryAssessments: [],
            activity: [],
          },
        }),
  );
  await page.route("**/api/v1/planning/review", (r) => {
    if (held) return;
    if (fail) return r.fulfill({ status: 503 });
    return r.fulfill({
      json: {
        ...reviewData,
        spares: reviewData.spares.map((s) => ({
          ...s,
          updatedUtc: "2026-11-04T12:00:00Z",
        })),
        reviews,
      },
    });
  });
  await page.route("**/api/v1/plans", async (r) => {
    sent = r.request().postDataJSON();
    plans.push({
      ...sent,
      id: "new-plan",
      revision: "new",
      reason: String(sent!.notes),
      status: "planned",
    } as Plan);
    await r.fulfill({ json: plans.at(-1) });
  });
  await page.route("**/api/v1/plans/*", async (r) => {
    if (r.request().method() !== "PUT") return r.fallback();
    writes++;
    const input = r.request().postDataJSON();
    plans = plans.map((p) =>
      p.id === input.id
        ? {
            ...input,
            revision: "updated",
            completedUtc:
              input.status === "complete" ? new Date().toISOString() : null,
          }
        : p,
    );
    await r.fulfill({ json: plans.find((p) => p.id === input.id) });
  });
  await page.route("**/api/v1/plans/*/history", (r) =>
    r.fulfill({ json: { items: [], nextId: null } }),
  );
  await page.route("**/api/v1/disks/*/spare-reviews", (r) =>
    r.fulfill({
      json: {
        generatedUtc: new Date().toISOString(),
        items: [
          {
            status: "superseded",
            review: {
              id: 1,
              assetName: "Earlier spare",
              verdict: "unknown",
              evidence: "Earlier fit investigation",
              author: "Jess",
              recordedUtc: "2026-11-01T12:00:00Z",
              validUntilUtc: "2026-12-01T12:00:00Z",
            },
          },
        ],
        nextBefore: null,
      },
    }),
  );
  await page.goto("/planning");
  return {
    data,
    reviews,
    get sent() {
      return sent;
    },
    get writes() {
      return writes;
    },
    failOverview() {
      overviewFailed = true;
    },
    fail() {
      fail = true;
    },
    resume() {
      held = false;
      fail = false;
    },
  };
}
for (const state of [
  "normal",
  "calm",
  "empty",
  "loading",
  "unavailable",
  "stale",
  "dialog",
]) {
  test(`005 Planning · ${state}`, async ({ page }) => {
    const context = await arrange(
      page,
      state === "stale" || state === "dialog" ? "normal" : state,
    );
    if (state === "stale") {
      await expect(page.locator(".planning-drive")).toHaveCount(5);
      context.fail();
      await page.clock.fastForward(30001);
      await expect(
        page.getByRole("status").filter({ hasText: "Refresh failed" }),
      ).toBeVisible();
      await expect(
        page.getByText("Out of date", { exact: true }),
      ).toBeVisible();
      await expect(
        page.getByText("Spare fits · WD Red Plus 8 TB", { exact: true }),
      ).toHaveCount(0);
    } else if (state === "normal") {
      await expect(page.locator(".planning-drive")).toHaveCount(5);
      await expect(
        page.getByRole("img", {
          name: "1 media evidence, 1 observed issue, 3 prepare ahead",
        }),
      ).toBeVisible();
      await expect(page.getByText("1 overdue", { exact: true })).toBeVisible();
      await expect(
        page.getByText("1 due in 2 weeks", { exact: true }),
      ).toBeVisible();
    } else if (state === "calm")
      await expect(
        page.getByRole("heading", {
          name: "No drives match the planning rules.",
        }),
      ).toBeVisible();
    else if (state === "empty") {
      await expect(
        page.getByRole("heading", { name: "Connect a NAS" }),
      ).toBeVisible();
      await expect(page.getByLabel("Planning summary")).toHaveCount(0);
    } else if (state === "loading") {
      await expect(
        page.getByLabel("Loading drive reviews").first(),
      ).toBeVisible();
      await expect(
        page.getByRole("region", { name: "Ordered", exact: true }),
      ).toContainText("Replace WD-CA7Z9K21");
    } else if (state === "unavailable") {
      await expect(page.getByRole("alert")).toContainText(
        "Your plans below are current.",
      );
      await expect(
        page.getByRole("region", { name: "Ordered", exact: true }),
      ).toBeVisible();
    } else if (state === "dialog") {
      await page
        .getByRole("button", { name: "Plan a review", exact: true })
        .click();
      await expect(page.getByLabel("Estimate (optional)")).toBeEmpty();
      await expect(page.getByLabel("Due (optional)")).toHaveValue("2026-11-18");
    }
    await expect(page.locator(".planning-page details")).toHaveCount(0);
    for (const width of [1440, 900, 390])
      for (const theme of ["light", "dark"]) {
        await page.setViewportSize({ width, height: 1000 });
        await page.evaluate(
          (theme) => (document.documentElement.dataset.theme = theme),
          theme,
        );
        const audit = await new AxeBuilder({ page })
          .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"])
          .analyze();
        expect(audit.violations, `${state}, ${width}, ${theme}`).toEqual([]);
        expect(
          await page.evaluate(
            () => document.documentElement.scrollWidth <= innerWidth,
          ),
          `${state} overflow at ${width}`,
        ).toBe(true);
        if (theme === "light")
          await page.screenshot({
            path: `/tmp/homelab-005-screenshots/005-${state}-${width}.png`,
            fullPage: true,
          });
      }
    if (state === "stale" || state === "unavailable") {
      context.resume();
      await page.getByRole("button", { name: "Retry", exact: true }).click();
      await expect(page.getByText("Out of date", { exact: true })).toHaveCount(
        0,
      );
      await expect(page.getByRole("alert")).toHaveCount(0);
    }
  });
}
test("005 actions, keyboard evidence, history and separate notes", async ({
  page,
}) => {
  const context = await arrange(page);
  await page
    .getByRole("link", { name: "In plan · Ordered", exact: true })
    .click();
  await expect(page.locator("#plan-p1")).toBeFocused();
  const ordered = page.getByRole("region", { name: "Ordered", exact: true });
  await ordered.getByRole("button", { name: "Mark done", exact: true }).click();
  await expect(
    page.getByRole("region", { name: "Done · last 30 days", exact: true }),
  ).toContainText("Replace WD-CA7Z9K21");
  await expect(page.locator("#plan-p1")).toBeFocused();
  expect(context.writes).toBe(1);
  await expect(
    page.locator("#planning-plans").getByRole("status"),
  ).toContainText("Moved to Done");
  const finding = page
    .locator(".planning-chip > span")
    .filter({ hasText: "3 checksum errors in latest scrub" });
  await finding.scrollIntoViewIfNeeded();
  await finding.focus();
  await expect(page.getByRole("tooltip")).toContainText(
    "A transport error alone",
  );
  await page.keyboard.press("Escape");
  await page
    .getByRole("article", {
      name: "Seagate IronWolf 8 TB ZA1K3QX9, Observed issue",
      exact: true,
    })
    .getByRole("button", { name: "Check a spare" })
    .click();
  await expect(page.getByRole("dialog")).toContainText(
    "Earlier fit investigation",
  );
  await expect(page.locator(".planning-page details")).toHaveCount(0);
  await page.getByRole("button", { name: "Close", exact: true }).click();
  await page
    .getByRole("button", { name: "Plan a review", exact: true })
    .click();
  await page.getByLabel("Notes", { exact: true }).fill("Check the cable first");
  await page.getByLabel("Owner (optional)").fill("Jess");
  await page.getByRole("button", { name: "Save plan", exact: true }).click();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(context.sent).toMatchObject({
    notes: "Check the cable first",
    owner: "Jess",
    budget: null,
    dueUtc: "2026-11-18T19:00:00.000Z",
    evidence: [{ kind: "finding", findingId: "f-102" }],
  });
  expect(context.sent).not.toHaveProperty("reason");
  await expect(
    page.getByRole("link", { name: "In plan · Planned", exact: true }),
  ).toHaveCount(2);
});

test("005 spare states, estimate source, currencies and calendar boundaries", async ({
  page,
}) => {
  const context = await arrange(page, "normal", (data) => {
    data.review.reviews[0].spareFit.assetId = "s1";
    data.review.reviews[0].openPlan = null;
    data.review.reviews[1].spareFit = {
      status: "expired",
      validUntilUtc: "2026-11-01T12:00:00Z",
    };
    data.review.reviews[2].spareFit = { status: "incompatible" };
    data.plans = data.plans.filter((p) => p.id !== "p1");
    data.plans.push({
      id: "euro",
      revision: "fixture",
      diskId: null,
      title: "Euro spare",
      reason: "",
      owner: "",
      dueUtc: "2026-11-05T01:00:00Z",
      budget: 20,
      currency: "EUR",
      status: "planned",
    });
    data.plans.push({
      id: "old-done",
      revision: "fixture",
      diskId: null,
      title: "Old completed plan",
      reason: "",
      owner: "",
      dueUtc: null,
      budget: null,
      currency: "USD",
      status: "complete",
      completedUtc: "2026-09-01T12:00:00Z",
    });
    data.plans.push({
      id: "unknown-done",
      revision: "fixture",
      diskId: null,
      title: "Completion unknown",
      reason: "",
      owner: "",
      dueUtc: null,
      budget: null,
      currency: "USD",
      status: "complete",
      completedUtc: null,
    });
  });
  await expect(
    page.getByText("Spare check expired", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByText("Spare doesn’t fit", { exact: true }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Old completed plan", exact: true }),
  ).toHaveCount(0);
  await expect(
    page.getByRole("heading", { name: "Completion unknown", exact: true }),
  ).toHaveCount(0);
  await expect(page.locator("#plan-euro")).toContainText("Due Nov 4 · 0 days");
  await expect(page.locator(".planning-budget")).toHaveCount(2);
  await page
    .getByRole("button", { name: "Plan replacement", exact: true })
    .click();
  await expect(page.getByLabel("Estimate (optional)")).toHaveValue("189.99");
  await expect(page.getByRole("dialog")).toContainText(
    "From your Inventory price",
  );
  await page.getByRole("button", { name: "Cancel", exact: true }).click();
  context.data.review.spares.forEach((s) => (s.quantity = 0));
  await page.clock.fastForward(30001);
  await expect(
    page.getByRole("button", { name: "Check a spare", exact: true }),
  ).toHaveCount(0);
  await expect(page.locator(".planning-quantity.tone-warn")).toHaveCount(2);
  context.data.review.spares = [];
  await page.clock.fastForward(30001);
  await expect(
    page.getByText("No spares recorded", { exact: true }),
  ).toHaveCount(5);
});

test("005 a saved move stays visible if the following overview refresh fails", async ({
  page,
}) => {
  const context = await arrange(page);
  await expect(page.locator("#plan-p2")).toBeVisible();
  context.failOverview();
  await page
    .locator("#plan-p2")
    .getByRole("button", { name: "Mark ordered", exact: true })
    .click();
  await expect(
    page.getByRole("region", { name: "Ordered", exact: true }),
  ).toContainText("Buy a cold spare for the IronWolf vdev");
  await expect(page.locator("#plan-p2")).toBeFocused();
  await expect(
    page.getByRole("alert").filter({ hasText: "Overview refresh unavailable" }),
  ).toBeVisible();
  expect(context.writes).toBe(1);
});
