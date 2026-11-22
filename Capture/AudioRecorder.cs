using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace iRecord.Capture;

public sealed class AudioRecorder : IDisposable
{
    private readonly List<IWaveIn> _captures = new();
    private readonly List<WaveFileWriter> _writers = new();
    private readonly object _sync = new();

    public string? SystemPath { get; private set; }
    public string? MicPath { get; private set; }
    public long SystemBytes { get; private set; }
    public long MicBytes { get; private set; }
    public List<string> Errors { get; } = new();

    public bool HasAnyAudio => _writers.Count > 0;

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
            var writer = new WaveFileWriter(path, capture.WaveFormat);

            capture.DataAvailable += (_, e) =>
            {
                lock (_sync)
                {
                    try
                    {
                        writer.Write(e.Buffer, 0, e.BytesRecorded);
                        if (loopback) SystemBytes += e.BytesRecorded;
                        else MicBytes += e.BytesRecorded;
                    }
                    catch { }
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
