"use client";

import { Component, type ErrorInfo, type ReactNode } from "react";
import { Button } from "./button";

export interface ErrorBoundaryProps {
  children: ReactNode;
  fallbackTitle?: string;
  fallbackDescription?: string;
  retryLabel?: string;
  onError?: (error: Error, errorInfo: ErrorInfo) => void;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo): void {
    this.props.onError?.(error, errorInfo);
  }

  handleRetry = (): void => {
    this.setState({ hasError: false, error: null });
  };

  render(): ReactNode {
    if (this.state.hasError) {
      return (
        <div
          role="alert"
          className="flex flex-col items-center justify-center gap-[var(--space-4)] px-[var(--space-6)] py-[var(--space-12)] text-center"
        >
          <div className="flex max-w-md flex-col gap-[var(--space-2)]">
            <h2 className="text-base font-semibold text-[var(--color-ink-primary)]">
              {this.props.fallbackTitle ?? "Something went wrong"}
            </h2>
            <p className="text-sm text-[var(--color-ink-secondary)]">
              {this.props.fallbackDescription ??
                "An unexpected error occurred. Please try again."}
            </p>
          </div>
          <Button type="button" variant="outline" onClick={this.handleRetry}>
            {this.props.retryLabel ?? "Try again"}
          </Button>
        </div>
      );
    }

    return this.props.children;
  }
}
