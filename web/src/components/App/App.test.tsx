import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, expect, test, vi } from "vitest";
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
  matchTier: 1,
  exactTitle: true,
};

async function search(text: string) {
  await userEvent.type(screen.getByRole("searchbox"), text);
  await userEvent.click(screen.getByRole("button", { name: /search/i }));
}

beforeEach(() => {
  localStorage.clear();
  window.history.replaceState(null, "", "/");
});

afterEach(() => vi.unstubAllGlobals());

test("shows results with their explanation", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);

  await search("tolkien hobbit");

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(screen.getByText(/Exact title match/)).toBeInTheDocument();
  expect(screen.getByText("J.R.R. Tolkien · 1937")).toBeInTheDocument();
  expect(screen.getByText("Best match")).toBeInTheDocument();
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

test("shows an empty bookshelf picture when there are no results, even after the toast is dismissed", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: "No matching books found.", results: [] })));
  render(<App />);

  await search("zzzz");
  const toast = (await screen.findByText("No matching books found.")).closest(".toast") as HTMLElement;

  expect(screen.getByRole("img", { name: "An empty bookshelf" })).toBeInTheDocument();

  await userEvent.click(within(toast).getByRole("button", { name: "Dismiss" }));
  expect(screen.getByRole("img", { name: "An empty bookshelf" })).toBeInTheDocument();
});

test("does not show the empty bookshelf when there are results or an error", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  const { unmount } = render(<App />);
  await search("tolkien hobbit");
  await screen.findByText("The Hobbit");
  expect(screen.queryByRole("img", { name: "An empty bookshelf" })).not.toBeInTheDocument();
  unmount();

  vi.stubGlobal("fetch", vi.fn(() => reply({ title: "Open Library is unavailable." }, 502, "application/problem+json")));
  render(<App />);
  await search("hobbit");
  await screen.findByRole("alert");
  expect(screen.queryByRole("img", { name: "An empty bookshelf" })).not.toBeInTheDocument();
});

test("shows an error toast from problem details", async () => {
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

test("disables the search box while searching and enables it when the search finishes", async () => {
  let finish: (response: Response) => void = () => {};
  vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>((resolve) => (finish = resolve))));
  render(<App />);
  const box = screen.getByRole("searchbox", { name: /search for a book/i });

  await search("tolkien hobbit");

  expect(box).toBeDisabled();

  finish(new Response(JSON.stringify({ aiUsed: true, message: null, results: [hobbit] }), { status: 200 }));

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(box).toBeEnabled();
});

test("disables the example chips while searching and enables them when the search finishes", async () => {
  let finish: (response: Response) => void = () => {};
  vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>((resolve) => (finish = resolve))));
  render(<App />);
  const chipButtons = () => [...document.querySelectorAll<HTMLButtonElement>(".chip")];
  expect(chipButtons().length).toBeGreaterThan(0);

  await search("tolkien hobbit");

  expect(chipButtons().every((chip) => chip.disabled)).toBe(true);

  finish(new Response(JSON.stringify({ aiUsed: true, message: null, results: [hobbit] }), { status: 200 }));

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(chipButtons().every((chip) => !chip.disabled)).toBe(true);
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

test("labels the best, exact and close matches", async () => {
  const close = { ...hobbit, workId: "OL2W", title: "Poems from The Hobbit", exactTitle: false, matchTier: 3 };
  const second = { ...hobbit, workId: "OL3W", title: "The Hobbit (1990)" };
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit, second, close] })));
  render(<App />);

  await search("hobbit");

  await screen.findByText("Best match");
  expect(screen.getByText("Exact title")).toBeInTheDocument();
  expect(screen.getByText("Close title")).toBeInTheDocument();
});

test("shows the server notice next to the results when the best match is weak", async () => {
  const message = "No confident match. These are the closest results.";
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message, results: [hobbit] })));
  render(<App />);

  await search("hobbit");

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(screen.getByText(message)).toBeInTheDocument();
});

test("runs the search from the q parameter when the page opens", async () => {
  window.history.replaceState(null, "", "/?q=tolkien%20hobbit");
  const fetchMock = vi.fn((_url: string, _init: RequestInit) => reply({ aiUsed: true, message: null, results: [hobbit] }));
  vi.stubGlobal("fetch", fetchMock);

  render(<App />);

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(JSON.parse(fetchMock.mock.calls[0][1].body as string)).toEqual({ query: "tolkien hobbit" });
  expect(screen.getByRole("searchbox")).toHaveValue("tolkien hobbit");
});

test("does not search on open when there is no q parameter", () => {
  const fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);

  render(<App />);

  expect(fetchMock).not.toHaveBeenCalled();
});

test("puts the searched text in the q parameter so the page can be shared", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);

  await search("tolkien hobbit");
  await screen.findByText("The Hobbit");

  expect(new URLSearchParams(window.location.search).get("q")).toBe("tolkien hobbit");
});

test("sets the page title to the book count and the query after a search", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);
  expect(document.title).toBe("Find That Book");

  await search("tolkien hobbit");
  await screen.findByText("The Hobbit");

  expect(document.title).toBe("1 book for “tolkien hobbit” · Find That Book");
});

test("moves focus to a results heading that names the query and the book count", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);

  await search("tolkien hobbit");

  const heading = await screen.findByRole("heading", { level: 2, name: "1 book for “tolkien hobbit”" });
  expect(heading).toHaveFocus();
  expect(screen.getByRole("heading", { level: 3, name: "The Hobbit" })).toBeInTheDocument();
});

test("announces the AI notice as a status and styles the weak-match notice as information", async () => {
  const message = "No confident match. These are the closest results.";
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: false, message, results: [hobbit] })));
  render(<App />);

  await search("hobbit");

  await screen.findByText("The Hobbit");
  const ai = screen.getByText(/basic parsing/i);
  const weak = screen.getByText(message);
  expect(ai.closest('[role="status"]')).toHaveClass("toast", "notice");
  expect(ai.closest('[role="status"]')).not.toHaveClass("info");
  expect(weak.closest('[role="status"]')).toHaveClass("toast", "info");
});

test("the weak-match toast can be dismissed without hiding the AI notice, and returns after the next search", async () => {
  const message = "No confident match. These are the closest results.";
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: false, message, results: [hobbit] })));
  render(<App />);
  await search("hobbit");
  expect(await screen.findByText(message)).toBeInTheDocument();
  expect(screen.getByText("Closest results")).toBeInTheDocument();

  await userEvent.click(within(screen.getByText("Closest results").closest(".toast") as HTMLElement).getByRole("button", { name: "Dismiss" }));
  expect(screen.queryByText(message)).not.toBeInTheDocument();
  expect(screen.getByText("Basic search mode")).toBeInTheDocument();

  await userEvent.click(screen.getByRole("button", { name: /search/i }));
  expect(await screen.findByText(message)).toBeInTheDocument();
});

test("the empty state is an info toast that can be dismissed and returns after the next search", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: "No matching books found.", results: [] })));
  const { container } = render(<App />);
  await search("zzzz");

  const toast = (await screen.findByText("No matching books found.")).closest(".toast") as HTMLElement;
  expect(toast).toHaveClass("info");
  expect(toast).toHaveAttribute("role", "status");
  expect(toast).toHaveTextContent("Try just the author's last name, or fewer words.");
  expect(container.querySelector(".empty")).toBeNull();

  await userEvent.click(within(toast).getByRole("button", { name: "Dismiss" }));
  expect(screen.queryByText("No matching books found.")).not.toBeInTheDocument();

  await userEvent.click(screen.getByRole("button", { name: /search/i }));
  expect(await screen.findByText("No matching books found.")).toBeInTheDocument();
});

test("the empty state suggests how to search again", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: "No matching books found.", results: [] })));
  render(<App />);

  await search("zzzz");

  expect(await screen.findByText(/try just the author/i)).toBeInTheDocument();
});

test("the error toast can be dismissed", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ title: "Open Library is unavailable." }, 502, "application/problem+json")));
  render(<App />);
  await search("hobbit");
  expect(await screen.findByRole("alert")).toHaveTextContent("Search failed");

  await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));

  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

test("the basic search notice stays dismissed for later searches", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: false, message: null, results: [hobbit] })));
  render(<App />);
  await search("hobbit");
  expect(await screen.findByText("Basic search mode")).toBeInTheDocument();

  await userEvent.click(screen.getByRole("button", { name: "Dismiss" }));
  expect(screen.queryByText("Basic search mode")).not.toBeInTheDocument();

  await userEvent.click(screen.getByRole("button", { name: /search/i }));
  await screen.findByText("The Hobbit");
  expect(screen.queryByText("Basic search mode")).not.toBeInTheDocument();
});

test("does not show the basic search notice next to an empty result", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: false, message: "No matching books found.", results: [] })));
  render(<App />);

  await search("zzzz");

  await screen.findByText("No matching books found.");
  expect(screen.queryByText("Basic search mode")).not.toBeInTheDocument();
});

test("the empty state gives its advice once, even when the server message already carries advice", async () => {
  const serverMessage = "No matching books found. Try adding an author or a different part of the title.";
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: serverMessage, results: [] })));
  render(<App />);

  await search("zzzz");

  const toast = (await screen.findByText("No matching books found.")).closest(".toast") as HTMLElement;
  expect(toast).toHaveTextContent("Try just the author's last name, or fewer words.");
  expect(toast).not.toHaveTextContent("Try adding an author");
});

test("the error toast has a try again button that repeats the search", async () => {
  const fetchMock = vi
    .fn((_url: string, _init: RequestInit) => reply({ title: "Open Library is unavailable." }, 502, "application/problem+json"))
    .mockImplementationOnce(() => reply({ title: "Open Library is unavailable." }, 502, "application/problem+json"))
    .mockImplementationOnce(() => reply({ aiUsed: true, message: null, results: [hobbit] }));
  vi.stubGlobal("fetch", fetchMock);
  render(<App />);
  await search("hobbit");
  await screen.findByRole("alert");

  await userEvent.click(screen.getByRole("button", { name: /try again/i }));

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(fetchMock).toHaveBeenCalledTimes(2);
  expect(JSON.parse(fetchMock.mock.calls[1][1].body as string)).toEqual({ query: "hobbit" });
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

test("shows the progress stage while searching and removes it with the results", async () => {
  let finish: (response: Response) => void = () => {};
  vi.stubGlobal("fetch", vi.fn(() => new Promise<Response>((resolve) => (finish = resolve))));
  render(<App />);

  await search("tolkien hobbit");

  await screen.findByRole("status");
  expect(document.querySelectorAll(".stage")).toHaveLength(1);

  finish(new Response(JSON.stringify({ aiUsed: true, message: null, results: [hobbit] }), { status: 200 }));

  expect(await screen.findByText("The Hobbit")).toBeInTheDocument();
  expect(document.querySelectorAll(".stage")).toHaveLength(0);
});

test("the hero shrinks after the first search", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  render(<App />);
  const hero = document.querySelector(".hero") as HTMLElement;
  expect(hero).not.toHaveClass("compact");

  await search("hobbit");
  await screen.findByText("The Hobbit");

  expect(hero).toHaveClass("compact");
});

test("saves the examples after a successful search and restores them on the next visit", async () => {
  vi.stubGlobal("fetch", vi.fn(() => reply({ aiUsed: true, message: null, results: [hobbit] })));
  const first = render(<App />);

  await search("moby dick");
  await screen.findByText("The Hobbit");
  const saved = chips();
  first.unmount();
  window.history.replaceState(null, "", "/");

  render(<App />);

  expect(saved[0]).toBe("moby dick");
  expect(chips()).toEqual(saved);
});
