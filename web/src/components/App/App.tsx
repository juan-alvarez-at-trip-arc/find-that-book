import { useEffect, useRef, useState, type FormEvent } from "react";
import { searchBooks } from "../../lib/api";
import { promoteExample, readStoredExamples, storeExamples } from "../../lib/examples";
import { FeaturedResults } from "../FeaturedResults/FeaturedResults";
import { ResultsSkeleton } from "../ResultsSkeleton/ResultsSkeleton";
import { SearchProgress } from "../SearchProgress/SearchProgress";
import { ThemeToggle } from "../ThemeToggle/ThemeToggle";
import { Toast } from "../Toast/Toast";
import type { SearchResponse } from "../../lib/types";
import "./App.css";

const INITIAL_EXAMPLES = [
  "tolkien hobbit illustrated deluxe 1937",
  "mark huckleberry",
  "austen bennet",
  "dickens",
  "twilight meyer",
];

function queryFromUrl(): string {
  return (new URLSearchParams(window.location.search).get("q") ?? "").slice(0, 200);
}

export default function App() {
  const [query, setQuery] = useState(queryFromUrl);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [data, setData] = useState<SearchResponse | null>(null);
  const [examples, setExamples] = useState(() => readStoredExamples(INITIAL_EXAMPLES));
  const [searched, setSearched] = useState<string | null>(null);
  const [dismissed, setDismissed] = useState<string[]>([]);
  const latest = useRef(0);
  const opened = useRef(false);
  const resultsHeading = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (opened.current) return;
    opened.current = true;
    if (query.trim()) void run(query);
  });

  useEffect(() => {
    if (data && data.results.length > 0) resultsHeading.current?.focus();
  }, [data]);

  async function run(text: string) {
    if (!text.trim()) return;
    window.history.replaceState(null, "", `?${new URLSearchParams({ q: text.trim() })}`);
    setSearched(text.trim());
    const id = ++latest.current;
    setLoading(true);
    setDismissed([]);
    setError(null);
    setData(null);
    try {
      const response = await searchBooks(text);
      if (id === latest.current) {
        setData(response);
        if (response.results.length > 0) {
          setExamples((current) => {
            const next = promoteExample(current, text);
            storeExamples(next);
            return next;
          });
        }
      }
    } catch (e) {
      if (id !== latest.current) return;
      setData(null);
      setError(e instanceof Error && !(e instanceof TypeError) ? e.message : "Something went wrong. Please try again.");
    } finally {
      if (id === latest.current) setLoading(false);
    }
  }

  function onSubmit(event: FormEvent) {
    event.preventDefault();
    void run(query);
  }

  return (
    <main className="page">
      <header className="top">
        <div className="logo">Find<span>That</span>Book</div>
        <ThemeToggle />
      </header>

      <section className={searched ? "hero compact" : "hero"}>
        <h1>What was that book?</h1>
        <p className="lead">Type a title, an author, or whatever you remember.</p>

        <form onSubmit={onSubmit} className="search">
          <input
            type="search"
            value={query}
            onChange={(e) => setQuery(e.target.value)}
            placeholder="e.g. tolkien hobbit illustrated deluxe 1937"
            aria-label="Search for a book"
            maxLength={200}
            disabled={loading}
          />
          <button type="submit" disabled={loading}>{loading ? "Searching…" : "Search"}</button>
        </form>

        <div className="examples">
          {examples.map((example) => (
            <button
              key={example}
              type="button"
              className="chip"
              disabled={loading}
              onClick={() => {
                setQuery(example);
                void run(example);
              }}
            >
              {example}
            </button>
          ))}
        </div>
      </section>

      <div className="toasts">
        {error && !dismissed.includes("error") && (
          <Toast
            kind="error"
            title="Search failed"
            onDismiss={() => setDismissed((current) => [...current, "error"])}
            action={searched && <button type="button" className="toast-action" onClick={() => void run(searched)}>Try again</button>}
          >
            {error}
          </Toast>
        )}
        {data && !data.aiUsed && !dismissed.includes("notice") && (
          <Toast kind="notice" title="Basic search mode" onDismiss={() => setDismissed((current) => [...current, "notice"])}>
            AI is unavailable, so results use basic parsing and may be less precise.
          </Toast>
        )}
        {data && data.results.length > 0 && data.message && !dismissed.includes("info") && (
          <Toast kind="info" title="Closest results" onDismiss={() => setDismissed((current) => [...current, "info"])}>
            {data.message}
          </Toast>
        )}
        {data && data.results.length === 0 && !dismissed.includes("empty") && (
          <Toast kind="info" title={data.message ?? "No matching books found."} onDismiss={() => setDismissed((current) => [...current, "empty"])}>
            Try just the author's last name, or fewer words.
          </Toast>
        )}
      </div>
      {loading && (
        <>
          <SearchProgress />
          <ResultsSkeleton />
        </>
      )}
      {data && data.results.length > 0 && (
        <>
          <h2 ref={resultsHeading} tabIndex={-1} className="count">
            {data.results.length} {data.results.length === 1 ? "book" : "books"} for “{searched}”
          </h2>
          <FeaturedResults results={data.results} />
        </>
      )}
    </main>
  );
}
