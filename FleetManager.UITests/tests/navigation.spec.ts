import { test, expect } from "@playwright/test";

test.describe("Navigation menu", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    // Wait for the custom elements to be upgraded and rendered
    await page.waitForSelector("fleet-nav-menu .nav-list");
  });

  test("shows the FleetManager logo", async ({ page }) => {
    await expect(page.locator(".nav-logo")).toHaveText("FleetManager");
  });

  test("renders all five nav items", async ({ page }) => {
    const items = page.locator(".nav-item");
    await expect(items).toHaveCount(5);
    await expect(items.nth(0)).toContainText("Dashboard");
    await expect(items.nth(1)).toContainText("Topology Modify");
    await expect(items.nth(2)).toContainText("Order List");
    await expect(items.nth(3)).toContainText("Order History");
    await expect(items.nth(4)).toContainText("Vehicle Details");
  });

  test("Dashboard is the active view on load", async ({ page }) => {
    await expect(page.locator('.nav-item[data-view="dashboard"]')).toHaveClass(
      /active/
    );
    await expect(page.locator('.view[data-view="dashboard"]')).not.toHaveClass(
      /hidden/
    );
  });

  test("clicking Topology Modify shows topology view", async ({ page }) => {
    await page.locator('.nav-item[data-view="topology"]').click();

    await expect(page.locator('.nav-item[data-view="topology"]')).toHaveClass(
      /active/
    );
    await expect(page.locator('.view[data-view="topology"]')).not.toHaveClass(
      /hidden/
    );
    await expect(page.locator('.view[data-view="dashboard"]')).toHaveClass(
      /hidden/
    );
  });

  test("clicking Order List shows orders view", async ({ page }) => {
    await page.locator('.nav-item[data-view="orders"]').click();

    await expect(page.locator('.nav-item[data-view="orders"]')).toHaveClass(
      /active/
    );
    await expect(page.locator('.view[data-view="orders"]')).not.toHaveClass(
      /hidden/
    );
  });

  test("clicking Order History shows order-history view", async ({ page }) => {
    await page.locator('.nav-item[data-view="order-history"]').click();

    await expect(
      page.locator('.nav-item[data-view="order-history"]')
    ).toHaveClass(/active/);
    await expect(
      page.locator('.view[data-view="order-history"]')
    ).not.toHaveClass(/hidden/);
  });

  test("clicking Vehicle Details shows vehicles view", async ({ page }) => {
    await page.locator('.nav-item[data-view="vehicles"]').click();

    await expect(page.locator('.nav-item[data-view="vehicles"]')).toHaveClass(
      /active/
    );
    await expect(page.locator('.view[data-view="vehicles"]')).not.toHaveClass(
      /hidden/
    );
  });

  test("only one view is visible at a time", async ({ page }) => {
    await page.locator('.nav-item[data-view="orders"]').click();

    const visibleViews = await page
      .locator(".view:not(.hidden)")
      .evaluateAll((els) => els.map((el) => el.getAttribute("data-view")));

    expect(visibleViews).toHaveLength(1);
    expect(visibleViews[0]).toBe("orders");
  });
});
