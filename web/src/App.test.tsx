import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, expect, test, vi } from "vitest";
import App from "./App";

const reply = (body: unknown, status = 200, type = "application/json") =>
  Promise.resolve(new Response(JSON.stringify(body), { status, headers: { "Content-Type": type } }));

const hobbit = {
  title: "The Hobbit",
  author: "J.R.R. Tolkien",
  authors: ["J.R.R. Tolkien"],
  first_publish_year: 1937,
  workId: "OL1W",
  url: "https://openlibrary.org/works/OL1W",
  coverUrl: null,
  explanation: "Exact title match; J.R.R. Tolkien is primary author.",
};

async function search(text: string) {
  await userEvent.type(screen.getByRole("searchbox"), text);
  await userEvent.click(screen.getByRole("button", { name: /search/i }));
}

afterEach(() => vi.unstubAllGlobals());

test("shows results with their explanation", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);

  await search("tolkien hobbit");

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(screen.getByText(/Exact title match/)).toBeInTheDocument();
  expect(screen.getByText(/first published 1937/)).toBeInTheDocument();
  expect(screen.queryByText(/basic parsing/i)).not.toBeInTheDocument();
});

test("shows a notice when AI was not used", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: false, message: null, results: [hobbit] })));
  render(<App />);

  await search("tolkien hobbit");

  expect(await screen.findByText(/basic parsing/i)).toBeInTheDocument();
});

test("shows the server message when there are no results", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: "No matching books found.", results: [] })));
  render(<App />);

  await search("zzzz");

  expect(await screen.findByText("No matching books found.")).toBeInTheDocument();
});

test("shows an error banner from problem details", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ title: "Open Library is unavailable." }, 502, "application/problem+json")));
  render(<App />);

  await search("hobbit");

  expect(await screen.findByRole("alert")).toHaveTextContent("Open Library is unavailable.");
});

test("shows a generic error when the request itself fails", async () => {
  vi.stubGlobal("fetch", vi.fn(() => Promise.reject(new TypeError("network"))));
  render(<App />);

  await search("hobbit");

  expect(await screen.findByRole("alert")).toHaveTextContent(/something went wrong/i);
});

test("clicking an example runs that search", async () => {
  const fetchMock = vi.fn((_url: string, _init: RequestInit) => reply({ aiUsed: true, message: null, results: [hobbit] }));
  vi.stubGlobal("fetch", fetchMock);
  render(<App />);

  await userEvent.click(screen.getByRole("button", { name: "mark huckleberry" }));

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(JSON.parse(fetchMock.mock.calls[0][1].body as string)).toEqual({ query: "mark huckleberry" });
});

test("does not search for blank input", async () => {
  const fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
  render(<App />);

  await userEvent.click(screen.getByRole("button", { name: /search/i }));

  expect(fetchMock).not.toHaveBeenCalled();
});
