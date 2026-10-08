import { expect, test } from "vitest";
import { promoteExample } from "./examples";

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
