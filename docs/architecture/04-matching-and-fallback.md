# Matching and fallback design

```mermaid
flowchart LR
    q[Query text] --> parse{AI parser}
    parse -- ok --> hyp[Hypotheses, aiUsed true]
    parse -- "AiUnavailableException<br/>(error, timeout or open circuit)" --> heur[Heuristic parser] --> hyp2[Hypotheses, aiUsed false]
    hyp --> cat[Catalog lookup]
    hyp2 --> cat
    cat --> rank["Matcher: deterministic tiers<br/>T1=10 T2=20 T3 AI=30 T4=40<br/>T5 exact=50 T3 guessed=60 T5 near=70<br/>lower is stronger"]
    rank --> top[Top 5]
    top --> expl{AI explanation}
    expl -- ok --> out[Response]
    expl -- "AiUnavailableException<br/>(error, timeout or open circuit)" --> tmpl[Template explanation] --> out
```

Ranking never uses the AI. The AI only extracts the query and writes explanations.

While the AI circuit is open (see [08-ai-resilience.md](08-ai-resilience.md)), both AI steps fail at once, so a search takes the non-AI path without waiting for a timeout. Open Library has no fallback: an open Open Library circuit (see [06-open-library-protection.md](06-open-library-protection.md)) fails the search with a 502.
