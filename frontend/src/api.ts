export const statuses = { Saved: 'Gemt', Applied: 'Ansøgt', Interview: 'Samtale', Offer: 'Tilbud', Rejected: 'Afslag' } as const;
export type Status = keyof typeof statuses;
export type Job = { id: string; title: string; company: string; location: string; sourceUrl: string; status: Status; createdAt: string };
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
