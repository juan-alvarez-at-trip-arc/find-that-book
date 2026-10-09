import { useEffect, useState } from "react";
import { Scene } from "./Scenes";

const STEPS = [
  { title: "Understanding your query…", short: "Understand", detail: "Pulling the title, author and year out of your words." },
  { title: "Searching Open Library…", short: "Search", detail: "Looking through millions of catalogued editions." },
  { title: "Matching titles and authors…", short: "Match", detail: "Scoring every candidate against your clues." },
  { title: "Writing explanations…", short: "Explain", detail: "Saying why each book made the list." },
];
const SLOW = "Still working, this is taking longer than usual…";
const SLOW_DETAIL = "Open Library or the AI gateway may be slow right now.";

export const STEP_MS = 2500;

/** Time-based feedback only: the API reports no progress, so the steps mirror the pipeline order, not live state. */
export function SearchProgress() {
  const [step, setStep] = useState(0);

  useEffect(() => {
    const id = setInterval(() => setStep((s) => Math.min(s + 1, STEPS.length)), STEP_MS);
    return () => clearInterval(id);
  }, []);

  const slow = step >= STEPS.length;

  return (
    <div className="stage" role="status">
      <Scene step={step} count={STEPS.length} />
      <h3 key={step} className="stage-title">{slow ? SLOW : STEPS[step].title}</h3>
      <p key={`detail-${step}`} className="stage-detail">{slow ? SLOW_DETAIL : STEPS[step].detail}</p>
      <ol className="stage-steps" aria-hidden="true">
        {STEPS.map((s, i) => (
          <li key={s.short} data-step-state={i < step ? "done" : i === step ? "active" : "pending"}>
            <b>{i < step ? "✓" : i + 1}</b>
            {s.short}
          </li>
        ))}
      </ol>
    </div>
  );
}
