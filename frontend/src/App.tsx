import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { addReference, analyzeJob, createJob, deleteReference, documentExportUrl, generateDocuments, getDocuments, getJobs, getReferences, getRuns, getSources, markSeen, recalculate, saveDocument, startDiscovery, statuses, updateStatus, type DocumentContent, type DocumentHistory, type Job, type NewJob, type Reference, type Run, type Source, type SourceResult, type Status } from './api';
const blank: NewJob = { title: '', company: '', location: '', sourceUrl: '' };
const runLabels: Record<string, string> = { Queued: 'I kø', Running: 'Henter jobs…', Completed: 'Gennemført', Partial: 'Delvist gennemført', Failed: 'Fejlet', Interrupted: 'Afbrudt' };
function parse<T>(value: string | null, fallback: T): T { try { return value ? JSON.parse(value) as T : fallback; } catch { return fallback; } }
type Ai = { summary: string; requirementsMet: string[]; transferableSkills: string[]; gaps: string[]; realism: string; uncertainties: string[]; evidence: { quote: string; claim: string }[] };
type MatchDetails = { score?: number; role?: number; experience?: number; skills?: number; geography?: number; development?: number; realism?: string; metRequirements?: string[]; matchedSkills?: string[]; transferableSkills?: string[]; missingSkills?: string[]; uncertainties?: string[] };
const jobTypes = ['Software', 'Backend', 'Fullstack', 'Frontend', '.NET/C#', 'Cybersikkerhed', 'Automatisering', 'System/integration', 'Graduate'];
function jobType(job: Job) {
  const title = job.title.toLowerCase();
  if (/graduate|entry.level|new grad/.test(title)) return 'Graduate';
  if (/cyber|security|sikkerhed/.test(title)) return 'Cybersikkerhed';
  if (/automation|automatisering/.test(title)) return 'Automatisering';
  if (/integration|system developer|systemudvikler/.test(title)) return 'System/integration';
  if (/full.?stack/.test(title)) return 'Fullstack';
  if (/front.?end/.test(title)) return 'Frontend';
  if (/back.?end/.test(title)) return 'Backend';
  if (/\.net|c#/.test(title)) return '.NET/C#';
  return 'Software';
}
export default function App() {
  const [jobs, setJobs] = useState<Job[]>([]);
  const [runs, setRuns] = useState<Run[]>([]);
  const [sources, setSources] = useState<Source[]>([]);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState('');
  const [actionError, setActionError] = useState('');
  const [notice, setNotice] = useState('');
  const [filter, setFilter] = useState('All');
  const [showExcluded, setShowExcluded] = useState(false);
  const [unseenOnly, setUnseenOnly] = useState(false);
  const [locationFilter, setLocationFilter] = useState('');
  const [typeFilter, setTypeFilter] = useState('All');
  const [minimumScore, setMinimumScore] = useState(0);
  const [form, setForm] = useState<NewJob>(blank);
  const [saving, setSaving] = useState(false);
  const [updating, setUpdating] = useState<string | null>(null);
  const [analyzing, setAnalyzing] = useState<string | null>(null);
  const [starting, setStarting] = useState(false);
  const [expanded, setExpanded] = useState<string | null>(null);
  const [references, setReferences] = useState<Reference[]>([]);
  const [documentHistories, setDocumentHistories] = useState<Record<string, DocumentHistory[]>>({});
  const [activeVersions, setActiveVersions] = useState<Record<string, number>>({});
  const [documentDrafts, setDocumentDrafts] = useState<Record<string, DocumentContent>>({});
  const [selectedReferences, setSelectedReferences] = useState<string[]>([]);
  const [documentLanguage, setDocumentLanguage] = useState<'Dansk' | 'English'>('Dansk');
  const [generatingDocuments, setGeneratingDocuments] = useState(false);
  const [savingDocument, setSavingDocument] = useState<string | null>(null);
  const [uploadingReference, setUploadingReference] = useState(false);
  const [referenceFile, setReferenceFile] = useState<File | null>(null);
  const [referenceKind, setReferenceKind] = useState<Reference['kind']>('Cv');
  const [referenceName, setReferenceName] = useState('');
  const titleRef = useRef<HTMLInputElement>(null);
  const latest = runs[0];
  const running = latest?.state === 'Queued' || latest?.state === 'Running';
  const load = useCallback(async () => {
    setLoading(true); setLoadError('');
    try { setJobs(await getJobs()); } catch (e) { setLoadError((e as Error).message); }
    finally { setLoading(false); }
  }, []);
  useEffect(() => {
    void load();
    void Promise.all([getRuns(), getSources()]).then(([r, s]) => { setRuns(r); setSources(s); }).catch(e => setActionError((e as Error).message));
  }, [load]);
  useEffect(() => { void getReferences().then(setReferences).catch(e => setActionError((e as Error).message)); }, []);
  useEffect(() => {
    if (!running) return;
    const timer = window.setInterval(() => {
      void getRuns().then(r => {
        setRuns(r);
        if (r[0] && !['Queued', 'Running'].includes(r[0].state)) void load();
      }).catch(e => setActionError((e as Error).message));
    }, 2000);
    return () => window.clearInterval(timer);
  }, [running, load]);
  function replaceJob(updated: Job) { setJobs(previous => previous.map(item => item.id === updated.id ? updated : item)); }
  async function submit(event: FormEvent) {
    event.preventDefault(); setSaving(true); setActionError(''); setNotice('');
    try { const job = await createJob(form); setJobs(previous => [job, ...previous]); setForm(blank); setFilter('All'); setUnseenOnly(false); setNotice('Jobbet er gemt lokalt.'); }
    catch (e) { setActionError((e as Error).message); } finally { setSaving(false); }
  }
  async function changeStatus(job: Job, status: Status) {
    setUpdating(job.id); setActionError(''); setNotice('');
    try { replaceJob(await updateStatus(job.id, status)); setNotice(`Status for ${job.title} er ændret til ${statuses[status].toLowerCase()}.`); }
    catch (e) { setActionError((e as Error).message); } finally { setUpdating(null); }
  }
  async function openJob(job: Job) {
    if (expanded === job.id) { setExpanded(null); return; }
    setExpanded(job.id);
    void getDocuments(job.id).then(items => setDocumentHistories(previous => ({ ...previous, [job.id]: items }))).catch(e => setActionError((e as Error).message));
    if (!job.seenAt) try { replaceJob(await markSeen(job.id)); } catch (e) { setActionError((e as Error).message); }
  }
  async function uploadReference() {
    if (!referenceFile) return;
    setUploadingReference(true); setActionError(''); setNotice('');
    try {
      const reference = await addReference(referenceFile, referenceKind, referenceName.trim() || referenceFile.name);
      setReferences(previous => [reference, ...previous]); setReferenceFile(null); setReferenceName('');
      const input = document.getElementById('reference-file') as HTMLInputElement | null; if (input) input.value = '';
      setNotice('Referencen er gemt lokalt uden for projektmappen.');
    } catch (e) { setActionError((e as Error).message); }
    finally { setUploadingReference(false); }
  }
  async function removeReference(reference: Reference) {
    if (!window.confirm(`Slet ${reference.displayName} fra det lokale referencebibliotek?`)) return;
    try { await deleteReference(reference.id); setReferences(previous => previous.filter(x => x.id !== reference.id)); setSelectedReferences(previous => previous.filter(id => id !== reference.id)); setNotice('Referencen og dens lokale tekstudtræk er slettet.'); }
    catch (e) { setActionError((e as Error).message); }
  }
  async function generateDocumentsFor(job: Job, kind: 'Cv' | 'Application' | 'Both') {
    setGeneratingDocuments(true); setActionError(''); setNotice('');
    try {
      const histories = await generateDocuments(job.id, kind, selectedReferences, documentLanguage);
      setDocumentHistories(previous => ({ ...previous, [job.id]: histories }));
      setDocumentDrafts({}); setActiveVersions({}); setNotice('Dokumentudkastet er gemt lokalt. Gennemgå alle oplysninger før brug.');
    } catch (e) { setActionError((e as Error).message); }
    finally { setGeneratingDocuments(false); }
  }
  function changeDraft(documentId: string, content: DocumentContent) { setDocumentDrafts(previous => ({ ...previous, [documentId]: content })); }
  async function saveDraft(history: DocumentHistory, content: DocumentContent) {
    setSavingDocument(history.id); setActionError('');
    try { const histories = await saveDocument(history.id, content); setDocumentHistories(previous => ({ ...previous, [history.jobId]: histories })); setActiveVersions(previous => ({ ...previous, ...Object.fromEntries(histories.map(item => [item.id, item.versions.at(-1)?.number ?? 1])) })); setDocumentDrafts(previous => { const next = { ...previous }; delete next[history.id]; return next; }); setNotice('En ny lokal dokumentversion er gemt.'); }
    catch (e) { setActionError((e as Error).message); }
    finally { setSavingDocument(null); }
  }
  function printDocument(content: DocumentContent) {
    const escape = (value: string) => value.replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]!);
    const popup = window.open('', '_blank');
    if (!popup) { setActionError('Browseren blokerede PDF-forhåndsvisningen. Tillad pop-ups for localhost og prøv igen.'); return; }
    const sections = content.sections.map(s => `<section><h2>${escape(s.heading)}</h2>${s.blocks.map(b => b.type === 'bullet' ? `<p class="bullet">${escape(b.text)}</p>` : `<p>${escape(b.text)}</p>`).join('')}</section>`).join('');
    popup.document.write(`<!doctype html><html lang="da"><meta charset="utf-8"><title>${escape(content.title)}</title><style>body{font:11pt/1.5 Arial,sans-serif;color:#17212b;max-width:760px;margin:48px auto}h1{font-size:24pt;color:#0f5b57}h2{font-size:13pt;border-bottom:1px solid #b8d3d0;padding-bottom:4px;margin-top:24px}.bullet{padding-left:18px;position:relative}.bullet:before{content:'•';position:absolute;left:2px;color:#0f766e}@media print{body{margin:16mm auto}}</style><body><h1>${escape(content.title)}</h1>${content.subtitle ? `<p>${escape(content.subtitle)}</p>` : ''}${sections}<script>window.onload=()=>window.print()<\/script></body></html>`);
    popup.document.close();
  }
  async function discover() {
    setStarting(true); setActionError(''); setNotice('');
    try { const run = await startDiscovery(); setRuns(previous => [run, ...previous]); }
    catch (e) { setActionError((e as Error).message); } finally { setStarting(false); }
  }
  async function analyze(job: Job) {
    setAnalyzing(job.id); setActionError('');
    try { replaceJob(await analyzeJob(job.id)); setNotice('Codex-vurderingen er klar. Scoren er fortsat regelbaseret.'); }
    catch (e) { setActionError((e as Error).message); } finally { setAnalyzing(null); }
  }
  async function rescore() {
    setStarting(true); setActionError('');
    try { await recalculate(); await load(); setNotice('Scores er opdateret fra din lokale profil.'); }
    catch (e) { setActionError((e as Error).message); } finally { setStarting(false); }
  }
  const visible = jobs.filter(job => (filter === 'All' || job.status === filter) && (showExcluded || job.eligibility !== 'Excluded') && (!unseenOnly || !job.seenAt)
    && (!locationFilter.trim() || `${job.location} ${job.country}`.toLowerCase().includes(locationFilter.trim().toLowerCase()))
    && (typeFilter === 'All' || jobType(job) === typeFilter)
    && (job.score ?? 0) >= minimumScore)
    .sort((a, b) => (b.score ?? -1) - (a.score ?? -1) || Number(b.isActive) - Number(a.isActive)
      || (b.statusVerifiedAt ?? '').localeCompare(a.statusVerifiedAt ?? '') || b.createdAt.localeCompare(a.createdAt));
  const ready = !loading && !loadError;
  const sourceResults = parse<SourceResult[]>(latest?.details ?? null, []);
  return <div className="min-h-screen">
    <header className="border-b border-slate-200 bg-white"><div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-5"><a href="/" className="flex items-center gap-3 font-bold tracking-tight"><span className="grid h-9 w-9 place-items-center rounded-xl bg-teal-800 text-white">C</span>Career Agent</a><span className="rounded-full bg-teal-50 px-3 py-1 text-xs font-medium text-teal-800">Lokalt arbejdsrum</span></div></header>
    <main className="mx-auto max-w-7xl px-6 py-10">
      <div className="mb-8 flex flex-wrap items-end justify-between gap-4"><div><p className="mb-2 text-xs font-semibold uppercase tracking-widest text-teal-700">Din næste mulighed</p><h1 className="text-3xl font-bold tracking-tight">Mit joboverblik</h1><p className="mt-3 text-slate-500">Relevante muligheder, med dine prioriteter først.</p></div><div className="flex flex-wrap gap-2"><button className="secondary" onClick={() => titleRef.current?.focus()}>+ Tilføj job</button><button className="primary" disabled={starting || running} onClick={() => void discover()}>{starting || running ? 'Arbejder…' : 'Hent nye jobs'}</button></div></div>
      <div className="mb-6 grid gap-4 sm:grid-cols-3">{[['Muligheder', jobs.filter(j => j.eligibility !== 'Excluded').length, 'Jobs inden for dine søgekriterier'], ['Ikke set', jobs.filter(j => !j.seenAt && j.eligibility !== 'Excluded').length, 'Nye muligheder at gennemgå'], ['Ansøgt', jobs.filter(j => j.status === 'Applied').length, 'Ansøgninger du selv har sendt']].map(([label, count, description]) => <section className="card p-6" key={label}><h2 className="text-sm font-medium text-slate-500">{label}</h2><p className="my-3 text-4xl font-semibold">{ready ? count : '—'}</p><p className="text-xs text-slate-400">{description}</p></section>)}</div>
      <section className="card mb-6 p-6"><div className="flex flex-wrap items-start justify-between gap-4"><div><h2 className="font-semibold">Lokalt referencebibliotek</h2><p className="mt-2 max-w-3xl text-sm text-slate-500">Tilføj CV'er og ansøgninger én gang. De gemmes på denne Mac uden for projektmappen. Når du vælger dem til en generering, sendes relevante tekstuddrag til Codex via ChatGPT.</p></div><span className="rounded-full bg-slate-100 px-3 py-1 text-xs">{references.length} filer</span></div><div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4"><label className="text-xs">Referencetype<select value={referenceKind} onChange={e => setReferenceKind(e.target.value as Reference['kind'])}><option value="Cv">CV</option><option value="Application">Ansøgning</option></select></label><label className="text-xs">Navn (valgfrit)<input value={referenceName} maxLength={120} onChange={e => setReferenceName(e.target.value)} placeholder="Fx CV, engelsk version" /></label><label className="text-xs">Fil · PDF eller DOCX<input id="reference-file" type="file" accept=".pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document" onChange={e => setReferenceFile(e.target.files?.[0] ?? null)} /></label><div className="flex items-end"><button className="primary w-full" disabled={!referenceFile || uploadingReference} onClick={() => void uploadReference()}>{uploadingReference ? 'Gemmer…' : 'Tilføj reference'}</button></div></div>{references.length > 0 && <ul className="mt-4 divide-y divide-slate-100">{references.map(reference => <li className="flex flex-wrap items-center justify-between gap-3 py-3 text-sm" key={reference.id}><span><strong>{reference.displayName}</strong><span className="ml-2 text-xs text-slate-500">{reference.kind === 'Cv' ? 'CV' : 'Ansøgning'} · {reference.fileType} · {(reference.sizeBytes / 1024).toFixed(0)} KB</span></span><button className="text-xs text-red-700 hover:underline" onClick={() => void removeReference(reference)}>Slet lokalt</button></li>)}</ul>}<p className="mt-3 text-xs text-slate-400">Kun tekstbaserede filer op til 10 MB. Scannede PDF'er uden tekst understøttes ikke endnu.</p></section>
      <section className="card mb-6 p-5"><div className="flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-sm font-semibold">Seneste søgning: {latest ? runLabels[latest.state] || latest.state : 'Ikke startet'}</h2><p className="mt-1 text-xs text-slate-500">{sources.filter(s => s.enabled).length} aktive virksomhedskilder · Daglig automatisk søgning er slået fra</p></div><button className="secondary" disabled={starting || running} onClick={() => void rescore()}>Genberegn matches</button></div>{latest && <p className="mt-3 text-sm">{new Date(latest.createdAt).toLocaleString('da-DK')} · {latest.added} nye · {latest.updated} genfundne{latest.error && ` · ${latest.error}`}</p>}
        <details className="mt-3 text-sm"><summary className="cursor-pointer text-teal-800">Kilder og dækning</summary><p className="my-3 text-xs text-slate-500">Søgningen dækker kun de aktive virksomhedskilder. Fravær af resultater betyder ikke, at der ikke findes andre relevante jobs.</p><ul className="space-y-2">{sources.map(source => { const result = sourceResults.find(x => x.sourceId === source.id); return <li key={source.id} className="flex flex-wrap justify-between gap-2 border-b border-slate-100 pb-2"><span>{source.company}</span><span className="text-xs text-slate-500">{!source.enabled ? 'Afventer adgangsafklaring' : result ? result.state === 'Success' ? `${result.fetched} opslag kontrolleret` : result.message : source.provider}</span></li>; })}</ul></details>
      </section>
      <div className="mb-4 min-h-6 text-sm" aria-live="polite">{notice && <p className="text-teal-800">{notice}</p>}{actionError && <p role="alert" className="rounded-lg bg-red-50 p-3 text-red-800">{actionError}</p>}</div>
      <div className="grid items-start gap-6 lg:grid-cols-[minmax(0,1fr)_340px]">
        <section className="card overflow-hidden" aria-busy={loading}>
          <div className="border-b border-slate-100 p-6"><div className="flex flex-wrap items-center justify-between gap-3"><h2 className="font-semibold">Mine jobs <span className="ml-2 text-sm font-normal text-slate-400">{ready ? visible.length : ''}</span></h2><label className="flex items-center gap-2">Status<select aria-label="Filtrér status" value={filter} onChange={e => setFilter(e.target.value)}><option value="All">Alle statusser</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label></div><div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-3"><label className="text-xs">Lokation<input aria-label="Filtrér lokation" value={locationFilter} onChange={e => setLocationFilter(e.target.value)} placeholder="Fx Aarhus" /></label><label className="text-xs">Jobtype<select aria-label="Filtrér jobtype" value={typeFilter} onChange={e => setTypeFilter(e.target.value)}><option value="All">Alle jobtyper</option>{jobTypes.map(type => <option key={type} value={type}>{type}</option>)}</select></label><label className="text-xs">Mindste matchscore: {minimumScore}<input aria-label="Mindste matchscore" type="range" min="0" max="100" step="5" value={minimumScore} onChange={e => setMinimumScore(Number(e.target.value))} /></label></div><div className="mt-4 flex flex-wrap gap-4"><label className="flex items-center gap-2 text-xs"><input className="m-0 w-auto" type="checkbox" checked={unseenOnly} onChange={e => setUnseenOnly(e.target.checked)} />Kun ikke sete</label><label className="flex items-center gap-2 text-xs"><input className="m-0 w-auto" type="checkbox" checked={showExcluded} onChange={e => setShowExcluded(e.target.checked)} />Vis også frasorterede</label></div><p className="mt-3 text-xs text-slate-400">Sorteret efter matchscore. Kompetencer 30 %, erfaring 25 %, jobtype 20 %, geografi 15 % og udvikling 10 %. Scoren er en prioritering, ikke sandsynligheden for et jobtilbud.</p></div>
          {loading ? <p className="p-12 text-center text-slate-500" role="status">Henter dine jobs…</p> : loadError ? <div className="p-8" role="alert"><p className="mb-4 text-red-700">{loadError}</p><button className="secondary" onClick={() => void load()}>Prøv igen</button></div> : visible.length === 0 ? <div className="px-6 py-16 text-center"><h3 className="font-semibold">{jobs.length ? 'Ingen jobs med disse filtre' : 'Plads til din næste mulighed'}</h3><p className="mt-2 text-sm text-slate-500">{jobs.length ? 'Prøv en anden status eller vis frasorterede jobs.' : 'Hent nye jobs fra kilderne eller tilføj et job manuelt.'}</p></div> : <ul className="divide-y divide-slate-100">{visible.map(job => {
            const details = parse<MatchDetails>(job.matchDetails, {});
            const ai = parse<Ai | null>(job.aiAssessment, null);
            const metRequirements = ai?.requirementsMet ?? details.metRequirements ?? [];
            const transferableSkills = ai?.transferableSkills ?? details.transferableSkills ?? [];
            const gaps = ai?.gaps ?? details.missingSkills ?? [];
            return <li key={job.id} className="p-6"><div className="flex flex-wrap items-start justify-between gap-4"><div className="min-w-0 flex-1"><div className="mb-2 flex flex-wrap gap-2 text-xs"><span className="rounded bg-teal-50 px-2 py-1 text-teal-800">{job.score === null ? 'Ikke vurderet' : `${job.score}/100 · Vægtet match`}</span>{!job.seenAt && <span className="rounded bg-blue-50 px-2 py-1 text-blue-700">Ikke set</span>}{!job.isActive && <span>Ikke længere aktiv</span>}{job.eligibility === 'Excluded' && <span>Frasorteret</span>}</div><h3 className="break-words font-semibold">{job.title}</h3><p className="mt-1 break-words text-sm text-slate-500">{job.company}{job.location && ` · ${job.location}`}</p><p className="mt-3 text-sm leading-relaxed text-slate-600">{job.matchReason}</p>{job.possibleDuplicateOf && <p className="mt-2 text-xs text-amber-700">Mulig dublet — ikke automatisk sammenlagt.</p>}<div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-400"><span>Fundet {new Date(job.createdAt).toLocaleDateString('da-DK')}</span><span>Offentliggjort: {job.publishedAt ? new Date(job.publishedAt).toLocaleDateString('da-DK') : 'dato ikke oplyst'}</span><span>Ansøgningsfrist: {job.deadline ? new Date(job.deadline).toLocaleDateString('da-DK') : 'ikke oplyst'}</span><span>{job.statusVerifiedAt ? `${job.isActive ? 'Aktiv status kontrolleret' : 'Lukket status kontrolleret'} ${new Date(job.statusVerifiedAt).toLocaleDateString('da-DK')}` : 'Opslagets aktuelle status kan ikke verificeres'}</span>{/^https?:\/\//i.test(job.sourceUrl) ? <a className="font-medium text-teal-700 hover:underline" href={job.sourceUrl} target="_blank" rel="noopener noreferrer">Originalt jobopslag ↗</a> : <span>Originalt joblink ikke tilgængeligt</span>}<button className="font-medium text-teal-700 hover:underline" aria-expanded={expanded === job.id} onClick={() => void openJob(job)}>{expanded === job.id ? 'Luk detaljer' : 'Læs detaljer og markér set'}</button></div></div><select aria-label={`Status for ${job.title}`} value={job.status} disabled={updating !== null} onChange={e => void changeStatus(job, e.target.value as Status)}>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></div>
              {expanded === job.id && <div className="mt-5 space-y-4 border-t border-slate-100 pt-5 text-sm"><p><strong>Regelbaseret vurdering af realisme:</strong> {details.realism ?? 'Vurderingen mangler.'}</p><div><h4 className="font-semibold">Krav du ser ud til at opfylde</h4>{metRequirements.length ? <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{metRequirements.map((item, i) => <li key={i}>{item}</li>)}</ul> : <p className="mt-1 text-slate-500">Ingen krav kunne bekræftes automatisk ud fra de tilgængelige oplysninger.</p>}</div><div><h4 className="font-semibold">Overførbare kompetencer</h4>{transferableSkills.length ? <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{transferableSkills.map((item, i) => <li key={i}>{item}</li>)}</ul> : <p className="mt-1 text-slate-500">Ingen tydelige overførbare match fundet.</p>}</div><div><h4 className="font-semibold">Mulige kompetencegab</h4>{gaps.length ? <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{gaps.map((item, i) => <li key={i}>{item}</li>)}</ul> : <p className="mt-1 text-slate-500">Ingen tydelige gab fundet; kontrollér opslagets konkrete krav.</p>}</div>{!!details.uncertainties?.length && <div><h4 className="font-semibold">Oplysninger der mangler eller bør afklares</h4><ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{details.uncertainties.map((u, i) => <li key={i}>{u}</li>)}</ul></div>}<div><h4 className="font-semibold">Jobtekst</h4><p className="mt-2 max-h-80 overflow-y-auto whitespace-pre-wrap break-words leading-relaxed text-slate-600">{job.description || 'Der er ingen jobtekst for dette manuelt oprettede job.'}</p></div>{ai ? <div className="rounded-xl bg-teal-50 p-4"><h4 className="font-semibold">Uddybende Codex-vurdering · kontrollér selv oplysningerne</h4><p className="mt-2"><strong>Codex vurderer realismen som:</strong> {ai.realism}</p><p className="my-2">{ai.summary}</p><h5 className="mt-3 font-semibold">Usikkerheder</h5><ul className="list-disc pl-5">{ai.uncertainties.map((v, i) => <li key={i}>{v}</li>)}</ul><details className="mt-3"><summary>Grundlag i jobteksten</summary>{ai.evidence.map((e, i) => <blockquote key={i} className="mt-2 border-l-2 border-teal-300 pl-3">“{e.quote}”<p className="text-xs">{e.claim}</p></blockquote>)}</details></div> : job.description.length >= 80 && job.eligibility !== 'Excluded' && <div><button className="secondary" disabled={analyzing !== null} onClick={() => void analyze(job)}>{analyzing === job.id ? 'Codex vurderer…' : 'Få uddybende Codex-vurdering'}</button><p className="mt-2 text-xs text-slate-500">Sender jobteksten og din korte kompetenceprofil til Codex via ChatGPT-login. Bruger abonnementets kvote; intet CV sendes.</p></div>}
                <section className="rounded-xl border border-teal-100 bg-teal-50/40 p-4"><div className="flex flex-wrap items-center justify-between gap-3"><div><h4 className="font-semibold">CV og ansøgning til dette job</h4><p className="mt-1 text-xs text-slate-600">Generering starter kun, når du trykker. Udkast gemmes lokalt; intet sendes til arbejdsgiveren.</p></div><label className="text-xs">Sprog<select value={documentLanguage} onChange={e => setDocumentLanguage(e.target.value as 'Dansk' | 'English')}><option value="Dansk">Dansk</option><option value="English">English</option></select></label></div>
                  <div className="mt-3"><p className="text-xs font-medium">Vælg referencefiler, der må bruges</p>{references.length ? <div className="mt-2 flex flex-wrap gap-x-4 gap-y-2">{references.map(reference => <label className="flex items-center gap-2 text-xs" key={reference.id}><input className="m-0 w-auto" type="checkbox" checked={selectedReferences.includes(reference.id)} onChange={e => setSelectedReferences(previous => e.target.checked ? [...previous, reference.id] : previous.filter(id => id !== reference.id))} />{reference.displayName} ({reference.kind === 'Cv' ? 'CV' : 'ansøgning'})</label>)}</div> : <p className="mt-1 text-xs text-slate-500">Du kan generere fra profilen uden referencer, eller tilføje dem i biblioteket ovenfor.</p>}</div>
                  <p className="mt-3 rounded-lg bg-white p-3 text-xs leading-relaxed text-slate-600">Privatliv: Ved klik sendes jobteksten, den registrerede profil og relevante tekstuddrag fra de valgte referencer til Codex via dit ChatGPT-login. E-mail, telefon og genkendelige postnumre i referencer fjernes lokalt først. Du kan slå modeltræning fra under ChatGPT → Indstillinger → Datakontrol. Forbrug tæller mod Codex-kvoten.</p>
                  <div className="mt-3 flex flex-wrap gap-2"><button className="primary" disabled={generatingDocuments || job.description.length < 80} onClick={() => void generateDocumentsFor(job, 'Cv')}>{generatingDocuments ? 'Arbejder…' : 'Generér CV'}</button><button className="secondary" disabled={generatingDocuments || job.description.length < 80} onClick={() => void generateDocumentsFor(job, 'Application')}>Generér ansøgning</button><button className="secondary" disabled={generatingDocuments || job.description.length < 80} onClick={() => void generateDocumentsFor(job, 'Both')}>Generér begge</button></div>
                  {job.description.length < 80 && <p className="mt-2 text-xs text-amber-800">Der mangler tilstrækkelig jobtekst til et målrettet udkast.</p>}
                  {(documentHistories[job.id] ?? []).map(history => { const chosen = history.versions.find(v => v.number === activeVersions[history.id]) ?? history.versions.at(-1); if (!chosen) return null; const content = documentDrafts[history.id] ?? chosen.content; const updateSection = (sectionIndex: number, change: (section: DocumentContent['sections'][number]) => DocumentContent['sections'][number]) => changeDraft(history.id, { ...content, sections: content.sections.map((section, index) => index === sectionIndex ? change(section) : section) }); return <article className="document-preview-print mt-5 rounded-xl border border-slate-200 bg-white p-4" key={history.id}>
                    <div className="flex flex-wrap items-center justify-between gap-3"><h5 className="font-semibold">{history.kind === 'Cv' ? 'CV' : 'Ansøgning'} · forhåndsvisning</h5><label className="text-xs">Version<select value={chosen.number} onChange={e => { setActiveVersions(previous => ({ ...previous, [history.id]: Number(e.target.value) })); setDocumentDrafts(previous => { const next = { ...previous }; delete next[history.id]; return next; }); }}><option value={chosen.number}>{`v${chosen.number} · ${new Date(chosen.createdAt).toLocaleString('da-DK')}`}</option>{history.versions.filter(v => v.number !== chosen.number).map(v => <option value={v.number} key={v.id}>{`v${v.number} · ${new Date(v.createdAt).toLocaleString('da-DK')}`}</option>)}</select></label></div>
                    <label className="mt-3 block text-xs">Titel<input value={content.title} maxLength={180} onChange={e => changeDraft(history.id, { ...content, title: e.target.value })} /></label><label className="mt-2 block text-xs">Undertitel<input value={content.subtitle ?? ''} maxLength={180} onChange={e => changeDraft(history.id, { ...content, subtitle: e.target.value || null })} /></label>
                    {content.sections.map((section, si) => <section className="mt-4" key={si}><label className="block text-xs font-semibold">Afsnit<input value={section.heading} maxLength={100} onChange={e => updateSection(si, old => ({ ...old, heading: e.target.value }))} /></label>{section.blocks.map((block, bi) => <div className="mt-2" key={bi}><label className="block text-xs">{block.type === 'bullet' ? 'Punkt' : 'Afsnitstekst'}<textarea rows={block.type === 'bullet' ? 3 : 4} maxLength={1600} value={block.text} onChange={e => updateSection(si, old => ({ ...old, blocks: old.blocks.map((item, index) => index === bi ? { ...item, text: e.target.value } : item) }))} /></label>{!!block.evidence?.length && <details className="mt-1 text-xs text-slate-500"><summary className="cursor-pointer">AI'ens faktagrundlag · kontrollér efter redigering</summary><ul className="mt-1 list-disc pl-5">{block.evidence.map((evidence, index) => <li key={index}><span className="font-medium">{evidence.id === 'job:description' ? 'Jobopslag' : 'Profil'}:</span> “{evidence.quote}”</li>)}</ul></details>}</div>)}</section>)}
                    <div className="mt-4 flex flex-wrap gap-2"><button className="primary" disabled={savingDocument === history.id} onClick={() => void saveDraft(history, content)}>{savingDocument === history.id ? 'Gemmer…' : 'Gem som ny version'}</button><a className="secondary" href={documentExportUrl(history.id, chosen.number)}>Eksportér DOCX</a><button className="secondary" onClick={() => printDocument(content)}>Eksportér PDF</button></div><p className="mt-2 text-xs text-slate-500">Kontrollér fakta og redigér teksten, før du bruger dokumentet. Eksport sender ikke dokumentet nogen steder hen.</p>
                  </article>; })}
                </section></div>}
            </li>;
          })}</ul>}
        </section>
        <aside className="card p-6"><h2 className="font-semibold">Tilføj en mulighed</h2><p className="mb-6 mt-2 text-sm text-slate-500">Gem et job fra en anden kilde.</p><form onSubmit={submit}><fieldset disabled={saving || !ready} className="space-y-4"><label>Jobtitel *<input ref={titleRef} required maxLength={200} value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} placeholder="Fx frontendudvikler" /></label><label>Virksomhed *<input required maxLength={200} value={form.company} onChange={e => setForm({ ...form, company: e.target.value })} placeholder="Virksomhedens navn" /></label><label>Sted (valgfrit)<input maxLength={200} value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} placeholder="Fx Aarhus eller remote" /></label><label>Link til job (valgfrit)<input type="url" maxLength={2048} value={form.sourceUrl} onChange={e => setForm({ ...form, sourceUrl: e.target.value })} placeholder="https://" /></label><button className="primary w-full" type="submit">{saving ? 'Gemmer…' : 'Gem job'}</button></fieldset></form><p className="mt-5 text-xs leading-relaxed text-slate-400">Du styrer processen. En statusændring sender ingen ansøgning.</p></aside>
      </div><footer className="mt-10 text-xs text-slate-400">Career Agent · Dit private joboverblik</footer>
    </main>
  </div>;
}
