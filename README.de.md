# NixerOfficeCopilot

[简体中文](README.zh.md) | [English](README.md) | [日本語](README.ja.md) | **Deutsch** | [Español](README.es.md)

Ein VSTO-Copilot für Microsoft Word, Excel und PowerPoint. Das Projekt trennt die Office-Host-Add-ins von einem eigenständigen Agent Broker und bietet ein gemeinsames Chatfenster, Modellintegrationen, Office-Tool-Aufrufe sowie Erweiterungsmöglichkeiten durch MCP, Skills und Memory.

## Funktionen

- **Drei Office-Hosts**: Word, Excel und PowerPoint verfügen jeweils über ein VSTO-Add-in und nutzen das gemeinsame Fenster und die Agent-Laufzeit.
- **Mehrere Modellanbieter**: OpenAI, Azure OpenAI, DeepSeek, Ollama, LM Studio, Anthropic und benutzerdefinierte OpenAI-kompatible Endpunkte.
- **Office-Tool-Aufrufe**: Host-spezifische Tools lesen und bearbeiten das aktuelle Dokument. Schreibvorgänge erfordern je nach Richtlinie eine Bestätigung oder ein Backup.
- **Erweiterbarkeit**: MCP (stdio / HTTP), Skills, Benutzer-Memory und konfigurierbare integrierte Tools.
- **PowerPoint-Workflows**: Kataloge für Stile und Schriftarten, Folienvorlagen, Vorschauen und Erstellung von Präsentationen.
- **Lokale Gesprächsdaten**: Gespräche werden im JSONL-Format gespeichert und durch einen Index ergänzt; Laufzeitdaten sind pro Windows-Benutzer isoliert.
- **Desktop-Fenster**: WinForms hostet das WebView2-Frontend, während der Broker Modellzugangsdaten und die Agent-Ausführung verwaltet.

## Demo-Videos

| Word | Excel | PowerPoint |
| --- | --- | --- |
| ![Word-Demo](Resources/v/word.webp) | ![Excel-Demo](Resources/v/excel.webp) | ![PowerPoint-Demo](Resources/v/ppt.webp) |

## Architektur

```text
Office-Hostprozess (Word / Excel / PowerPoint)
  VSTO-Add-in
    ├─ Gemeinsamer Aufgabenbereich (WinForms + WebView2)
    ├─ Host-Adapter und native Office-Tools
    └─ HostBridge-Named-Pipe-Dienst

Gemeinsamer Aufgabenbereich <== Pipe A: JSON-Anfragen und Ereignisse ==> Agent Broker
HostBridge                  <== Pipe B: Kontext und Tool-Aufrufe =========> Agent.Runtime
Agent.Runtime               ==> HTTPS / Streaming-Antworten ============> Modelldienst/API

Agent Broker (eine Instanz pro Office-Fenster)
  Broker-Pipe-Dienst → Agent.Runtime → Tool-Dispatch und Turn-Ausführung
                                          ├─ Planning / Skills / MCP
                                          ├─ Memory / Gesprächspersistenz
                                          └─ Modelladapter
```

Jedes Office-Fenster besitzt eine eigene Broker-Instanz. Das Fenster und der Broker sowie der Broker und der Office-Host kommunizieren jeweils über separate Named Pipes. Modell-HTTP-Anfragen, Turn-Orchestrierung und Entscheidungen zur Tool-Berechtigung laufen im Broker; der Dokumentzugriff erfolgt über Adapter im jeweiligen Office-Hostprozess.

## Projektstruktur

| Projekt | Aufgabe |
| --- | --- |
| `NixerWordCopilot`, `NixerExcelCopilot`, `NixerPowerpointCopilot` | VSTO-Add-in-Einstiegspunkte, Ribbon und Lebenszyklus der Host-Fenster |
| `OfficeCopilot.Panel.WebView`, `OfficeCopilot.Panel.WebAssets` | Gemeinsamer Aufgabenbereich, WebView2-Bridge und Frontend-Ressourcen |
| `OfficeCopilot.Host.*`, `OfficeCopilot.Host.Abstractions` | Kontextzugriff und native Office-Tools für Word, Excel und PowerPoint |
| `OfficeCopilot.HostBridge.*` | Anfrage-/Antwort- und Ereignisprotokoll zwischen Office-Hosts und Broker |
| `OfficeCopilot.Agent.Client`, `OfficeCopilot.Agent.Broker` | Broker-IPC-Client, Einstiegspunkt des separaten Prozesses und Pipe-Dienst |
| `OfficeCopilot.Agent.Runtime`, `OfficeCopilot.Agent.Planning` | Agent-Turn-Ausführung, Kontext, Modelle, Tool-Dispatch und Skill-Planung |
| `OfficeCopilot.Agent.Persistence`, `OfficeCopilot.Memory` | Gesprächspersistenz und lokales Memory |
| `OfficeCopilot.Mcp`, `OfficeCopilot.Skills` | MCP-Dienstregistrierung, Tool-Aufrufe und Laden von Skills |
| `OfficeCopilot.OpenXml.*`, `OfficeCopilot.Slides.*` | Office-Open-XML-Funktionen sowie PowerPoint-Rendering, Vorlagen und Stile |
| `OfficeCopilot.Settings.*`, `OfficeCopilot.Diagnostics` | Konfigurationsverträge, Einstellungsoberfläche, Speicherpfade und einheitliche Protokollierung |
| `OfficeCopilot.Agent.Tests`, `OfficeCopilot.Eval`, `OfficeCopilot.OpenXml.SmokeTest` | Unit-Tests, Evaluation und Open-XML-Smoke-Tests |

Eine detaillierte Datei- und Modulübersicht finden Sie in [`docs/CODE_MAP.md`](docs/CODE_MAP.md); weitere Architekturinformationen stehen in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Voraussetzungen

- Windows.
- Desktopversion von Microsoft Word, Excel oder PowerPoint. Zum Erstellen und Debuggen der VSTO-Add-ins werden die entsprechenden Office-Entwicklungskomponenten benötigt.
- Visual Studio 2022 oder höher, Visual Studio MSBuild und das .NET Framework 4.7.2 Targeting Pack.
- Microsoft Edge WebView2 Runtime.
- Die benötigten NuGet-Pakete und UI-Komponentenreferenzen des Projekts, einschließlich DevExpress 26.1, das in den bestehenden Projekten verwendet wird.
- Ein erreichbarer Modellservice-Endpunkt und Zugangsdaten in den Modelleinstellungen. Lokale Dienste wie Ollama können ebenfalls konfiguriert werden.

## Erstellen und testen

Das Repository enthält Legacy-VSTO- und `packages.config`-Projekte. **Verwenden Sie `dotnet build` nicht, um die gesamte Solution zu erstellen.** Führen Sie das zentrale Skript in Windows PowerShell aus dem Repository-Stammverzeichnis aus:

```powershell
# Debug-Build
.\build.ps1

# Release-Neuaufbau und Agent-Unit-Tests
.\build.ps1 -Configuration Release -Rebuild -Test
```

Das Skript sucht Visual Studio MSBuild, stellt NuGet-Pakete wieder her, erstellt die Solution und prüft, ob die Runtime-Assembly in der Ausgabe des separaten Brokers aktuell ist. `OfficeCopilot.Agent.Tests` ist ein Legacy-MSTest-Projekt; die Tests laufen über die Visual Studio Test Platform.

Zum Bereitstellen der Installationsdateien verwenden Sie `installer/Stage-Package.ps1`. Das Skript ordnet die drei Add-ins und die Broker-Ausgabe unter `installer/staging/` für den nachfolgenden Installer-Prozess an und prüft die Satellitenassemblies der anwendungseigenen UI-Ressourcen. Das staging-Verzeichnis wird dabei neu erstellt. Führen Sie vor dem Erstellen des Installers zuerst das Skript aus und kopieren Sie anschließend die von DevExpress heruntergeladenen `<Culture>`-Ordner (z. B. `zh-Hans`, `de`, `es`) nach `installer/staging/<Culture>/`, sodass sie im Stammverzeichnis der endgültigen Distribution liegen.

## Konfiguration und lokale Daten

Nach dem ersten Start können Sie über das Ribbon eines Copilot-Add-ins die Einstellungen für Modelle, MCP, Skills, Memory und Tools öffnen. Das Standardverzeichnis für Benutzerdaten lautet:

```text
%APPDATA%\NixerOfficeCopilot\
```

Dort liegen unter anderem `settings.json`, Gesprächsdaten, Prompts, Skills und Protokolle. Die Einstellungsdatei kann Modellzugangsdaten enthalten. Committen Sie weder diese Datei noch Gesprächsdaten von Benutzern in das Repository.

## Dokumentation

- [Code Map](docs/CODE_MAP.md)
- [Architektur und Review-Verlauf](docs/ARCHITECTURE.md)
- [Prüfung der MCP-Aufrufkette](docs/MCP_CALL_CHAIN_AUDIT.md)
- [PowerPoint-Copilot: Entwurf und Implementierung](docs/POWERPOINT_COPILOT_BLUEPRINT.md)
- [Word-Test-Harness](docs/word_harness.md)

## Lizenz und Kontakt

Die Lizenz finden Sie unter [`LICENSE`](LICENSE). Fragen und Fehlerberichte können Sie über [GitHub Issues](https://github.com/NixerWong/NixerOfficeCopilot/issues) einreichen.
