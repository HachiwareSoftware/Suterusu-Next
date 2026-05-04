using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Suterusu.Services
{
    public sealed class OneOcrRuntimeResolution
    {
        private OneOcrRuntimeResolution(bool success, string runtimePath, string error)
        {
            Success = success;
            RuntimePath = runtimePath;
            Error = error;
        }

        public bool Success { get; }

        public string RuntimePath { get; }

        public string Error { get; }

        public static OneOcrRuntimeResolution Ok(string runtimePath)
            => new OneOcrRuntimeResolution(true, runtimePath, null);

        public static OneOcrRuntimeResolution Fail(string error)
            => new OneOcrRuntimeResolution(false, null, error);
    }

    public static class OneOcrRuntimeLocator
    {
        public const string OneOcrDllName = "oneocr.dll";
        public const string OneOcrModelName = "oneocr.onemodel";
        public const string OnnxRuntimeDllName = "onnxruntime.dll";

        private static readonly Regex ScreenSketchFolderPattern = new Regex(
            @"^Microsoft\.ScreenSketch_(?<version>[^_]+)_x64__8wekyb3d8bbwe$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static OneOcrRuntimeResolution Resolve(string configuredPath)
            => Resolve(configuredPath, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), prepareLocalCopy: true);

        public static OneOcrRuntimeResolution Resolve(string configuredPath, string programFilesPath)
            => Resolve(configuredPath, programFilesPath, prepareLocalCopy: false);

        private static OneOcrRuntimeResolution Resolve(string configuredPath, string programFilesPath, bool prepareLocalCopy)
        {
            if (!string.IsNullOrWhiteSpace(configuredPath))
                return PrepareIfNeeded(ValidateRuntimePath(configuredPath.Trim(), explicitPath: true), prepareLocalCopy);

            if (prepareLocalCopy)
            {
                var appLocal = ValidateRuntimePath(GetAppLocalRuntimePath(), explicitPath: false);
                if (appLocal.Success)
                    return appLocal;
            }

            string windowsAppsPath = Path.Combine(programFilesPath ?? string.Empty, "WindowsApps");
            if (!Directory.Exists(windowsAppsPath))
                return OneOcrRuntimeResolution.Fail("Snipping Tool OneOCR runtime not found. Set the OneOCR runtime folder manually.");

            try
            {
                var candidates = Directory.EnumerateDirectories(windowsAppsPath, "Microsoft.ScreenSketch_*_x64__8wekyb3d8bbwe")
                    .Select(CreateCandidate)
                    .Where(c => c != null)
                    .OrderByDescending(c => c.Version)
                    .ToList();

                foreach (var candidate in candidates)
                {
                    string runtimePath = Path.Combine(candidate.PackagePath, "SnippingTool");
                    var validation = ValidateRuntimePath(runtimePath, explicitPath: false);
                    if (validation.Success)
                        return PrepareIfNeeded(validation, prepareLocalCopy);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                OneOcrRuntimeResolution appxResolution = ResolveFromAppxPackage();
                if (appxResolution.Success)
                    return PrepareIfNeeded(appxResolution, prepareLocalCopy);

                return OneOcrRuntimeResolution.Fail("Cannot inspect WindowsApps for Snipping Tool OneOCR runtime: " + ex.Message);
            }
            catch (IOException ex)
            {
                return OneOcrRuntimeResolution.Fail("Cannot inspect WindowsApps for Snipping Tool OneOCR runtime: " + ex.Message);
            }

            return OneOcrRuntimeResolution.Fail(
                "Snipping Tool OneOCR runtime not found. Install or update Snipping Tool, or set the folder that contains oneocr.dll, oneocr.onemodel, and onnxruntime.dll.");
        }

        private static OneOcrRuntimeResolution ResolveFromAppxPackage()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"(Get-AppxPackage Microsoft.ScreenSketch).InstallLocation\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(startInfo))
                {
                    if (process == null)
                        return OneOcrRuntimeResolution.Fail("Failed to query Snipping Tool package location.");

                    string stdout = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    string installLocation = (stdout ?? string.Empty)
                        .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .FirstOrDefault()
                        ?.Trim();

                    if (string.IsNullOrWhiteSpace(installLocation))
                        return OneOcrRuntimeResolution.Fail("Snipping Tool package location not found.");

                    return ValidateRuntimePath(Path.Combine(installLocation, "SnippingTool"), explicitPath: false);
                }
            }
            catch (Exception ex)
            {
                return OneOcrRuntimeResolution.Fail("Failed to query Snipping Tool package location: " + ex.Message);
            }
        }

        private static OneOcrRuntimeResolution PrepareIfNeeded(OneOcrRuntimeResolution resolution, bool prepareLocalCopy)
        {
            if (!prepareLocalCopy || !resolution.Success)
                return resolution;

            if (!IsWindowsAppsPath(resolution.RuntimePath))
                return resolution;

            return CopyRuntimeToAppLocal(resolution.RuntimePath);
        }

        private static bool IsWindowsAppsPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.IndexOf("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static OneOcrRuntimeResolution CopyRuntimeToAppLocal(string sourceRuntimePath)
        {
            try
            {
                string destination = GetAppLocalRuntimePath();
                Directory.CreateDirectory(destination);

                foreach (string file in new[] { OneOcrDllName, OneOcrModelName, OnnxRuntimeDllName })
                {
                    string source = Path.Combine(sourceRuntimePath, file);
                    string target = Path.Combine(destination, file);

                    if (!File.Exists(target) || new FileInfo(source).Length != new FileInfo(target).Length)
                        File.Copy(source, target, overwrite: true);
                }

                return ValidateRuntimePath(destination, explicitPath: false);
            }
            catch (Exception ex)
            {
                return OneOcrRuntimeResolution.Fail("Failed to prepare app-local OneOCR runtime copy: " + ex.Message);
            }
        }

        private static string GetAppLocalRuntimePath()
        {
            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "OneOCR");
        }

        public static OneOcrRuntimeResolution ValidateRuntimePath(string runtimePath, bool explicitPath)
        {
            if (string.IsNullOrWhiteSpace(runtimePath))
                return OneOcrRuntimeResolution.Fail("OneOCR runtime folder is empty.");

            if (!Directory.Exists(runtimePath))
            {
                return OneOcrRuntimeResolution.Fail(explicitPath
                    ? "Configured OneOCR runtime folder does not exist: " + runtimePath
                    : "OneOCR runtime folder does not exist: " + runtimePath);
            }

            string missing = string.Join(", ", new[]
                {
                    OneOcrDllName,
                    OneOcrModelName,
                    OnnxRuntimeDllName
                }
                .Where(file => !File.Exists(Path.Combine(runtimePath, file))));

            if (!string.IsNullOrWhiteSpace(missing))
                return OneOcrRuntimeResolution.Fail("OneOCR runtime folder is missing required file(s): " + missing);

            return OneOcrRuntimeResolution.Ok(runtimePath);
        }

        private static Candidate CreateCandidate(string packagePath)
        {
            string name = Path.GetFileName(packagePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            Match match = ScreenSketchFolderPattern.Match(name ?? string.Empty);
            if (!match.Success)
                return null;

            Version version;
            if (!Version.TryParse(match.Groups["version"].Value, out version))
                version = new Version(0, 0);

            return new Candidate(packagePath, version);
        }

        private sealed class Candidate
        {
            public Candidate(string packagePath, Version version)
            {
                PackagePath = packagePath;
                Version = version;
            }

            public string PackagePath { get; }

            public Version Version { get; }
        }
    }
}
