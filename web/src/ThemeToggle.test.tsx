import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { ThemeToggle } from "./ThemeToggle";

const root = document.documentElement;

beforeEach(() => {
  localStorage.clear();
  root.removeAttribute("data-theme");
});
afterEach(() => vi.restoreAllMocks());

const button = (name: string) => screen.getByRole("button", { name });

test("defaults to system mode and sets no explicit theme", () => {
  render(<ThemeToggle />);

  expect(button("System")).toHaveAttribute("aria-pressed", "true");
  expect(button("Light")).toHaveAttribute("aria-pressed", "false");
  expect(button("Dark")).toHaveAttribute("aria-pressed", "false");
  expect(root).not.toHaveAttribute("data-theme");
});

test("choosing dark or light sets the theme and remembers it", async () => {
  render(<ThemeToggle />);

  await userEvent.click(button("Dark"));
  expect(root).toHaveAttribute("data-theme", "dark");
  expect(button("Dark")).toHaveAttribute("aria-pressed", "true");
  expect(button("System")).toHaveAttribute("aria-pressed", "false");
  expect(localStorage.getItem("ftb-theme")).toBe("dark");

  await userEvent.click(button("Light"));
  expect(root).toHaveAttribute("data-theme", "light");
  expect(localStorage.getItem("ftb-theme")).toBe("light");
});

test("choosing system again removes the explicit theme", async () => {
  render(<ThemeToggle />);
  await userEvent.click(button("Dark"));

  await userEvent.click(button("System"));

  expect(root).not.toHaveAttribute("data-theme");
  expect(button("System")).toHaveAttribute("aria-pressed", "true");
  expect(localStorage.getItem("ftb-theme")).toBe("system");
});

test("restores the saved mode on load", () => {
  localStorage.setItem("ftb-theme", "dark");

  render(<ThemeToggle />);

  expect(button("Dark")).toHaveAttribute("aria-pressed", "true");
  expect(root).toHaveAttribute("data-theme", "dark");
});

test("ignores an invalid saved value", () => {
  localStorage.setItem("ftb-theme", "purple");

  render(<ThemeToggle />);

  expect(button("System")).toHaveAttribute("aria-pressed", "true");
  expect(root).not.toHaveAttribute("data-theme");
});

test("still works when storage is blocked", async () => {
  vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
    throw new Error("blocked");
  });
  vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
    throw new Error("blocked");
  });
  render(<ThemeToggle />);

  expect(button("System")).toHaveAttribute("aria-pressed", "true");
  await userEvent.click(button("Light"));

  expect(root).toHaveAttribute("data-theme", "light");
  expect(button("Light")).toHaveAttribute("aria-pressed", "true");
});

test("groups the buttons under an accessible label", () => {
  render(<ThemeToggle />);

  expect(screen.getByRole("group", { name: "Theme" })).toBeInTheDocument();
});
