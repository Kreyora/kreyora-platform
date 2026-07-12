"use client";

import {
  forwardRef,
  useMemo,
  useState,
  type HTMLAttributes,
  type ImgHTMLAttributes,
} from "react";

export type AvatarSize = "sm" | "md" | "lg";

export interface AvatarProps extends HTMLAttributes<HTMLSpanElement> {
  src?: string;
  alt?: string;
  name?: string;
  size?: AvatarSize;
  imageProps?: Omit<ImgHTMLAttributes<HTMLImageElement>, "src" | "alt">;
}

const sizeClasses: Record<AvatarSize, string> = {
  sm: "h-8 w-8 text-xs",
  md: "h-10 w-10 text-sm",
  lg: "h-12 w-12 text-base",
};

function getInitials(name: string): string {
  const parts = name.trim().split(/\s+/).filter(Boolean);
  if (parts.length === 0) return "?";
  if (parts.length === 1) return parts[0].slice(0, 2).toUpperCase();
  return `${parts[0][0]}${parts[parts.length - 1][0]}`.toUpperCase();
}

export const Avatar = forwardRef<HTMLSpanElement, AvatarProps>(
  ({ className, src, alt, name = "", size = "md", imageProps, ...props }, ref) => {
    const [imageError, setImageError] = useState(false);
    const initials = useMemo(() => getInitials(name), [name]);
    const showImage = Boolean(src) && !imageError;

    return (
      <span
        ref={ref}
        className={[
          "relative inline-flex shrink-0 items-center justify-center overflow-hidden rounded-[var(--radius-full)] bg-[var(--color-canvas-subtle)] font-medium text-[var(--color-ink-secondary)]",
          sizeClasses[size],
          className,
        ]
          .filter(Boolean)
          .join(" ")}
        {...props}
      >
        {showImage ? (
          <img
            src={src}
            alt={alt ?? name}
            onError={() => setImageError(true)}
            className="h-full w-full object-cover"
            {...imageProps}
          />
        ) : (
          <span aria-hidden="true">{initials}</span>
        )}
        {!showImage && alt ? <span className="sr-only">{alt}</span> : null}
      </span>
    );
  },
);

Avatar.displayName = "Avatar";
