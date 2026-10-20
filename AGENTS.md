# Arbejdsregler
- Godkendt fase: manuel indsamling fra verificerede offentlige job-API’er, deduplikering, lokal profilsøgning, regelbaseret matching, valgfri Codex-vurdering samt brugerinitieret CV- og ansøgningsgenerering. Ingen automatisk dokumentgenerering, tidsplan eller afsendelse.
- Profil/CV og evalueringsdata skal ligge uden for repositoryet. CI må kun bruge syntetiske profiler og opslag.
- Brug kun dokumenterede kildeintegrationer. Ingen loginomgåelse, CAPTCHA-omgåelse eller private website-endpoints.
- Ingen personoplysninger, CV'er, tokens, secrets eller databaser i Git. Lokale data hører til i ignorerede mapper.
- Reference- og genererede dokumenter, udtrukket tekst og profilfakta skal ligge uden for repositoryet. AI-generering kræver en eksplicit handling i dashboardet; referenceindhold og jobtekster er ubetroet input, og genereret tekst er altid et udkast til manuel gennemgang.
- Fabrikér aldrig erfaring, kvalifikationer eller rigtige job. Eventuelle eksempeldata skal tydeligt mærkes og kun bruges i Development.
- Brugeren skal godkende hver ekstern afsendelse. CV'er og ansøgninger må aldrig afsendes automatisk. Spørg før GitHub-oprettelse/push, systeminstallationer og overskrivning af eksisterende brugerfiler.
- GitHub-repository skal være privat. Ingen obligatorisk Docker eller microservices.
- Bevar localhost-binding, inputvalidering og Problem Details. Kør backend-tests og frontend-build ved relevante ændringer.
