import { useEffect, useRef, useState, type FormEvent } from 'react';
import { createJob, getJobs, statuses, updateStatus, type Job, type NewJob, type Status } from './api';
const blank: NewJob = { title: '', company: '', location: '', sourceUrl: '' };
export default function App() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [actionError, setActionError] = useState('');
  const [notice, setNotice] = useState('');
  const [filter, setFilter] = useState('All');
  const [form, setForm] = useState<NewJob>(blank);
  const [saving, setSaving] = useState(false);
  const [updating, setUpdating] = useState<string | null>(null);
  const titleRef = useRef<HTMLInputElement>(null);
  async function load() {
    setLoading(true); setLoadError('');
    try { setJobs(await getJobs()); } catch (e) { setLoadError((e as Error).message); }
    finally { setLoading(false); }
  }
  useEffect(() => { void load(); }, []);
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setActionError(''); setNotice('');
    try {
      const job = await createJob(form);
      setJobs(previous => [job, ...previous]); setForm(blank); setFilter('All'); setNotice('Jobbet er gemt lokalt.');
    } catch (e) { setActionError((e as Error).message); }
    finally { setSaving(false); }
  }
  async function changeStatus(job: Job, status: Status) {
    setUpdating(job.id); setActionError(''); setNotice('');
    try {
      const updated = await updateStatus(job.id, status);
      setJobs(previous => previous.map(item => item.id === job.id ? updated : item));
      setNotice(`Status for ${job.title} er ændret til ${statuses[status].toLowerCase()}.`);
    } catch (e) { setActionError((e as Error).message); }
    finally { setUpdating(null); }
  }
  const visible = jobs.filter(job => filter === 'All' || job.status === filter);
  const ready = !loading && !loadError;
  return <div className="min-h-screen">
    <header className="border-b border-slate-200 bg-white"><div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-5">
      <a href="/" className="flex items-center gap-3 font-bold tracking-tight"><span className="grid h-9 w-9 place-items-center rounded-xl bg-teal-800 text-white">C</span>Career Agent</a>
      <span className="rounded-full bg-teal-50 px-3 py-1 text-xs font-medium text-teal-800">Lokalt arbejdsrum</span>
    </div></header>
    <main className="mx-auto max-w-7xl px-6 py-10">
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4"><div><p className="mb-2 text-xs font-semibold uppercase tracking-widest text-teal-700">Din næste mulighed</p><h1 className="text-3xl font-bold tracking-tight text-slate-900">Mit joboverblik</h1><p className="mt-3 text-slate-500">Saml dine muligheder, og følg dem ét skridt ad gangen.</p></div><button className="primary" onClick={() => titleRef.current?.focus()}>+ Tilføj job</button></div>
      <div className="mb-8 grid gap-4 sm:grid-cols-3">{[
        ['Alle jobs', jobs.length, 'Dit samlede overblik'],
        ['Ansøgt', jobs.filter(j => j.status === 'Applied').length, 'Ansøgninger du har sendt'],
        ['Samtaler', jobs.filter(j => j.status === 'Interview').length, 'Muligheder i dialog'],
      ].map(([label, count, description]) => <section className="card p-6" key={label}><h2 className="text-sm font-medium text-slate-500">{label}</h2><p className="my-3 text-4xl font-semibold tracking-tight">{ready ? count : '—'}</p><p className="text-xs text-slate-400">{description}</p></section>)}</div>
      <div className="mb-4 min-h-6 text-sm" aria-live="polite">{notice && <p className="text-teal-800">{notice}</p>}{actionError && <p role="alert" className="rounded-lg bg-red-50 p-3 text-red-800">{actionError}</p>}</div>
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <section className="card overflow-hidden" aria-busy={loading}>
          <div className="flex flex-wrap items-center justify-between gap-4 border-b border-slate-100 p-6"><h2 className="font-semibold">Mine jobs <span className="ml-2 text-sm font-normal text-slate-400">{ready ? visible.length : ''}</span></h2><label className="flex items-center gap-2 text-sm text-slate-500">Status<select aria-label="Filtrér status" value={filter} onChange={e => setFilter(e.target.value)}><option value="All">Alle statusser</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label></div>
          {loading ? <p className="p-12 text-center text-slate-500" role="status">Henter dine jobs…</p> : loadError ? <div className="p-8" role="alert"><p className="mb-4 text-red-700">{loadError}</p><button className="secondary" onClick={() => void load()}>Prøv igen</button></div> : visible.length === 0 ? <div className="px-6 py-16 text-center"><div className="mb-4 text-3xl text-teal-700" aria-hidden="true">◇</div><h3 className="font-semibold">{jobs.length ? 'Ingen jobs med denne status' : 'Plads til din næste mulighed'}</h3><p className="mt-2 text-sm text-slate-500">{jobs.length ? 'Vælg en anden status for at se flere jobs.' : 'Tilføj dit første job i formularen for at komme i gang.'}</p></div> : <ul className="divide-y divide-slate-100">{visible.map(job => <li key={job.id} className="flex flex-wrap items-center justify-between gap-4 p-6"><div className="min-w-0 flex-1"><h3 className="break-words font-semibold">{job.title}</h3><p className="mt-1 break-words text-sm text-slate-500">{job.company}{job.location && ` · ${job.location}`}</p><div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-400"><span>Gemt {new Date(job.createdAt).toLocaleDateString('da-DK')}</span>{/^https?:\/\//i.test(job.sourceUrl) && <a className="font-medium text-teal-700 hover:underline" href={job.sourceUrl} target="_blank" rel="noopener noreferrer">Se jobopslag ↗</a>}</div></div><select className="max-w-full" aria-label={`Status for ${job.title}`} value={job.status} disabled={updating !== null} onChange={e => void changeStatus(job, e.target.value as Status)}>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></li>)}</ul>}
        </section>
        <aside className="card p-6"><h2 className="font-semibold">Tilføj en mulighed</h2><p className="mb-6 mt-2 text-sm leading-relaxed text-slate-500">Et job, du vil holde øje med? Gem det her.</p><form onSubmit={submit} className="space-y-4"><fieldset disabled={saving || !ready} className="space-y-4">
          <label>Jobtitel <span className="text-teal-700">*</span><input ref={titleRef} required maxLength={200} value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} placeholder="Fx frontendudvikler" /></label>
          <label>Virksomhed <span className="text-teal-700">*</span><input required maxLength={200} value={form.company} onChange={e => setForm({ ...form, company: e.target.value })} placeholder="Virksomhedens navn" /></label>
          <label>Sted <span className="font-normal text-slate-400">(valgfrit)</span><input maxLength={200} value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} placeholder="Fx Aarhus eller remote" /></label>
          <label>Link til job <span className="font-normal text-slate-400">(valgfrit)</span><input type="url" maxLength={2048} value={form.sourceUrl} onChange={e => setForm({ ...form, sourceUrl: e.target.value })} placeholder="https://" /></label>
          <button className="primary mt-2 w-full" type="submit">{saving ? 'Gemmer…' : 'Gem job'}</button>
        </fieldset></form><p className="mt-5 text-xs leading-relaxed text-slate-400">Du styrer processen. En statusændring sender ingen ansøgning.</p></aside>
      </div><footer className="mt-10 text-xs text-slate-400">Career Agent · Dit private joboverblik</footer>
    </main>
  </div>;
}
