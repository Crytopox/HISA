using System.ComponentModel;
using System.Diagnostics;
using System.Media;
using Microsoft.Extensions.Logging;

namespace Hisa.App.Services;

internal static class AlertSoundPlayer
{
    public static IReadOnlyList<string> GetAvailableSoundFiles()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddFromDirectory(GetShippedSoundsDirectory(), names);
        AddFromDirectory(GetUserSoundsDirectory(), names);
        if (names.Count == 0)
        {
            names.Add("default-alert.wav");
        }

        return names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static void Play(string? configuredSound, double volume)
    {
        try
        {
            var resolvedPath = ResolveAlertSoundPath(configuredSound);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            {
                var clampedVolume = Math.Clamp(volume, 0.0, 1.0);
                if (OperatingSystem.IsWindows())
                {
                    var waveBytes = File.ReadAllBytes(resolvedPath);
                    var adjusted = TryApplyVolumeToWavPcm(waveBytes, clampedVolume) ?? waveBytes;
                    using var memory = new MemoryStream(adjusted, writable: false);
                    using var player = new SoundPlayer(memory);
                    player.Play();
                    return;
                }

                _ = Task.Run(() => PlayViaExternalPlayerAsync(resolvedPath, clampedVolume));
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                Console.Beep(1100, 120);
            }

            LogWarning("Alert sound file was not found for playback: {ConfiguredSound}", configuredSound ?? "<null>");
        }
        catch (Exception ex)
        {
            LogWarning(ex, "Alert sound playback failed for {ConfiguredSound}", configuredSound ?? "<null>");
        }
    }

    public static string? ResolveAlertSoundPath(string? configuredSound)
    {
        var sound = (configuredSound ?? string.Empty).Trim();
        if (sound.Length == 0)
        {
            sound = "default-alert.wav";
        }

        if (Path.IsPathRooted(sound))
        {
            return sound;
        }

        var userSoundPath = Path.Combine(GetUserSoundsDirectory(), sound);
        if (File.Exists(userSoundPath))
        {
            return userSoundPath;
        }

        var shippedSoundPath = Path.Combine(GetShippedSoundsDirectory(), sound);
        if (File.Exists(shippedSoundPath))
        {
            return shippedSoundPath;
        }

        return null;
    }

    private static string GetUserSoundsDirectory()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HISA",
            "AlertSounds");
    }

    private static string GetShippedSoundsDirectory()
    {
        return Path.Combine(AppContext.BaseDirectory, "Assets", "Sounds");
    }

    private static void AddFromDirectory(string directory, ISet<string> names)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(directory, "*.wav"))
        {
            names.Add(Path.GetFileName(file));
        }
    }

    private static async Task PlayViaExternalPlayerAsync(string resolvedPath, double volume)
    {
        string? playbackPath = null;
        try
        {
            playbackPath = await PreparePlaybackFileAsync(resolvedPath, volume);
            foreach (var candidate in GetExternalPlayerCandidates(playbackPath))
            {
                try
                {
                    using var process = new Process
                    {
                        StartInfo = candidate
                    };

                    if (!process.Start())
                    {
                        continue;
                    }

                    await process.WaitForExitAsync();
                    if (process.ExitCode == 0)
                    {
                        return;
                    }

                    LogWarning(
                        "Alert sound player {Player} exited with code {ExitCode} for {SoundPath}",
                        candidate.FileName,
                        process.ExitCode,
                        resolvedPath);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
                {
                    LogDebug(ex, "Alert sound player {Player} is unavailable.", candidate.FileName);
                }
            }

            LogWarning("No usable external audio player was found for {SoundPath}", resolvedPath);
        }
        catch (Exception ex)
        {
            LogWarning(ex, "External alert sound playback failed for {SoundPath}", resolvedPath);
        }
        finally
        {
            if (playbackPath is not null &&
                !string.Equals(playbackPath, resolvedPath, StringComparison.Ordinal) &&
                File.Exists(playbackPath))
            {
                try
                {
                    File.Delete(playbackPath);
                }
                catch (Exception ex)
                {
                    LogDebug(ex, "Failed to delete temporary alert sound file {TempPath}", playbackPath);
                }
            }
        }
    }

    private static async Task<string> PreparePlaybackFileAsync(string resolvedPath, double volume)
    {
        if (Math.Abs(volume - 1.0) < 0.0001)
        {
            return resolvedPath;
        }

        var waveBytes = await File.ReadAllBytesAsync(resolvedPath);
        var adjusted = TryApplyVolumeToWavPcm(waveBytes, volume);
        if (adjusted is null)
        {
            return resolvedPath;
        }

        var tempPath = Path.Combine(Path.GetTempPath(), $"hisa-alert-{Guid.NewGuid():N}.wav");
        await File.WriteAllBytesAsync(tempPath, adjusted);
        return tempPath;
    }

    private static IEnumerable<ProcessStartInfo> GetExternalPlayerCandidates(string soundPath)
    {
        if (OperatingSystem.IsLinux())
        {
            yield return CreateProcessStartInfo("pw-play", soundPath);
            yield return CreateProcessStartInfo("paplay", soundPath);
            yield return CreateProcessStartInfo("aplay", soundPath);
            yield return CreateProcessStartInfo("ffplay", "-nodisp", "-autoexit", "-loglevel", "quiet", soundPath);
            yield return CreateProcessStartInfo("canberra-gtk-play", "-f", soundPath);
            yield break;
        }

        if (OperatingSystem.IsMacOS())
        {
            yield return CreateProcessStartInfo("afplay", soundPath);
        }
    }

    private static ProcessStartInfo CreateProcessStartInfo(string fileName, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static byte[]? TryApplyVolumeToWavPcm(byte[] wavBytes, double volume)
    {
        if (wavBytes.Length < 44)
        {
            return null;
        }

        if (wavBytes[0] != 'R' || wavBytes[1] != 'I' || wavBytes[2] != 'F' || wavBytes[3] != 'F')
        {
            return null;
        }

        if (wavBytes[8] != 'W' || wavBytes[9] != 'A' || wavBytes[10] != 'V' || wavBytes[11] != 'E')
        {
            return null;
        }

        var fmtOffset = FindChunk(wavBytes, "fmt ");
        var dataOffset = FindChunk(wavBytes, "data");
        if (fmtOffset < 0 || dataOffset < 0)
        {
            return null;
        }

        var audioFormat = ReadUInt16(wavBytes, fmtOffset + 8);
        var channels = ReadUInt16(wavBytes, fmtOffset + 10);
        var bitsPerSample = ReadUInt16(wavBytes, fmtOffset + 22);
        if (channels <= 0)
        {
            return null;
        }

        var dataSize = ReadInt32(wavBytes, dataOffset + 4);
        var dataStart = dataOffset + 8;
        if (dataStart < 0 || dataStart + dataSize > wavBytes.Length)
        {
            return null;
        }

        var output = (byte[])wavBytes.Clone();
        var normalizeGain = ComputeNormalizationGain(output, dataStart, dataSize, audioFormat, bitsPerSample);
        var gain = Math.Clamp(volume, 0.0, 1.0) * normalizeGain;

        if (audioFormat == 1 && bitsPerSample == 8)
        {
            for (var i = dataStart; i < dataStart + dataSize; i++)
            {
                var centered = output[i] - 128;
                var scaled = (int)Math.Round(centered * gain);
                scaled = Math.Clamp(scaled, -128, 127);
                output[i] = (byte)(scaled + 128);
            }

            return output;
        }

        if (audioFormat == 1 && bitsPerSample == 16)
        {
            for (var i = dataStart; i + 1 < dataStart + dataSize; i += 2)
            {
                var sample = BitConverter.ToInt16(output, i);
                var scaled = (int)Math.Round(sample * gain);
                scaled = Math.Clamp(scaled, short.MinValue, short.MaxValue);
                var packed = BitConverter.GetBytes((short)scaled);
                output[i] = packed[0];
                output[i + 1] = packed[1];
            }

            return output;
        }

        if (audioFormat == 1 && bitsPerSample == 24)
        {
            for (var i = dataStart; i + 2 < dataStart + dataSize; i += 3)
            {
                var sample = ReadInt24(output, i);
                var scaled = (int)Math.Round(sample * gain);
                scaled = Math.Clamp(scaled, -8_388_608, 8_388_607);
                WriteInt24(output, i, scaled);
            }

            return output;
        }

        if (audioFormat == 1 && bitsPerSample == 32)
        {
            for (var i = dataStart; i + 3 < dataStart + dataSize; i += 4)
            {
                var sample = BitConverter.ToInt32(output, i);
                var scaled = (long)Math.Round(sample * gain);
                scaled = Math.Clamp(scaled, int.MinValue, int.MaxValue);
                var packed = BitConverter.GetBytes((int)scaled);
                Buffer.BlockCopy(packed, 0, output, i, 4);
            }

            return output;
        }

        // IEEE float WAV
        if (audioFormat == 3 && bitsPerSample == 32)
        {
            for (var i = dataStart; i + 3 < dataStart + dataSize; i += 4)
            {
                var sample = BitConverter.ToSingle(output, i);
                var scaled = (float)Math.Clamp(sample * gain, -1.0, 1.0);
                var packed = BitConverter.GetBytes(scaled);
                Buffer.BlockCopy(packed, 0, output, i, 4);
            }

            return output;
        }

        return null;
    }

    private static int FindChunk(byte[] bytes, string chunkId)
    {
        for (var i = 12; i + 8 <= bytes.Length;)
        {
            var id = new string(new[] { (char)bytes[i], (char)bytes[i + 1], (char)bytes[i + 2], (char)bytes[i + 3] });
            var size = ReadInt32(bytes, i + 4);
            if (id == chunkId)
            {
                return i;
            }

            var padded = Math.Max(0, size);
            if ((padded & 1) == 1)
            {
                padded++;
            }

            i += 8 + padded;
        }

        return -1;
    }

    private static ushort ReadUInt16(byte[] bytes, int offset) => BitConverter.ToUInt16(bytes, offset);
    private static int ReadInt32(byte[] bytes, int offset) => BitConverter.ToInt32(bytes, offset);

    private static int ReadInt24(byte[] bytes, int offset)
    {
        var value = bytes[offset] | (bytes[offset + 1] << 8) | (bytes[offset + 2] << 16);
        if ((value & 0x800000) != 0)
        {
            value |= unchecked((int)0xFF000000);
        }

        return value;
    }

    private static void WriteInt24(byte[] bytes, int offset, int value)
    {
        bytes[offset] = (byte)(value & 0xFF);
        bytes[offset + 1] = (byte)((value >> 8) & 0xFF);
        bytes[offset + 2] = (byte)((value >> 16) & 0xFF);
    }

    private static double ComputeNormalizationGain(byte[] bytes, int dataStart, int dataSize, ushort audioFormat, ushort bitsPerSample)
    {
        const double targetPeak = 0.95;
        double peak = 0.0;

        if (audioFormat == 1 && bitsPerSample == 8)
        {
            for (var i = dataStart; i < dataStart + dataSize; i++)
            {
                peak = Math.Max(peak, Math.Abs((bytes[i] - 128) / 128.0));
            }
        }
        else if (audioFormat == 1 && bitsPerSample == 16)
        {
            for (var i = dataStart; i + 1 < dataStart + dataSize; i += 2)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(bytes, i) / 32768.0));
            }
        }
        else if (audioFormat == 1 && bitsPerSample == 24)
        {
            for (var i = dataStart; i + 2 < dataStart + dataSize; i += 3)
            {
                peak = Math.Max(peak, Math.Abs(ReadInt24(bytes, i) / 8388608.0));
            }
        }
        else if (audioFormat == 1 && bitsPerSample == 32)
        {
            for (var i = dataStart; i + 3 < dataStart + dataSize; i += 4)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToInt32(bytes, i) / 2147483648.0));
            }
        }
        else if (audioFormat == 3 && bitsPerSample == 32)
        {
            for (var i = dataStart; i + 3 < dataStart + dataSize; i += 4)
            {
                peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(bytes, i)));
            }
        }

        if (peak <= 0.000001)
        {
            return 1.0;
        }

        var gain = targetPeak / peak;
        return Math.Clamp(gain, 1.0, 4.0);
    }

    private static ILogger? GetLogger()
    {
        var loggerFactory = Program.Host?.Services.GetService(typeof(ILoggerFactory)) as ILoggerFactory;
        return loggerFactory?.CreateLogger("Hisa.App.AlertSoundPlayer");
    }

    private static void LogWarning(string message, params object?[] args)
    {
        var logger = GetLogger();
        if (logger is not null)
        {
            logger.LogWarning(message, args);
            return;
        }

        Trace.TraceWarning(FormatMessage(message, args));
    }

    private static void LogWarning(Exception ex, string message, params object?[] args)
    {
        var logger = GetLogger();
        if (logger is not null)
        {
            logger.LogWarning(ex, message, args);
            return;
        }

        Trace.TraceWarning($"{FormatMessage(message, args)} {ex}");
    }

    private static void LogDebug(Exception ex, string message, params object?[] args)
    {
        var logger = GetLogger();
        if (logger is not null)
        {
            logger.LogDebug(ex, message, args);
            return;
        }

        Trace.TraceInformation($"{FormatMessage(message, args)} {ex.Message}");
    }

    private static string FormatMessage(string message, params object?[] args)
    {
        return args.Length == 0
            ? message
            : $"{message} | {string.Join(", ", args.Select(a => a?.ToString() ?? "<null>"))}";
    }
}
