import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { expect, test } from "vitest";
import type { BookResult } from "../../lib/types";
import { FeaturedResults } from "./FeaturedResults";

const book = (over: Partial<BookResult>): BookResult => ({
  title: "The Hobbit",
  author: "J.R.R. Tolkien",
  authors: ["J.R.R. Tolkien"],
  first_publish_year: 1937,
  workId: "OL1W",
  url: "https://openlibrary.org/works/OL1W",
  coverUrl: null,
  explanation: "Exact title match; J.R.R. Tolkien is primary author.",
  matchTier: 1,
  exactTitle: true,
  ...over,
});

const second = book({ workId: "OL2W", title: "The Hobbit (1990)", explanation: "Same title, later edition." });
const close = book({ workId: "OL3W", title: "Poems from The Hobbit", exactTitle: false, matchTier: 3, explanation: "Title contains the query." });

test("shows the top match large with its reason always visible", () => {
  render(<FeaturedResults results={[book({}), second, close]} />);

  const featured = screen.getByRole("article");
  expect(within(featured).getByRole("heading", { name: "The Hobbit" })).toBeInTheDocument();
  expect(within(featured).getByText("Best match")).toBeInTheDocument();
  expect(within(featured).getByText("J.R.R. Tolkien · 1937")).toBeInTheDocument();
  expect(within(featured).getByText(/Exact title match/)).toBeVisible();
  expect(within(featured).getByRole("link", { name: "View on Open Library" })).toHaveAttribute("href", "https://openlibrary.org/works/OL1W");
});

test("lists the other matches with a badge and a collapsed reason", async () => {
  render(<FeaturedResults results={[book({}), second, close]} />);

  const others = screen.getAllByRole("listitem");
  expect(others).toHaveLength(2);
  expect(within(others[0]).getByText("Exact title")).toBeInTheDocument();
  expect(within(others[1]).getByText("Close title")).toBeInTheDocument();
  const reason = within(others[1]).getByText("Title contains the query.");
  expect(reason).not.toBeVisible();
  await userEvent.click(within(others[1]).getByText("Why this book?"));
  expect(reason).toBeVisible();
});

test("shows only the featured match when there is one result", () => {
  render(<FeaturedResults results={[book({})]} />);

  expect(screen.getByRole("article")).toBeInTheDocument();
  expect(screen.queryByRole("list")).not.toBeInTheDocument();
  expect(screen.queryByText(/other possible matches/i)).not.toBeInTheDocument();
});

test("falls back to a placeholder cover and an unknown author", () => {
  render(<FeaturedResults results={[book({ author: "", authors: [], first_publish_year: null, coverUrl: null })]} />);

  expect(screen.getByText("Unknown author")).toBeInTheDocument();
  expect(screen.queryByRole("img")).not.toBeInTheDocument();
});

test("uses the cover image when there is one", () => {
  render(<FeaturedResults results={[book({ coverUrl: "https://covers.openlibrary.org/b/id/1-M.jpg" })]} />);

  expect(screen.getByRole("img", { name: "Cover of The Hobbit" })).toHaveAttribute("src", "https://covers.openlibrary.org/b/id/1-M.jpg");
});

test("names the matched edition title under the title when the work is titled differently", () => {
  const kafka = book({ title: "海辺のカフカ", matchedTitle: "Kafka on the Shore" });
  render(<FeaturedResults results={[kafka, book({ workId: "OL9W", title: "Other", matchedTitle: "Another Edition" })]} />);

  expect(within(screen.getByRole("article")).getByText("Matched on edition title “Kafka on the Shore”")).toBeInTheDocument();
  expect(within(screen.getByRole("listitem")).getByText("Matched on edition title “Another Edition”")).toBeInTheDocument();
});

test("shows no edition title line when the work title matched", () => {
  render(<FeaturedResults results={[book({ matchedTitle: null })]} />);

  expect(screen.queryByText(/Matched on edition title/)).not.toBeInTheDocument();
});
