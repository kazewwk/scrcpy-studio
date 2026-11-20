using System.Diagnostics;
using System.IO.Compression;
using System.IO.Pipes;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScrcpyStudio;

internal sealed record Option(
    string Key, string Flag, string? Alias, string? Argument, bool Optional,
    string Description, bool Environment, string? Platform, string Group, string Title,
    string[] Choices, string? Placeholder)
{
    internal string ValueLabel => Optional ? $"[={Argument}]" : $"={Argument}";
    internal string Syntax => Flag + (Environment || Argument is not null ? ValueLabel : "");
}

internal static class Catalog
{
    internal static readonly Option[] Options = Read();
    internal static readonly Dictionary<string, Option> ByKey = Options.ToDictionary(o => o.Key);

    private static Option[] Read()
    {
        using Stream stream = RuntimeFiles.Resource("Options.json");
        return JsonSerializer.Deserialize<Option[]>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidDataException("参数目录为空。");
    }
}

internal static class RuntimeFiles
{
    internal static string Home => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScrcpyStudio");

    internal static Stream Resource(string name) =>
        Assembly.GetExecutingAssembly().GetManifestResourceStream($"ScrcpyStudio.{name}")
        ?? throw new InvalidDataException($"缺少内置资源 {name}。");

    internal static string Extract()
    {
        using var bundle = Resource("Backend.zip");
        string hash = Convert.ToHexString(SHA256.HashData(bundle)).ToLowerInvariant();
        string parent = Path.Combine(Home, "runtime");
        string target = Path.Combine(parent, hash);
        if (Directory.Exists(target))
            return target;

        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, $".extract-{Guid.NewGuid():N}");
        bundle.Position = 0;
        try
        {
            using var archive = new ZipArchive(bundle);
            archive.ExtractToDirectory(staging);
            try
            {
                Directory.Move(staging, target);
            }
            catch (IOException) when (Directory.Exists(target))
            {
                // Another launcher finished extracting the same bundle.
            }
        }
        finally
        {
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
        }
        return target;
    }

    internal static Dictionary<string, string?> LoadSettings()
    {
        string path = Path.Combine(Home, "settings.json");
        if (!File.Exists(path))
            return new();

        Dictionary<string, string?> values =
            JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path)) ?? new();
        return values.Where(x => Catalog.ByKey.ContainsKey(x.Key)).ToDictionary();
    }

    internal static void SaveSettings(Dictionary<string, string?> values)
    {
        Directory.CreateDirectory(Home);
        string path = Path.Combine(Home, "settings.json");
        string staging = Path.Combine(Home, $".settings-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllText(staging, JsonSerializer.Serialize(values,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(staging, path, overwrite: true);
        }
        finally
        {
            File.Delete(staging);
        }
    }
}

internal sealed class LaunchPlan
{
    internal static readonly string[] Selectors = ["serial", "select-usb", "select-tcpip", "tcpip"];
    private static readonly string[] QuerySettings =
        [.. Selectors, "port", "force-adb-forward", "tunnel-host", "tunnel-port", "verbosity"];

    internal static Dictionary<string, string?> ForQuery(
        Dictionary<string, string?> config, string query)
    {
        var result = config.Where(pair => Catalog.ByKey[pair.Key].Environment
            || (query is not ("help" or "version") && QuerySettings.Contains(pair.Key)))
            .ToDictionary();
        result[query] = null;
        return result;
    }

    internal LaunchPlan(string backend, IReadOnlyDictionary<string, string?> config)
    {
        Backend = backend;
        Executable = Path.Combine(backend, "scrcpy.exe");
        foreach (Option option in Catalog.Options)
        {
            if (!config.TryGetValue(option.Key, out string? value))
                continue;
            if (option.Environment)
                Overrides.Add(option.Key, value ?? "");
            else
                Arguments.Add(option.Argument is null || (option.Optional && value is null)
                    ? option.Flag : $"{option.Flag}={value}");
        }
    }

    private string Backend { get; }
    internal string Executable { get; }
    internal List<string> Arguments { get; } = [];
    internal Dictionary<string, string> Overrides { get; } = new();
    internal string Adb => Overrides.GetValueOrDefault("ADB") ?? Path.Combine(Backend, "adb.exe");

    internal ProcessStartInfo CreateStartInfo()
    {
        var info = StartInfo(Executable, Arguments);
        info.Environment["ADB"] = Adb;
        foreach ((string name, string value) in Overrides)
            info.Environment[name] = value;
        return info;
    }

    internal string PowerShell()
    {
        static string Quote(string value) => $"'{value.Replace("'", "''")}'";
        var env = Overrides.ToDictionary();
        if (Path.IsPathFullyQualified(Backend))
            env["ADB"] = Adb;
        string prefix = string.Concat(env.Select(x => $"$env:{x.Key} = {Quote(x.Value)}; "));
        return prefix + "& " + string.Join(" ", Arguments.Prepend(Executable).Select(Quote));
    }

    internal static ProcessStartInfo StartInfo(string exe, IEnumerable<string> args)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardErrorEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (string arg in args)
            info.ArgumentList.Add(arg);
        return info;
    }

    internal async Task<(int Exit, string Output, string Error)> RunAdbAsync(params string[] args)
    {
        using Process process = Process.Start(StartInfo(Adb, args))
            ?? throw new InvalidOperationException("无法启动 ADB。");
        process.StandardInput.Close();

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        try
        {
            await process.WaitForExitAsync(deadline.Token);
            return (process.ExitCode, (await stdout).TrimEnd(), (await stderr).TrimEnd());
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
                process.Kill();
            throw new TimeoutException("ADB 等待超时。");
        }
    }
}

internal sealed class BackendSession : IDisposable
{
    private readonly NamedPipeServerStream _pipe;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Process _process;
    private Task? _connection;
    private bool _started;

    internal BackendSession(LaunchPlan plan, Action<string> output)
    {
        string pipeName = $"scrcpy-studio-{Guid.NewGuid():N}";
        ProcessStartInfo info = plan.CreateStartInfo();
        info.Environment["SCRCPY_STUDIO_PIPE"] = $@"\\.\pipe\{pipeName}";
        _pipe = new NamedPipeServerStream(pipeName, PipeDirection.Out, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        _process = new Process { StartInfo = info };
        _process.OutputDataReceived += (_, e) => OnLine(e.Data);
        _process.ErrorDataReceived += (_, e) => OnLine(e.Data);

        void OnLine(string? line)
        {
            if (line is not null)
                output(line);
        }
    }

    internal async Task<int> RunAsync()
    {
        _connection = _pipe.WaitForConnectionAsync(_lifetime.Token);
        try
        {
            if (!_process.Start())
                throw new InvalidOperationException("无法启动 scrcpy。");

            _started = true;
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            await _process.WaitForExitAsync();
            return _process.ExitCode;
        }
        finally
        {
            await _lifetime.CancelAsync();
            try { await _connection; }
            catch (OperationCanceledException) { }
        }
    }

    internal async Task<bool> SendAsync(byte command)
    {
        if (!_started || _process.HasExited || _connection is null)
            return false;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            await _connection.WaitAsync(timeout.Token);
            await _pipe.WriteAsync(new byte[] { command }, timeout.Token);
            return true;
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException)
        {
            return false;
        }
    }

    internal void ContinueExit()
    {
        try
        {
            if (_started && !_process.HasExited)
                _process.StandardInput.WriteLine();
        }
        catch (IOException) { }
        catch (InvalidOperationException) { }
    }

    internal void ForceStop()
    {
        try
        {
            if (_started && !_process.HasExited)
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }

    public void Dispose()
    {
        _pipe.Dispose();
        _process.Dispose();
        _lifetime.Dispose();
    }
}
