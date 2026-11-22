using System.Buffers.Binary;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace iRecord.Capture;

public sealed class AudioRecorder : IDisposable
{
    private static readonly Guid IeeeFloatSubFormat = new("00000003-0000-0010-8000-00aa00389b71");

    private readonly List<IWaveIn> _captures = new();
    private readonly List<WaveFileWriter> _writers = new();
    private readonly object _sync = new();

    private float _micGain = 1f;
    private float _systemLevel;
    private float _micLevel;

    public string? SystemPath { get; private set; }
    public string? MicPath { get; private set; }
    public long SystemBytes { get; private set; }
    public long MicBytes { get; private set; }
    public List<string> Errors { get; } = new();

    public bool HasAnyAudio => _writers.Count > 0;

    public float SystemLevel => _systemLevel;
    public float MicLevel => _micLevel;

    public void SetMicGain(float gain) => _micGain = Math.Clamp(gain, 0f, 2f);

    public void Start(string directory, string mode)
    {
        Directory.CreateDirectory(directory);

        if (mode is "system" or "both")
            StartOne(true, Path.Combine(directory, "system.wav"));

        if (mode is "mic" or "both")
            StartOne(false, Path.Combine(directory, "mic.wav"));
    }

    private void StartOne(bool loopback, string path)
    {
        try
        {
            IWaveIn capture = loopback ? new WasapiLoopbackCapture() : new WasapiCapture();
            var format = capture.WaveFormat;
            var writer = new WaveFileWriter(path, format);

            capture.DataAvailable += (_, e) =>
            {
                lock (_sync)
                {
                    try
                    {
                        bool applyGain = !loopback && Math.Abs(_micGain - 1f) > 0.001f;
                        if (applyGain)
                            ApplyGain(e.Buffer, e.BytesRecorded, format, _micGain);

                        float peak = ComputePeak(e.Buffer, e.BytesRecorded, format);
                        if (loopback)
                        {
                            _systemLevel = Math.Max(peak, _systemLevel * 0.75f);
                            SystemBytes += e.BytesRecorded;
                        }
                        else
                        {
                            _micLevel = Math.Max(peak, _micLevel * 0.75f);
                            MicBytes += e.BytesRecorded;
                        }

                        writer.Write(e.Buffer, 0, e.BytesRecorded);
                    }
                    catch
                    {
                    }
                }
            };

            capture.StartRecording();
            _captures.Add(capture);
            _writers.Add(writer);

            if (loopback) SystemPath = path;
            else MicPath = path;
        }
        catch (Exception ex)
        {
            Errors.Add(ex.Message);
        }
    }

    public void Pause()
    {
        foreach (var capture in _captures)
        {
            try { capture.StopRecording(); }
            catch { }
        }
    }

    public void Resume()
    {
        foreach (var capture in _captures)
        {
            try { capture.StartRecording(); }
            catch { }
        }
    }

    public void Stop()
    {
        foreach (var capture in _captures)
        {
            try { capture.StopRecording(); }
            catch { }
        }
    }

    private static bool IsIeeeFloat(WaveFormat format)
    {
        if (format.Encoding == WaveFormatEncoding.IeeeFloat)
            return true;

        return format is WaveFormatExtensible extensible && extensible.SubFormat == IeeeFloatSubFormat;
    }

    private static float ComputePeak(byte[] buffer, int bytes, WaveFormat format)
    {
        float peak = 0f;
        int bits = format.BitsPerSample;

        if (IsIeeeFloat(format) && bits == 32)
        {
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(i, 4));
                float abs = Math.Abs(value);
                if (abs > peak) peak = abs;
            }

            return Math.Min(1f, peak);
        }

        if (bits == 16)
        {
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short value = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2));
                float abs = Math.Abs(value / 32768f);
                if (abs > peak) peak = abs;
            }
        }
        else if (bits == 24)
        {
            for (int i = 0; i + 2 < bytes; i += 3)
            {
                int value = buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16);
                if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                float abs = Math.Abs(value / 8388608f);
                if (abs > peak) peak = abs;
            }
        }
        else if (bits == 32)
        {
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i, 4));
                float abs = Math.Abs(value / 2147483648f);
                if (abs > peak) peak = abs;
            }
        }
        else if (bits == 8)
        {
            for (int i = 0; i < bytes; i++)
            {
                float abs = Math.Abs((buffer[i] - 128) / 128f);
                if (abs > peak) peak = abs;
            }
        }

        return Math.Min(1f, peak);
    }

    private static void ApplyGain(byte[] buffer, int bytes, WaveFormat format, float gain)
    {
        int bits = format.BitsPerSample;

        if (IsIeeeFloat(format) && bits == 32)
        {
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                float value = BinaryPrimitives.ReadSingleLittleEndian(buffer.AsSpan(i, 4));
                value = Math.Clamp(value * gain, -1f, 1f);
                BinaryPrimitives.WriteSingleLittleEndian(buffer.AsSpan(i, 4), value);
            }

            return;
        }

        if (bits == 16)
        {
            for (int i = 0; i + 1 < bytes; i += 2)
            {
                short value = BinaryPrimitives.ReadInt16LittleEndian(buffer.AsSpan(i, 2));
                int scaled = (int)Math.Clamp(value * gain, (float)short.MinValue, (float)short.MaxValue);
                BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(i, 2), (short)scaled);
            }
        }
        else if (bits == 24)
        {
            for (int i = 0; i + 2 < bytes; i += 3)
            {
                int value = buffer[i] | (buffer[i + 1] << 8) | (buffer[i + 2] << 16);
                if ((value & 0x800000) != 0) value |= unchecked((int)0xFF000000);
                int scaled = (int)Math.Clamp((double)value * gain, -8388608d, 8388607d);
                buffer[i] = (byte)(scaled & 0xFF);
                buffer[i + 1] = (byte)((scaled >> 8) & 0xFF);
                buffer[i + 2] = (byte)((scaled >> 16) & 0xFF);
            }
        }
        else if (bits == 32)
        {
            for (int i = 0; i + 3 < bytes; i += 4)
            {
                int value = BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(i, 4));
                int scaled = (int)Math.Clamp((double)value * gain, int.MinValue, int.MaxValue);
                BinaryPrimitives.WriteInt32LittleEndian(buffer.AsSpan(i, 4), scaled);
            }
        }
        else if (bits == 8)
        {
            for (int i = 0; i < bytes; i++)
            {
                int value = buffer[i] - 128;
                int scaled = (int)Math.Clamp(value * gain, -128f, 127f) + 128;
                buffer[i] = (byte)scaled;
            }
        }
    }

    public void Dispose()
    {
        foreach (var capture in _captures)
        {
            try { capture.StopRecording(); }
            catch { }

            try { capture.Dispose(); }
            catch { }
        }

        lock (_sync)
        {
            foreach (var writer in _writers)
            {
                try { writer.Flush(); }
                catch { }
                try { writer.Dispose(); }
                catch { }
            }
            _writers.Clear();
        }

        _captures.Clear();
    }
}
