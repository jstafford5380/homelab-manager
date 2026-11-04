import { test, expect } from "@playwright/test";
import AxeBuilder from "@axe-core/playwright";

test("API key settings: issue once, access options, expiry, revoke, and accessibility", async ({ page }) => {
  const now = new Date("2026-11-04T20:00:00Z");
  await page.clock.setFixedTime(now);
  await page.context().grantPermissions(["clipboard-read", "clipboard-write"]);
  await page.emulateMedia({ reducedMotion: "reduce" });
  const id = "acfe46a3-8569-4481-a0cc-10fb526bd546";
  const secret = "hmk_acfe46a385694481a0cc10fb526bd546_" + "a".repeat(64);
  let keys: Record<string, unknown>[] = [];
  let failIssue = true;
  let failList = true;
  let failRevoke = true;
  await page.route("**/api/v1/session", route => route.fulfill({ json: { authenticated: true, setupRequired: false } }));
  await page.route("**/api/v1/overview", route => route.fulfill({ json: {
    settings: { timezone: "UTC" }, connections: [], disks: [], capabilities: [], findings: [], plans: [], recovery: [], recoveryAssessments: [], activity: [],
  } }));
  await page.route("**/api/v1/connections", route => route.fulfill({ json: [] }));
  await page.route("**/api/v1/settings", route => route.fulfill({ json: { settings: { timezone: "UTC" }, revisions: {} } }));
  await page.route("**/api/v1/api-keys", async route => {
    if (route.request().method() === "GET") {
      if (failList) return route.fulfill({ status: 503, json: { error: "Keys temporarily unavailable." } });
      return route.fulfill({ json: keys });
    }
    expect(route.request().headers()["x-homelab-request"]).toBe("1");
    if (failIssue) return route.fulfill({ status: 503, json: { error: "Key could not be issued. Try again." } });
    expect(route.request().postDataJSON()).toEqual({ name: "Desktop MCP", access: "read-write", expiresInDays: 30 });
    const key = { id, name: "Desktop MCP", prefix: "hmk_" + id.replaceAll("-", ""), access: "read-write", createdUtc: now.toISOString(), expiresUtc: "2026-12-04T20:00:00Z", lastUsedUtc: null, revokedUtc: null };
    keys = [key];
    return route.fulfill({ status: 201, json: { key, secret } });
  });
  await page.route(`**/api/v1/api-keys/${id}`, route => {
    expect(route.request().method()).toBe("DELETE");
    expect(route.request().headers()["x-homelab-request"]).toBe("1");
    if (failRevoke) return route.fulfill({ status: 503, json: { error: "Revocation failed. Try again." } });
    keys = keys.map(key => ({ ...key, revokedUtc: now.toISOString() }));
    return route.fulfill({ status: 204 });
  });
  await page.goto("/settings/api-keys");
  await expect(page.getByRole("heading", { name: "API keys", exact: true })).toBeVisible();
  await expect(page.getByRole("alert")).toContainText("Keys temporarily unavailable.");
  failList = false;
  await page.getByRole("button", { name: "Try again", exact: true }).click();
  await expect(page.getByText("No API keys issued yet.")).toBeVisible();
  await page.setViewportSize({ width: 1440, height: 1000 });
  await page.screenshot({ path: "test-results/api-key-layout-desktop.png", fullPage: true });
  await expect(page.getByLabel("Access", { exact: true })).toContainText("Read-only");
  await page.getByLabel("Key name").fill("Desktop MCP");
  await page.getByLabel("Access", { exact: true }).click();
  await page.getByRole("option", { name: "Read and write", exact: true }).click();
  await page.getByLabel("Expires after").click();
  await page.getByRole("option", { name: "30 days", exact: true }).click();
  await page.getByRole("button", { name: "Issue API key", exact: true }).click();
  await expect(page.getByRole("alert")).toContainText("Key could not be issued.");
  await expect(page.getByLabel("Key name")).toHaveValue("Desktop MCP");
  failIssue = false;
  await page.getByRole("button", { name: "Issue API key", exact: true }).click();
  const issued = page.getByRole("dialog", { name: "Save your API key" });
  await expect(issued).toBeVisible();
  await expect(issued.getByLabel("API key secret")).toHaveValue(secret);
  const audit = async () => {
    for (const theme of ["light", "dark"]) {
      await page.evaluate(theme => { document.documentElement.dataset.theme = theme; }, theme);
      await page.evaluate(() => new Promise(resolve => requestAnimationFrame(() => requestAnimationFrame(resolve))));
      const result = await new AxeBuilder({ page }).withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa"]).analyze();
      expect(result.violations.map(({ id, nodes }) => ({ id, targets: nodes.map(node => ({ target: node.target, reason: node.failureSummary })) }))).toEqual([]);
    }
  };
  await audit();
  await issued.getByRole("button", { name: "Copy API key", exact: true }).click();
  expect(await page.evaluate(() => navigator.clipboard.readText())).toBe(secret);
  await issued.getByRole("button", { name: "I’ve saved it" }).click();
  await expect(page.getByLabel("API key secret")).toHaveCount(0);
  await expect(page.getByText("Desktop MCP · Active")).toBeVisible();
  await audit();
  await page.reload();
  await expect(page.getByText("Desktop MCP · Active")).toBeVisible();
  await expect(page.getByRole("dialog")).toHaveCount(0);
  expect(await page.evaluate(secret => JSON.stringify(localStorage).includes(secret) || JSON.stringify(sessionStorage).includes(secret), secret)).toBe(false);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.getByRole("button", { name: "Revoke Desktop MCP" }).click();
  const revoke = page.getByRole("dialog", { name: "Revoke Desktop MCP?" });
  await revoke.getByRole("button", { name: "Revoke key", exact: true }).click();
  await expect(revoke.getByRole("alert")).toContainText("Revocation failed.");
  await audit();
  failRevoke = false;
  await revoke.getByRole("button", { name: "Revoke key", exact: true }).click();
  await expect(revoke).toBeHidden();
  await expect(page.getByText("Desktop MCP · Revoked")).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth)).toBeLessThanOrEqual(390);
  await audit();
  await page.getByRole("heading", { name: "API keys", exact: true }).click();
  await page.screenshot({ path: "test-results/api-keys-mobile-dark.png", fullPage: true });
});
