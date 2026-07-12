import { forwardRef, type HTMLAttributes } from "react";

export interface SkeletonProps extends HTMLAttributes<HTMLDivElement> {
  rounded?: "sm" | "md" | "lg" | "full";
}

const roundedClasses = {
  sm: "rounded-[var(--radius-sm)]",
  md: "rounded-[var(--radius-md)]",
  lg: "rounded-[var(--radius-lg)]",
  full: "rounded-[var(--radius-full)]",
} as const;

export const Skeleton = forwardRef<HTMLDivElement, SkeletonProps>(
  ({ className, rounded = "md", ...props }, ref) => {
    return (
      <div
        ref={ref}
        aria-hidden="true"
        className={[
          "animate-pulse bg-[var(--color-canvas-subtle)]",
          roundedClasses[rounded],
          className,
        ]
          .filter(Boolean)
          .join(" ")}
        {...props}
      />
    );
  },
);

Skeleton.displayName = "Skeleton";
