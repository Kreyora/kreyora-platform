"use client";

import { useInView } from "@/hooks/use-in-view";
import type { ReactNode } from "react";

interface SectionProps {
  children: ReactNode;
  className?: string;
  dark?: boolean;
  id?: string;
}

export function Section({ children, className, dark, id }: SectionProps) {
  const { ref, inView } = useInView({ threshold: 0.05 });

  return (
    <section
      ref={ref as React.RefObject<HTMLElement>}
      id={id}
      className={[
        "transition-[opacity,transform] duration-[var(--duration-entrance)] ease-[var(--easing-default)]",
        inView ? "opacity-100 translate-y-0" : "opacity-0 translate-y-3",
        dark
          ? "bg-[var(--color-surface-dark)] text-[var(--color-on-dark)]"
          : "bg-[var(--color-canvas)]",
        className,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      {children}
    </section>
  );
}
