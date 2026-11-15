import { test, expect } from '@playwright/test';
import { documentMarkup, documentStyles, cvPage } from '../src/documentPresentation';
import type { DocumentContent, DocumentBlock } from '../src/api';
import { mkdir } from 'node:fs/promises';
const paragraph = (text: string): DocumentBlock => ({ type: 'paragraph', text, evidence: [] });
const bullet = (text: string): DocumentBlock => ({ type: 'bullet', text, evidence: [] });
const entry = (label: string, text: string, period: string, technologies: string): DocumentBlock => ({ type: 'entry', label, text, period, technologies, evidence: [] });

test('reference CV uses two A4 sheets with date columns, teal headings, continuation header and footers', async ({ page }) => {
  const content: DocumentContent = { title: 'Softwareingeniør', subtitle: null, language: 'Dansk',
    identity: { fullName: 'EKSEMPEL KANDIDAT', location: 'Eksempelby', email: 'example@example.com', phone: '12345678', links: '' },
    sections: [
      { heading: 'Profil', page: 1, blocks: [paragraph('Dette er et syntetisk CV til kontrol af layout. Kandidaten arbejder med softwareudvikling, API’er og databaser samt brugergrænseflader til web og mobil. Profilen beskriver konkrete opgaver og relevant teknisk erfaring. Indholdet er udelukkende eksempeldata til udvikling og bruges ikke som en rigtig kandidatprofil. Målet er at kontrollere den faste opsætning, læsbarhed og plads til afsnittene på to A4-sider.')] },
      { heading: 'Erhvervserfaring', page: 1, blocks: [
        entry('Eksempelbank', 'Softwareudvikler · Studentermedhjælper, deltid', 'Jan. 2026 – nu', 'C# · .NET · REST API’er · SQL · Git'),
        bullet('Udvikler og vedligeholder funktionalitet i en syntetisk bankapplikation med C# og .NET.'),
        bullet('Arbejder med API-integrationer, SQL-forespørgsler og fejlfinding af eksisterende funktionalitet.'),
        bullet('Tester ændringer sammen med teamet og beskriver løsningerne i den tekniske dokumentation.'),
        entry('Eksempel Forsikring', 'Softwareudvikler · Praktik', 'Aug. 2025 – jan. 2026', 'React · TypeScript · REST API’er · SQL · Git'),
        bullet('Udviklede syntetiske brugergrænseflader og integrationer mellem frontend og backend.'),
        bullet('Arbejdede med datavalidering, API-kald og tilgængelige komponenter i et udviklingsteam.'),
        bullet('Bidrog til test, fejlfinding og dokumentation af løsningernes opbygning.'),
      ] },
      { heading: 'Uddannelse', page: 1, blocks: [entry('Eksempeluniversitet', 'Diplomingeniør i softwareteknologi', '2023 – juni 2026', ''), paragraph('Syntetisk uddannelsesforløb med softwareudvikling, databaser, systemdesign, test samt web- og mobiludvikling. Afsluttende projekt med en digital serviceplatform.')] },
      { heading: 'Udvalgt projekt', page: 2, blocks: [entry('Eksempelplatform', 'Digital serviceplatform · Bachelorprojekt', '2026', 'ASP.NET Core · EF Core · SQL · Next.js · React Native'), paragraph('Udviklede i en syntetisk projektgruppe en proof of concept med webapp, mobilapp, backend-API og relationel database til forskellige brugergrupper.'), bullet('Samlede brugeradministration og servicehenvendelser i en løsning med rollebaseret adgang.'), bullet('Arbejdede med API-design og datamodellering samt test af sammenhængende brugerforløb.'), bullet('Opsatte en syntetisk deploymentproces med automatiserede build- og testkontroller.')] },
      { heading: 'Tekniske kompetencer', page: 2, blocks: [
        { type: 'subheading', text: 'Backend og databaser', evidence: [] }, paragraph('C#, .NET, ASP.NET Core, Entity Framework Core, REST API’er og SQL.'),
        { type: 'subheading', text: 'Frontend og mobil', evidence: [] }, paragraph('React, Next.js, TypeScript og JavaScript. Projekterfaring med React Native.'),
        { type: 'subheading', text: 'Værktøjer og deployment', evidence: [] }, paragraph('Git, GitHub, automatiserede builds og test i en syntetisk udviklingsproces.'),
        { type: 'subheading', text: 'Udviklingsmetoder', evidence: [] }, paragraph('API-design, datamodellering, fejlfinding, softwaretest og teknisk dokumentation.'),
      ] },
      { heading: 'Supplerende faglighed', page: 2, blocks: [paragraph('Syntetiske kurser og projekter inden for sikker systemudvikling, brugercentreret design og databehandling. Alle oplysninger på denne side er tydeligt markerede eksempeldata.')] },
      { heading: 'Sprog og praktisk', page: 2, blocks: [paragraph('Eksempeldata: Dansk og engelsk. Bosat i Eksempelby. Søger fuldtidsansættelse.')] },
    ] };
  await page.setViewportSize({ width: 1050, height: 1300 });
  await page.setContent(`<style>${documentStyles}body{margin:0;background:#eee}</style><article class="professional-document">${documentMarkup(content, 'Cv')}</article>`);
  await expect(page.locator('.cv-page')).toHaveCount(2);
  await expect(page.locator('.cv-page').first().getByRole('heading', { name: 'Uddannelse' })).toBeVisible();
  await expect(page.locator('.cv-page').last().getByText('PROJEKTER & KOMPETENCER', { exact: true })).toBeVisible();
  await expect(page.locator('.cv-entry-period').first()).toHaveText('Jan. 2026 – nu');
  for (const sheet of await page.locator('.cv-page').all()) {
    const box = await sheet.boundingBox();
    expect(box!.width).toBeCloseTo(793.7, 0);
    expect(box!.height).toBeCloseTo(1122.5, 0);
    const footer = await sheet.locator('.cv-footer').boundingBox();
    const contentBottom = await sheet.locator('h2, p, ul, .cv-entry').evaluateAll(elements => Math.max(...elements.map(e => e.getBoundingClientRect().bottom)));
    expect(contentBottom).toBeLessThan(footer!.y - 12);
  }
  expect(cvPage({ heading: 'Uddannelse og projekt', blocks: [] })).toBe(1);
  expect(cvPage({ heading: 'Tekniske kompetencer', blocks: [] })).toBe(2);
  const output = process.env.CAREER_AGENT_LAYOUT_QA_DIR;
  if (output) {
    await mkdir(output, { recursive: true });
    await page.pdf({ path: `${output}/synthetic-cv.pdf`, printBackground: true, preferCSSPageSize: true });
    for (let i = 0; i < 2; i++) await page.locator('.cv-page').nth(i).screenshot({ path: `${output}/sheet-${i + 1}.png` });
  }
});
