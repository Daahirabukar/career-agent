import { test, expect } from '@playwright/test';
import { documentMarkup } from '../src/documentPresentation';
import type { DocumentContent } from '../src/api';

test('document preview keeps private metadata out and escapes untrusted text', () => {
  const content: DocumentContent = { title: '<img src=x onerror=alert(1)>', subtitle: 'Synthetic Company',
    identity: { fullName: 'Example Candidate', email: 'example@example.com', phone: '', location: 'Aarhus', links: '' }, language: 'English',
    sections: [{ heading: 'Experience', blocks: [{ type: 'paragraph', text: 'Synthetic experience.', evidence: [{ id: 'profile:summary', quote: 'PRIVATE EVIDENCE ONLY' }] },
      { type: 'bullet', text: 'Built an API.', evidence: [] }] }] };
  const cv = documentMarkup(content, 'Cv');
  expect(cv).toContain('&lt;img');
  expect(cv).not.toContain('<img');
  expect(cv).toContain('<ul><li>');
  expect(cv).toContain('Example Candidate');
  expect(cv).not.toContain('PRIVATE EVIDENCE ONLY');
  const letter = documentMarkup(content, 'Application');
  expect(letter).not.toContain('<h2>');
  expect(letter).toContain('Dear hiring team');
  expect(letter).toContain('Kind regards');
});
