export const motionClasses = {
  fadeIn: "animate-fade-in",
  fadeOut: "animate-fade-out",
  slideInRight: "animate-slide-in-right",
  slideOutRight: "animate-slide-out-right",
  staggerChildren: "stagger-children",
  hoverLift:
    "transition-transform duration-[var(--duration-hover)] ease-[var(--easing-default)] hover:-translate-y-[2px]",
  pressFeedback:
    "transition-transform duration-[var(--duration-press)] ease-[var(--easing-default)] active:scale-[0.97]",
} as const;

export type MotionClass = keyof typeof motionClasses;
