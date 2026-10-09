import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, test, vi } from "vitest";
import { Toast } from "./Toast";

test("an error toast is announced as an alert with its title and message", () => {
  render(<Toast kind="error" title="Search failed" onDismiss={() => {}}>Open Library is unavailable.</Toast>);

  const toast = screen.getByRole("alert");
  expect(toast).toHaveTextContent("Search failed");
  expect(toast).toHaveTextContent("Open Library is unavailable.");
});

test("a notice toast is announced as a status", () => {
  render(<Toast kind="notice" title="Basic search mode" onDismiss={() => {}}>Results may be less precise.</Toast>);

  expect(screen.getByRole("status")).toHaveTextContent("Basic search mode");
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

test("an info toast is announced as a status and has its own style", () => {
  render(<Toast kind="info" title="Closest results" onDismiss={() => {}}>Try adding an author.</Toast>);

  const toast = screen.getByRole("status");
  expect(toast).toHaveClass("toast", "info");
  expect(toast).toHaveTextContent("Closest results");
  expect(toast).toHaveTextContent("Try adding an author.");
});

test("the dismiss button calls onDismiss", async () => {
  const onDismiss = vi.fn();
  render(<Toast kind="error" title="Search failed" onDismiss={onDismiss}>Oops.</Toast>);

  await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));

  expect(onDismiss).toHaveBeenCalledOnce();
});

test("shows the action when one is given", () => {
  render(
    <Toast kind="error" title="Search failed" onDismiss={() => {}} action={<button type="button">Try again</button>}>Oops.</Toast>,
  );

  expect(screen.getByRole("button", { name: "Try again" })).toBeInTheDocument();
});
