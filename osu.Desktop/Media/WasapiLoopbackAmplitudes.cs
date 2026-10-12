// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;
using NAudio.Dsp;
using NAudio.Wave;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;

namespace osu.Desktop.Media
{
    /// <summary>
    /// Captures the system audio output in real time (WASAPI loopback) and exposes its frequency
    /// amplitudes for visualisation purposes.
    /// </summary>
    public class WasapiLoopbackAmplitudes : IHasAmplitudes, IDisposable
    {
        private const int fft_points = 2048; // must be a power of two.
        private const int fft_power = 11; // log2(fft_points).
        private const int amplitudes_size = ChannelAmplitudes.AMPLITUDES_SIZE;

        /// <summary>
        /// Minimum number of new samples required between two FFT computations.
        /// </summary>
        private const int min_samples_between_updates = fft_points / 4;

        private readonly Complex[] fftData = new Complex[fft_points];
        private readonly float[] sampleRing = new float[fft_points];

        private int ringPosition;
        private int totalSamples;
        private int lastFftSampleCount;

        private readonly object processingLock = new object();

        /// <summary>
        /// The most recent frequency amplitude bins (immutable once published).
        /// </summary>
        private volatile float[] latestAmplitudes = new float[amplitudes_size];

        /// <summary>
        /// The peak sample amplitude observed in the most recent capture chunk (0..1).
        /// </summary>
        private volatile float latestPeak;

        private WasapiLoopbackCapture? capture;

        public ChannelAmplitudes CurrentAmplitudes => new ChannelAmplitudes(latestPeak, latestPeak, latestAmplitudes);

        /// <summary>
        /// Starts capturing the system audio output, if not already started.
        /// </summary>
        public void Start()
        {
            lock (processingLock)
            {
                if (capture != null)
                    return;

                var loopbackCapture = new WasapiLoopbackCapture();
                loopbackCapture.DataAvailable += onDataAvailable;
                loopbackCapture.StartRecording();
                capture = loopbackCapture;
            }
        }

        private void onDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                WasapiLoopbackCapture? currentCapture = capture;
                if (currentCapture == null)
                    return;

                if (currentCapture.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
                    return;

                int channels = currentCapture.WaveFormat.Channels;
                int bytesPerSample = currentCapture.WaveFormat.BitsPerSample / 8; // 4 for IEEE float.
                int usableBytes = e.BytesRecorded - e.BytesRecorded % bytesPerSample;

                lock (processingLock)
                {
                    ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, usableBytes));
                    int frames = samples.Length / channels;

                    float peak = 0;

                    for (int frame = 0; frame < frames; frame++)
                    {
                        float sum = 0;

                        for (int channel = 0; channel < channels; channel++)
                            sum += samples[frame * channels + channel];

                        float mono = sum / channels;

                        sampleRing[ringPosition] = mono;
                        ringPosition = (ringPosition + 1) % fft_points;
                        totalSamples++;

                        float absolute = Math.Abs(mono);
                        if (absolute > peak)
                            peak = absolute;
                    }

                    latestPeak = peak;

                    if (totalSamples - lastFftSampleCount >= min_samples_between_updates)
                    {
                        lastFftSampleCount = totalSamples;
                        recomputeAmplitudes();
                    }
                }
            }
            catch
            {
                // never let capture failures propagate into the audio thread.
            }
        }

        private void recomputeAmplitudes()
        {
            for (int i = 0; i < fft_points; i++)
            {
                int sampleIndex = (ringPosition - fft_points + i + fft_points) % fft_points;
                var complex = fftData[i];
                complex.X = sampleRing[sampleIndex] * (float)FastFourierTransform.HannWindow(i, fft_points);
                complex.Y = 0;
                fftData[i] = complex;
            }

            FastFourierTransform.FFT(true, fft_power, fftData);

            float[] bins = new float[amplitudes_size];
            int usefulBins = fft_points / 2;

            for (int i = 0; i < amplitudes_size; i++)
            {
                int start = i * usefulBins / amplitudes_size;
                int end = Math.Max((i + 1) * usefulBins / amplitudes_size, start + 1);

                float sum = 0;

                for (int j = start; j < end; j++)
                {
                    float real = fftData[j].X;
                    float imaginary = fftData[j].Y;
                    sum += MathF.Sqrt(real * real + imaginary * imaginary);
                }

                // a full-scale sine lands near 1.0 after this normalisation (Hann window halves the peak magnitude).
                float amplitude = sum / (end - start) / (fft_points / 2f) * 2;

                bins[i] = Math.Clamp(amplitude, 0, 1);
            }

            latestAmplitudes = bins;
        }

        public void Dispose()
        {
            lock (processingLock)
            {
                capture?.StopRecording();
                capture?.Dispose();
                capture = null;
            }
        }
    }
}
