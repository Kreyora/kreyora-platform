"use client";

import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import type { AssistantPolicy, AssistantPolicyUpdate, DailyHours } from "@/lib/types";
import { ErrorNote, describeAssistantError, type ScreenError } from "./assistant-display";

const LANGUAGES = [
  { code: "ne", label: "Nepali (Devanagari)" },
  { code: "ne-Latn", label: "Romanized Nepali" },
  { code: "en", label: "English" },
] as const;

const DAY_LABEL: Record<DailyHours["day"], string> = {
  sunday: "Sunday", monday: "Monday", tuesday: "Tuesday", wednesday: "Wednesday", thursday: "Thursday", friday: "Friday", saturday: "Saturday",
};

const control = "min-h-11 rounded-[var(--radius-md)] border border-[var(--color-border)] bg-[var(--color-canvas)] px-3 text-sm disabled:opacity-60";
const field = `${control} w-full`;

function toUpdate(policy: AssistantPolicy): AssistantPolicyUpdate {
  const { timeZone: _tz, reviewedAt: _r, fixedEscalationCategories: _c, availableTools: _a, platformCaps: _p, ...rest } = policy;
  void _tz; void _r; void _c; void _a; void _p;
  return rest;
}

export interface PolicyFormProps {
  policy: AssistantPolicy;
  canEdit: boolean;
  onSave: (update: AssistantPolicyUpdate) => Promise<AssistantPolicy>;
  onRefresh: () => void;
}

/** Assistant settings. Saving stamps "reviewed" on the server; a stale version is refused (409) and asks for a refresh. */
export function PolicyForm({ policy, canEdit, onSave, onRefresh }: PolicyFormProps) {
  const [draft, setDraft] = useState<AssistantPolicyUpdate>(() => toUpdate(policy));
  const [keywords, setKeywords] = useState(policy.escalationKeywords.join(", "));
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<ScreenError | null>(null);
  const [saved, setSaved] = useState(false);
  const set = <K extends keyof AssistantPolicyUpdate>(key: K, value: AssistantPolicyUpdate[K]) => {
    setSaved(false);
    setDraft((d) => ({ ...d, [key]: value }));
  };
  const disabled = !canEdit || saving;

  async function submit(event: FormEvent) {
    event.preventDefault();
    if (!canEdit) return;
    if (draft.supportedLanguages.length === 0) {
      setError({ kind: "validation", message: "Choose at least one language." });
      return;
    }

    setSaving(true);
    setError(null);
    try {
      const next = await onSave({ ...draft, escalationKeywords: keywords.split(",").map((k) => k.trim()).filter(Boolean) });
      setDraft(toUpdate(next));
      setSaved(true);
    } catch (failure) {
      setError(describeAssistantError(failure));
    } finally {
      setSaving(false);
    }
  }

  return (
    <form id="settings" onSubmit={submit} className="min-w-0 rounded-[var(--radius-lg)] border border-[var(--color-border)] p-5" aria-labelledby="settings-title">
      <h2 id="settings-title" className="text-sm font-semibold text-[var(--color-ink-primary)]">Settings</h2>
      <p className="mt-1 text-xs text-[var(--color-ink-secondary)]">
        {policy.reviewedAt ? `Last reviewed ${new Date(policy.reviewedAt).toLocaleDateString("en-GB")}.` : "Not reviewed yet — saving marks the settings reviewed."}
        {!canEdit && " View only: ask the owner or an admin to change these."}
      </p>

      <fieldset className="mt-4 min-w-0" disabled={disabled}>
        <label className="flex min-h-11 items-center gap-3 text-sm text-[var(--color-ink-primary)]">
          <input type="checkbox" className="h-5 w-5" checked={draft.enabled} onChange={(e) => set("enabled", e.target.checked)} />
          Answer customers automatically when ready
        </label>

        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <label className="text-sm text-[var(--color-ink-primary)]">
            Reply language
            <select className={`${field} mt-1`} value={draft.replyStyle} onChange={(e) => set("replyStyle", e.target.value as AssistantPolicyUpdate["replyStyle"])}>
              <option value="matchCustomer">Match the customer</option>
              <option value="alwaysRomanized">Always Romanized Nepali</option>
              <option value="alwaysDevanagari">Always Nepali (Devanagari)</option>
              <option value="alwaysEnglish">Always English</option>
            </select>
          </label>
          <label className="text-sm text-[var(--color-ink-primary)]">
            Tone
            <select className={`${field} mt-1`} value={draft.tone} onChange={(e) => set("tone", e.target.value as AssistantPolicyUpdate["tone"])}>
              <option value="friendly">Friendly</option>
              <option value="formal">Formal</option>
            </select>
          </label>
        </div>

        <fieldset className="mt-4 min-w-0">
          <legend className="text-sm text-[var(--color-ink-primary)]">Languages customers write in</legend>
          <div className="mt-1 flex flex-wrap gap-x-4">
            {LANGUAGES.map((language) => (
              <label key={language.code} className="flex min-h-11 items-center gap-2 text-sm">
                <input
                  type="checkbox"
                  className="h-5 w-5"
                  checked={draft.supportedLanguages.includes(language.code)}
                  onChange={(e) => set("supportedLanguages", e.target.checked ? [...draft.supportedLanguages, language.code] : draft.supportedLanguages.filter((l) => l !== language.code))}
                />
                {language.label}
              </label>
            ))}
          </div>
        </fieldset>

        <label className="mt-4 block text-sm text-[var(--color-ink-primary)]">
          Brand note <span className="text-xs text-[var(--color-ink-secondary)]">(how your shop talks; no prices or promises)</span>
          <textarea className={`${field} mt-1 min-h-20 py-2`} maxLength={300} value={draft.brandNote ?? ""} onChange={(e) => set("brandNote", e.target.value || null)} />
        </label>

        <fieldset className="mt-4 min-w-0">
          <legend className="text-sm text-[var(--color-ink-primary)]">Business hours ({policy.timeZone})</legend>
          <div className="mt-2 space-y-1">
            {draft.businessHours.map((hours, index) => (
              <div key={hours.day} className="flex flex-wrap items-center gap-2 text-sm">
                <span className="w-24 shrink-0">{DAY_LABEL[hours.day]}</span>
                <label className="flex min-h-11 items-center gap-1">
                  <input type="checkbox" className="h-5 w-5" checked={hours.closed} onChange={(e) => set("businessHours", draft.businessHours.map((h, i) => (i === index ? { ...h, closed: e.target.checked } : h)))} />
                  Closed
                </label>
                {!hours.closed && (
                  <>
                    <label className="sr-only" htmlFor={`opens-${hours.day}`}>{DAY_LABEL[hours.day]} opens</label>
                    <input id={`opens-${hours.day}`} type="time" className={`${control} w-32 min-w-0`} value={hours.opens ?? "09:00"} onChange={(e) => set("businessHours", draft.businessHours.map((h, i) => (i === index ? { ...h, opens: e.target.value } : h)))} />
                    <span aria-hidden="true">–</span>
                    <label className="sr-only" htmlFor={`closes-${hours.day}`}>{DAY_LABEL[hours.day]} closes</label>
                    <input id={`closes-${hours.day}`} type="time" className={`${control} w-32 min-w-0`} value={hours.closes ?? "19:00"} onChange={(e) => set("businessHours", draft.businessHours.map((h, i) => (i === index ? { ...h, closes: e.target.value } : h)))} />
                  </>
                )}
              </div>
            ))}
          </div>
        </fieldset>

        <div className="mt-4 grid gap-4 sm:grid-cols-2">
          <label className="text-sm text-[var(--color-ink-primary)]">
            Outside business hours
            <select className={`${field} mt-1`} value={draft.outsideHoursBehavior} onChange={(e) => set("outsideHoursBehavior", e.target.value as AssistantPolicyUpdate["outsideHoursBehavior"])}>
              <option value="answerAndPromiseFollowUp">Answer and say the team follows up</option>
              <option value="answerNormally">Answer normally</option>
              <option value="doNotAnswer">Don&apos;t answer</option>
            </select>
          </label>
          <label className="text-sm text-[var(--color-ink-primary)]">
            Photos and files it can&apos;t read
            <select className={`${field} mt-1`} value={draft.unrecognizedMediaBehavior} onChange={(e) => set("unrecognizedMediaBehavior", e.target.value as AssistantPolicyUpdate["unrecognizedMediaBehavior"])}>
              <option value="askForDetails">Ask for the product name</option>
              <option value="handToPerson">Hand to a person</option>
            </select>
          </label>
        </div>

        <label className="mt-4 block text-sm text-[var(--color-ink-primary)]">
          Hand to a person when a message mentions <span className="text-xs text-[var(--color-ink-secondary)]">(comma-separated)</span>
          <input className={`${field} mt-1`} value={keywords} onChange={(e) => { setSaved(false); setKeywords(e.target.value); }} placeholder="wholesale, refund" />
        </label>

        <fieldset className="mt-4 min-w-0">
          <legend className="text-sm text-[var(--color-ink-primary)]">What the assistant may do</legend>
          <p className="text-xs text-[var(--color-ink-secondary)]">It can always hand a chat to a person. Facts like prices and stock always come from your shop data.</p>
          <div className="mt-1 grid gap-x-4 sm:grid-cols-2">
            {policy.availableTools.filter((tool) => tool !== "EscalateToHuman").map((tool) => (
              <label key={tool} className="flex min-h-11 items-center gap-2 text-sm">
                <input type="checkbox" className="h-5 w-5" checked={draft.allowedTools.includes(tool)} onChange={(e) => set("allowedTools", e.target.checked ? [...draft.allowedTools, tool] : draft.allowedTools.filter((t) => t !== tool))} />
                {tool}
              </label>
            ))}
          </div>
        </fieldset>

        <div className="mt-4 grid gap-4 sm:grid-cols-3">
          {([
            ["maxToolSteps", "Tool steps per reply", policy.platformCaps.maxToolSteps],
            ["maxRepliesPerConversationPerHour", "Replies per chat per hour", policy.platformCaps.maxRepliesPerConversationPerHour],
            ["maxOutputTokens", "Reply length (tokens)", policy.platformCaps.maxOutputTokens],
          ] as const).map(([key, label, cap]) => (
            <label key={key} className="text-sm text-[var(--color-ink-primary)]">
              {label} <span className="text-xs text-[var(--color-ink-secondary)]">(max {cap})</span>
              <input type="number" className={`${field} mt-1`} min={1} max={cap} value={draft[key]} onChange={(e) => set(key, Math.min(cap, Math.max(1, Number(e.target.value) || 1)))} />
            </label>
          ))}
        </div>
      </fieldset>

      {error && <ErrorNote error={error} />}
      {error?.kind === "conflict" && <Button type="button" variant="outline" className="mt-2" onClick={onRefresh}>Refresh</Button>}
      {saved && <p role="status" className="mt-4 text-sm text-[var(--color-ink-primary)]">Saved. Settings are marked reviewed.</p>}
      {canEdit && <Button type="submit" className="mt-4" disabled={saving}>{saving ? "Saving…" : "Save settings"}</Button>}
    </form>
  );
}
