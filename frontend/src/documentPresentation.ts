import type { DocumentContent } from './api';
const escape = (value: string) => value.replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]!);
export const documentStyles = `
  .professional-document { font:11pt/1.45 Arial,sans-serif; color:#111; background:white; overflow-wrap:anywhere; }
  .professional-document .candidate-name { font-size:21pt; font-weight:700; margin:0 0 5pt; letter-spacing:-.4pt; }
  .professional-document .contact-line { font-size:9.5pt; line-height:1.5; margin:0 0 16pt; color:#333; }
  .professional-document h1 { font-size:14pt; line-height:1.3; font-weight:700; margin:0 0 5pt; color:#000; }
  .professional-document .document-subtitle { font-size:11pt; margin:0 0 18pt; color:#000; }
  .professional-document h2 { font-size:12pt; font-weight:700; margin:18pt 0 6pt; color:#000; break-after:avoid; }
  .professional-document p { margin:0 0 9pt; orphans:3; widows:3; }
  .professional-document ul { margin:0 0 10pt; padding-left:15pt; list-style:disc; }
  .professional-document li { margin-bottom:5pt; padding-left:1pt; }
  .professional-document .letter-body p { margin-bottom:13pt; }
  .professional-document .signoff { margin-top:17pt; }
  .cv-document { width:210mm; max-width:none; font:10.5pt/14pt Helvetica,Arial,sans-serif; color:#19344a; }
  .cv-document .cv-page { width:210mm; min-height:297mm; box-sizing:border-box; background:white; padding:17mm 18mm 22mm; position:relative; }
  .cv-document .cv-page + .cv-page { margin-top:24px; }
  .cv-document .candidate-name { margin:0 0 10pt; font:700 25pt/1.1 Helvetica,Arial,sans-serif; color:#19344a; letter-spacing:0; }
  .cv-document .cv-role { margin:0 0 7pt; font-size:12pt; font-weight:700; color:#267c85; }
  .cv-document .contact-line { font-size:10pt; color:#19344a; margin:0 0 16pt; }
  .cv-document .cv-continuation-title { font-size:18pt; line-height:1.2; font-weight:700; margin:0 0 8pt; color:#19344a; }
  .cv-document .cv-continuation-subtitle { font-size:10pt; color:#5b6773; margin:0 0 16pt; }
  .cv-document h2 { font-size:11pt; font-weight:700; color:#267c85; text-transform:uppercase; margin:17pt 0 12pt; padding-bottom:5pt; border-bottom:.5pt solid #d8e3e8; }
  .cv-document .cv-entry { margin:0 0 8pt; break-inside:avoid; }
  .cv-document .cv-entry-line { display:flex; align-items:baseline; justify-content:space-between; gap:12pt; }
  .cv-document .cv-entry-label,.cv-document .cv-entry-role,.cv-document .cv-subheading { font-size:11pt; font-weight:700; }
  .cv-document .cv-entry-role { margin-top:3pt; }
  .cv-document .cv-entry-period { flex-shrink:0; font-size:10pt; color:#5b6773; }
  .cv-document .cv-entry-technologies { font-size:9.5pt; font-style:italic; color:#267c85; margin-top:4pt; }
  .cv-document .cv-subheading { margin:10pt 0 2pt; break-after:avoid; }
  .cv-document p { margin:0 0 8pt; }
  .cv-document ul { margin:0 0 12pt; padding-left:12pt; }
  .cv-document li { margin-bottom:0; padding-left:0; }
  .cv-document .cv-footer { position:absolute; bottom:8mm; left:18mm; right:18mm; display:grid; grid-template-columns:1fr 1fr 1fr; font-size:9pt; color:#5b6773; }
  .cv-document .cv-page-number { text-align:center; color:#19344a; font-size:10pt; }
  @page cv { size:A4; margin:0; }
  @page { size:letter; margin:19mm 20mm; }
  @media print { .cv-document .cv-page { page:cv; break-after:page; margin:0; } .cv-document .cv-page:last-child { break-after:auto; } .cv-document .cv-page + .cv-page { margin-top:0; } body { margin:0; background:white; } .professional-document { padding:0!important; box-shadow:none!important; max-width:none!important; } }
`;
export function cvPage(section: DocumentContent['sections'][number]) {
  if (section.page === 1 || section.page === 2) return section.page;
  if (/uddannelse|education/i.test(section.heading)) return 1;
  return /projekt|project|kompetenc|skill|suppler|additional|sprog|language|praktisk/i.test(section.heading) ? 2 : 1;
}
function sectionMarkup(section: DocumentContent['sections'][number], showHeading: boolean) {
  let html = showHeading ? `<h2>${escape(section.heading)}</h2>` : '';
  let inList = false;
  for (const block of section.blocks) {
    if (block.type === 'bullet' && !inList) { html += '<ul>'; inList = true; }
    if (block.type !== 'bullet' && inList) { html += '</ul>'; inList = false; }
    if (block.type === 'entry') html += `<div class="cv-entry"><div class="cv-entry-line"><strong class="cv-entry-label">${escape(block.label ?? '')}</strong>${block.period ? `<span class="cv-entry-period">${escape(block.period)}</span>` : ''}</div><p class="cv-entry-role">${escape(block.text)}</p>${block.technologies ? `<p class="cv-entry-technologies">${escape(block.technologies)}</p>` : ''}</div>`;
    else if (block.type === 'subheading') html += `<p class="cv-subheading">${escape(block.text)}</p>`;
    else html += block.type === 'bullet' ? `<li>${escape(block.text)}</li>` : `<p>${escape(block.text)}</p>`;
  }
  if (inList) html += '</ul>';
  return html;
}
function cvMarkup(content: DocumentContent) {
  const name = content.identity?.fullName?.trim() ?? '';
  const identity = content.identity;
  const contacts = identity ? [identity.location, identity.phone, identity.email, identity.links].filter(Boolean) : [];
  const firstHeader = `<p class="candidate-name">${escape((name || content.title).toUpperCase())}</p>${name ? `<p class="cv-role">${escape(content.title.toUpperCase())}</p>` : ''}${contacts.length ? `<p class="contact-line">${contacts.map(escape).join(' | ')}</p>` : ''}`;
  const secondHeader = `<p class="cv-continuation-title">${content.language === 'English' ? 'PROJECTS &amp; SKILLS' : 'PROJEKTER &amp; KOMPETENCER'}</p><p class="cv-continuation-subtitle">${[name, content.title].filter(Boolean).map(escape).join(' · ')}</p>`;
  return `<div class="cv-document">${[1, 2].map(page => `<section class="cv-page" aria-label="CV side ${page}">${page === 1 ? firstHeader : secondHeader}${content.sections.filter(section => cvPage(section) === page).map(section => sectionMarkup(section, true)).join('')}<footer class="cv-footer"><span>${escape(name ? `${name} · CV` : 'CV')}</span><span class="cv-page-number">${page} / 2</span><span></span></footer></section>`).join('')}</div>`;
}
export function documentMarkup(content: DocumentContent, kind: 'Cv' | 'Application') {
  if (kind === 'Cv') return cvMarkup(content);
  const identity = content.identity;
  const name = identity?.fullName?.trim();
  const contacts = identity ? [identity.location, identity.email, identity.phone, identity.links].filter(Boolean) : [];
  const letter = kind === 'Application';
  let html = name ? `<p class="candidate-name">${escape(name)}</p>` : '';
  if (contacts.length) html += `<p class="contact-line">${contacts.map(escape).join(' · ')}</p>`;
  html += `<h1>${escape(content.title)}</h1>`;
  if (content.subtitle) html += `<p class="document-subtitle">${escape(content.subtitle)}</p>`;
  if (letter && content.language) html += `<p>${content.language === 'English' ? 'Dear hiring team' : 'Til rekrutteringsteamet'}</p>`;
  html += `<div class="${letter ? 'letter-body' : 'cv-body'}">`;
  for (const section of content.sections) html += sectionMarkup(section, false);
  html += '</div>';
  if (letter && content.language) html += `<p class="signoff">${content.language === 'English' ? 'Kind regards' : 'Med venlig hilsen'}${name ? `<br>${escape(name)}` : ''}</p>`;
  return html;
}
