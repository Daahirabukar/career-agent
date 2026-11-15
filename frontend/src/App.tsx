import { documentMarkup, documentStyles } from './documentPresentation';
import { compareJobs, defaultSearch, isVerified, matchesSearch, type SearchFilters } from './jobSearch';
import { useCallback, useEffect, useRef, useState, type FormEvent } from 'react';
import { emptyDocumentProfile, getDocumentProfile, saveDocumentProfile, type DocumentProfile, addReference, analyzeJob, createJob, deleteReference, documentExportUrl, generateDocuments, getDocuments, getJobs, getReferences, getRuns, getSources, markSeen, recalculate, saveDocument, startDiscovery, statuses, updateStatus, type DocumentContent, type DocumentHistory, type Job, type NewJob, type Reference, type Run, type Source, type SourceResult, type Status } from './api';
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
  const [search, setSearch] = useState<SearchFilters>(defaultSearch);
  const setSearchField = <K extends keyof SearchFilters>(key: K, value: SearchFilters[K]) => setSearch(previous => ({ ...previous, [key]: value }));
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
  const [applicationJobId, setApplicationJobId] = useState<string | null>(null);
  const [manualOpen, setManualOpen] = useState(false);
  const [loadingDocuments, setLoadingDocuments] = useState(false);
  const applicationRef = useRef<HTMLElement>(null);
  useEffect(() => { if (applicationJobId) applicationRef.current?.focus(); }, [applicationJobId]);
  const [references, setReferences] = useState<Reference[]>([]);
  const [documentHistories, setDocumentHistories] = useState<Record<string, DocumentHistory[]>>({});
  const [activeVersions, setActiveVersions] = useState<Record<string, number>>({});
  const [documentDrafts, setDocumentDrafts] = useState<Record<string, DocumentContent>>({});
  const [selectedReferences, setSelectedReferences] = useState<string[]>([]);
  const [documentLanguage, setDocumentLanguage] = useState<'Dansk' | 'English'>('Dansk');
  const [generatingDocuments, setGeneratingDocuments] = useState(false);
  const [generationError, setGenerationError] = useState('');
  const [documentProfile, setDocumentProfile] = useState<DocumentProfile>(emptyDocumentProfile);
  const [profileConfirmed, setProfileConfirmed] = useState(false);
  const [profileDirty, setProfileDirty] = useState(false);
  const [savingProfile, setSavingProfile] = useState(false);
  const [editingDocument, setEditingDocument] = useState<string | null>(null);
  useEffect(() => { void getDocumentProfile().then(p => { if (p?.identity) setDocumentProfile(p); }).catch(e => setActionError((e as Error).message)); }, []);
  function updateDocumentProfile(change: Partial<DocumentProfile>) { setDocumentProfile(previous => ({ ...previous, ...change })); setProfileDirty(true); setProfileConfirmed(false); }
  async function saveProfile() {
    if (!profileConfirmed) return;
    setSavingProfile(true); setActionError('');
    try { setDocumentProfile(await saveDocumentProfile(documentProfile)); setProfileDirty(false); setNotice('Dine bekræftede oplysninger er gemt lokalt. Nye udkast bruger dem; tidligere versioner bevares.'); }
    catch (e) { setActionError((e as Error).message); }
    finally { setSavingProfile(false); }
  }
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
    if (generatingDocuments || loadingDocuments) return;
    if (profileDirty) { setActionError('Gem og bekræft dine nye profiloplysninger før generering, så udkastet får de rigtige fakta.'); return; }
    setApplicationJobId(job.id);
    setGeneratingDocuments(true); setGenerationError(''); setActionError(''); setNotice('');
    try {
      const histories = await generateDocuments(job.id, kind, selectedReferences, documentLanguage);
      setDocumentHistories(previous => ({ ...previous, [job.id]: histories }));
      setDocumentDrafts({}); setActiveVersions({}); setNotice('Dit CV og din ansøgning er klar til gennemgang.');
    } catch (e) { setGenerationError((e as Error).message); }
    finally { setGeneratingDocuments(false); }
  }
  async function viewDocuments(job: Job) {
    setApplicationJobId(job.id); setLoadingDocuments(true); setGenerationError(''); setActionError(''); setNotice('');
    try { const items = await getDocuments(job.id); setDocumentHistories(previous => ({ ...previous, [job.id]: items })); }
    catch (e) { setActionError((e as Error).message); }
    finally { setLoadingDocuments(false); }
  }
  function changeDraft(documentId: string, content: DocumentContent) { setDocumentDrafts(previous => ({ ...previous, [documentId]: content })); }
  async function saveDraft(history: DocumentHistory, content: DocumentContent) {
    setSavingDocument(history.id); setActionError('');
    try { const histories = await saveDocument(history.id, content); setDocumentHistories(previous => ({ ...previous, [history.jobId]: histories })); setActiveVersions(previous => ({ ...previous, ...Object.fromEntries(histories.map(item => [item.id, item.versions.at(-1)?.number ?? 1])) })); setDocumentDrafts(previous => { const next = { ...previous }; delete next[history.id]; return next; }); setNotice('En ny lokal dokumentversion er gemt.'); }
    catch (e) { setActionError((e as Error).message); }
    finally { setSavingDocument(null); }
  }
  function printDocument(content: DocumentContent, kind: 'Cv' | 'Application') {
    const popup = window.open('', '_blank');
    if (!popup) { setActionError('Browseren blokerede PDF-forhåndsvisningen. Tillad pop-ups for localhost og prøv igen.'); return; }
    popup.document.write(`<!doctype html><html lang="${content.language === 'English' ? 'en' : 'da'}"><meta charset="utf-8"><title>Dokument</title><style>${documentStyles} body{margin:0 auto;padding:0;width:${kind === 'Cv' ? '210mm' : 'auto'};max-width:${kind === 'Cv' ? 'none' : '720px'}}</style><body><article class="professional-document">${documentMarkup(content, kind)}</article><script>window.onload=()=>window.print()<\/script></body></html>`);
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
  const visible = jobs.filter(job => job.id === applicationJobId || (matchesSearch(job, search) && (filter === 'All' || job.status === filter) && (showExcluded || job.eligibility !== 'Excluded') && (!unseenOnly || !job.seenAt)
    && (!locationFilter.trim() || `${job.location} ${job.country}`.toLowerCase().includes(locationFilter.trim().toLowerCase()))
    && (typeFilter === 'All' || jobType(job) === typeFilter)
    && (job.score ?? 0) >= minimumScore))
    .sort((a, b) => compareJobs(a, b, search.sort));
  const ready = !loading && !loadError;
  const sourceResults = parse<SourceResult[]>(latest?.details ?? null, []);
  function applicationWorkspace(job: Job) {
    const hasDocuments = (documentHistories[job.id] ?? []).some(history => history.versions.length > 0);
    return <section ref={applicationRef} tabIndex={-1} aria-label={`CV og ansøgning til ${job.title}`} className="application-workspace mt-5 rounded-2xl border border-teal-200 bg-teal-50/50 p-5 sm:p-7">
      <div className="flex flex-wrap items-start justify-between gap-3"><div><p className="eyebrow">Din ansøgning</p><h4 className="mt-2 text-xl font-semibold">CV og ansøgning til {job.company}</h4><p className="mt-1 text-sm text-slate-500">{job.title}</p></div><button className="secondary" disabled={generatingDocuments} onClick={() => setApplicationJobId(null)}>Luk dokumenter</button></div>
      <ol className="my-5 flex flex-wrap gap-x-6 gap-y-2 text-xs font-medium text-slate-500"><li className="text-teal-800">1. Job valgt</li><li className={generatingDocuments ? 'text-teal-800' : ''}>2. CV og ansøgning skrives</li><li className={hasDocuments && !generatingDocuments ? 'text-teal-800' : ''}>3. Gennemgå og download</li></ol>
      {generatingDocuments && <div className="rounded-xl border border-teal-100 bg-white p-6" role="status"><div className="flex items-center gap-3"><span className="h-5 w-5 animate-spin rounded-full border-2 border-teal-200 border-t-teal-800" /><strong>Skriver dit CV og din ansøgning…</strong></div><p className="mt-3 text-sm text-slate-500">Det kan tage op til tre minutter. Du får begge udkast her, når de er klar.</p></div>}
      {loadingDocuments && <p role="status">Henter dine gemte udkast…</p>}
      {generationError && <div role="alert" className="my-4 rounded-xl bg-red-50 p-4 text-sm text-red-800"><strong>Dokumenterne kunne ikke færdiggøres.</strong><p className="mt-1">{generationError}</p><button className="secondary mt-3" disabled={generatingDocuments} onClick={() => void generateDocumentsFor(job, 'Both')}>Prøv at lave begge igen</button></div>}
      {hasDocuments && !generatingDocuments && !generationError && <p className="rounded-lg bg-teal-100 p-3 text-sm text-teal-900" role="status">Her er dine gemte udkast. Gennemgå fakta og tekst, og gem dine ændringer før download.</p>}
      {!hasDocuments && !generatingDocuments && !loadingDocuments && !generationError && <p className="text-sm text-slate-500">Der er ingen gemte udkast til jobbet endnu. Tryk Ansøg for at lave begge dokumenter.</p>}
      {!generatingDocuments && (documentHistories[job.id] ?? []).map(history => { const chosen = history.versions.find(v => v.number === activeVersions[history.id]) ?? history.versions.at(-1); if (!chosen) return null; const content = documentDrafts[history.id] ?? chosen.content; const updateSection = (sectionIndex: number, change: (section: DocumentContent['sections'][number]) => DocumentContent['sections'][number]) => changeDraft(history.id, { ...content, sections: content.sections.map((section, index) => index === sectionIndex ? change(section) : section) }); return <article className="document-preview-print mt-5 rounded-xl border border-slate-200 bg-white p-4" key={history.id}>
                    <div className="flex flex-wrap items-center justify-between gap-3"><h5 className="font-semibold">{history.kind === 'Cv' ? 'CV' : 'Ansøgning'} · forhåndsvisning</h5><label className="text-xs">Version<select value={chosen.number} onChange={e => { setActiveVersions(previous => ({ ...previous, [history.id]: Number(e.target.value) })); setDocumentDrafts(previous => { const next = { ...previous }; delete next[history.id]; return next; }); }}><option value={chosen.number}>{`v${chosen.number} · ${new Date(chosen.createdAt).toLocaleString('da-DK')}`}</option>{history.versions.filter(v => v.number !== chosen.number).map(v => <option value={v.number} key={v.id}>{`v${v.number} · ${new Date(v.createdAt).toLocaleString('da-DK')}`}</option>)}</select></label></div>
                    <div className="my-4 flex flex-wrap items-center justify-between gap-3"><span className="text-xs text-slate-500">{history.kind === 'Cv' ? 'CV i dit referenceformat · to A4-sider' : 'Sammenhængende ansøgningsbrev'}</span><button className="secondary" onClick={() => setEditingDocument(previous => previous === history.id ? null : history.id)}>{editingDocument === history.id ? 'Vis dokument' : 'Redigér tekst'}</button></div>
                    {editingDocument !== history.id && <div className="overflow-auto rounded-lg border border-slate-200 bg-slate-100 p-3 sm:p-6"><style>{documentStyles}</style><div className={`professional-document mx-auto bg-white shadow-sm ${history.kind === 'Cv' ? 'w-fit' : 'max-w-[720px] p-5 sm:p-10'}`} dangerouslySetInnerHTML={{ __html: documentMarkup(content, history.kind) }} /></div>}
                    {editingDocument === history.id && <div className="mt-4 rounded-lg bg-slate-50 p-4">
                    <label className="mt-3 block text-xs">Titel<input value={content.title} maxLength={180} onChange={e => changeDraft(history.id, { ...content, title: e.target.value })} /></label><label className="mt-2 block text-xs">Undertitel<input value={content.subtitle ?? ''} maxLength={180} onChange={e => changeDraft(history.id, { ...content, subtitle: e.target.value || null })} /></label>
                    {content.sections.map((section, si) => <section className="mt-4" key={si}><label className="block text-xs font-semibold">Afsnit<input value={section.heading} maxLength={100} onChange={e => updateSection(si, old => ({ ...old, heading: e.target.value }))} /></label>{section.blocks.map((block, bi) => <div className="mt-2" key={bi}>{block.type === 'entry' && <div className="grid gap-3 sm:grid-cols-2">{([['label', 'Arbejdsgiver eller projekt'], ['period', 'Periode'], ['technologies', 'Teknologier']] as const).map(([field, label]) => <label className="text-xs" key={field}>{label}<input maxLength={field === 'technologies' ? 400 : field === 'period' ? 100 : 200} value={block[field] ?? ''} onChange={e => updateSection(si, old => ({ ...old, blocks: old.blocks.map((item, index) => index === bi ? { ...item, [field]: e.target.value || null } : item) }))} /></label>)}</div>}<label className="block text-xs">{block.type === 'bullet' ? 'Punkt' : 'Afsnitstekst'}<textarea rows={block.type === 'bullet' ? 3 : 4} maxLength={1600} value={block.text} onChange={e => updateSection(si, old => ({ ...old, blocks: old.blocks.map((item, index) => index === bi ? { ...item, text: e.target.value } : item) }))} /></label>{!!block.evidence?.length && <details className="mt-1 text-xs text-slate-500"><summary className="cursor-pointer">AI'ens faktagrundlag · kontrollér efter redigering</summary><ul className="mt-1 list-disc pl-5">{block.evidence.map((evidence, index) => <li key={index}><span className="font-medium">{evidence.id === 'job:description' ? 'Jobopslag' : 'Profil'}:</span> “{evidence.quote}”</li>)}</ul></details>}</div>)}</section>)}
                    </div>}
                    <div className="mt-4 flex flex-wrap gap-2"><button className="primary" disabled={savingDocument === history.id} onClick={() => void saveDraft(history, content)}>{savingDocument === history.id ? 'Gemmer…' : 'Gem som ny version'}</button><a role="link" className={`secondary ${documentDrafts[history.id] ? 'pointer-events-none opacity-40' : ''}`} aria-disabled={!!documentDrafts[history.id]} tabIndex={documentDrafts[history.id] ? -1 : undefined} href={documentDrafts[history.id] ? undefined : documentExportUrl(history.id, chosen.number)}>Download Word</a><button className="secondary" onClick={() => printDocument(content, history.kind)}>Download PDF</button></div><p className="mt-2 text-xs text-slate-500">{documentDrafts[history.id] ? 'Du har ændringer, der ikke er gemt. Gem som ny version før Word-download.' : 'Kontrollér fakta før du bruger dokumentet.'}</p>
                  </article>; })}
      {hasDocuments && !generatingDocuments && <button className="secondary mt-5" onClick={() => void generateDocumentsFor(job, 'Both')}>Lav en ny version af begge</button>}
      <p className="mt-5 text-xs text-slate-500">Udkastene gemmes lokalt. Når du er tilfreds, downloader du dem og sender dem selv til virksomheden.</p>
    </section>;
  }
  return <div className="min-h-screen">
    <header className="border-b border-slate-200 bg-white"><div className="mx-auto flex max-w-5xl items-center justify-between px-5 py-5"><a href="/" className="flex items-center gap-3 font-bold tracking-tight"><span className="grid h-9 w-9 place-items-center rounded-xl bg-teal-800 text-white">C</span>Career Agent</a><span className="text-xs text-slate-500">Dit private jobrum</span></div></header>
    <main className="mx-auto max-w-5xl px-5 py-9 sm:py-12">
      <div className="mb-7 flex flex-wrap items-end justify-between gap-5"><div><p className="eyebrow">Find dit næste job</p><h1 className="mt-3 text-3xl font-semibold tracking-tight sm:text-4xl">Mit joboverblik</h1><p className="mt-3 max-w-xl text-sm leading-relaxed text-slate-500">Find et job, du vil søge. Tryk <strong className="text-slate-700">Ansøg</strong> for at få et målrettet CV og en ansøgning.</p></div><button className="secondary" disabled={starting || running} onClick={() => void discover()}>{starting || running ? 'Henter jobs…' : 'Opdatér joblisten'}</button></div>
      <section className="card mb-5 p-5 sm:p-6" aria-label="Søgning">
        <div className="grid gap-4 sm:grid-cols-[2fr_1fr]"><label>Søg efter roller og teknologier<input aria-label="Søg efter roller og teknologier" value={search.query} onChange={e => setSearchField('query', e.target.value)} placeholder="Software developer, backend, .NET…" /></label><label>Lokation<input aria-label="Filtrér lokation" value={locationFilter} onChange={e => setLocationFilter(e.target.value)} placeholder="Hele Danmark · fx Aarhus" /></label></div>
        <details className="mt-4"><summary className="cursor-pointer text-sm font-medium text-teal-800">Flere filtre</summary><div className="mt-4 grid gap-4 sm:grid-cols-3"><label>Erfaringsniveau<select className="mt-2 w-full" value={search.level} onChange={e => setSearchField('level', e.target.value)}><option value="early">Junior og almindelige roller</option><option value="junior">Kun junior og graduate</option><option value="all">Alle niveauer, også senior</option></select></label><label>Offentliggjort inden for<select className="mt-2 w-full" value={search.ageDays} onChange={e => setSearchField('ageDays', Number(e.target.value))}><option value={30}>30 dage</option><option value={60}>60 dage</option><option value={90}>90 dage</option><option value={0}>Alle datoer</option></select></label><label>Sortering<select className="mt-2 w-full" value={search.sort} onChange={e => setSearchField('sort', e.target.value)}><option value="newest">Nyeste først</option><option value="score">Bedste match først</option></select></label><label>Status<select className="mt-2 w-full" aria-label="Filtrér status" value={filter} onChange={e => setFilter(e.target.value)}><option value="All">Alle statusser</option>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></label><label>Jobtype<select className="mt-2 w-full" aria-label="Filtrér jobtype" value={typeFilter} onChange={e => setTypeFilter(e.target.value)}><option value="All">Alle jobtyper</option>{jobTypes.map(type => <option key={type} value={type}>{type}</option>)}</select></label><label>Mindste matchscore: {minimumScore}<input aria-label="Mindste matchscore" type="range" min="0" max="100" step="5" value={minimumScore} onChange={e => setMinimumScore(Number(e.target.value))} /></label></div><div className="mt-4 flex flex-wrap gap-4">
        <label className="check-label"><input type="checkbox" checked={search.softwareOnly} onChange={e => setSearchField('softwareOnly', e.target.checked)} />Kun softwareudvikling</label><label className="check-label"><input type="checkbox" checked={unseenOnly} onChange={e => setUnseenOnly(e.target.checked)} />Kun ikke sete</label><label className="check-label"><input type="checkbox" checked={search.includeUnverified} onChange={e => setSearchField('includeUnverified', e.target.checked)} />Medtag lukkede og ikke nyligt verificerede</label><label className="check-label"><input type="checkbox" checked={search.includeDismissed} onChange={e => setSearchField('includeDismissed', e.target.checked)} />Medtag fravalgte og afslag</label><label className="check-label"><input type="checkbox" checked={showExcluded} onChange={e => setShowExcluded(e.target.checked)} />Vis også frasorterede</label></div><p className="mt-4 text-xs text-slate-500">Komma adskiller alternative søgninger. Du søger i de tilsluttede kilders hentede opslag.</p></details>
      </section>
      <section className="mb-7 rounded-xl border border-teal-100 bg-teal-50/60 p-4" aria-label="Indstillinger for CV og ansøgning"><div className="flex flex-wrap items-center justify-between gap-3"><p className="text-sm font-medium text-teal-950">Ansøg = lav CV + ansøgning</p><label className="flex items-center gap-3 text-xs">Sprog<select aria-label="Sprog til dokumenter" disabled={generatingDocuments} value={documentLanguage} onChange={e => setDocumentLanguage(e.target.value as 'Dansk' | 'English')}><option value="Dansk">Dansk</option><option value="English">English</option></select></label></div><p className="mt-2 text-xs leading-relaxed text-slate-600">Ved klik sendes din registrerede profil, jobteksten og uddrag fra valgte referencefiler til Codex via ChatGPT. Bruger din Codex-kvote. Intet sendes til arbejdsgiveren.</p><details className="mt-3"><summary className="cursor-pointer text-xs font-medium text-teal-800">Referencefiler til generering · {selectedReferences.length ? `${selectedReferences.length} valgt` : 'kun din profil'}</summary><div className="mt-3 flex flex-wrap gap-4">{references.map(reference => <label className="check-label" key={reference.id}><input disabled={generatingDocuments} type="checkbox" checked={selectedReferences.includes(reference.id)} onChange={e => setSelectedReferences(previous => e.target.checked ? [...previous, reference.id] : previous.filter(id => id !== reference.id))} />{reference.displayName}</label>)}{!references.length && <p className="text-xs text-slate-500">Du kan tilføje filer i Mine referencefiler nederst på siden. Din profil er nok til at starte.</p>}</div></details></section>
      <details className="card mb-6 p-5"><summary className="cursor-pointer text-sm font-semibold">Din dokumentprofil <span className="ml-2 font-normal text-slate-500">{documentProfile.identity.fullName ? documentProfile.identity.fullName : 'Tilføj navn og konkrete erfaringer for et stærkere resultat'}</span></summary><div className="mt-4 space-y-5"><p className="text-sm leading-relaxed text-slate-600">Søgeprofilen er kort. Tilføj dine faktiske opgaver, perioder og projekter her, så dokumenterne kan blive konkrete. Oplysningerne gemmes kun på denne Mac. Navn og kontaktoplysninger indsættes lokalt og sendes ikke til AI.</p><fieldset disabled={generatingDocuments || savingProfile}><div className="grid gap-3 sm:grid-cols-2">{([['fullName', 'Fulde navn'], ['email', 'E-mail'], ['phone', 'Telefon'], ['location', 'By'], ['links', 'LinkedIn eller portfolio']] as const).map(([key, label]) => <label key={key}>{label}<input maxLength={key === 'links' ? 1000 : 200} value={documentProfile.identity[key]} onChange={e => updateDocumentProfile({ identity: { ...documentProfile.identity, [key]: e.target.value } })} /></label>)}</div><div className="mt-5 grid gap-4 sm:grid-cols-2">{([['experience', 'Erhvervserfaring', 'Arbejdsgiver, faktisk titel, periode og 2–4 konkrete opgaver. Beskriv kun resultater, du kan dokumentere.'], ['education', 'Uddannelse', 'Uddannelse, institution og faktisk afslutningsdato.'], ['projects', 'Udvalgte projekter', 'Projekt, din egen rolle, hvad du byggede og hvilke teknologier du brugte.'], ['motivation', 'Din faglige motivation', 'Hvilke opgaver vil du arbejde med, og hvorfor? Skriv med dine egne ord.']] as const).map(([key, label, placeholder]) => <label key={key}>{label}<textarea rows={4} maxLength={4000} placeholder={placeholder} value={documentProfile[key]} onChange={e => updateDocumentProfile({ [key]: e.target.value })} /></label>)}</div><label className="check-label mt-4"><input type="checkbox" checked={profileConfirmed} onChange={e => setProfileConfirmed(e.target.checked)} />Jeg har kontrolleret, at disse oplysninger er korrekte og må bruges som fakta i mine dokumenter.</label><button className="primary mt-4" disabled={!profileConfirmed || !profileDirty || savingProfile} onClick={() => void saveProfile()}>{savingProfile ? 'Gemmer…' : 'Gem bekræftede oplysninger'}</button></fieldset></div></details>
      <div className="mb-4 text-sm" aria-live="polite">{notice && <p className="rounded-lg bg-teal-50 p-3 text-teal-800">{notice}</p>}{actionError && <p role="alert" className="rounded-lg bg-red-50 p-3 text-red-800">{actionError}</p>}</div>
      <section aria-busy={loading}><div className="mb-4 flex items-center justify-between"><h2 className="font-semibold">Dine jobmuligheder <span className="ml-2 text-sm font-normal text-slate-500">{ready ? visible.length : ''}</span></h2><span className="text-xs text-slate-500">{search.sort === 'newest' ? 'Nyeste først' : 'Bedste match først'} · {sources.filter(source => source.enabled).length} kilder</span></div>
        {loading ? <p className="card p-10 text-center" role="status">Henter dine jobs…</p> : loadError ? <div className="card p-7" role="alert"><p>{loadError}</p><button className="secondary mt-4" onClick={() => void load()}>Prøv igen</button></div> : !visible.length ? <div className="card p-10 text-center"><h3 className="font-semibold">{jobs.length ? 'Ingen jobs med disse filtre' : 'Plads til din næste mulighed'}</h3><p className="mt-2 text-sm text-slate-500">Udvid dine filtre eller opdatér joblisten.</p></div> : <ul className="space-y-4">{visible.map(job => {
          const details = parse<MatchDetails>(job.matchDetails, {});
          const ai = parse<Ai | null>(job.aiAssessment, null);
          const metRequirements = ai?.requirementsMet ?? details.metRequirements ?? [];
          const transferableSkills = ai?.transferableSkills ?? details.transferableSkills ?? [];
          const gaps = ai?.gaps ?? details.missingSkills ?? [];
          return <li key={job.id} className="card p-5 sm:p-6"><div className="flex flex-wrap items-start justify-between gap-4"><div className="min-w-0 flex-1"><p className="text-xs font-medium text-slate-500">{job.company} · {job.location || 'Lokation ikke oplyst'}</p><h3 className="mt-2 break-words text-lg font-semibold tracking-tight">{job.title}</h3><div className="mt-3 flex flex-wrap gap-3 text-xs text-slate-500"><span className="rounded-md bg-teal-50 px-2 py-1 font-medium text-teal-800">{job.score === null ? 'Ikke vurderet' : `${job.score}/100 match`}</span><span className="py-1">{job.publishedAt ? `Opslået ${new Date(job.publishedAt).toLocaleDateString('da-DK')}` : 'Offentliggørelsesdato ukendt'}</span>{!job.seenAt && <span className="py-1 text-blue-700">Ikke set</span>}{!job.isActive && <span className="py-1 text-amber-800">Lukket</span>}{job.isImported && !isVerified(job) && <span className="py-1 text-amber-800">Status skal opdateres</span>}</div></div><div className="flex w-full flex-col gap-2 sm:w-auto sm:items-end"><button className="primary min-w-40" disabled={generatingDocuments || loadingDocuments || job.description.length < 80} onClick={() => void generateDocumentsFor(job, 'Both')}>{generatingDocuments && applicationJobId === job.id ? 'Skriver dokumenter…' : 'Ansøg'}</button><span className="text-xs text-slate-500">Laver CV + ansøgning</span></div></div>
          {job.description.length < 80 && <p className="mt-3 text-xs text-amber-800">Jobbet mangler jobtekst. CV og ansøgning kræver et opslag med tilstrækkelig tekst.</p>}
          <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-4"><div className="flex flex-wrap gap-4 text-xs font-medium text-teal-800"><button aria-expanded={expanded === job.id} onClick={() => void openJob(job)}>{expanded === job.id ? 'Luk jobdetaljer' : 'Se jobdetaljer'}</button><button disabled={generatingDocuments || loadingDocuments} onClick={() => void viewDocuments(job)}>Se mine udkast</button>{/^https?:\/\//i.test(job.sourceUrl) && <a href={job.sourceUrl} target="_blank" rel="noopener noreferrer">Originalt opslag ↗</a>}</div><select className="text-xs" aria-label={`Status for ${job.title}`} value={job.status} disabled={updating !== null} onChange={e => void changeStatus(job, e.target.value as Status)}>{Object.entries(statuses).map(([value, label]) => <option key={value} value={value}>{label}</option>)}</select></div>
          {applicationJobId === job.id && applicationWorkspace(job)}
          {expanded === job.id && <div className="mt-5 space-y-4 border-t border-slate-100 pt-5 text-sm"><p><strong>Regelbaseret vurdering af realisme:</strong> {details.realism ?? 'Vurderingen mangler.'}</p><div><h4 className="font-semibold">Krav du ser ud til at opfylde</h4>{metRequirements.length ? <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{metRequirements.map((item, i) => <li key={i}>{item}</li>)}</ul> : <p className="mt-1 text-slate-500">Ingen krav kunne bekræftes automatisk ud fra de tilgængelige oplysninger.</p>}</div><div><h4 className="font-semibold">Overførbare kompetencer</h4>{transferableSkills.length ? <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{transferableSkills.map((item, i) => <li key={i}>{item}</li>)}</ul> : <p className="mt-1 text-slate-500">Ingen tydelige overførbare match fundet.</p>}</div><div><h4 className="font-semibold">Mulige kompetencegab</h4>{gaps.length ? <ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{gaps.map((item, i) => <li key={i}>{item}</li>)}</ul> : <p className="mt-1 text-slate-500">Ingen tydelige gab fundet; kontrollér opslagets konkrete krav.</p>}</div>{!!details.uncertainties?.length && <div><h4 className="font-semibold">Oplysninger der mangler eller bør afklares</h4><ul className="mt-2 list-disc space-y-1 pl-5 text-slate-600">{details.uncertainties.map((u, i) => <li key={i}>{u}</li>)}</ul></div>}<div><h4 className="font-semibold">Jobtekst</h4><p className="mt-2 max-h-80 overflow-y-auto whitespace-pre-wrap break-words leading-relaxed text-slate-600">{job.description || 'Der er ingen jobtekst for dette manuelt oprettede job.'}</p></div>{ai ? <div className="rounded-xl bg-teal-50 p-4"><h4 className="font-semibold">Uddybende Codex-vurdering · kontrollér selv oplysningerne</h4><p className="mt-2"><strong>Codex vurderer realismen som:</strong> {ai.realism}</p><p className="my-2">{ai.summary}</p><h5 className="mt-3 font-semibold">Usikkerheder</h5><ul className="list-disc pl-5">{ai.uncertainties.map((v, i) => <li key={i}>{v}</li>)}</ul><details className="mt-3"><summary>Grundlag i jobteksten</summary>{ai.evidence.map((e, i) => <blockquote key={i} className="mt-2 border-l-2 border-teal-300 pl-3">“{e.quote}”<p className="text-xs">{e.claim}</p></blockquote>)}</details></div> : job.description.length >= 80 && job.eligibility !== 'Excluded' && <div><button className="secondary" disabled={analyzing !== null} onClick={() => void analyze(job)}>{analyzing === job.id ? 'Codex vurderer…' : 'Få uddybende Codex-vurdering'}</button><p className="mt-2 text-xs text-slate-500">Sender jobteksten og din korte kompetenceprofil til Codex via ChatGPT-login. Bruger abonnementets kvote; intet CV sendes.</p></div>}</div>}
          </li>;
        })}</ul>}
      </section>
      <div className="mt-10 space-y-3"><details className="card p-5" id="reference-library"><summary className="cursor-pointer font-semibold">Mine referencefiler <span className="ml-2 text-sm font-normal text-slate-500">{references.length} filer</span></summary><div className="mt-4"><div className="flex flex-wrap items-start justify-between gap-4"><div><h2 className="font-semibold">Lokalt referencebibliotek</h2><p className="mt-2 max-w-3xl text-sm text-slate-500">Tilføj CV'er og ansøgninger én gang. De gemmes på denne Mac uden for projektmappen. Når du vælger dem til en generering, sendes relevante tekstuddrag til Codex via ChatGPT.</p></div><span className="rounded-full bg-slate-100 px-3 py-1 text-xs">{references.length} filer</span></div><div className="mt-4 grid gap-3 sm:grid-cols-2 lg:grid-cols-4"><label className="text-xs">Referencetype<select value={referenceKind} onChange={e => setReferenceKind(e.target.value as Reference['kind'])}><option value="Cv">CV</option><option value="Application">Ansøgning</option></select></label><label className="text-xs">Navn (valgfrit)<input value={referenceName} maxLength={120} onChange={e => setReferenceName(e.target.value)} placeholder="Fx CV, engelsk version" /></label><label className="text-xs">Fil · PDF eller DOCX<input id="reference-file" type="file" accept=".pdf,.docx,application/pdf,application/vnd.openxmlformats-officedocument.wordprocessingml.document" onChange={e => setReferenceFile(e.target.files?.[0] ?? null)} /></label><div className="flex items-end"><button className="primary w-full" disabled={!referenceFile || uploadingReference} onClick={() => void uploadReference()}>{uploadingReference ? 'Gemmer…' : 'Tilføj reference'}</button></div></div>{references.length > 0 && <ul className="mt-4 divide-y divide-slate-100">{references.map(reference => <li className="flex flex-wrap items-center justify-between gap-3 py-3 text-sm" key={reference.id}><span><strong>{reference.displayName}</strong><span className="ml-2 text-xs text-slate-500">{reference.kind === 'Cv' ? 'CV' : 'Ansøgning'} · {reference.fileType} · {(reference.sizeBytes / 1024).toFixed(0)} KB</span></span><button className="text-xs text-red-700 hover:underline" onClick={() => void removeReference(reference)}>Slet lokalt</button></li>)}</ul>}<p className="mt-3 text-xs text-slate-400">Kun tekstbaserede filer op til 10 MB. Scannede PDF'er uden tekst understøttes ikke endnu.</p></div></details>
<details className="card p-5"><summary className="cursor-pointer font-semibold">Søgestatus og kilder</summary><div className="mt-4"><div className="flex flex-wrap items-center justify-between gap-3"><div><h2 className="text-sm font-semibold">Seneste søgning: {latest ? runLabels[latest.state] || latest.state : 'Ikke startet'}</h2><p className="mt-1 text-xs text-slate-500">{sources.filter(s => s.enabled).length} aktive virksomhedskilder · Daglig automatisk søgning er slået fra</p></div><button className="secondary" disabled={starting || running} onClick={() => void rescore()}>Genberegn matches</button></div>{latest && <p className="mt-3 text-sm">{new Date(latest.createdAt).toLocaleString('da-DK')} · {latest.added} nye · {latest.updated} genfundne{latest.error && ` · ${latest.error}`}</p>}
        <details className="mt-3 text-sm"><summary className="cursor-pointer text-teal-800">Kilder og dækning</summary><p className="my-3 text-xs text-slate-500">Søgningen dækker kun de aktive virksomhedskilder. Fravær af resultater betyder ikke, at der ikke findes andre relevante jobs.</p><ul className="space-y-2">{sources.map(source => { const result = sourceResults.find(x => x.sourceId === source.id); return <li key={source.id} className="flex flex-wrap justify-between gap-2 border-b border-slate-100 pb-2"><span>{source.company}</span><span className="text-xs text-slate-500">{!source.enabled ? 'Afventer adgangsafklaring' : result ? result.state === 'Success' ? `${result.fetched} opslag kontrolleret` : result.message : source.provider}</span></li>; })}</ul></details>
      </div></details>
<details className="card p-5" open={manualOpen} onToggle={event => setManualOpen(event.currentTarget.open)}><summary className="cursor-pointer font-semibold">Tilføj et job manuelt</summary><div className="mt-5"><h2 className="font-semibold">Tilføj en mulighed</h2><p className="mb-6 mt-2 text-sm text-slate-500">Gem et job fra en anden kilde.</p><form onSubmit={submit}><fieldset disabled={saving || !ready} className="space-y-4"><label>Jobtitel *<input ref={titleRef} required maxLength={200} value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} placeholder="Fx frontendudvikler" /></label><label>Virksomhed *<input required maxLength={200} value={form.company} onChange={e => setForm({ ...form, company: e.target.value })} placeholder="Virksomhedens navn" /></label><label>Sted (valgfrit)<input maxLength={200} value={form.location} onChange={e => setForm({ ...form, location: e.target.value })} placeholder="Fx Aarhus eller remote" /></label><label>Link til job (valgfrit)<input type="url" maxLength={2048} value={form.sourceUrl} onChange={e => setForm({ ...form, sourceUrl: e.target.value })} placeholder="https://" /></label><button className="primary w-full" type="submit">{saving ? 'Gemmer…' : 'Gem job'}</button></fieldset></form><p className="mt-5 text-xs leading-relaxed text-slate-400">Du styrer processen. En statusændring sender ingen ansøgning.</p></div></details></div>
      <footer className="mt-8 text-xs text-slate-400">Career Agent · Dokumenter er udkast til din gennemgang.</footer>
    </main>
  </div>;
}
