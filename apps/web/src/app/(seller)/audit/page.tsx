import { ViewerBadge } from "@/components/viewer-badge";

export default function AuditPage() {
  return (
    <div>
      <h1 className="text-heading-page text-(--color-ink-primary)">Audit Activity</h1>
      <div className="mt-2"><ViewerBadge /></div>
      <p className="mt-4 text-body-app text-(--color-ink-secondary)">
        This page will be implemented in M01-S08.
      </p>
    </div>
  );
}
