import { useEffect, useState } from "react";

const STEPS = [
  "Understanding your query…",
  "Searching Open Library…",
  "Matching titles and authors…",
  "Writing explanations…",
];
const SLOW = "Still working, this is taking longer than usual…";

export const STEP_MS = 2500;

/** Time-based feedback only: the API reports no progress, so the steps mirror the pipeline order, not live state. */
export function SearchProgress() {
  const [step, setStep] = useState(0);

  useEffect(() => {
    const id = setInterval(() => setStep((s) => Math.min(s + 1, STEPS.length)), STEP_MS);
    return () => clearInterval(id);
  }, []);

  const text = step < STEPS.length ? STEPS[step] : SLOW;

  return (
    <div className="progress" role="status">
      <div className="progress-bar" aria-hidden="true">
        <span />
      </div>
      <div className="progress-row">
        <span className="dots" aria-hidden="true">
          <i />
          <i />
          <i />
        </span>
        <span key={step} className="progress-text">{text}</span>
      </div>
      <ol className="pips" aria-hidden="true">
        {STEPS.map((label, i) => (
          <li key={label} data-step-state={i < step ? "done" : i === step ? "active" : "pending"} />
        ))}
      </ol>
    </div>
  );
}
