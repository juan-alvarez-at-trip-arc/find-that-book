import { useEffect, useState } from "react";
import { STEP_MS } from "../../lib/progress";
import { Scene } from "./Scenes";

const STEPS = [
  { title: "Understanding your query…", short: "Understand", detail: "Pulling the title, author and year out of your words." },
  { title: "Searching Open Library…", short: "Search", detail: "Looking through millions of catalogued editions." },
  { title: "Matching titles and authors…", short: "Match", detail: "Scoring every candidate against your clues." },
  { title: "Writing explanations…", short: "Explain", detail: "Saying why each book made the list." },
];
const SLOW = "Still working, this is taking longer than usual…";
const SLOW_DETAIL = "Open Library or the AI gateway may be slow right now.";

/** Time-based feedback only: the API reports no progress, so the steps mirror the pipeline order, not live state. */
export function SearchProgress() {
  const [step, setStep] = useState(0);

  useEffect(() => {
    if (step >= STEPS.length) return;
    const id = setTimeout(() => setStep(step + 1), STEP_MS[step]);
    return () => clearTimeout(id);
  }, [step]);

  const slow = step >= STEPS.length;

  return (
    <div className="stage" role="status">
      <Scene step={step} count={STEPS.length} />
      <h2 key={step} className="stage-title">{slow ? SLOW : STEPS[step].title}</h2>
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
