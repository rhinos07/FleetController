import { defineConfig, devices } from "@playwright/test";

const BASE_URL = "http://localhost:5200";

export default defineConfig({
  testDir: "./tests",
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  workers: process.env.CI ? 1 : undefined,
  reporter: [["html", { open: "never" }], ["list"]],

  use: {
    baseURL: BASE_URL,
    trace: "on-first-retry",
  },

  projects: [
    {
      name: "chromium",
      use: { ...devices["Desktop Chrome"] },
    },
    {
      name: "firefox",
      use: { ...devices["Desktop Firefox"] },
    },
  ],

  webServer: {
    command: [
      "dotnet run",
      "--project ../Vda5050FleetController",
      "--configuration Debug",
      "--no-build",
      `--urls ${BASE_URL}`,
    ].join(" "),
    url: BASE_URL,
    reuseExistingServer: !process.env.CI,
    timeout: 60_000,
    env: {
      ASPNETCORE_ENVIRONMENT: "UITests",
    },
  },
});
