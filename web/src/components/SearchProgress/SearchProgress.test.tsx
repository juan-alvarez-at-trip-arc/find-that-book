import { act, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
import { SearchProgress, STEP_MS } from "./SearchProgress";

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

  advance(STEP_MS);
  expect(screen.getByRole("status")).toHaveTextContent("Searching Open Library");

  advance(STEP_MS);
  expect(screen.getByRole("status")).toHaveTextContent("Matching titles and authors");

  advance(STEP_MS);
  expect(screen.getByRole("status")).toHaveTextContent("Writing explanations");
});

test("says it is taking longer than usual once the steps run out, and stays there", () => {
  render(<SearchProgress />);

  advance(STEP_MS * 4);
  expect(screen.getByRole("status")).toHaveTextContent("taking longer than usual");

  advance(STEP_MS * 10);
  expect(screen.getByRole("status")).toHaveTextContent("taking longer than usual");
});

test("marks finished, current and upcoming steps for the pips", () => {
  const { container } = render(<SearchProgress />);

  advance(STEP_MS * 2);

  const states = [...container.querySelectorAll("[data-step-state]")].map((el) => el.getAttribute("data-step-state"));
  expect(states).toEqual(["done", "done", "active", "pending"]);
});

test("stops its timer when it goes away", () => {
  const { unmount } = render(<SearchProgress />);

  unmount();

  expect(vi.getTimerCount()).toBe(0);
});
