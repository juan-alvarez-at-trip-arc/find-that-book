import type { ReactNode } from "react";
import "./Toast.css";

type Props = {
  kind: "error" | "notice" | "info";
  title: string;
  children: ReactNode;
  onDismiss: () => void;
  action?: ReactNode;
};

export function Toast({ kind, title, children, onDismiss, action }: Props) {
  return (
    <div role={kind === "error" ? "alert" : "status"} className={`toast ${kind}`}>
      <svg className="toast-icon" viewBox="0 0 24 24" aria-hidden="true">
        {kind === "error" ? (
          <>
            <circle cx="12" cy="12" r="10" fill="currentColor" />
            <path d="M12 7v6m0 3.5v.5" stroke="#fff" strokeWidth="2.2" strokeLinecap="round" />
          </>
        ) : kind === "info" ? (
          <>
            <circle cx="12" cy="12" r="10" fill="currentColor" />
            <path d="M12 11v6m0-9.5v.5" stroke="#fff" strokeWidth="2.2" strokeLinecap="round" />
          </>
        ) : (
          <>
            <path d="M12 2.5 22 20H2z" fill="currentColor" />
            <path d="M12 9v5m0 3v.5" stroke="#fff" strokeWidth="2.2" strokeLinecap="round" />
          </>
        )}
      </svg>
      <div className="toast-body">
        <strong>{title}</strong>
        <span>{children}</span>
        {action}
      </div>
      <button type="button" className="toast-close" aria-label="Dismiss" onClick={onDismiss}>×</button>
    </div>
  );
}
