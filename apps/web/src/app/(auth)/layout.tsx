export default function AuthLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="flex min-h-full items-center justify-center bg-(--color-canvas-subtle)">
      <div className="w-full max-w-md px-6 py-12">
        <div className="mb-8 text-center">
          <span className="text-lg font-bold text-(--color-ink-primary)">
            Kreyora
          </span>
        </div>
        {children}
      </div>
    </div>
  );
}
