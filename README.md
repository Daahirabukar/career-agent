# Career Agent

Privat, local-first joboverblik med manuel registrering og søgning efter godkendte offentlige kilde-API’er. React + TypeScript + Vite + Tailwind, ASP.NET Core 10 og EF Core/SQLite. Ingen LinkedIn-automatisering, automatisk daglig søgning eller automatisk afsendelse. CV'er og ansøgninger kan kun genereres ved eksplicit klik.

## Forudsætninger

Kontrolleret på macOS 26.2, Intel x86_64: .NET SDK 10.0.402, Node 22.19.0, npm 10.9.3 og Git 2.50.1. Python er ikke nødvendig for appen. GitHub CLI og Docker er ikke nødvendige.

.NET er installeret separat i `$HOME/.local/share/career-agent-dotnet`. Brug følgende i **begge terminaler**, så eksisterende projekters SDK ikke ændres globalt:

```sh
export DOTNET_ROOT="$HOME/.local/share/career-agent-dotnet"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd /Users/daahirmac/career-agent
```

På en ny Intel-Mac med understøttet macOS kan SDK installeres efter godkendelse:

```sh
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/career-agent-dotnet-install.sh
bash /tmp/career-agent-dotnet-install.sh --version 10.0.402 --architecture x64 --install-dir "$HOME/.local/share/career-agent-dotnet"
```

`global.json` vælger SDK-versionen; NuGet- og npm-lockfiler fastholder pakkerne. Hold SDK og pakker opdateret med sikkerhedsrettelser. .NET 10 er LTS; .NET 8 vælges ikke til dette nye projekt, da support udløber november 2026.

## Installation og database

Fra projektets rod, efter terminalopsætningen ovenfor:

```sh
dotnet restore --locked-mode
dotnet tool restore
dotnet ef database update --project backend/CareerAgent.Api
npm --prefix frontend ci
```

Databasen ligger i `backend/CareerAgent.Api/data/career-agent.db` (relativt til API-projektets content root) og er ignoreret i Git. Første start har ingen seed-jobs. EF-migrationen til discovery køres eksplicit, som vist ovenfor.

Migrationer køres eksplicit, så appstart ikke automatisk ændrer databasen. Ved fremtidige modelændringer:

```sh
dotnet ef migrations add BeskrivAendringen --project backend/CareerAgent.Api
dotnet ef database update --project backend/CareerAgent.Api
```

Gem alle genererede migrationsfiler i Git. Tag en lokal kopi af databasen med backend stoppet før ændringer til vigtige data.

## Start lokalt

Terminal 1, fra projektets rod:

```sh
dotnet run --project backend/CareerAgent.Api --launch-profile local
```

Terminal 2, fra projektets rod:

```sh
npm --prefix frontend run dev
```

Åbn **http://localhost:5173**. Backend lytter på **http://localhost:5080**. Kontrollér den med:

```sh
curl --fail http://localhost:5080/api/health
```

Forventet svar: `{"status":"ok"}`. Begge servere stoppes med Ctrl+C. Gemte jobs og statusser overlever genstart.

Ingen `.env` er påkrævet. `.env.example` viser den valgfrie frontend-variabel; kopiér den til `frontend/.env.local` ved behov. Vite-variabler er offentlige i browseren og må aldrig indeholde secrets. Backend læser ikke `.env` automatisk. Til tests eller en anden lokal database kan standard .NET-konfigurationen `ConnectionStrings__Jobs` angives som miljøvariabel.

## Funktioner og API

- `GET /api/health`: kontrollerer databaseforbindelsen.
- `GET /api/jobs`: jobs sorteret efter matchscore.
- `GET /api/sources`: aktive kilder og afventende kildekandidater.
- `GET /api/discovery/runs`: seneste hentninger og fejlede kilder.
- `POST /api/discovery/runs`: starter én manuel søgning. Ingen cron/job er slået til.
- `POST /api/matching/recalculate`: genberegner lokal score fra profilen.
- `PATCH /api/jobs/{id}/seen`: markér et opslag som set uden at ændre ansøgningsstatus.
- `POST /api/jobs/{id}/analysis`: valgfri, brugerinitieret Codex-vurdering.
- `GET/POST/DELETE /api/references`: se, tilføj og slet lokale reference-CV'er og ansøgninger (PDF/DOCX).
- `GET /api/jobs/{id}/documents`: se dokumenter og tidligere versioner for jobbet.
- `POST /api/jobs/{id}/documents/generate`: manuel generering af CV, ansøgning eller begge. Kun dette eksplicitte endpoint starter Codex.
- `PUT /api/documents/{id}`: gem en redigeret version lokalt.
- `GET /api/documents/{id}/export/docx?version=N`: eksportér den valgte DOCX-version. PDF eksporteres via browserens lokale udskrivningsdialog.
- `POST /api/jobs`: `{ "title": "...", "company": "...", "location": "...", "sourceUrl": "https://..." }`.
- `PATCH /api/jobs/{id}/status`: `{ "status": "Interview" }`.

Titel og virksomhed er obligatoriske og højst 200 tegn. Sted er valgfrit, højst 200 tegn. Kildelink er valgfrit, højst 2048 tegn, kun HTTP/HTTPS og uden indlejrede loginoplysninger. Statusværdier: `New`, `Saved`, `Applied`, `Interview`, `Offer`, `Rejected`, `Dismissed`. Importerede opslag starter som `New`; manuelt oprettede jobs starter som `Saved`. API'et fastsætter ID og UTC-oprettelsestid. Fejl returneres som Problem Details.

En statusændring er kun registrering: appen sender ikke en ansøgning.

## Arkitektur og privatliv

En modulær monolit med `Jobs/` til model, input og endpoints samt `Data/` til EF Core. En separat frontend bruger API'et som eneste datakilde. SQLite er passende til én lokal bruger, og integrationstests bruger en isoleret SQLite-fil pr. test.

- API og Vite er bundet til localhost. Ingen ekstern hosting eller tunnel er konfigureret.
- CORS tillader kun `http://localhost:5173` i Development. Andre browser-origins afvises også før writes. Det er ikke brugerautentifikation.
- Der er ingen loginfunktion. Appen er til den lokale bruger; eksponering på netværk kræver et særskilt sikkerhedsdesign med autentifikation og HTTPS.
- Databasefiler er ikke krypteret af appen. De beskyttes af din lokale konto og maskinens diskbeskyttelse. Del ikke data-mappen.
- Ingen følsomme request-body- eller databaseværdier logges af appen.
- `.gitignore` udelukker miljøfiler, databaser, personlige dokumenter og genererede filer. Kontrollér altid staged filer før commit.
- Kildekataloget aktiverer kun fem verificerede, offentlige Greenhouse/Lever-læsepunkter. Jobnet, LinkedIn, Jobindex, The Hub og øvrige virksomhedssider er ikke automatisk tilsluttet. Yderligere virksomhedsbrætter kræver verificeret feed/API og kildekonfiguration.
- Kildehentning er manuel, højst hvert femte minut og kun fra faste HTTPS-endpoints. Redirects er afvist. Et ufuldstændigt eller ugyldigt kildesvar giver ingen ændringer; fejl for én kilde får ikke eksisterende opslag til at udløbe.
- Kildeteksten af HTML-afkodes og renses for script/style. Import beholder opslagets tekst lokalt til forklaring og scoring. URL’er normaliseres; usikre dubletter bliver markeret til manuel kontrol.
- Søgeprofilen ligger i `~/Library/Application Support/CareerAgent/profile.json`, uden for repositoryet, med filtilladelser 600. Brug `CareerAgent__ProfilePath` for en anden filsti. Filen er endnu ikke krypteret af appen.
- Referencefiler, lokalt tekstudtræk og versionsindhold ligger i `~/Library/Application Support/CareerAgent/documents/` uden for repositoryet. Mapper begrænses til ejeradgang; filer oprettes med ejerens læse-/skriverettigheder. Brug `CareerAgent__DocumentsPath` for en anden lokal placering. Databasen gemmer dokumentmetadata og versionsreferencer, ikke dokumentteksten.
- Referencebiblioteket tager imod PDF/DOCX op til 10 MB og udtrækker tekst lokalt. Scannede PDF'er og OCR understøttes ikke. Referencefiler vælges igen ved hver generering, men skal ikke uploades til appen igen. Der udvælges korte, lokalt valgte tekstuddrag; synlige e-mails, telefonnumre og genkendelige danske postnumre fjernes, før uddrag sendes.
- CV/ansøgning genereres kun efter klik på **Generér CV**, **Generér ansøgning** eller **Generér begge** inde på et job. Profilfacts er dokumentets eneste kilde til kandidatens faktuelle oplysninger; hvert afsnit kræver et ordret, verificeret citat fra profilen eller jobopslaget, som vises ved punktet. Referencer bruges til stil og struktur. Udkast, redigeringer og versioner gemmes lokalt og er aldrig automatisk sendt. AI kan stadig formulere en faktuel påstand forkert, så kontrollér altid udkast og citater.
- Den separate dokumenttjeneste bruger Codex CLI med ChatGPT-login, ephemeral lokal session og read-only sandbox. Der er ingen OpenAI API-nøgle eller separat API-regning; forbruget tæller mod ChatGPT-planens Codex-kvote. På personlige ChatGPT-planer gælder kontoens indstilling for modeltræning også Codex-opgaver. Se [Codex-authentication](https://learn.chatgpt.com/docs/auth), [Codex non-interactive mode](https://learn.chatgpt.com/docs/non-interactive-mode) og [Datakontrol for Codex](https://help.openai.com/en/articles/7730893-data-controls-faq). Uddrag og job/profilfakta forlader computeren ved eksplicit generering og behandles af OpenAI; kontrollér kontoens datakontrol, før du sender private dokumentoplysninger.
- Lokal score er regelbaseret og vægter kompetencer 30, erfaring 25, jobtype 20, geografi 15 og faglig udvikling 10. Jyske Bank indgår som professionel erfaring, Tryg som praktik; ingen varighed i år udledes. Krav om 2–3 års erfaring reducerer erfaringsmatch, men medfører ikke automatisk frasortering. Beslægtede teknologier vises som overførbare, ikke som direkte erfaring. Aktuelle opslag prioriteres også efter kildens seneste statuskontrol. Manglende oplysning om frist, dato, arbejdssprog eller status vises eksplicit; scoren er en prioritering, ikke en ansættelsessandsynlighed.
- Dashboardet viser dokumenterede match, mulige kompetencegab, realismevurdering og originalt joblink. Du kan filtrere på lokation, jobtype og minimumscore. Dubletter med samme normaliserede URL samles; lignende opslag med forskellige URL'er markeres til kontrol.
- Codex-vurdering startes per opslag fra dashboardet. Kun kort profil inklusive registreret erfaring, jobtekst og den lokale score sendes til Codex via eksisterende ChatGPT-login; CV sendes aldrig. API-nøgler, brugerens almindelige Codex-konfiguration, plugins, MCP-apps, web, shell og billedeværktøjer er fjernet/deaktiveret i underprocessen. Kildeindhold behandles som ubetroet data. AI-tekst vises med citater, der valideres mod jobteksten; den ændrer ikke den regelbaserede score. Funktionen bruger ChatGPT-abonnementets Codex-kvote. Manglende login/kvote gør kun den valgfrie vurdering utilgængelig.
- Eksterne joblinks åbnes kun ved dit klik. Statusændringer og “set” sender ingen data til virksomheder.

## Build og tests

```sh
dotnet build --configuration Release
dotnet test --configuration Release
npm --prefix frontend run build
```

Backend-tests dækker validering, SQLite-lagring og genstart, statusændring, kildeadaptere, deduplikering, fejlsikre genkørsler, scorekriterier, AI-outputvalidering samt manuel dokumentgenerering, upload, versionshistorik og DOCX-eksport.

Browsertesten bruger eksisterende Google Chrome og kræver begge servere startet som ovenfor **med en separat testdatabase**. Testen opretter tydeligt markerede testjobs. Den må ikke køres mod dine personlige data:

```sh
ConnectionStrings__Jobs='Data Source=/tmp/career-agent-browser-test.db' dotnet ef database update --project backend/CareerAgent.Api
ConnectionStrings__Jobs='Data Source=/tmp/career-agent-browser-test.db' dotnet run --project backend/CareerAgent.Api --launch-profile local
# I frontend-terminalen, mens Vite kører:
npm --prefix frontend run test:e2e
```

Browsertesten bruger eksisterende Google Chrome. CI i `.github/workflows/ci.yml` bygger og tester backend og bygger frontend ved push/pull request. CI foretager ingen kildekald eller AI-kald.

## Kilder og profil

Fem aktuelle, offentlige læsekilder er konfigureret: IMC, AutoProff og Intellishore via Greenhouse; Veo og Siteimprove via Lever. Kilderne blev live-verificeret 16. oktober 2026. Aktive virksomhedsboards giver kun delvis dansk dækning; kataloget viser tydeligt kilder, der afventer adgangsafklaring.

STAR beskriver Jobnet-webservicen som gratis, men målgruppen er virksomheder/organisationer, og onboarding er nødvendig. Indhent ikke annoncer, før adgang er godkendt. LinkedIn forbyder uautoriseret scraping/automation. Der er ikke implementeret integration til LinkedIn, Jobindex eller The Hub. [STAR Jobnet](https://www.star.dk/da/digital-service/saadan-arbejder-vi-med-it-i-styrelsen/oversigt-over-digitale-platforme-for-eksterne-brugere/styrelsen-for-arbejdsmarked-og-rekrutterings-webservices-og-wiki/jobnet-webservice) · [Greenhouse Job Board API](https://docs.greenhouse.io/job-board) · [Lever Postings API](https://github.com/lever/postings-api) · [LinkedIn-regler](https://www.linkedin.com/help/linkedin/answer/a1341387/prohibition-of-scraping-software?intendedLocale=en&lang=en-us).

Profilen er oprettet lokalt som en kort, faktabaseret tekst. Ingen antal års erfaring er opfundet. Redigér profilfilen og tryk **Genberegn matches** for at opdatere score; profilhash invaliderer ældre Codex-vurderinger.

For Codex CLI er brugeren logget ind med ChatGPT. Programmatisk Codex kører med ChatGPT-login; appen bruger ingen OpenAI API-nøgle. API-nøglelogin vil bruge separat API-betaling og er ikke konfigureret. Se [Codex-authentication](https://learn.chatgpt.com/docs/auth) · [Codex non-interactive](https://learn.chatgpt.com/docs/non-interactive-mode).

## Repository og næste fase

Projektet ligger i det private GitHub-repository [Daahirabukar/career-agent](https://github.com/Daahirabukar/career-agent). Lokale databaser og miljøfiler er ikke uploadet. GitHub Actions bygger og tester ved push og pull requests.

Automatisk daglig kørsel er bevidst ikke aktiveret. CV'er og ansøgninger genereres manuelt og forbliver lokale udkast. Ekstern afsendelse er ikke implementeret; hver ansøgning kræver fortsat din udtrykkelige godkendelse.
