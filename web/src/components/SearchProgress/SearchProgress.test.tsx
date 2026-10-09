import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { STEP_MS } from "../../lib/progress";
import { SearchProgress } from "./SearchProgress";

const TOTAL_MS = STEP_MS.reduce((sum, ms) => sum + ms, 0);

beforeEach(() => vi.useFakeTimers());
afterEach(() => vi.useRealTimers());

function advance(ms: number) {
  act(() => {
    vi.advanceTimersByTime(ms);
  });
}

test("announces the first step in a status region", () => {
  render(<SearchProgress />);

  expect(screen.getByRole("status")).toHaveTextContent("Understanding your query");
});

test("moves through the steps over time", () => {
  render(<SearchProgress />);

  advance(STEP_MS[0]);
  expect(screen.getByRole("status")).toHaveTextContent("Searching Open Library");

  advance(STEP_MS[1]);
  expect(screen.getByRole("status")).toHaveTextContent("Matching titles and authors");

  advance(STEP_MS[2]);
  expect(screen.getByRole("status")).toHaveTextContent("Writing explanations");
});

test("gives each step its own duration: understand 10s, search 2s, match 1s, explain 30s", () => {
  expect(STEP_MS).toEqual([10000, 2000, 1000, 30000]);
});

test("stays on a step until its own duration has passed", () => {
  render(<SearchProgress />);

  advance(STEP_MS[0] - 1);
  expect(screen.getByRole("status")).toHaveTextContent("Understanding your query");

  advance(1);
  advance(STEP_MS[1] - 1);
  expect(screen.getByRole("status")).toHaveTextContent("Searching Open Library");
});

test("says it is taking longer than usual once the steps run out, and stays there", () => {
  render(<SearchProgress />);

  STEP_MS.slice(0, -1).forEach((ms) => advance(ms));
  advance(STEP_MS[STEP_MS.length - 1] - 1);
  expect(screen.getByRole("status")).toHaveTextContent("Writing explanations");

  advance(1);
  expect(screen.getByRole("status")).toHaveTextContent("taking longer than usual");

  advance(TOTAL_MS * 2);
  expect(screen.getByRole("status")).toHaveTextContent("taking longer than usual");
});

test("marks finished, current and upcoming steps for the pips", () => {
  const { container } = render(<SearchProgress />);

  advance(STEP_MS[0]);
  advance(STEP_MS[1]);

  const states = [...container.querySelectorAll("[data-step-state]")].map((el) => el.getAttribute("data-step-state"));
  expect(states).toEqual(["done", "done", "active", "pending"]);
});

test("stops its timer when it goes away", () => {
  const { unmount } = render(<SearchProgress />);

  unmount();

  expect(vi.getTimerCount()).toBe(0);
});
