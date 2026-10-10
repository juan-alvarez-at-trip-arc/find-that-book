import { expect, test } from "vitest";
import html from "../../index.html?raw";
import css from "../components/App/App.css?raw";

function pageBackground(selector: string): string {
  const line = css.split("\n").find((l: string) => l.startsWith(selector) && l.includes("--bg:"));
  const match = line?.match(/--bg: (#[0-9a-fA-F]{6});/);
  if (!match) throw new Error("no --bg for " + selector);
  return match[1].toLowerCase();
}

function themeColor(scheme: "light" | "dark"): string {
  const pattern = new RegExp(`<meta name="theme-color" content="(#[0-9a-fA-F]{6})" media="\\(prefers-color-scheme: ${scheme}\\)"`);
  const match = html.match(pattern);
  if (!match) throw new Error("no theme-color for " + scheme);
  return match[1].toLowerCase();
}

test("the light theme-color meta matches the light page background", () => {
  expect(themeColor("light")).toBe(pageBackground(":root {"));
});

test("the dark theme-color meta matches the dark page background", () => {
  expect(themeColor("dark")).toBe(pageBackground(':root[data-theme="dark"] {'));
});
