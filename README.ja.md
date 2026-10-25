# NixerOfficeCopilot

[简体中文](README.zh.md) | [English](README.md) | **日本語** | [Deutsch](README.de.md) | [Español](README.es.md)

Microsoft Word、Excel、PowerPoint 向けの VSTO Copilot です。Office ホストアドインと独立した Agent Broker を分離した構成で、共通の会話ペイン、各種モデル連携、Office ツール呼び出しに加え、MCP、Skills、メモリによる拡張機能を提供します。

## 主な機能

- **3 つの Office ホスト**：Word、Excel、PowerPoint にそれぞれ VSTO アドインを用意し、ペインと Agent ランタイムを共有します。
- **複数のモデルプロバイダー**：OpenAI、Azure OpenAI、DeepSeek、Ollama、LM Studio、Anthropic、および任意の OpenAI 互換エンドポイントに対応します。
- **Office ツール呼び出し**：ホスト固有のツールで現在の文書を読み書きします。書き込み操作では、ツールのポリシーに応じて確認またはバックアップを行います。
- **拡張機能**：MCP（stdio / HTTP）、Skills、ユーザーメモリ、設定可能な組み込みツールに対応します。
- **PowerPoint ワークフロー**：スタイルとフォントのカタログ、スライドテンプレート、プレビュー、プレゼンテーションの構築機能を備えます。
- **ローカル会話データ**：会話は JSONL 形式で永続化され、インデックスも用意されます。実行時データは Windows ユーザーごとに分離されます。
- **デスクトップペイン**：WinForms が WebView2 フロントエンドをホストし、モデル資格情報と Agent の実行は Broker が管理します。

## デモ動画

| Word | Excel | PowerPoint |
| --- | --- | --- |
| ![Word のデモ](Resources/v/word.webp) | ![Excel のデモ](Resources/v/excel.webp) | ![PowerPoint のデモ](Resources/v/ppt.webp) |

## アーキテクチャ

```text
Office ホストプロセス（Word / Excel / PowerPoint）
  VSTO アドイン
    ├─ 共通タスクペイン（WinForms + WebView2）
    ├─ ホストアダプターとネイティブ Office ツール
    └─ HostBridge 名前付きパイプサービス

共通タスクペイン <== Pipe A: JSON リクエストとイベント ==> Agent Broker
HostBridge       <== Pipe B: コンテキストとツール呼び出し ==> Agent.Runtime
Agent.Runtime    ==> HTTPS / ストリーミング応答 ==========> モデルサービス/API

Agent Broker（Office ウィンドウごとに 1 インスタンス）
  Broker パイプサービス → Agent.Runtime → ツールのディスパッチとターン実行
                                          ├─ Planning / Skills / MCP
                                          ├─ Memory / 会話の永続化
                                          └─ モデルアダプター
```

各 Office ウィンドウは独立した Broker インスタンスを持ちます。ペインと Broker、Broker と Office ホストは、それぞれ別の名前付きパイプで通信します。モデルへの HTTP リクエスト、ターンのオーケストレーション、ツール権限の判断は Broker で行い、Office 文書へのアクセスは対応するホストプロセス内のアダプターが実行します。

## プロジェクト構成

| プロジェクト | 役割 |
| --- | --- |
| `NixerWordCopilot`、`NixerExcelCopilot`、`NixerPowerpointCopilot` | VSTO アドインのエントリーポイント、Ribbon、ホストウィンドウのライフサイクル |
| `OfficeCopilot.Panel.WebView`、`OfficeCopilot.Panel.WebAssets` | 共通タスクペイン、WebView2 ブリッジ、フロントエンドリソース |
| `OfficeCopilot.Host.*`、`OfficeCopilot.Host.Abstractions` | Word、Excel、PowerPoint のコンテキスト読み取りとネイティブ Office ツール |
| `OfficeCopilot.HostBridge.*` | Office ホストと Broker 間のリクエスト/レスポンスおよびイベントプロトコル |
| `OfficeCopilot.Agent.Client`、`OfficeCopilot.Agent.Broker` | Broker IPC クライアント、独立プロセスのエントリーポイント、パイプサービス |
| `OfficeCopilot.Agent.Runtime`、`OfficeCopilot.Agent.Planning` | Agent ターン実行、コンテキスト、モデル、ツールディスパッチ、Skill の計画 |
| `OfficeCopilot.Agent.Persistence`、`OfficeCopilot.Memory` | 会話の永続化とローカルメモリ |
| `OfficeCopilot.Mcp`、`OfficeCopilot.Skills` | MCP サービス登録、ツール呼び出し、Skills の読み込み |
| `OfficeCopilot.OpenXml.*`、`OfficeCopilot.Slides.*` | Office Open XML 機能と PowerPoint のレンダリング、テンプレート、スタイル |
| `OfficeCopilot.Settings.*`、`OfficeCopilot.Diagnostics` | 設定コントラクト、設定 UI、保存先、統合ログ |
| `OfficeCopilot.Agent.Tests`、`OfficeCopilot.Eval`、`OfficeCopilot.OpenXml.SmokeTest` | 単体テスト、評価、Open XML スモーク検証 |

ファイルやモジュールの案内は [`docs/CODE_MAP.md`](docs/CODE_MAP.md)、アーキテクチャの詳細は [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) を参照してください。

## 必要環境

- Windows。
- Microsoft Word、Excel、または PowerPoint のデスクトップ版。VSTO アドインのビルドとデバッグには、対応する Office 開発コンポーネントが必要です。
- Visual Studio 2022 以降、Visual Studio MSBuild、.NET Framework 4.7.2 targeting pack。
- Microsoft Edge WebView2 Runtime。
- プロジェクトで必要な NuGet パッケージと UI コンポーネント参照（既存プロジェクトで使用している DevExpress 26.1 を含む）。
- モデル設定で到達可能なモデルサービスのエンドポイントと資格情報を設定してください。Ollama などのローカルサービスも設定できます。

## ビルドとテスト

このリポジトリには legacy VSTO および `packages.config` 形式のプロジェクトが含まれています。**ソリューション全体のビルドに `dotnet build` を使用しないでください。** Windows PowerShell でリポジトリのルートから統合スクリプトを実行します。

```powershell
# Debug ビルド
.\build.ps1

# Release で再ビルドし、Agent の単体テストを実行
.\build.ps1 -Configuration Release -Rebuild -Test
```

スクリプトは Visual Studio MSBuild を検索し、NuGet パッケージを復元してソリューションをビルドします。また、独立 Broker の出力にある Runtime アセンブリが最新かどうかを確認します。`OfficeCopilot.Agent.Tests` は legacy MSTest プロジェクトで、Visual Studio Test Platform を使用してテストを実行します。

配布ファイルのステージングには `installer/Stage-Package.ps1` を使用します。3 つのアドインと Broker の出力を `installer/staging/` に整理し、アプリ独自の UI リソース衛星アセンブリを検証します。このスクリプトは staging ディレクトリを再作成します。インストーラーを作成する際は、まずスクリプトを実行し、その後 DevExpress からダウンロードした `<Culture>` フォルダー（例：`zh-Hans`、`de`、`es`）を `installer/staging/<Culture>/` にコピーして、最終配布ディレクトリのルートに配置してください。

## 設定とローカルデータ

初回起動後、各 Copilot アドインの Ribbon からモデル、MCP、Skills、メモリ、ツールの設定を開けます。既定のユーザーデータ保存先は次のとおりです。

```text
%APPDATA%\NixerOfficeCopilot\
```

ここには `settings.json`、会話データ、プロンプト、Skills、ログなどが保存されます。設定ファイルにはモデル資格情報が含まれる場合があります。設定ファイルやユーザーの会話データをリポジトリにコミットしないでください。

## ドキュメント

- [コードマップ](docs/CODE_MAP.md)
- [アーキテクチャとレビュー履歴](docs/ARCHITECTURE.md)
- [MCP 呼び出しチェーン監査](docs/MCP_CALL_CHAIN_AUDIT.md)
- [PowerPoint Copilot の設計と実装](docs/POWERPOINT_COPILOT_BLUEPRINT.md)
- [Word テストハーネス](docs/word_harness.md)

## ライセンスとお問い合わせ

ライセンスについては [`LICENSE`](LICENSE) を参照してください。質問や不具合の報告は [GitHub Issues](https://github.com/NixerWong/NixerOfficeCopilot/issues) からお願いします。
