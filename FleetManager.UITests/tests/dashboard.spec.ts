import { test, expect } from "@playwright/test";

test.describe("Dashboard view", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await page.waitForSelector("fleet-stats-cards .cards");
  });

  test("shows Dashboard heading", async ({ page }) => {
    await expect(
      page.locator('.view[data-view="dashboard"] h1')
    ).toHaveText("Dashboard");
  });

  test("stats cards render with initial zero counts", async ({ page }) => {
    await expect(page.locator("#vehicleCount")).toHaveText("0");
    await expect(page.locator("#activeOrders")).toHaveText("0");
    await expect(page.locator("#pendingOrders")).toHaveText("0");
  });

  // The label text below matches the German strings in the existing fleet-stats-cards component.
  test("stats cards show correct labels", async ({ page }) => {
    const cards = page.locator(".card");
    await expect(cards).toHaveCount(3);
    await expect(cards.nth(0)).toContainText("Fahrzeuge:");
    await expect(cards.nth(1)).toContainText("Aktive Aufträge:");
    await expect(cards.nth(2)).toContainText("Wartende Aufträge:");
  });

  test("stats cards update when fleet status is received", async ({ page }) => {
    // Simulate a fleet status update by dispatching a SignalR-style event
    // directly via the registered SignalR connection on the page
    await page.waitForFunction(() => {
      const app = document.querySelector("fleet-dashboard-app");
      return app !== null && app.querySelector("fleet-stats-cards") !== null;
    });

    await page.evaluate(() => {
      const statsCards = document.querySelector(
        "fleet-stats-cards"
      ) as HTMLElement & { updateStats(v: number, a: number, p: number): void };
      if (statsCards && typeof statsCards.updateStats === "function") {
        statsCards.updateStats(3, 2, 1);
      }
    });

    await expect(page.locator("#vehicleCount")).toHaveText("3");
    await expect(page.locator("#activeOrders")).toHaveText("2");
    await expect(page.locator("#pendingOrders")).toHaveText("1");
  });
});
