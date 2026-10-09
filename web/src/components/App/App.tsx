import { useEffect, useRef, useState, type FormEvent } from "react";
import { searchBooks } from "../../lib/api";
import { promoteExample, readStoredExamples, storeExamples } from "../../lib/examples";
import { ResultCard } from "../ResultCard/ResultCard";
import { SearchProgress } from "../SearchProgress/SearchProgress";
import { ShelfSkeleton } from "../ShelfSkeleton/ShelfSkeleton";
import { ThemeToggle } from "../ThemeToggle/ThemeToggle";
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

      {error && (
        <div role="alert" className="banner error">
          {error}
          {searched && (
            <button type="button" className="retry" onClick={() => void run(searched)}>Try again</button>
          )}
        </div>
      )}
      {data && !data.aiUsed && (
        <div role="status" className="banner notice">AI is unavailable, so results use basic parsing and may be less precise.</div>
      )}
      {loading && (
        <>
          <SearchProgress />
          <ShelfSkeleton />
        </>
      )}
      {data && data.results.length === 0 && (
        <div className="empty" role="status">
          <p>{data.message ?? "No matching books found."}</p>
          <p className="tip">Try just the author's last name, or fewer words.</p>
        </div>
      )}
      {data && data.results.length > 0 && (
        <>
          {data.message && <div role="status" className="banner info">{data.message}</div>}
          <h2 ref={resultsHeading} tabIndex={-1} className="count">
            {data.results.length} {data.results.length === 1 ? "book" : "books"} for “{searched}”
          </h2>
          <ol className="results">
            {data.results.map((book, i) => <ResultCard key={book.workId} book={book} rank={i + 1} />)}
          </ol>
        </>
      )}
    </main>
  );
}
