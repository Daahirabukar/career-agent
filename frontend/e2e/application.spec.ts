import { test, expect, type Page } from '@playwright/test';
const jobId = 'synthetic-job';
function histories() {
  return ['Cv', 'Application'].map(kind => ({
    id: `document-${kind}`, jobId, kind, versions: [{ id: `v-${kind}`, number: 1,
      createdAt: new Date().toISOString(), engine: 'synthetic-test', referenceIds: [],
      content: { title: kind === 'Cv' ? 'Syntetisk CV' : 'Syntetisk ansøgning', subtitle: null,
        sections: [{ heading: 'Profil', blocks: [{ type: 'paragraph', text: 'Syntetisk dokumenttekst til browsertest.', evidence: [{ id: 'profile:summary', quote: 'Syntetisk profil' }] }] }] },
    }],
  }));
}
async function setup(page: Page, description = 'Build C# and .NET APIs. Full-time software development with mentoring and training in Aarhus. Synthetic example only.') {
  const date = new Date().toISOString();
  await page.route('**/api/**', route => {
    const url = route.request().url();
    if (url.endsWith('/api/jobs')) return route.fulfill({ json: [{
      id: jobId, title: 'Software Developer', company: 'EKSEMPEL Virksomhed', location: 'Aarhus',
      sourceUrl: 'https://example.com/job', status: 'New', createdAt: date, description, score: 80,
      eligibility: 'Review', matchReason: 'Synthetic match', matchDetails: '{}', seenAt: date,
      isActive: true, isImported: true, publishedAt: date, statusVerifiedAt: date,
    }] });
    if (url.endsWith('/api/references')) return route.fulfill({ json: [{ id: 'ref-1', kind: 'Cv', displayName: 'Syntetisk reference', fileType: 'PDF', sizeBytes: 100, createdAt: date }] });
    return route.fulfill({ json: [] });
  });
}

test('Ansøg sends one explicit Both request and displays both drafts, progress and exports', async ({ page }) => {
  await setup(page);
  let release!: () => void;
  const gate = new Promise<void>(resolve => { release = resolve; });
  const requests: unknown[] = [];
  await page.route(`**/api/jobs/${jobId}/documents/generate`, async route => {
    requests.push(route.request().postDataJSON());
    await gate;
    await route.fulfill({ json: histories() });
  });
  await page.goto('/');
  await expect(page.getByRole('button', { name: 'Ansøg', exact: true })).toBeVisible();
  expect(requests).toHaveLength(0);
  await expect(page.getByLabel('Jobtitel *')).toBeHidden();
  await page.getByLabel('Sprog til dokumenter').selectOption('English');
  await page.getByText('Referencefiler til generering', { exact: false }).click();
  await page.getByLabel('Syntetisk reference', { exact: true }).check();
  await page.getByRole('button', { name: 'Ansøg', exact: true }).click();
  await expect(page.getByText('Skriver dit CV og din ansøgning…', { exact: true })).toBeVisible();
  await expect(page.getByRole('button', { name: 'Skriver dokumenter…', exact: true })).toBeDisabled();
  expect(requests).toEqual([{ kind: 'Both', referenceIds: ['ref-1'], language: 'English' }]);
  await page.getByLabel('Søg efter roller og teknologier').fill('ingen-resultater');
  await expect(page.getByText('Skriver dit CV og din ansøgning…', { exact: true })).toBeVisible();
  release();
  await expect(page.getByRole('heading', { name: 'CV · forhåndsvisning', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Ansøgning · forhåndsvisning', exact: true })).toBeVisible();
  await expect(page.getByRole('link', { name: 'Download Word', exact: true })).toHaveCount(2);
  await expect(page.locator('.professional-document')).toHaveCount(2);
  await expect(page.getByRole('textbox', { name: 'Titel', exact: true })).toHaveCount(0);
  await page.getByRole('button', { name: 'Redigér tekst', exact: true }).first().click();
  await page.getByRole('textbox', { name: 'Titel', exact: true }).fill('Redigeret syntetisk CV');
  await expect(page.getByRole('link', { name: 'Download Word', exact: true }).first()).toHaveAttribute('aria-disabled', 'true');
  await expect(page.getByLabel('Status for Software Developer')).toHaveValue('New');
  await page.setViewportSize({ width: 390, height: 844 });
  expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true);
});

test('generation errors stay visible beside the job and retry creates both documents', async ({ page }) => {
  await setup(page);
  let attempts = 0;
  await page.route(`**/api/jobs/${jobId}/documents/generate`, route => {
    attempts++;
    return attempts === 1
      ? route.fulfill({ status: 503, json: { title: 'Kontrollér ChatGPT-login og kvote.' } })
      : route.fulfill({ json: histories() });
  });
  await page.goto('/');
  await page.getByRole('button', { name: 'Ansøg', exact: true }).click();
  await expect(page.getByRole('alert')).toContainText('Kontrollér ChatGPT-login og kvote.');
  await page.getByRole('button', { name: 'Prøv at lave begge igen' }).click();
  await expect(page.getByRole('heading', { name: 'Ansøgning · forhåndsvisning', exact: true })).toBeVisible();
  expect(attempts).toBe(2);
});

test('viewing saved drafts never generates; insufficient job text disables Ansøg', async ({ page }) => {
  await setup(page);
  let generated = false;
  await page.route(`**/api/jobs/${jobId}/documents`, route => route.fulfill({ json: histories() }));
  await page.route('**/documents/generate', route => { generated = true; return route.abort(); });
  await page.goto('/');
  await page.getByRole('button', { name: 'Se mine udkast', exact: true }).click();
  await expect(page.getByRole('heading', { name: 'Ansøgning · forhåndsvisning', exact: true })).toBeVisible();
  expect(generated).toBe(false);
  await page.unroute('**/api/**');
  await setup(page, 'Too little text');
  await page.reload();
  await expect(page.getByRole('button', { name: 'Ansøg', exact: true })).toBeDisabled();
});

test('document facts are saved only after confirmation and never start generation', async ({ page }) => {
  await setup(page);
  let saved: { confirmed: boolean; profile: { identity: { fullName: string } } } | undefined;
  let generationCount = 0;
  await page.route('**/api/document-profile', route => {
    if (route.request().method() === 'PUT') { saved = route.request().postDataJSON(); return route.fulfill({ json: saved!.profile }); }
    return route.fulfill({ json: { identity: { fullName: '', email: '', phone: '', location: '', links: '' }, experience: '', education: '', projects: '', motivation: '' } });
  });
  await page.route('**/documents/generate', route => { generationCount++; return route.fulfill({ json: histories() }); });
  await page.goto('/');
  await page.getByText('Din dokumentprofil', { exact: false }).click();
  await page.getByLabel('Fulde navn', { exact: true }).fill('Example Candidate');
  await page.getByLabel('Erhvervserfaring', { exact: true }).fill('Example Bank. Implemented a synthetic API.');
  await expect(page.getByRole('button', { name: 'Gem bekræftede oplysninger' })).toBeDisabled();
  expect(saved).toBeUndefined();
  await page.getByLabel('Jeg har kontrolleret, at disse oplysninger', { exact: false }).check();
  await page.getByRole('button', { name: 'Gem bekræftede oplysninger' }).click();
  await expect(page.getByText('Dine bekræftede oplysninger er gemt lokalt.', { exact: false })).toBeVisible();
  expect(saved!.confirmed).toBe(true);
  expect(saved!.profile.identity.fullName).toBe('Example Candidate');
  expect(generationCount).toBe(0);
});
