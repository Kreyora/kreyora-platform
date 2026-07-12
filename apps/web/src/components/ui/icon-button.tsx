"use client";

import { forwardRef, type ButtonHTMLAttributes } from "react";
import { Button, type ButtonSize, type ButtonVariant } from "./button";

export interface IconButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant;
  size?: ButtonSize;
  loading?: boolean;
  "aria-label": string;
}

const iconSizeClasses: Record<ButtonSize, string> = {
  sm: "min-h-11 min-w-11 p-[var(--space-2)]",
  md: "min-h-11 min-w-11 p-[var(--space-3)]",
  lg: "min-h-11 min-w-11 p-[var(--space-4)]",
};

export const IconButton = forwardRef<HTMLButtonElement, IconButtonProps>(
  ({ className, size = "md", children, ...props }, ref) => {
    return (
      <Button
        ref={ref}
        size={size}
        className={[iconSizeClasses[size], "shrink-0", className].filter(Boolean).join(" ")}
        {...props}
      >
        {children}
      </Button>
    );
  },
);

IconButton.displayName = "IconButton";
