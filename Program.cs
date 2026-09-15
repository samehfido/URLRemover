using AsmResolver.DotNet;
using AsmResolver.DotNet.Builder;
using AsmResolver.PE;
using AsmResolver.PE.Builder;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables;
using AsmResolver.PE.File;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace URLRemover
{
    class Program
    {
        // Regex patterns for URL detection
        private static readonly string[] UrlPatterns = new[]
        {
            @"https?://[^\s<>"",]+",           // http/https URLs
            @"www\.[^\s<>"",]+",                // www. prefixed domains
            @"ftp(s)?://[^\s<>"",]+"           // FTP URLs
        };

        static async Task<int> Main(string[] args)
        {
            Console.WriteLine("=== URL Remover - .NET 8 ===");

            var rootCommand = new RootCommand("URLRemover: Scan and remove URLs from DLL assemblies.");

            Option<DirectoryInfo> dirOption = new("--directory")
            {
                Description = "Input directory containing DLL files"
            };

            // Directory option
            rootCommand.Options.Add(dirOption);

            // Files option
            var filesOption = new Option<string[]>("--files")
            {
                Description = "Specific file patterns (e.g., *.dll)"
            };
            rootCommand.Options.Add(filesOption);

            // Output option
            var outputOption = new Option<string>("--output")
            {
                Description = "Output log file path"
            };
            rootCommand.Options.Add(outputOption);

            // Replace option
            var replaceOption = new Option<string>("--replace")
            {
                Description = "Replacement text for URLs"
            };
            rootCommand.Options.Add(replaceOption);

            // Force modification option
            var forceOption = new Option<bool>("--force")
            {
                Description = "Force modifications without confirmation"
            };
            rootCommand.Options.Add(forceOption);

            // Dry-run mode
            var dryRunOption = new Option<bool>("--dry-run")
            {
                Description = "Preview changes without modifying files"
            };
            rootCommand.Options.Add(dryRunOption);

            // Output format option
            var formatOption = new Option<string>("--format")
            {
                Description = "Output format: console, file, both"
            };
            rootCommand.Options.Add(formatOption);

            // Single file option
            var singleFileOption = new Option<FileInfo>("--file")
            {
                Description = "Single DLL file to process"
            };
            rootCommand.Options.Add(singleFileOption);

            var parseResult = rootCommand.Parse(args);
            if (await InvokeAsync(parseResult) == 1)//No valid DLL files to process.
            {
                var results = new ConcurrentBag<FileResult>();
                var filePath = args[0] ?? string.Empty;

                if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                    return 1;

                try
                {
                    //var fileResult = await ProcessDllAsync(filePath, parseResult);
                    //results.Add(fileResult);

                    //if (fileResult.FoundUrls != null && fileResult.FoundUrls.Any())
                    //    Console.WriteLine($" ✓ Found and {("replaced")} {fileResult.UrlCount} URL(s)");
                    //else if (!fileResult.FoundUrls!.Any())
                    //    Console.WriteLine(" - No URLs found");

                    var patcher = new DllStringReplacer(new FileResult(filePath));
                    var result = patcher.ReplaceInFile(filePath, "https://", "h00ps://", out byte[] data, Encoding.UTF8);
                    result += patcher.Replace(data, "https://", "h00ps://", Encoding.Unicode, out data);
                    if (result > 0)
                        Console.WriteLine($" ✓ Found and {("replaced")} {result} URL(s)");
                    else
                        Console.WriteLine(" - No URLs found");

                    var outputPath = $"{filePath}_report.txt";

                    await SaveToFile(outputPath, GenerateReport(patcher.FileResult));
                    File.WriteAllBytes(filePath, data);

                }
                catch (Exception ex)
                {
                    results.Add(new FileResult(filePath, false, null, $"Error: {ex.Message}"));
                    Console.WriteLine($" ✗ Error: {ex.GetType().Name}");
                }
            }
            Console.ReadLine();
            return 0;
        }

        public enum InputSourceType
        {
            None,
            File,
            Directory,
            Patterns
        }

        static async Task<int> InvokeAsync(ParseResult parseResult)
        {

            var outputFormat = (parseResult.RootCommandResult.GetValue<string>("--format")) ?? "both";
            var isDryRun = parseResult.RootCommandResult.GetValue<bool>("--dry-run") == true;

            Console.WriteLine($"Mode: {(isDryRun ? "DRY-RUN" : "MODIFY")} | Output Format: {outputFormat}");

            var singleFile = parseResult.RootCommandResult.GetValue<FileInfo>("--file");
            var directory = parseResult.RootCommandResult.GetValue<DirectoryInfo>("--directory");
            var files = parseResult.RootCommandResult.GetValue<string[]>("--files") ?? Array.Empty<string>();

            var filesToProcess = new List<string>();

            InputSourceType sourceType;
            if (singleFile != null)
                sourceType = InputSourceType.File;
            else if (directory != null)
                sourceType = InputSourceType.Directory;
            else if (files.Length > 0)
                sourceType = InputSourceType.Patterns;
            else
                sourceType = InputSourceType.None;

            switch (sourceType)
            {
                case InputSourceType.File when singleFile != null:
                    filesToProcess.Add(singleFile.FullName);
                    break;

                case InputSourceType.Directory when directory != null:
                    ScanDirectory(directory, filesToProcess);
                    break;

                case InputSourceType.Patterns when files.Length > 0:
                    foreach (var pattern in files)
                        filesToProcess.AddRange(ParseFilePattern(pattern));
                    break;
                case InputSourceType.None:
                    break;
                default:
                    break;
            }

            if (!filesToProcess.Any())
            {
                Console.WriteLine("Error: No valid DLL files to process. Use --directory or provide file patterns.");
                return 1;
            }

            var results = new ConcurrentBag<FileResult>();
            int totalFiles = filesToProcess.Count;

            await Parallel.ForEachAsync(filesToProcess, async (filePath, token) =>
            {
                if (!token.IsCancellationRequested)
                {
                    Console.Write($"\rProgress: [{(int)((filesToProcess.IndexOf(filePath) + 1) * 100.0 / totalFiles):3}%] Processing: {Path.GetFileName(filePath)}...");

                    try
                    {
                        var fileResult = await ProcessDllAsync(filePath, parseResult);
                        results.Add(fileResult);

                        if (fileResult.FoundUrls != null && fileResult.FoundUrls.Any() && !isDryRun)
                            Console.WriteLine($" ✓ Found and {(isDryRun ? "would replace" : "replaced")} {fileResult.UrlCount} URL(s)");
                        else if (!fileResult.FoundUrls!.Any())
                            Console.WriteLine(" - No URLs found");
                    }
                    catch (Exception ex)
                    {
                        results.Add(new FileResult(filePath, false, null, $"Error: {ex.Message}"));
                        Console.WriteLine($" ✗ Error: {ex.GetType().Name}");
                    }
                }
            });

            var report = GenerateReport(results.ToList());

            if (outputFormat == "file" || outputFormat == "both")
            {
                foreach (var result in results)
                {
                    var outputPath = (parseResult.RootCommandResult.GetValue<string>("--output"))
                        ?? $"{result.Path}_report.txt";
                    await SaveToFile(outputPath, report);
                    Console.WriteLine($"\nFull report saved to: {outputPath}");
                }
            }

            if (isDryRun)
                Console.WriteLine("\n=== DRY-RUN MODE - No changes were made ===");

            return results.Any(r => r.Error != null) ? 1 : 0;
        }

        static void ScanDirectory(DirectoryInfo directory, List<string> filesToProcess)
        {
            if (!directory.Exists)
            {
                Console.WriteLine($"Error: Directory not found: {directory.FullName}");
                return;
            }

            var queue = new Queue<FileInfo>(directory.GetFiles("*.dll", SearchOption.AllDirectories));

            int counter = queue.Count;
            while (counter > 0)
            {
                var file = queue.Dequeue();

                if (IsSystemDll(file)) continue;

                filesToProcess.Add(file.FullName);

                try
                {
                    using var fileHandle = File.OpenRead(file.FullName);
                    PEImage.FromStream(fileHandle);
                    queue.Enqueue(file);
                }
                catch
                {
                    break;
                }
                counter--;
            }
        }

        static bool IsSystemDll(FileInfo file)
        {
            string name = Path.GetFileName(file.Name).ToLower();

            var systemPaths = new[]
            {
                @"C:\Windows\System32", @"C:\Program Files\Common Files\Microsoft Shared",
                @"C:\Program Files (x86)\Common Files\Microsoft Shared"
            };

            if (systemPaths.Any(p => file.FullName.StartsWith(p)))
                return true;

            var netDlls = new[] { "mscorlib.dll", "System.Runtime.dll", "System.Core.dll",
                                 "System.Private.CoreLib.dll" };

            if (netDlls.Any(n => name == n))
                return true;

            return false;
        }

        static List<string> ParseFilePattern(string pattern)
        {
            var files = new List<string>();

            if (pattern.Contains("*"))
            {
                var dir = new DirectoryInfo(Path.GetDirectoryName(pattern)?.TrimEnd('\\') ?? Directory.GetCurrentDirectory());
                var extension = Path.GetExtension(pattern).ToLower();
                foreach (var f in dir.GetFiles($"{extension.TrimStart('.')}", SearchOption.AllDirectories))
                    files.Add(f.FullName);
            }

            if (File.Exists(pattern))
                files.Add(pattern);

            return files;
        }

        static async Task<FileResult> ProcessDllAsync(string dllPath, ParseResult parseResult)
        {
            bool isDryRun = parseResult.RootCommandResult.GetValue<bool>("--dry-run") == true;
            var replacementText = (parseResult.RootCommandResult.GetValue<string>("--replace")) ?? "[REDACTED]";
            bool forceVal = parseResult.RootCommandResult.GetValue<bool>("--force") == true;

            FileResult result = new FileResult(dllPath);

            try
            {
                if (!isDryRun)
                {
                    string backupPath;
                    var parentDir = Directory.GetParent(dllPath);
                    if (parentDir == null || !File.Exists(Path.Combine(parentDir.FullName, "backups")))
                        Directory.CreateDirectory(Path.Combine(parentDir!.FullName, "backups"));

                    backupPath = Path.Combine(Directory.GetParent(dllPath)!.FullName + "\\backups\\" + Path.GetFileName(dllPath));
                    if (!File.Exists(backupPath)) File.Copy(dllPath, backupPath, true);
                    result.BackupPath = backupPath;
                }

                var assemblyMetadata = AssemblyDefinition.FromFile(dllPath);
                //await LoadAssemblyAsync(dllPath);

                if (assemblyMetadata == null)
                {
                    result.Error = "Failed to load assembly metadata";
                    return result;
                }

                if (assemblyMetadata.Modules == null || !assemblyMetadata.Modules.Any())
                    throw new Exception("Has no Modules!");

                var foundUrls = new List<UrlMatch>();
                foreach (var module in assemblyMetadata.Modules)
                    foreach (var type in module.GetAllTypes().Where(t => !t.IsInterface))
                    {
                        if (type.Attributes.HasFlag(TypeAttributes.RuntimeSpecialName) || type.HasCustomAttribute("System.Runtime.CompilerServices", "CompilerGeneratedAttribute"))
                            continue;

                        if (type.Methods.Count < 1) continue;
                        foreach (var method in type.Methods)
                        {
                            try
                            {
                                if (method.MethodBody == null || method.Unmanaged || !method.HasMethodBody) continue;

                                var bodyInstructions = method.CilMethodBody?.Instructions;
                                if (bodyInstructions is null) continue;
                                foreach (var instruction in bodyInstructions)
                                {
                                    // Look for load string instructions (ldstr opcode is 0x28)
                                    if (instruction.OpCode.Code == CilCode.Ldstr && instruction.Operand is string strValue)
                                    {
                                        foreach (var pattern in UrlPatterns)
                                        {
                                            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                                            var matches = regex.Matches(strValue);
                                            foreach (Match match in matches)
                                            {
                                                foundUrls.Add(new UrlMatch
                                                {
                                                    Value = match.Value,
                                                    Position = match.Index,
                                                    MethodName = method.Name ?? "unknown",
                                                    TypeName = method.DeclaringType?.FullName ?? "unknown",
                                                    LineNumber = instruction.Offset,
                                                    OriginalText = strValue
                                                });

                                                if (!isDryRun)
                                                {
                                                    method.CilMethodBody.VerifyLabels();
                                                    method.CilMethodBody.VerifyLabelsOnBuild = false;
                                                    method.CilMethodBody.MaxStack = method.CilMethodBody.ComputeMaxStack();
                                                    method.CilMethodBody.ComputeMaxStackOnBuild = false;

                                                    instruction.Operand = strValue.Replace("https", "h00ps");
                                                    result.Modified = true;
                                                }
                                            }
                                        }
                                    }

                                    // Also check any operand that is a string
                                    if (instruction.Operand is string operandString)
                                    {
                                        foreach (var pattern in UrlPatterns)
                                        {
                                            var regex = new Regex(pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                                            var matches = regex.Matches(operandString);
                                            foreach (Match match in matches)
                                            {
                                                foundUrls.Add(new UrlMatch
                                                {
                                                    Value = match.Value,
                                                    Position = match.Index,
                                                    MethodName = method.Name ?? "unknown",
                                                    TypeName = method.DeclaringType?.FullName ?? "unknown",
                                                    LineNumber = instruction.Offset,
                                                    OriginalText = operandString
                                                });
                                                if (!isDryRun)
                                                {
                                                    instruction.Operand = operandString.Replace("https", "h00ps");
                                                    result.Modified = true;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"  Warning: Could not read IL from method {method.Name} in type {method.DeclaringType?.FullName}: {ex.Message}");
                            }
                        }
                    }

                result.FoundUrls = foundUrls;

                if (!isDryRun)
                {
                    var imageBuilder = new ManagedPEImageBuilder();
                    var factory = new DotNetDirectoryFactory();
                    factory.MetadataBuilderFlags = MetadataBuilderFlags.PreserveAll;
                    imageBuilder.DotNetDirectoryFactory = factory;

                    var module = assemblyMetadata.ManifestModule;

                    var buildResult = imageBuilder.CreateImage(module);
                    var image = buildResult.ConstructedImage;

                    // Print all errors.
                    if (buildResult.DiagnosticBag.Exceptions.Count > 0)
                    {
                        Trace.WriteLine($"Construction finished with {buildResult.DiagnosticBag.Exceptions.Count} errors.");
                        foreach (var error in buildResult.DiagnosticBag.Exceptions)
                            Trace.WriteLine(error.Message);
                    }

                    // Write image to the disk.
                    var fileBuilder = new ManagedPEFileBuilder();
                    var file = fileBuilder.CreateFile(image);
                    file.Write(dllPath);


                    //var imageBuilder = new ManagedPEImageBuilder(MetadataBuilderFlags.PreserveAll);
                    //var factory = new DotNetDirectoryFactory();
                    //factory.MetadataBuilderFlags = MetadataBuilderFlags.PreserveBlobIndices
                    //                             | MetadataBuilderFlags.PreserveTypeReferenceIndices;
                    // imageBuilder.DotNetDirectoryFactory = factory;

                    //assemblyMetadata.Write(dllPath, imageBuilder);
                    result.Modified = true;
                }
                if (!isDryRun && forceVal)
                {
                    await ApplyReplacementsAsync(dllPath, replacementText);
                    result.Modified = true;
                }
            }
            catch (Exception ex)
            {
                result.Error = $"Processing failed: {ex.Message}";
                Console.WriteLine($"  Error details: {ex.GetType().Name}: {ex.Message}");
            }

            return result;
        }

        static async Task ApplyReplacementsAsync(string dllPath, string replacementText)
        {
            //var originalBytes = await File.ReadAllBytesAsync(dllPath);
            Console.WriteLine($"  [Would modify {dllPath} with replacement text: '{replacementText}']");
        }


        static string GenerateReport(FileResult result)
        {
            if (result == null)
                throw new ArgumentNullException(nameof(result));

            var sb = new StringBuilder();

            const int width = 80;
            string separator = new string('=', width);

            sb.AppendLine(separator);
            sb.AppendLine("URL REMOVER - PROCESSING REPORT");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine(separator);

            int totalFiles = 1;
            int foundUrlsCount = result.UrlCount;

            bool modifiedFile =
                result.Modified ||
                (!result.IsDryRunModified && result.FoundUrls != null && result.FoundUrls.Any());

            int modifiedFiles = modifiedFile ? 1 : 0;

            sb.AppendLine();
            sb.AppendLine($"Total Files Processed: {totalFiles}");
            sb.AppendLine($"URLs Found: {foundUrlsCount}");
            sb.AppendLine($"Files Modified: {modifiedFiles}");
            sb.AppendLine();

            sb.AppendLine($"File: {Path.GetFileName(result.Path)}");
            sb.AppendLine($"  URLs Found: {result.UrlCount}");

            if (result.FoundUrls != null && result.FoundUrls.Any())
            {
                var sampleUrls = result.FoundUrls.Take(25).ToList();

                foreach (var url in sampleUrls)
                    sb.AppendLine($"    - [{url.MethodName}] {url.Value}");

                if (result.UrlCount > 25)
                    sb.AppendLine($"    ... and {result.UrlCount - 25} more URL(s)");
            }

            if (!string.IsNullOrEmpty(result.Error))
                sb.AppendLine($"  Error: {result.Error}");

            if (!string.IsNullOrEmpty(result.BackupPath))
                sb.AppendLine($"  Backup: {Path.GetFileName(result.BackupPath)}");

            sb.AppendLine();
            sb.AppendLine(separator);

            return sb.ToString();
        }

        static string GenerateReport(List<FileResult> results)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine("=".PadRight(80, '='));
            sb.AppendLine("URL REMOVER - PROCESSING REPORT");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine("=".PadRight(80, '=').PadLeft(2 + 76));

            int totalFiles = results.Count;
            int foundUrlsCount = results.Sum(r => r.UrlCount);
            int modifiedFiles = results.Count(r => r.Modified || !r.IsDryRunModified && r.FoundUrls != null && r.FoundUrls.Any());

            sb.AppendLine();
            sb.AppendLine($"Total Files Processed: {totalFiles}");
            sb.AppendLine($"URLs Found: {foundUrlsCount}");
            sb.AppendLine($"Files Modified: {modifiedFiles}");
            sb.AppendLine();

            foreach (var result in results)
            {
                sb.AppendLine($"File: {Path.GetFileName(result.Path)}");
                sb.AppendLine($"  URLs Found: {result.UrlCount}");

                if (result.FoundUrls != null && result.FoundUrls.Any())
                {
                    var sampleUrls = result.FoundUrls.Take(25).ToList();

                    foreach (var url in sampleUrls)
                        sb.AppendLine($"    - [{url.MethodName}] {url.Value}");

                    if (result.UrlCount > 25)
                        sb.AppendLine($"    ... and {result.UrlCount - 25} more URL(s)");
                }

                if (!string.IsNullOrEmpty(result.Error))
                    sb.AppendLine($"  Error: {result.Error}");

                if (!string.IsNullOrEmpty(result.BackupPath))
                    sb.AppendLine($"  Backup: {Path.GetFileName(result.BackupPath)}");

                sb.AppendLine();
            }

            sb.AppendLine("=".PadRight(80, '=').PadLeft(2 + 76));

            return sb.ToString();
        }

        static async Task SaveToFile(string outputPath, string report)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            await File.WriteAllTextAsync(outputPath, report);
        }

        private class FileResult
        {
            public string Path { get; set; } = "";
            public string BackupPath { get; set; } = "";
            public int UrlCount
            {
                get
                {
                    return FoundUrls != null ? FoundUrls.Count : 0;
                }
            }
            public List<UrlMatch>? FoundUrls { get; set; } = new();
            public bool Modified { get; set; }
            public bool IsDryRunModified { get; set; }
            public string Error { get; set; } = "";

            public FileResult(string path) => Path = path;

            public FileResult(string path, bool modified, List<UrlMatch>? foundUrls, string error)
                : this(path)
            {
                Modified = modified;
                FoundUrls = foundUrls ?? new();
                Error = error;
            }
        }

        private class UrlMatch
        {
            public string Value { get; set; } = "";
            public int Position { get; set; }
            public string MethodName { get; set; } = "<unknown>";
            public string TypeName { get; set; } = "<unknown>";
            public int LineNumber { get; set; }
            public string OriginalText { get; set; } = "";
        }


        private sealed class DllStringReplacer
        {
            public FileResult FileResult;
            public DllStringReplacer(FileResult results)
            {
                FileResult = results;

            }
            /// <summary>
            /// Reads a DLL/binary file, replaces strings, and writes the result back to disk.
            /// </summary>
            public int ReplaceInFile(
                string filePath,
                string oldValue,
                string newValue, out byte[] output,
                Encoding encoding = null,
                bool createBackup = true,
                bool padShorterWithNulls = false,
                bool allowResize = false
                )
            {
                if (FileResult == null)
                    FileResult = new FileResult(filePath);

                FileResult.FoundUrls = new List<UrlMatch>();

                if (string.IsNullOrEmpty(oldValue))
                    throw new ArgumentException("oldValue cannot be empty.", nameof(oldValue));

                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}", filePath);

                byte[] data = File.ReadAllBytes(filePath);

                byte[] result;
                int count = Replace(
                    data,
                    oldValue,
                    newValue,
                    encoding ?? Encoding.UTF8, out result,
                    padShorterWithNulls,
                    allowResize
                    );

                if (createBackup)
                    FileResult.BackupPath = CreateBackup(filePath);

                output = result;

                return count;
            }

            /// <summary>
            /// Replaces strings in a byte array.
            /// </summary>
            public int Replace(
                byte[] input,
                string oldValue,
                string newValue,
                Encoding encoding, out byte[] output,
                bool padShorterWithNulls = false,
                bool allowResize = false)
            {

                if (input == null)
                    throw new ArgumentNullException(nameof(input));

                if (string.IsNullOrEmpty(oldValue))
                    throw new ArgumentException("oldValue cannot be empty.", nameof(oldValue));

                byte[] pattern = GetBytes(oldValue, encoding ?? Encoding.UTF8);
                byte[] replacement = GetBytes(newValue, encoding ?? Encoding.UTF8);

                int targetLength = pattern.Length;

                // Safe mode: file size does not change.
                if (!allowResize)
                {
                    if (replacement.Length > targetLength)
                        throw new InvalidOperationException(
                            "Replacement is longer than the search text. " +
                            "Use allowResize=true only for non-DLL files or when you know how to fix PE offsets.");

                    byte[] finalReplacement = replacement;

                    if (replacement.Length != targetLength)
                    {
                        if (!padShorterWithNulls)
                            throw new InvalidOperationException(
                                "Replacement must be exactly the same length unless padShorterWithNulls is true.");

                        // Pad shorter replacement with null bytes.
                        finalReplacement = new byte[targetLength];
                        Buffer.BlockCopy(replacement, 0, finalReplacement, 0, replacement.Length);
                    }

                    int count = 0;
                    int start = 0;

                    while (start <= input.Length - targetLength)
                    {
                        int found = Find(input, pattern, start);

                        if (found < 0)
                            break;

                        int slice = found + 100;
                        if (slice < input.Length)
                        {
                            var toStr = GetText(input[found..slice], encoding ?? Encoding.UTF8);
                            FileResult.FoundUrls.Add(new UrlMatch
                            {
                                Value = toStr,
                                MethodName = found.ToString()
                            });

                        }

                        Buffer.BlockCopy(finalReplacement, 0, input, found, finalReplacement.Length);
                        count++;

                        // Continue after the replaced region.
                        start = found + targetLength;
                    }

                    output = input;
                    return count;
                }

                // Resize mode: file size may change.
                // Not recommended for DLLs unless you understand PE structure.
                byte[] effectiveReplacement = replacement;

                if (padShorterWithNulls && replacement.Length < targetLength)
                {
                    effectiveReplacement = new byte[targetLength];
                    Buffer.BlockCopy(replacement, 0, effectiveReplacement, 0, replacement.Length);
                }

                var list = new List<byte>(input);

                int count1 = 0;
                int index = 0;

                while (true)
                {
                    int found = Find(list, pattern, index);

                    if (found < 0)
                        break;

                    list.RemoveRange(found, pattern.Length);
                    list.InsertRange(found, effectiveReplacement);

                    count1++;

                    // Do not rescan the bytes we just inserted.
                    index = found + effectiveReplacement.Length;
                }

                output = list.ToArray();
                return count1;
            }

            private static string CreateBackup(string filePath)
            {
                string backupPath = filePath + ".bak";

                if (!File.Exists(backupPath))
                    File.Copy(filePath, backupPath);
                return backupPath;
            }

            private static byte[] GetBytes(string value, Encoding encoding)
            {
                return encoding.GetBytes(value);
            }

            private static string GetText(byte[] data, Encoding encoding)
            {
                if (encoding == Encoding.Unicode)
                    return encoding.GetString(data);

                int end = Array.IndexOf(data, (byte)0);

                if (end == -1)
                    end = data.Length;

                return encoding.GetString(data, 0, end);
            }
            private static string GetText(Span<byte> value, Encoding encoding)
            {
                return encoding.GetString(value);
            }

            private static int Find(byte[] data, byte[] pattern, int startIndex)
            {
                if (pattern.Length == 0 || data.Length < pattern.Length)
                    return -1;

                for (int i = startIndex; i <= data.Length - pattern.Length; i++)
                {
                    bool match = true;

                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (data[i + j] != pattern[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                        return i;
                }

                return -1;
            }

            private static int Find(List<byte> data, byte[] pattern, int startIndex)
            {
                if (pattern.Length == 0 || data.Count < pattern.Length)
                    return -1;

                for (int i = startIndex; i <= data.Count - pattern.Length; i++)
                {
                    bool match = true;

                    for (int j = 0; j < pattern.Length; j++)
                    {
                        if (data[i + j] != pattern[j])
                        {
                            match = false;
                            break;
                        }
                    }

                    if (match)
                        return i;
                }

                return -1;
            }

        }

    }
}