import { forwardRef, type HTMLAttributes } from "react";

export interface DividerProps extends HTMLAttributes<HTMLHRElement> {
  decorative?: boolean;
}

export const Divider = forwardRef<HTMLHRElement, DividerProps>(
  ({ className, decorative = true, ...props }, ref) => {
    return (
      <hr
        ref={ref}
        role={decorative ? "presentation" : "separator"}
        className={[
          "h-px w-full border-0 bg-[var(--color-border)]",
          className,
        ]
          .filter(Boolean)
          .join(" ")}
        {...props}
      />
    );
  },
);

Divider.displayName = "Divider";
