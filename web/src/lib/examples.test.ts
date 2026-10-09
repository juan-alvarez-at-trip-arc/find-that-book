import { beforeEach, describe, expect, test, vi } from "vitest";
import { promoteExample, readStoredExamples, storeExamples } from "./examples";

const base = ["a", "b", "c", "d", "e"];

test("puts a new query first and drops the last example", () => {
  expect(promoteExample(base, "new")).toEqual(["new", "a", "b", "c", "d"]);
});

test("moves an existing example to the front without dropping anything", () => {
  expect(promoteExample(base, "c")).toEqual(["c", "a", "b", "d", "e"]);
});

test("matches existing examples ignoring case and surrounding spaces", () => {
  expect(promoteExample(["Moby Dick", "dickens"], "  moby dick ")).toEqual(["moby dick", "dickens"]);
});

test("keeps the list length stable", () => {
  expect(promoteExample(base, "x")).toHaveLength(5);
  expect(promoteExample(base, "e")).toHaveLength(5);
});

test("ignores blank queries", () => {
  expect(promoteExample(base, "   ")).toEqual(base);
});

test("does not mutate its input", () => {
  const copy = [...base];

  promoteExample(base, "new");

  expect(base).toEqual(copy);
});

describe("stored examples", () => {
  beforeEach(() => localStorage.clear());

  test("returns the defaults when nothing is stored", () => {
    expect(readStoredExamples(base)).toEqual(base);
  });

  test("returns what was stored", () => {
    storeExamples(["z", "y", "x", "w", "v"]);
    expect(readStoredExamples(base)).toEqual(["z", "y", "x", "w", "v"]);
  });

  test.each([
    ["not json", "{"],
    ["not an array", '"a"'],
    ["wrong length", '["a","b"]'],
    ["a non-string item", '["a","b","c","d",5]'],
    ["a blank item", '["a","b","c","d"," "]'],
  ])("falls back to the defaults for %s", (_name, raw) => {
    localStorage.setItem("ftb-examples", raw);
    expect(readStoredExamples(base)).toEqual(base);
  });

  test("works when storage is blocked", () => {
    const blocked = vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("blocked");
    });
    const blockedSet = vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => {
      throw new Error("blocked");
    });

    expect(readStoredExamples(base)).toEqual(base);
    expect(() => storeExamples(base)).not.toThrow();

    blocked.mockRestore();
    blockedSet.mockRestore();
  });
});
