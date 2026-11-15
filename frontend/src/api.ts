export const statuses = { New: 'Ny', Saved: 'Gemt', Dismissed: 'Fravalgt', Applied: 'Ansøgt', Interview: 'Samtale', Offer: 'Tilbud', Rejected: 'Afslag' } as const;
export type Status = keyof typeof statuses;
export type Job = { id: string; title: string; company: string; location: string; sourceUrl: string; status: Status; createdAt: string; description: string; score: number | null; eligibility: string; matchReason: string; matchDetails: string; seenAt: string | null; isActive: boolean; isImported: boolean; possibleDuplicateOf: string | null; aiAssessment: string | null; aiModel: string | null; publishedAt: string | null; deadline: string | null; statusVerifiedAt: string | null; country: string; employmentType: string; workplaceType: string };
export type NewJob = Pick<Job, 'title' | 'company' | 'location' | 'sourceUrl'>;
const base = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:5080').replace(/\/$/, '');
async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try { response = await fetch(`${base}${path}`, init); }
  catch { throw new Error('Kunne ikke kontakte backend. Kontrollér, at den kører på localhost:5080.'); }
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { title?: string; errors?: Record<string, string[]> } | null;
    throw new Error(problem?.errors ? Object.values(problem.errors).flat().join(' ') : problem?.title || 'Der opstod en fejl. Prøv igen.');
  }
  return response.json() as Promise<T>;
}
export const getJobs = () => request<Job[]>('/api/jobs');
export const createJob = (job: NewJob) => request<Job>('/api/jobs', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(job) });
export const updateStatus = (id: string, status: Status) => request<Job>(`/api/jobs/${id}/status`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ status }) });

export type Source = { id: string; company: string; provider: string; enabled: boolean; note: string; careerUrl: string };
export type Run = { id: string; state: string; createdAt: string; finishedAt: string | null; added: number; updated: number; details: string; error: string };
export type SourceResult = { sourceId: string; state: string; fetched: number; added: number; updated: number; message: string };
export const getSources = () => request<Source[]>('/api/sources');
export const getRuns = () => request<Run[]>('/api/discovery/runs');
export const startDiscovery = () => request<Run>('/api/discovery/runs', { method: 'POST' });
export const markSeen = (id: string) => request<Job>(`/api/jobs/${id}/seen`, { method: 'PATCH' });
export const analyzeJob = (id: string) => request<Job>(`/api/jobs/${id}/analysis`, { method: 'POST' });
export const recalculate = () => request<{ status: string }>('/api/matching/recalculate', { method: 'POST' });

export type Reference = { id: string; kind: 'Cv' | 'Application'; displayName: string; fileType: string; sizeBytes: number; createdAt: string };
export type DocumentBlock = { type: 'paragraph' | 'bullet' | 'entry' | 'subheading'; text: string; label?: string | null; period?: string | null; technologies?: string | null; evidence: { id: string; quote: string }[] };
export type DocumentIdentity = { fullName: string; email: string; phone: string; location: string; links: string };
export type DocumentProfile = { identity: DocumentIdentity; experience: string; education: string; projects: string; motivation: string };
export const emptyDocumentProfile: DocumentProfile = { identity: { fullName: '', email: '', phone: '', location: '', links: '' }, experience: '', education: '', projects: '', motivation: '' };
export const getDocumentProfile = () => request<DocumentProfile>('/api/document-profile');
export const saveDocumentProfile = (profile: DocumentProfile) => request<DocumentProfile>('/api/document-profile', { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ profile, confirmed: true }) });
export type DocumentContent = { title: string; subtitle: string | null; identity?: DocumentIdentity | null; language?: 'Dansk' | 'English' | null; sections: { heading: string; page?: 1 | 2 | null; blocks: DocumentBlock[] }[] };
export type DocumentVersion = { id: string; number: number; createdAt: string; engine: string; referenceIds: string[]; content: DocumentContent };
export type DocumentHistory = { id: string; jobId: string; kind: 'Cv' | 'Application'; versions: DocumentVersion[] };
export const getReferences = () => request<Reference[]>('/api/references');
export async function addReference(file: File, kind: Reference['kind'], displayName: string): Promise<Reference> {
  const form = new FormData(); form.set('file', file); form.set('kind', kind); form.set('displayName', displayName);
  return request<Reference>('/api/references', { method: 'POST', body: form });
}
export async function deleteReference(id: string): Promise<void> {
  let response: Response;
  try { response = await fetch(`${base}/api/references/${id}`, { method: 'DELETE' }); }
  catch { throw new Error('Kunne ikke kontakte backend. Kontrollér, at den kører på localhost:5080.'); }
  if (!response.ok) { const p = await response.json().catch(() => null) as { title?: string } | null; throw new Error(p?.title || 'Referencen kunne ikke slettes.'); }
}
export const getDocuments = (jobId: string) => request<DocumentHistory[]>(`/api/jobs/${jobId}/documents`);
export const generateDocuments = (jobId: string, kind: 'Cv' | 'Application' | 'Both', referenceIds: string[], language: 'Dansk' | 'English') =>
  request<DocumentHistory[]>(`/api/jobs/${jobId}/documents/generate`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ kind, referenceIds, language }) });
export const saveDocument = (id: string, content: DocumentContent) =>
  request<DocumentHistory[]>(`/api/documents/${id}`, { method: 'PUT', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ content }) });
export const documentExportUrl = (id: string, version: number) => `${base}/api/documents/${id}/export/docx?version=${version}`;
