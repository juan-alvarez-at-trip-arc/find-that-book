import { render } from "@testing-library/react";
import { expect, test } from "vitest";
import { Toast } from "./Toast";

test.each(["error", "notice", "info"] as const)("the %s icon mark takes its color from the theme, not a literal", (kind) => {
  const { container } = render(<Toast kind={kind} title="Title" onDismiss={() => {}}>Message</Toast>);

  const mark = container.querySelector(".toast-icon .toast-mark");
  expect(mark).not.toBeNull();
  expect(mark).not.toHaveAttribute("stroke");
});
