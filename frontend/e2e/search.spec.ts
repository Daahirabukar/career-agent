import { test, expect } from '@playwright/test';
import { compareJobs, defaultSearch, matchesSearch } from '../src/jobSearch';
import type { Job } from '../src/api';

const now = Date.parse('2026-11-11T12:00:00Z');
const posting = (overrides: Partial<Job> = {}): Job => ({
  id: 'synthetic', title: 'Software Developer', company: 'Synthetic Company', location: 'Aarhus', sourceUrl: 'https://example.com/jobs/1',
  status: 'New', createdAt: '2026-11-11T10:00:00Z', description: 'Build .NET APIs', score: 70, eligibility: 'Review',
  matchReason: '', matchDetails: '{}', seenAt: null, isActive: true, isImported: true, possibleDuplicateOf: null,
  aiAssessment: null, aiModel: null, publishedAt: '2026-11-01T10:00:00Z', deadline: null,
  statusVerifiedAt: '2026-11-11T10:00:00Z', country: 'DK', employmentType: 'Full-time', workplaceType: 'Hybrid', ...overrides,
});

test('default search keeps software roles and hides senior, unrelated, old, closed and dismissed jobs', () => {
  expect(matchesSearch(posting(), defaultSearch, now)).toBe(true);
  for (const change of [
    { title: 'Senior Software Engineer' }, { title: 'Staff Engineer' }, { title: 'Security Engineer' },
    { publishedAt: '2025-01-01T00:00:00Z' }, { statusVerifiedAt: '2026-10-01T00:00:00Z' },
    { isActive: false }, { status: 'Dismissed' as const }, { deadline: '2026-11-10T00:00:00Z' },
  ]) expect(matchesSearch(posting(change), defaultSearch, now)).toBe(false);
});
test('unknown dates stay explicit; old jobs can be deliberately included', () => {
  expect(matchesSearch(posting({ publishedAt: null }), defaultSearch, now)).toBe(true);
  expect(matchesSearch(posting({ publishedAt: '2025-01-01T00:00:00Z' }), { ...defaultSearch, ageDays: 0 }, now)).toBe(true);
});
test('comma alternatives broaden queries; words within an alternative must all match', () => {
  expect(matchesSearch(posting(), { ...defaultSearch, query: 'frontend, .NET APIs' }, now)).toBe(true);
  expect(matchesSearch(posting(), { ...defaultSearch, query: 'frontend, .NET Azure' }, now)).toBe(false);
});
test('fresh verification cannot make an old job look newly published', () => {
  const old = posting({ id: 'old', score: 99, publishedAt: '2026-09-01T00:00:00Z' });
  const recent = posting({ id: 'recent', score: 60 });
  expect([old, recent].sort((a, b) => compareJobs(a, b, 'newest'))[0].id).toBe('recent');
  expect([old, recent].sort((a, b) => compareJobs(a, b, 'score'))[0].id).toBe('old');
});

test('dashboard searches alternatives and can reveal senior roles without modifying personal data', async ({ page }) => {
  const date = new Date().toISOString();
  const jobs = [
    posting({ id: 'one', title: 'Backend Developer', publishedAt: date, statusVerifiedAt: date }),
    posting({ id: 'two', title: 'Frontend Developer', description: 'React UI', publishedAt: date, statusVerifiedAt: date }),
    posting({ id: 'three', title: 'Senior Software Engineer', publishedAt: date, statusVerifiedAt: date }),
  ];
  await page.route('**/api/**', route => route.fulfill({ json: route.request().url().endsWith('/api/jobs') ? jobs : [] }));
  await page.goto('/');
  await expect(page.getByRole('heading', { name: 'Backend Developer', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Senior Software Engineer', exact: true })).toHaveCount(0);
  await page.getByLabel('Søg efter roller og teknologier').fill('React, unknown');
  await expect(page.getByRole('heading', { name: 'Frontend Developer', exact: true })).toBeVisible();
  await expect(page.getByRole('heading', { name: 'Backend Developer', exact: true })).toHaveCount(0);
  await page.getByLabel('Søg efter roller og teknologier').fill('');
  await page.getByText('Flere filtre', { exact: true }).click();
  await page.getByLabel('Erfaringsniveau').selectOption('all');
  await expect(page.getByRole('heading', { name: 'Senior Software Engineer', exact: true })).toBeVisible();
});
