"use client";

import {
  Children,
  forwardRef,
  isValidElement,
  type HTMLAttributes,
  type ReactNode,
  type TdHTMLAttributes,
  type ThHTMLAttributes,
} from "react";
import { Skeleton } from "./skeleton";

export interface TableProps extends HTMLAttributes<HTMLTableElement> {
  stickyHeader?: boolean;
  loading?: boolean;
  loadingRows?: number;
  empty?: boolean;
  emptyState?: ReactNode;
}

export const Table = forwardRef<HTMLTableElement, TableProps>(
  (
    {
      className,
      stickyHeader = false,
      loading = false,
      loadingRows = 5,
      empty = false,
      emptyState,
      children,
      ...props
    },
    ref,
  ) => {
    const childArray = Children.toArray(children);
    const header = childArray.find(
      (child) => isValidElement(child) && child.type === TableHeader,
    );
    const body = childArray.filter(
      (child) => !(isValidElement(child) && child.type === TableHeader),
    );

    return (
      <div className="relative w-full overflow-auto">
        <table
          ref={ref}
          className={[
            "w-full border-collapse text-sm text-[var(--color-ink-primary)]",
            stickyHeader
              ? "[&_thead_th]:sticky [&_thead_th]:top-0 [&_thead_th]:z-10 [&_thead_th]:bg-[var(--color-canvas)]"
              : undefined,
            className,
          ]
            .filter(Boolean)
            .join(" ")}
          {...props}
        >
          {header}
          {loading ? (
            <tbody>
              {Array.from({ length: loadingRows }).map((_, index) => (
                <tr key={index} className="border-b border-[var(--color-border)]">
                  <td colSpan={100} className="px-[var(--space-4)] py-[var(--space-3)]">
                    <Skeleton className="h-4 w-full" />
                  </td>
                </tr>
              ))}
            </tbody>
          ) : empty ? (
            <tbody>
              <tr>
                <td colSpan={100} className="px-[var(--space-4)] py-[var(--space-8)] text-center">
                  {emptyState ?? (
                    <p className="text-sm text-[var(--color-ink-secondary)]">No data available</p>
                  )}
                </td>
              </tr>
            </tbody>
          ) : (
            body
          )}
        </table>
      </div>
    );
  },
);

Table.displayName = "Table";

export const TableHeader = forwardRef<HTMLTableSectionElement, HTMLAttributes<HTMLTableSectionElement>>(
  ({ className, ...props }, ref) => (
    <thead
      ref={ref}
      className={["border-b border-[var(--color-border)]", className].filter(Boolean).join(" ")}
      {...props}
    />
  ),
);

TableHeader.displayName = "TableHeader";

export const TableBody = forwardRef<HTMLTableSectionElement, HTMLAttributes<HTMLTableSectionElement>>(
  ({ className, ...props }, ref) => (
    <tbody ref={ref} className={className} {...props} />
  ),
);

TableBody.displayName = "TableBody";

export const TableRow = forwardRef<HTMLTableRowElement, HTMLAttributes<HTMLTableRowElement>>(
  ({ className, ...props }, ref) => (
    <tr
      ref={ref}
      className={[
        "border-b border-[var(--color-border)] transition-colors duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:bg-[var(--color-canvas-subtle)]",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
      {...props}
    />
  ),
);

TableRow.displayName = "TableRow";

export const TableHead = forwardRef<HTMLTableCellElement, ThHTMLAttributes<HTMLTableCellElement>>(
  ({ className, ...props }, ref) => (
    <th
      ref={ref}
      className={[
        "px-[var(--space-4)] py-[var(--space-3)] text-left text-xs font-medium uppercase tracking-wide text-[var(--color-ink-secondary)]",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
      {...props}
    />
  ),
);

TableHead.displayName = "TableHead";

export const TableCell = forwardRef<HTMLTableCellElement, TdHTMLAttributes<HTMLTableCellElement>>(
  ({ className, ...props }, ref) => (
    <td
      ref={ref}
      className={["px-[var(--space-4)] py-[var(--space-3)]", className].filter(Boolean).join(" ")}
      {...props}
    />
  ),
);

TableCell.displayName = "TableCell";
