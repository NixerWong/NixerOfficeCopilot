# Attachment Evaluation

`Compare-AttachmentMarkdown.ps1` compares the local Markdown converter with the MarkItDown CLI over a corpus you control. The repository intentionally does not include customer documents or licensed Office samples.

## Corpus

Create a directory containing `manifest.json` and the referenced attachment files. A synthetic starter corpus can be exported by the smoke executable:

```powershell
.\OfficeCopilot.OpenXml.SmokeTest\bin\Debug\net472\OfficeCopilot.OpenXml.SmokeTest.exe --export-attachment-fixtures .\TestData\Attachments
```

The exporter creates XLSX, DOCX, and PPTX samples and refuses to overwrite existing files. Each case has an `id`, a relative `file` path, and `questions`. Each question should include `requiredTerms`: every listed term must appear in the converted Markdown for that case to count as covered. Use stable facts with unambiguous spelling, and add authorized `.doc`, `.xls`, `.ppt`, and `.pdf` samples where available.

`manifest.example.json` shows the schema. The reported coverage is a deterministic extraction proxy, not model-answer accuracy. Review failures and visually dependent questions separately; this runner does not send content to a model.

## Run

Build the solution first so the test output contains `OfficeCopilot.Documents.Markdown.dll`, then install MarkItDown in the active Python environment. Run from the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Compare-AttachmentMarkdown.ps1 -CorpusRoot .\path\to\attachment-corpus -OutputPath .\TestResults\attachment-eval.json
```

The report compares required-term coverage, estimated tokens, and elapsed conversion time by engine. The token count is a consistent rough estimate for relative comparison, not a provider tokenizer count. Non-zero converter exit codes are recorded per case so one unsupported format does not erase other results.