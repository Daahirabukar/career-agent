# Career Agent

Privat, local-first joboverblik med manuel registrering. React + TypeScript + Vite + Tailwind, ASP.NET Core 10 og EF Core/SQLite. Ingen scraping, planlagte jobs, AI-kald, CV-generering, e-mail eller automatisk ansøgning.

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

Databasen oprettes i `backend/CareerAgent.Api/data/career-agent.db`. Stien er relativ til API-projektets content root, ikke den terminalmappe du tilfældigvis står i. Databasen og dens SQLite-sidefiler er ignoreret i Git. Der er ingen seed-data; den første visning er tom.

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
- `GET /api/jobs`: jobs, nyeste først.
- `POST /api/jobs`: `{ "title": "...", "company": "...", "location": "...", "sourceUrl": "https://..." }`.
- `PATCH /api/jobs/{id}/status`: `{ "status": "Interview" }`.

Titel og virksomhed er obligatoriske og højst 200 tegn. Sted er valgfrit, højst 200 tegn. Kildelink er valgfrit, højst 2048 tegn, kun HTTP/HTTPS og uden indlejrede loginoplysninger. Statusværdier: `Saved`, `Applied`, `Interview`, `Offer`, `Rejected`. Nye jobs starter som `Saved`; API'et fastsætter ID og UTC-oprettelsestid. Fejl returneres som Problem Details.

En statusændring er kun registrering: appen sender ikke en ansøgning.

## Arkitektur og privatliv

En modulær monolit med `Jobs/` til model, input og endpoints samt `Data/` til EF Core. En separat frontend bruger API'et som eneste datakilde. SQLite er passende til én lokal bruger, og integrationstests bruger en isoleret SQLite-fil pr. test.

- API og Vite er bundet til localhost. Ingen ekstern hosting eller tunnel er konfigureret.
- CORS tillader kun `http://localhost:5173` i Development. Andre browser-origins afvises også før writes. Det er ikke brugerautentifikation.
- Der er ingen loginfunktion. Appen er til den lokale bruger; eksponering på netværk kræver et særskilt sikkerhedsdesign med autentifikation og HTTPS.
- Databasefiler er ikke krypteret af appen. De beskyttes af din lokale konto og maskinens diskbeskyttelse. Del ikke data-mappen.
- Ingen følsomme request-body- eller databaseværdier logges af appen.
- `.gitignore` udelukker miljøfiler, databaser, personlige dokumenter og genererede filer. Kontrollér altid staged filer før commit.
- Eksterne joblinks åbnes kun ved dit klik. Appen henter ikke jobopslag.

## Build og tests

```sh
dotnet build --configuration Release
dotnet test --configuration Release
npm --prefix frontend run build
```

Backend-tests dækker validering, SQLite-lagring, statusændring, ugyldige statusser, manglende job, Problem Details og lokal CORS/origin-beskyttelse.

Browsertesten bruger eksisterende Google Chrome og kræver begge servere startet som ovenfor **med en separat testdatabase**. Testen opretter tydeligt markerede testjobs. Den må ikke køres mod dine personlige data:

```sh
ConnectionStrings__Jobs='Data Source=/tmp/career-agent-browser-test.db' dotnet ef database update --project backend/CareerAgent.Api
ConnectionStrings__Jobs='Data Source=/tmp/career-agent-browser-test.db' dotnet run --project backend/CareerAgent.Api --launch-profile local
# I frontend-terminalen, mens Vite kører:
npm --prefix frontend run test:e2e
```

CI i `.github/workflows/ci.yml` bygger og tester backend og bygger frontend ved push/pull request. Ingen cron eller AI. Browsertesten er lokal og indgår ikke i CI.

## Repository og næste fase

Kun et lokalt Git-repository er oprettet. Intet er publiceret eller pushet. Et eventuelt GitHub-repository skal oprettes **privat** efter særskilt godkendelse.

Næste fase kan planlægge en jobcollector med kilderegler og dublethåndtering samt projektspecifikke Codex-skills. Det er ikke implementeret. Brugeren skal godkende fremtidige ansøgninger og ekstern afsendelse.
