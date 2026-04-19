import { test, expect } from "@playwright/test";

// ── Vehicle table ─────────────────────────────────────────────────────────────

test.describe("Vehicle table", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await page.locator('.nav-item[data-view="vehicles"]').click();
    await page.waitForSelector("fleet-vehicle-table table");
  });

  // The heading and column labels below match the German strings used in the
  // existing fleet-vehicle-table component and are asserted verbatim.
  test("shows the vehicle-table heading", async ({ page }) => {
    await expect(page.locator("fleet-vehicle-table h2")).toHaveText(
      "Fahrzeugtabelle"
    );
  });

  test("renders six column headers", async ({ page }) => {
    const headers = page.locator("fleet-vehicle-table thead th");
    await expect(headers).toHaveCount(6);
    await expect(headers.nth(0)).toHaveText("Fahrzeug");
    await expect(headers.nth(1)).toHaveText("Status");
    await expect(headers.nth(2)).toHaveText("Batterie (%)");
    await expect(headers.nth(3)).toHaveText("Order");
    await expect(headers.nth(4)).toHaveText("Position");
    await expect(headers.nth(5)).toHaveText("Last Seen (UTC)");
  });

  test("shows an empty-state row when no vehicles are reported", async ({
    page,
  }) => {
    // Either the pre-connection placeholder or the post-update "no vehicles"
    // message is acceptable — both are valid empty states.
    await expect(page.locator("#vehicleRows")).toContainText(
      /Warte auf Live-Daten|Keine Fahrzeuge gemeldet/
    );
  });

  test("renders vehicle rows when updateVehicles is called", async ({
    page,
  }) => {
    await page.evaluate(() => {
      const table = document.querySelector("fleet-vehicle-table") as HTMLElement & {
        updateVehicles(vehicles: unknown[]): void;
      };
      if (table && typeof table.updateVehicles === "function") {
        table.updateVehicles([
          {
            vehicleId: "agv-01",
            status: "Idle",
            battery: 85,
            orderId: null,
            position: { x: 5, y: 8, mapId: "DEMO-WAREHOUSE" },
            lastSeen: new Date().toISOString(),
          },
        ]);
      }
    });

    await expect(page.locator("#vehicleRows tr")).toHaveCount(1);
    await expect(page.locator("#vehicleRows tr td:first-child")).toHaveText(
      "agv-01"
    );
    await expect(page.locator("#vehicleRows tr td:nth-child(2)")).toHaveText(
      "Idle"
    );
  });

  test("shows 'no vehicles' message when the list is empty", async ({
    page,
  }) => {
    await page.evaluate(() => {
      const table = document.querySelector("fleet-vehicle-table") as HTMLElement & {
        updateVehicles(vehicles: unknown[]): void;
      };
      if (table && typeof table.updateVehicles === "function") {
        table.updateVehicles([]);
      }
    });

    // "Keine Fahrzeuge gemeldet" is the existing German UI string for "no vehicles"
    await expect(page.locator("#vehicleRows")).toContainText(
      "Keine Fahrzeuge gemeldet"
    );
  });
});

// ── Order list ────────────────────────────────────────────────────────────────

test.describe("Order list", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await page.locator('.nav-item[data-view="orders"]').click();
    await page.waitForSelector("fleet-order-list table");
  });

  test("shows Order List heading", async ({ page }) => {
    await expect(page.locator("fleet-order-list h2")).toHaveText("Order List");
  });

  test("shows an empty-state row when no active orders exist", async ({
    page,
  }) => {
    await expect(page.locator("#orderRows")).toContainText(
      /Waiting for live data|No active orders/
    );
  });

  test("renders active order rows", async ({ page }) => {
    await page.evaluate(() => {
      const list = document.querySelector("fleet-order-list") as HTMLElement & {
        updateOrders(orders: unknown[]): void;
      };
      if (list && typeof list.updateOrders === "function") {
        list.updateOrders([
          {
            orderId: "ORD-001",
            loadId: "PAL-42",
            sourceId: "IN-A",
            destId: "OUT-B",
            status: "InProgress",
            vehicleId: "agv-01",
          },
        ]);
      }
    });

    await expect(page.locator("#orderRows tr")).toHaveCount(1);
    await expect(page.locator("#orderRows tr td:first-child")).toHaveText(
      "ORD-001"
    );
  });

  test("shows 'No active orders' when all orders are completed", async ({
    page,
  }) => {
    await page.evaluate(() => {
      const list = document.querySelector("fleet-order-list") as HTMLElement & {
        updateOrders(orders: unknown[]): void;
      };
      if (list && typeof list.updateOrders === "function") {
        list.updateOrders([
          {
            orderId: "ORD-999",
            loadId: "PAL-01",
            sourceId: "IN-A",
            destId: "OUT-A",
            status: "Completed",
            vehicleId: "agv-02",
          },
        ]);
      }
    });

    await expect(page.locator("#orderRows")).toContainText("No active orders");
  });
});

// ── Order history ─────────────────────────────────────────────────────────────

test.describe("Order history", () => {
  test.beforeEach(async ({ page }) => {
    await page.goto("/");
    await page.locator('.nav-item[data-view="order-history"]').click();
    await page.waitForSelector("fleet-order-history table");
  });

  test("shows Order History heading", async ({ page }) => {
    await expect(page.locator("fleet-order-history h2")).toHaveText(
      "Order History"
    );
  });

  test("shows an empty-state row when no order history exists", async ({
    page,
  }) => {
    await expect(page.locator("#historyRows")).toContainText(
      /Waiting for live data|No order history available/
    );
  });

  test("renders completed orders in history", async ({ page }) => {
    await page.evaluate(() => {
      const history = document.querySelector("fleet-order-history") as HTMLElement & {
        updateOrders(orders: unknown[]): void;
      };
      if (history && typeof history.updateOrders === "function") {
        history.updateOrders([
          {
            orderId: "ORD-888",
            sourceId: "IN-B",
            destId: "OUT-C",
            status: "Completed",
            vehicleId: "agv-03",
          },
        ]);
      }
    });

    await expect(page.locator("#historyRows tr")).toHaveCount(1);
    await expect(page.locator("#historyRows tr td:first-child")).toHaveText(
      "ORD-888"
    );
  });
});

// ── Connection status ─────────────────────────────────────────────────────────

test.describe("Connection status", () => {
  test("shows a connection status element", async ({ page }) => {
    await page.goto("/");
    await page.waitForSelector("fleet-connection-status");
    await expect(page.locator("#connectionState")).toBeVisible();
  });
});
