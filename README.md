# URLRemover - .NET 8 C# Console Application

A powerful tool for scanning DLL assemblies and detecting/removing embedded URLs using ILReader library integration.

## Features

- **DLL Library Scanner**: Recursively scans directories or processes specific file patterns
- **Assembly Reverse Engineering**: Uses ILReader to parse assembly metadata without executing code
- **String Search & Analysis**: Regex-based URL pattern detection with detailed location tracking
- **Multiple Modification Options**: Replace, remove, or modify URLs in various formats
- **Safety Features**: Backup original DLLs, dry-run mode, comprehensive logging

## Installation

```bash
# Restore NuGet packages
dotnet restore

# Build the project
dotnet build

# Run with arguments
dotnet run -- --directory "C:\path\to\dlls" --output report.txt --dry-run
```

## Usage Examples

### Scan Directory (Dry-Run Mode)
```bash
URLRemover --directory "C:\bin\x64" --dry-run
```

### Process Specific Files
```bash
URLRemover --files "*.dll" --replace "[REDACTED]" --force
```

### Generate Report File
```bash
URLRemover --directory ".\lib\" --output "url_report.txt"
```

### Replace URLs with Custom Text
```bash
URLRemover --directory "C:\target\" --replace "EXTERNAL_LINK" --output changes.log
```

## Command-Line Options

| Option | Description | Default |
|--------|-------------|---------|
| `--directory` | Input directory containing DLL files | Required (or --files) |
| `--files` | Specific file patterns (e.g., *.dll, path\*.dll) | - |
| `--output` | Output log/report file path | Console only |
| `--replace` | Replacement text for URLs | `[REDACTED]` |
| `--force` | Force modifications without confirmation prompt | false |
| `--dry-run` | Preview changes without modifying files | true |
| `--format` | Output format: console, file, both | both |

## URL Detection Patterns

The application detects URLs using these patterns:

1. **HTTP/HTTPS**: `http://example.com/path` or `https://api.service.io/v2/data?id=123`
2. **WWW Prefix**: `www.example.org/page?param=value`
3. **FTP**: `ftp://files.server.net/downloads/file.zip`

## Output Format

### Console Output
```
=== URL Remover - .NET 8 ===
Mode: DRY-RUN | Output Format: both

Progress: [100%] Processing: Assembly.dll... ✓ Found and would replace 3 URL(s)
Progress: [200%] Processing: Lib.dll... - No URLs found
```

### Report File Structure
```
================================================================================
URL REMOVER - PROCESSING REPORT
Generated: 2026-09-07 HH:mm:ss
================================================================================

Total Files Processed: 2
URLs Found: 3
Files Modified: 1

File: Assembly.dll
  URLs Found: 3
    - [GetConfig] https://api.example.com/config.json
    - [Initialize] www.partner-site.org/download
    - [Execute] http://cdn.assets.net/style.css
  Backup: backup_Assembly.dll

================================================================================
```

## Safety Features

- **Automatic Backups**: Creates backups in `backups/` folder before any modifications
- **Dry-Run Mode**: Preview all detected URLs without changes (default)
- **Detailed Logging**: Track original → new value transformations
- **Error Handling**: Gracefully handles corrupted or protected assemblies

## Technical Details

### Dependencies
- **.NET 8.0** Runtime
- **ILReader v2.3.4**: For assembly metadata parsing
- **System.CommandLine v2.0.0-beta4**: CLI argument parsing

### Parallel Processing
The application uses `async/await` with `Parallel.ForEachAsync` for efficient multi-file processing with progress tracking.

## Building from Source

```bash
# Clone or download project
cd "D:\vscode projects\URL_Remover"

# Restore dependencies
dotnet restore

# Build release version
dotnet build -c Release

# Run the application
.\bin\Release\net8.0\URLRemover.dll --directory "." --dry-run
```

## License

MIT License - See LICENSE file for details.
