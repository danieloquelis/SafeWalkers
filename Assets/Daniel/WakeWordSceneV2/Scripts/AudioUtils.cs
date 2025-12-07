using System;
using UnityEngine;

namespace SafeWalkers.WakeWord
{
    /// <summary>
    /// Utility class for audio format conversions.
    /// Converts Unity AudioClip to WAV format for OpenAI Whisper API.
    /// </summary>
    public static class AudioUtils
    {
        /// <summary>
        /// Converts an AudioClip to WAV byte array format.
        /// Output format: 16-bit PCM, mono, 16kHz sample rate.
        /// </summary>
        /// <param name="clip">The AudioClip to convert</param>
        /// <returns>Byte array containing WAV file data with header</returns>
        public static byte[] ConvertAudioClipToWav(AudioClip clip)
        {
            if (clip == null)
            {
                Debug.LogError("[AudioUtils] Cannot convert null AudioClip to WAV");
                return null;
            }

            // Get audio samples
            float[] samples = GetAudioSamples(clip);
            if (samples == null || samples.Length == 0)
            {
                Debug.LogError("[AudioUtils] AudioClip contains no samples");
                return null;
            }

            // Convert float samples to 16-bit PCM
            short[] intData = new short[samples.Length];
            for (int i = 0; i < samples.Length; i++)
            {
                // Clamp to [-1, 1] range and convert to 16-bit
                float sample = Mathf.Clamp(samples[i], -1f, 1f);
                intData[i] = (short)(sample * short.MaxValue);
            }

            // Create WAV header
            byte[] header = CreateWavHeader(samples.Length, clip.frequency, clip.channels);

            // Create final WAV file data
            byte[] wavData = new byte[header.Length + intData.Length * 2];

            // Copy header
            Buffer.BlockCopy(header, 0, wavData, 0, header.Length);

            // Copy audio data
            Buffer.BlockCopy(intData, 0, wavData, header.Length, intData.Length * 2);

            return wavData;
        }

        /// <summary>
        /// Extracts all audio samples from an AudioClip.
        /// </summary>
        /// <param name="clip">The AudioClip to extract samples from</param>
        /// <returns>Float array of audio samples</returns>
        public static float[] GetAudioSamples(AudioClip clip)
        {
            if (clip == null)
                return null;

            float[] samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            return samples;
        }

        /// <summary>
        /// Creates an AudioClip from raw audio samples.
        /// </summary>
        /// <param name="samples">Audio samples</param>
        /// <param name="frequency">Sample rate (Hz)</param>
        /// <param name="name">Name for the AudioClip</param>
        /// <returns>Created AudioClip</returns>
        public static AudioClip CreateAudioClip(float[] samples, int frequency, string name = "Recording")
        {
            if (samples == null || samples.Length == 0)
            {
                Debug.LogError("[AudioUtils] Cannot create AudioClip from empty samples");
                return null;
            }

            AudioClip clip = AudioClip.Create(name, samples.Length, 1, frequency, false);
            clip.SetData(samples, 0);
            return clip;
        }

        /// <summary>
        /// Creates a WAV file header.
        /// Format: 16-bit PCM, mono or stereo based on channels parameter.
        /// </summary>
        /// <param name="sampleCount">Total number of samples</param>
        /// <param name="frequency">Sample rate (Hz)</param>
        /// <param name="channels">Number of audio channels (1 = mono, 2 = stereo)</param>
        /// <returns>Byte array containing WAV header</returns>
        private static byte[] CreateWavHeader(int sampleCount, int frequency, int channels)
        {
            byte[] header = new byte[44];

            const int bitsPerSample = 16;
            int byteRate = frequency * channels * (bitsPerSample / 8);
            int blockAlign = channels * (bitsPerSample / 8);
            int dataSize = sampleCount * (bitsPerSample / 8);

            // RIFF header
            WriteString(header, 0, "RIFF");
            WriteInt32(header, 4, 36 + dataSize); // File size - 8
            WriteString(header, 8, "WAVE");

            // fmt chunk
            WriteString(header, 12, "fmt ");
            WriteInt32(header, 16, 16); // fmt chunk size
            WriteInt16(header, 20, 1);  // Audio format (1 = PCM)
            WriteInt16(header, 22, (short)channels);
            WriteInt32(header, 24, frequency);
            WriteInt32(header, 28, byteRate);
            WriteInt16(header, 32, (short)blockAlign);
            WriteInt16(header, 34, (short)bitsPerSample);

            // data chunk
            WriteString(header, 36, "data");
            WriteInt32(header, 40, dataSize);

            return header;
        }

        /// <summary>
        /// Writes a string to a byte array at the specified offset.
        /// </summary>
        private static void WriteString(byte[] buffer, int offset, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                buffer[offset + i] = (byte)value[i];
            }
        }

        /// <summary>
        /// Writes a 32-bit integer to a byte array at the specified offset (little-endian).
        /// </summary>
        private static void WriteInt32(byte[] buffer, int offset, int value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
            buffer[offset + 2] = (byte)((value >> 16) & 0xFF);
            buffer[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        /// <summary>
        /// Writes a 16-bit integer to a byte array at the specified offset (little-endian).
        /// </summary>
        private static void WriteInt16(byte[] buffer, int offset, short value)
        {
            buffer[offset] = (byte)(value & 0xFF);
            buffer[offset + 1] = (byte)((value >> 8) & 0xFF);
        }

        /// <summary>
        /// Calculates the duration of an audio sample array in seconds.
        /// </summary>
        /// <param name="sampleCount">Number of samples</param>
        /// <param name="frequency">Sample rate (Hz)</param>
        /// <param name="channels">Number of audio channels</param>
        /// <returns>Duration in seconds</returns>
        public static float CalculateDuration(int sampleCount, int frequency, int channels)
        {
            if (frequency <= 0 || channels <= 0)
                return 0f;

            return (float)sampleCount / (frequency * channels);
        }
    }
}
