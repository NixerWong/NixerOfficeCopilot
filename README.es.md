# NixerOfficeCopilot

[简体中文](README.zh.md) | [English](README.md) | [日本語](README.ja.md) | [Deutsch](README.de.md) | **Español**

Un copiloto VSTO para Microsoft Word, Excel y PowerPoint. El proyecto separa los complementos de Office de un Agent Broker independiente y ofrece un panel de conversación compartido, integración con modelos, llamadas a herramientas de Office y extensibilidad mediante MCP, Skills y memoria.

## Funciones

- **Tres hosts de Office**: Word, Excel y PowerPoint cuentan con complementos VSTO propios y comparten el panel y el entorno de ejecución del Agent.
- **Varios proveedores de modelos**: OpenAI, Azure OpenAI, DeepSeek, Ollama, LM Studio, Anthropic y endpoints personalizados compatibles con OpenAI.
- **Llamadas a herramientas de Office**: las herramientas específicas de cada host leen y modifican el documento activo. Según la política de cada herramienta, las operaciones de escritura requieren confirmación o una copia de seguridad.
- **Extensibilidad**: MCP (stdio / HTTP), Skills, memoria del usuario y herramientas integradas configurables.
- **Flujos de trabajo de PowerPoint**: catálogos de estilos y fuentes, plantillas de diapositivas, vistas previas y creación de presentaciones.
- **Datos de conversación locales**: las conversaciones se guardan en formato JSONL y cuentan con un índice; los datos de ejecución se aíslan por usuario de Windows.
- **Panel de escritorio**: WinForms aloja el frontend de WebView2, mientras que el Broker administra las credenciales de los modelos y la ejecución del Agent.

## Vídeos de demostración

| Word | Excel | PowerPoint |
| --- | --- | --- |
| ![Demostración de Word](Resources/v/word.webp) | ![Demostración de Excel](Resources/v/excel.webp) | ![Demostración de PowerPoint](Resources/v/ppt.webp) |

## Arquitectura

```text
Proceso host de Office (Word / Excel / PowerPoint)
  Complemento VSTO
    ├─ Panel de tareas compartido (WinForms + WebView2)
    ├─ Adaptadores del host y herramientas nativas de Office
    └─ Servicio de canalizaciones con nombre de HostBridge

Panel compartido <== Pipe A: solicitudes JSON y eventos ==> Agent Broker
HostBridge       <== Pipe B: contexto y llamadas a herramientas ==> Agent.Runtime
Agent.Runtime    ==> HTTPS / respuestas en streaming ==========> Servicio/API de modelos

Agent Broker (una instancia independiente por ventana de Office)
  Servicio de canalizaciones del Broker → Agent.Runtime → despacho de herramientas y ejecución de turnos
                                                       ├─ Planning / Skills / MCP
                                                       ├─ Memory / persistencia de conversaciones
                                                       └─ Adaptadores de modelos
```

Cada ventana de Office tiene su propia instancia del Broker. El panel y el Broker, así como el Broker y el host de Office, se comunican mediante canalizaciones con nombre independientes. Las solicitudes HTTP a los modelos, la orquestación de turnos y las decisiones de permisos de las herramientas se gestionan en el Broker; las operaciones sobre documentos las ejecutan adaptadores específicos dentro del proceso host correspondiente.

## Estructura del proyecto

| Proyecto | Responsabilidad |
| --- | --- |
| `NixerWordCopilot`, `NixerExcelCopilot`, `NixerPowerpointCopilot` | Puntos de entrada de los complementos VSTO, Ribbon y ciclo de vida de las ventanas host |
| `OfficeCopilot.Panel.WebView`, `OfficeCopilot.Panel.WebAssets` | Panel de tareas compartido, puente de WebView2 y recursos del frontend |
| `OfficeCopilot.Host.*`, `OfficeCopilot.Host.Abstractions` | Lectura del contexto y herramientas nativas de Office para Word, Excel y PowerPoint |
| `OfficeCopilot.HostBridge.*` | Protocolo de solicitudes/respuestas y eventos entre los hosts de Office y el Broker |
| `OfficeCopilot.Agent.Client`, `OfficeCopilot.Agent.Broker` | Cliente IPC del Broker, punto de entrada del proceso independiente y servicio de canalizaciones |
| `OfficeCopilot.Agent.Runtime`, `OfficeCopilot.Agent.Planning` | Ejecución de turnos del Agent, contexto, modelos, despacho de herramientas y planificación de Skills |
| `OfficeCopilot.Agent.Persistence`, `OfficeCopilot.Memory` | Persistencia de conversaciones y memoria local |
| `OfficeCopilot.Mcp`, `OfficeCopilot.Skills` | Registro de servicios MCP, llamadas a herramientas y carga de Skills |
| `OfficeCopilot.OpenXml.*`, `OfficeCopilot.Slides.*` | Funciones de Office Open XML y renderizado, plantillas y estilos de PowerPoint |
| `OfficeCopilot.Settings.*`, `OfficeCopilot.Diagnostics` | Contratos de configuración, interfaz de ajustes, rutas de almacenamiento y registro unificado |
| `OfficeCopilot.Agent.Tests`, `OfficeCopilot.Eval`, `OfficeCopilot.OpenXml.SmokeTest` | Pruebas unitarias, evaluación y pruebas de humo de Open XML |

Consulte [`docs/CODE_MAP.md`](docs/CODE_MAP.md) para navegar por los archivos y módulos, y [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) para obtener más información sobre la arquitectura.

## Requisitos

- Windows.
- Versión de escritorio de Microsoft Word, Excel o PowerPoint. Para compilar y depurar los complementos VSTO se necesitan los componentes de desarrollo de Office correspondientes.
- Visual Studio 2022 o posterior, Visual Studio MSBuild y el targeting pack de .NET Framework 4.7.2.
- Microsoft Edge WebView2 Runtime.
- Los paquetes NuGet y las referencias de componentes de interfaz que requiere el proyecto, incluido DevExpress 26.1, usado por los proyectos existentes.
- Un endpoint de servicio de modelos accesible y las credenciales correspondientes configuradas en los ajustes de modelos. También se pueden configurar servicios locales como Ollama.

## Compilación y pruebas

El repositorio incluye proyectos VSTO heredados y proyectos con `packages.config`. **No use `dotnet build` para compilar toda la solución.** Desde la raíz del repositorio, ejecute el script unificado en Windows PowerShell:

```powershell
# Compilación Debug
.\build.ps1

# Reconstrucción Release y pruebas unitarias del Agent
.\build.ps1 -Configuration Release -Rebuild -Test
```

El script localiza Visual Studio MSBuild, restaura los paquetes NuGet, compila la solución y comprueba que el ensamblado Runtime de la salida del Broker independiente esté actualizado. `OfficeCopilot.Agent.Tests` es un proyecto MSTest heredado y ejecuta las pruebas mediante Visual Studio Test Platform.

Para preparar los archivos de despliegue, use `installer/Stage-Package.ps1`. El script organiza los tres complementos y la salida del Broker en `installer/staging/` para el proceso posterior del instalador y valida los ensamblados satélite de los recursos de interfaz propios de la aplicación. El directorio staging se vuelve a crear al ejecutar el script. Antes de empaquetar, ejecútelo y después copie las carpetas `<Culture>` descargadas de DevExpress (por ejemplo, `zh-Hans`, `de` y `es`) en `installer/staging/<Culture>/`, de modo que queden en la raíz del directorio de distribución final.

## Configuración y datos locales

Tras el primer inicio, puede abrir desde el Ribbon de cualquier complemento Copilot la configuración de modelos, MCP, Skills, memoria y herramientas. La ubicación predeterminada de los datos del usuario es:

```text
%APPDATA%\NixerOfficeCopilot\
```

Incluye `settings.json`, datos de conversaciones, prompts, Skills y registros, entre otros. El archivo de configuración puede contener credenciales de modelos. No lo incluya en el repositorio ni suba datos de conversaciones de usuarios.

## Documentación

- [Mapa del código](docs/CODE_MAP.md)
- [Arquitectura e historial de revisiones](docs/ARCHITECTURE.md)
- [Auditoría de la cadena de llamadas MCP](docs/MCP_CALL_CHAIN_AUDIT.md)
- [Diseño e implementación de PowerPoint Copilot](docs/POWERPOINT_COPILOT_BLUEPRINT.md)
- [Harness de pruebas de Word](docs/word_harness.md)

## Licencia y contacto

Consulte [`LICENSE`](LICENSE). Para preguntas o informes de errores, visite [GitHub Issues](https://github.com/NixerWong/NixerOfficeCopilot/issues).
