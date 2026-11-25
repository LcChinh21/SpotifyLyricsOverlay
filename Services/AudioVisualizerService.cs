#pragma warning disable CS0618
using System;
using NAudio.Dsp;
using NAudio.Wave;

namespace SpotifyLyricsOverlay.Services
{
    public class AudioVisualizerService : IDisposable
    {
        private const int FftLength = 1024;
        private const int FftLog2 = 10;
        public const int BarCount = 12;

        private WasapiLoopbackCapture? _capture;
        private readonly float[] _sampleBuffer = new float[FftLength];
        private readonly Complex[] _fftBuffer = new Complex[FftLength];
        private readonly double[] _bars = new double[BarCount];
        private int _sampleIndex = 0;
        private bool _isRunning = false;
        private readonly object _lock = new();

        public event Action<double[]>? SpectrumUpdated;

        public bool IsRunning => _isRunning;

        public void Start()
        {
            lock (_lock)
            {
                if (_isRunning) return;
                try
                {
                    _capture = new WasapiLoopbackCapture();
                    _capture.DataAvailable += OnDataAvailable;
                    _capture.RecordingStopped += (s, e) => { _isRunning = false; };
                    _capture.StartRecording();
                    _isRunning = true;
                }
                catch
                {
                    _isRunning = false;
                    StopInternal();
                }
            }
        }

        public void Stop()
        {
            lock (_lock)
            {
                StopInternal();
            }
        }

        private void StopInternal()
        {
            _isRunning = false;
            if (_capture != null)
            {
                try
                {
                    _capture.DataAvailable -= OnDataAvailable;
                    _capture.StopRecording();
                    _capture.Dispose();
                }
                catch { }
                _capture = null;
            }
            Array.Clear(_bars, 0, _bars.Length);
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (!_isRunning || _capture == null || e.BytesRecorded == 0) return;

            var format = _capture.WaveFormat;
            int channels = format.Channels;
            int bytesPerSample = format.BitsPerSample / 8;
            int frameBytes = channels * bytesPerSample;
            if (frameBytes <= 0) return;

            bool isFloat = format.Encoding == WaveFormatEncoding.IeeeFloat;

            for (int offset = 0; offset + frameBytes <= e.BytesRecorded; offset += frameBytes)
            {
                float mono = 0f;
                if (isFloat && bytesPerSample == 4)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        mono += BitConverter.ToSingle(e.Buffer, offset + ch * 4);
                    }
                    mono /= channels;
                }
                else if (bytesPerSample == 2)
                {
                    for (int ch = 0; ch < channels; ch++)
                    {
                        mono += BitConverter.ToInt16(e.Buffer, offset + ch * 2) / 32768f;
                    }
                    mono /= channels;
                }

                _sampleBuffer[_sampleIndex++] = mono;
                if (_sampleIndex >= FftLength)
                {
                    _sampleIndex = 0;
                    ComputeSpectrum();
                }
            }
        }

        private void ComputeSpectrum()
        {
            for (int i = 0; i < FftLength; i++)
            {
                double hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftLength - 1)));
                _fftBuffer[i].X = (float)(_sampleBuffer[i] * hann);
                _fftBuffer[i].Y = 0;
            }

            FastFourierTransform.FFT(true, FftLog2, _fftBuffer);

            // Logarithmic bin ranges across first 240 bins (approx 40Hz - 10kHz at 44.1/48kHz)
            int[] binEdges = { 1, 2, 4, 7, 11, 17, 26, 39, 58, 86, 128, 185, 240 };
            var snapshot = new double[BarCount];

            for (int b = 0; b < BarCount; b++)
            {
                int start = binEdges[b];
                int end = binEdges[b + 1];
                double peak = 0;
                for (int k = start; k < end; k++)
                {
                    double mag = Math.Sqrt(_fftBuffer[k].X * _fftBuffer[k].X + _fftBuffer[k].Y * _fftBuffer[k].Y);
                    if (mag > peak) peak = mag;
                }

                // Scale & normalize with mild compression
                double normalized = Math.Clamp(Math.Log10(1.0 + peak * 28.0) * 1.15, 0.04, 1.0);
                double smoothFactor = normalized > _bars[b] ? 0.55 : 0.22;
                _bars[b] += (normalized - _bars[b]) * smoothFactor;
                snapshot[b] = _bars[b];
            }

            SpectrumUpdated?.Invoke(snapshot);
        }

        public void Dispose()
        {
            Stop();
            GC.SuppressFinalize(this);
        }
    }
}
