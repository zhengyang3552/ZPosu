// Copyright (c) ppy Pty Ltd <contact@ppy.sh>. Licensed under the MIT Licence.
// See the LICENCE file in the repository root for full licence text.

using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Logging;

namespace osu.Desktop.Media
{
    /// <summary>
    /// Captures the system audio output in real time (WASAPI loopback) and exposes its frequency
    /// amplitudes for visualisation purposes.
    /// </summary>
    public class WasapiLoopbackAmplitudes : IHasAmplitudes, IDisposable
    {
        private const int fft_points = 2048; // must be a power of two.
        private const int amplitudes_size = ChannelAmplitudes.AMPLITUDES_SIZE;

        /// <summary>
        /// Minimum number of new samples required between two FFT computations.
        /// </summary>
        private const int min_samples_between_updates = fft_points / 4;

        private readonly float[] fftReal = new float[fft_points];
        private readonly float[] fftImaginary = new float[fft_points];
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

        /// <summary>
        /// The capture format normalised to a plain PCM / IEEE float format (the device mix format is
        /// usually <see cref="WaveFormatExtensible"/>, which needs unpacking before samples can be read).
        /// </summary>
        private WaveFormat? captureFormat;

        private float[] monoScratch = new float[16384];

        private bool reportedUnsupportedFormat;
        private bool reportedFirstData;
        private bool reportedError;

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

                // the format must be resolved before the capture is marked ready, as it is read from the capture thread.
                captureFormat = loopbackCapture.WaveFormat.AsStandardWaveFormat();
                capture = loopbackCapture;

                loopbackCapture.StartRecording();

                Logger.Log($"System audio capture started on \"{describeDevice()}\" ({captureFormat})", LoggingTarget.Runtime, LogLevel.Important);
            }
        }

        private static string describeDevice()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                return device.FriendlyName;
            }
            catch
            {
                return "unknown device";
            }
        }

        private void onDataAvailable(object? sender, WaveInEventArgs e)
        {
            try
            {
                WaveFormat? format = captureFormat;

                if (capture == null || format == null)
                    return;

                int channels = format.Channels;
                int bytesPerSample = format.BitsPerSample / 8;

                if (channels <= 0 || bytesPerSample <= 0)
                    return;

                int usableBytes = e.BytesRecorded - e.BytesRecorded % (bytesPerSample * channels);
                int frames = usableBytes / bytesPerSample / channels;

                if (frames == 0)
                    return;

                if (monoScratch.Length < frames)
                    monoScratch = new float[Math.Max(frames, monoScratch.Length * 2)];

                if (!tryReadMono(e.Buffer.AsSpan(0, usableBytes), format, channels, frames, monoScratch))
                    return;

                lock (processingLock)
                    pushMono(monoScratch.AsSpan(0, frames), frames);
            }
            catch (Exception exception)
            {
                // never let capture failures propagate into the audio thread, but surface them once for diagnosis.
                if (!reportedError)
                {
                    reportedError = true;
                    Logger.Log($"System audio capture failed while processing audio: {exception}", LoggingTarget.Runtime, LogLevel.Important);
                }
            }
        }

        /// <summary>
        /// Decodes and downmixes the raw capture buffer into <paramref name="destination"/> as mono floats (-1..1).
        /// </summary>
        private bool tryReadMono(ReadOnlySpan<byte> data, WaveFormat format, int channels, int frames, float[] destination)
        {
            // the mix format is often multichannel (e.g. 7.1) while the actual content lives in the front stereo pair,
            // so only the first two channels are mixed down (averaging all of them would attenuate the signal heavily).
            int usedChannels = Math.Min(channels, 2);

            switch (format.Encoding)
            {
                case WaveFormatEncoding.IeeeFloat when format.BitsPerSample == 32:
                {
                    ReadOnlySpan<float> samples = MemoryMarshal.Cast<byte, float>(data);

                    for (int frame = 0; frame < frames; frame++)
                    {
                        float sum = 0;

                        for (int channel = 0; channel < usedChannels; channel++)
                            sum += samples[frame * channels + channel];

                        destination[frame] = sum / usedChannels;
                    }

                    return true;
                }

                case WaveFormatEncoding.Pcm when format.BitsPerSample == 16:
                {
                    ReadOnlySpan<short> samples = MemoryMarshal.Cast<byte, short>(data);

                    for (int frame = 0; frame < frames; frame++)
                    {
                        float sum = 0;

                        for (int channel = 0; channel < usedChannels; channel++)
                            sum += samples[frame * channels + channel] / 32768f;

                        destination[frame] = sum / usedChannels;
                    }

                    return true;
                }

                case WaveFormatEncoding.Pcm when format.BitsPerSample == 24:
                {
                    for (int frame = 0; frame < frames; frame++)
                    {
                        float sum = 0;

                        for (int channel = 0; channel < usedChannels; channel++)
                        {
                            int offset = (frame * channels + channel) * 3;
                            int value = data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16);

                            // sign-extend the 24-bit sample.
                            if ((data[offset + 2] & 0x80) != 0)
                                value |= unchecked((int)0xFF000000);

                            sum += value / 8388608f;
                        }

                        destination[frame] = sum / usedChannels;
                    }

                    return true;
                }

                case WaveFormatEncoding.Pcm when format.BitsPerSample == 32:
                {
                    ReadOnlySpan<int> samples = MemoryMarshal.Cast<byte, int>(data);

                    for (int frame = 0; frame < frames; frame++)
                    {
                        float sum = 0;

                        for (int channel = 0; channel < usedChannels; channel++)
                            sum += samples[frame * channels + channel] / 2147483648f;

                        destination[frame] = sum / usedChannels;
                    }

                    return true;
                }

                default:
                {
                    if (!reportedUnsupportedFormat)
                    {
                        reportedUnsupportedFormat = true;
                        Logger.Log($"Unsupported system audio capture format ({format}); audio amplitudes will not be available.", LoggingTarget.Runtime, LogLevel.Important);
                    }

                    return false;
                }
            }
        }

        private void pushMono(ReadOnlySpan<float> mono, int count)
        {
            float peak = 0;

            for (int i = 0; i < count; i++)
            {
                float sample = mono[i];

                sampleRing[ringPosition] = sample;
                ringPosition = (ringPosition + 1) % fft_points;
                totalSamples++;

                float absolute = Math.Abs(sample);
                if (absolute > peak)
                    peak = absolute;
            }

            latestPeak = peak;

            if (!reportedFirstData && peak > 0.001f)
            {
                reportedFirstData = true;
                Logger.Log("System audio capture is receiving audio data.", LoggingTarget.Runtime, LogLevel.Important);
            }

            if (totalSamples - lastFftSampleCount >= min_samples_between_updates)
            {
                lastFftSampleCount = totalSamples;
                recomputeAmplitudes();
            }
        }

        private void recomputeAmplitudes()
        {
            for (int i = 0; i < fft_points; i++)
            {
                int sampleIndex = (ringPosition + i) % fft_points;
                fftReal[i] = sampleRing[sampleIndex] * hannWindow(i, fft_points);
                fftImaginary[i] = 0;
            }

            fft(fftReal, fftImaginary);

            float[] bins = new float[amplitudes_size];
            int usefulBins = fft_points / 2;

            for (int i = 0; i < amplitudes_size; i++)
            {
                int start = i * usefulBins / amplitudes_size;
                int end = Math.Max((i + 1) * usefulBins / amplitudes_size, start + 1);

                float sum = 0;

                for (int j = start; j < end; j++)
                {
                    float real = fftReal[j];
                    float imaginary = fftImaginary[j];
                    sum += MathF.Sqrt(real * real + imaginary * imaginary);
                }

                // a full-scale sine lands near 1.0 after this normalisation.
                float amplitude = sum / (end - start) / (fft_points / 2f) * 4;

                bins[i] = Math.Clamp(amplitude, 0, 1);
            }

            latestAmplitudes = bins;
        }

        private static float hannWindow(int index, int size) => (float)(0.5 * (1 - Math.Cos(2 * Math.PI * index / (size - 1))));

        /// <summary>
        /// Computes an in-place iterative radix-2 FFT. The length of the data must be a power of two.
        /// </summary>
        private static void fft(float[] real, float[] imaginary)
        {
            int n = real.Length;

            // bit-reversal permutation.
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;

                for (; (j & bit) != 0; bit >>= 1)
                    j ^= bit;

                j ^= bit;

                if (i < j)
                {
                    (real[i], real[j]) = (real[j], real[i]);
                    (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
                }
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = -2 * Math.PI / length;
                float stepReal = (float)Math.Cos(angle);
                float stepImaginary = (float)Math.Sin(angle);
                int half = length >> 1;

                for (int i = 0; i < n; i += length)
                {
                    float weightReal = 1;
                    float weightImaginary = 0;

                    for (int j = 0; j < half; j++)
                    {
                        int even = i + j;
                        int odd = even + half;

                        float oddReal = real[odd] * weightReal - imaginary[odd] * weightImaginary;
                        float oddImaginary = real[odd] * weightImaginary + imaginary[odd] * weightReal;

                        real[odd] = real[even] - oddReal;
                        imaginary[odd] = imaginary[even] - oddImaginary;
                        real[even] += oddReal;
                        imaginary[even] += oddImaginary;

                        float nextWeightReal = weightReal * stepReal - weightImaginary * stepImaginary;
                        weightImaginary = weightReal * stepImaginary + weightImaginary * stepReal;
                        weightReal = nextWeightReal;
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (processingLock)
            {
                capture?.StopRecording();
                capture?.Dispose();
                capture = null;
                captureFormat = null;
            }
        }
    }
}
