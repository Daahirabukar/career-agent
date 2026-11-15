import type { Job } from './api';

export type SearchFilters = {
  query: string; level: string; ageDays: number; includeUnverified: boolean; includeDismissed: boolean; softwareOnly: boolean; sort: string;
};
export const defaultSearch: SearchFilters = { query: '', level: 'early', ageDays: 90, includeUnverified: false, includeDismissed: false, softwareOnly: true, sort: 'newest' };
export const isSenior = (title: string) => /\b(senior|sr\.?|staff|principal|lead|manager|director|head|architect|arkitekt|chef)\b/i.test(title);
export const isSoftware = (title: string) => /software|udvikler|developer|programmer|programmør|back.?end|front.?end|full.?stack|\.net|c#|web engineer/i.test(title)
  && !/\b(sales|security|sikkerhed|devops|infrastructure|support|recruiter)\b/i.test(title);
export function isVerified(job: Job, now = Date.now()) {
  return !job.isImported || (job.isActive && !!job.statusVerifiedAt && now - Date.parse(job.statusVerifiedAt) <= 14 * 86400000);
}
export function matchesSearch(job: Job, filters: SearchFilters, now = Date.now()) {
  const haystack = `${job.title} ${job.company} ${job.description}`.toLocaleLowerCase('da');
  // Commas mean alternatives, so “backend, .NET, software” expands the search.
  const alternatives = filters.query.toLocaleLowerCase('da').split(',').map(q => q.trim()).filter(Boolean);
  if (alternatives.length && !alternatives.some(q => q.split(/\s+/).every(term => haystack.includes(term)))) return false;
  if (filters.softwareOnly && !isSoftware(job.title)) return false;
  if (filters.level === 'early' && isSenior(job.title)) return false;
  if (filters.level === 'junior' && !/junior|graduate|entry.level|new grad|nyuddannet|jun\/mid/i.test(job.title)) return false;
  if (!filters.includeDismissed && ['Dismissed', 'Rejected'].includes(job.status)) return false;
  if (!filters.includeUnverified && (!isVerified(job, now) || !job.isActive || (job.deadline && Date.parse(job.deadline) < now))) return false;
  // Missing source dates stay visible, explicitly labelled by the UI; discovery is never a publication date.
  if (filters.ageDays && job.publishedAt && now - Date.parse(job.publishedAt) > filters.ageDays * 86400000) return false;
  return true;
}
export function compareJobs(a: Job, b: Job, sort: string) {
  const published = (job: Job) => job.publishedAt ? Date.parse(job.publishedAt) : 0;
  const score = (b.score ?? -1) - (a.score ?? -1);
  const recent = published(b) - published(a);
  return (sort === 'score' ? score || recent : recent || score) || a.id.localeCompare(b.id);
}
