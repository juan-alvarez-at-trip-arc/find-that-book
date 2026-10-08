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

test("clears the previous results while a new search is running", async () => {
  const fetchMock = vi
    .fn()
    .mockImplementationOnce(() => reply({ aiUsed: false, message: null, results: [hobbit] }))
    .mockImplementationOnce(() => new Promise<Response>(() => {})); // second search never finishes
  vi.stubGlobal("fetch", fetchMock);
  render(<App />);

  await search("tolkien hobbit");
  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(screen.getByText(/basic parsing/i)).toBeInTheDocument();

  await userEvent.click(screen.getByRole("button", { name: /search/i }));

  expect(await screen.findByRole("button", { name: /searching/i })).toBeDisabled();
  expect(screen.queryByText("The Hobbit")).not.toBeInTheDocument();
  expect(screen.queryByText(/basic parsing/i)).not.toBeInTheDocument();
});

test("shows animated progress while searching and removes it when results arrive", async () => {
  let finish: (response: Response) => void = () => {};
  vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>((resolve) => (finish = resolve))));
  render(<App />);

  await search("tolkien hobbit");

  expect(await screen.findByRole("status")).toHaveTextContent("Understanding your query");

  finish(new Response(JSON.stringify({ aiUsed: true, message: null, results: [hobbit] }), { status: 200 }));

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
});

const chips = () => [...document.querySelectorAll(".chip")].map((el) => el.textContent);

test("a successful search is pushed to the front of the examples and the last one is dropped", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);
  const before = chips();

  await search("moby dick");
  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();

  expect(chips()).toEqual(["moby dick", ...before.slice(0, -1)]);
  expect(chips()).toHaveLength(before.length);
});

test("searching an example that is already listed moves it to the front and keeps the rest", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);
  const before = chips();
  const picked = before[3] as string;

  await userEvent.click(screen.getByRole("button", { name: picked }));
  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();

  expect(chips()).toEqual([picked, ...before.filter((c) => c !== picked)]);
});

test("a failed search does not change the examples", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ title: "Open Library is unavailable." }, 502, "application/problem+json")));
  render(<App />);
  const before = chips();

  await search("moby dick");
  await screen.findByRole("alert");

  expect(chips()).toEqual(before);
});

test("a search with no results does not change the examples", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: "No matching books found.", results: [] })));
  render(<App />);
  const before = chips();

  await search("zzzz");
  await screen.findByText("No matching books found.");

  expect(chips()).toEqual(before);
});

test("does not search for blank input", async () => {
  const fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
  render(<App />);

  await userEvent.click(screen.getByRole("button", { name: /search/i }));

  expect(fetchMock).not.toHaveBeenCalled();
});
