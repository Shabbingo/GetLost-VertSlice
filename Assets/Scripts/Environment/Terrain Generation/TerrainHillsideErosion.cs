using System;
using UnityEngine;

namespace GetLost.Environment
{
    /// <summary>
    /// Bounded, slope-aligned erosion detail evaluated on a shared world heightfield.
    /// Inspired by Rune Skovbo Johansen's Fast and Gorgeous Erosion Filter:
    /// https://blog.runevision.com/2026/03/fast-and-gorgeous-erosion-filter.html
    /// This grid adaptation uses measured slopes, compact cell blending, partial
    /// wave normalization and stacked masks; it is not a hydraulic simulation.
    /// </summary>
    public static class TerrainHillsideErosion
    {
        public static float[,] Apply(float[,] source, Vector2 origin, Vector2 cellSize,
            int seed, float relief, float gullySpacing, int octaves, Action<float> reportProgress = null)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            int height = source.GetLength(0), width = source.GetLength(1);
            if (width < 2 || height < 2 || cellSize.x <= 0f || cellSize.y <= 0f)
                throw new ArgumentException("Erosion needs a two-dimensional heightfield with positive sample spacing.");
            if (relief <= 0f || octaves <= 0) return source;

            float[,] result = new float[height, width];
            float sampleSpacing = Mathf.Max(cellSize.x, cellSize.y);
            for (int z = 0; z < height; z++)
            {
                if ((z & 63) == 0) reportProgress?.Invoke(z / (float)height);
                for (int x = 0; x < width; x++)
                {
                    Vector2 slope = Gradient(source, x, z, cellSize);
                    // Smooth onset is essential: normalizing a tiny gradient and then
                    // applying a square-root mask creates spikes at peaks and basins.
                    float mask = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(slope.magnitude / .25f));
                    float wavelength = Mathf.Max(20f, gullySpacing);
                    float amplitude = relief * .5f;
                    float change = 0f;
                    for (int octave = 0; octave < Mathf.Min(octaves, 5); octave++)
                    {
                        // Fade out under-resolved octaves instead of aliasing them
                        // into isolated pits/needles. Six to ten samples span a gully.
                        float resolutionMask = Mathf.SmoothStep(0f, 1f,
                            Mathf.InverseLerp(6f, 10f, wavelength / sampleSpacing));
                        if (resolutionMask <= 0f || mask <= 0f) break;
                        float slopeLength = slope.magnitude;
                        float active = mask * Mathf.SmoothStep(0f, 1f,
                            Mathf.Clamp01(slopeLength / .08f));
                        Vector2 acrossSlope = new Vector2(-slope.y, slope.x) / Mathf.Max(.0001f, slopeLength);
                        // Keep world coordinates in double precision before finding
                        // the local cell, including for negative/large world origins.
                        double px = ((double)origin.x + x * (double)cellSize.x) / wavelength;
                        double pz = ((double)origin.y + z * (double)cellSize.y) / wavelength;
                        Vector2 wave = CellWave(px, pz, acrossSlope, unchecked(seed + octave * 1013));
                        change += amplitude * resolutionMask * active * wave.x;
                        // Approximate gully slope guides the next octave. Recomputing
                        // finite differences of the detailed output also differentiates
                        // the rotating noise frame, causing unstable feedback/needles.
                        slope -= acrossSlope * (2f * Mathf.PI / wavelength * amplitude * active * resolutionMask * wave.y);
                        // Preserve larger ridges and channels; smaller gullies branch
                        // across their sides. All feedback is continuous and bounded.
                        mask *= Mathf.SmoothStep(0f, 1f, Mathf.Abs(wave.y));
                        wavelength *= .5f;
                        amplitude *= .5f;
                    }
                    result[z, x] = source[z, x] + Mathf.Clamp(change, -relief, relief);
                }
            }
            reportProgress?.Invoke(1f);
            return result;
        }

        static Vector2 Gradient(float[,] heights, int x, int z, Vector2 cellSize)
        {
            int x0 = Mathf.Max(0, x - 1), x1 = Mathf.Min(heights.GetLength(1) - 1, x + 1);
            int z0 = Mathf.Max(0, z - 1), z1 = Mathf.Min(heights.GetLength(0) - 1, z + 1);
            return new Vector2(
                (heights[z, x1] - heights[z, x0]) / ((x1 - x0) * cellSize.x),
                (heights[z1, x] - heights[z0, x]) / ((z1 - z0) * cellSize.y));
        }

        static Vector2 CellWave(double x, double z, Vector2 acrossSlope, int seed)
        {
            int ix = (int)Math.Floor(x), iz = (int)Math.Floor(z);
            Vector2 wave = Vector2.zero;
            float weightSum = 0f;
            // Radius 1.5 and +/- .25 jitter require a 5x5 support. A 3x3
            // neighborhood would drop contributing cells at grid boundaries.
            for (int oz = -2; oz <= 2; oz++)
            for (int ox = -2; ox <= 2; ox++)
            {
                int cx = ix + ox, cz = iz + oz;
                float dx = (float)(x - (cx + .5)) - Hash(cx, cz, seed) * .25f;
                float dz = (float)(z - (cz + .5)) - Hash(cx, cz, unchecked(seed + 7919)) * .25f;
                float weight = Mathf.Max(0f, 1f - (dx * dx + dz * dz) / 2.25f);
                if (weight <= 0f) continue;
                weight = weight * weight * weight;
                float phase = 2f * Mathf.PI * (dx * acrossSlope.x + dz * acrossSlope.y);
                wave += new Vector2(Mathf.Cos(phase), Mathf.Sin(phase)) * weight;
                weightSum += weight;
            }
            wave /= Mathf.Max(.0001f, weightSum);
            // Never fully normalize a near-cancelled wave. This bounded scale
            // (at most 2x) prevents the article's singular spikes and holes.
            return wave / Mathf.Max(.5f, wave.magnitude);
        }

        static float Hash(int x, int z, int seed)
        {
            unchecked
            {
                uint h = (uint)seed ^ (uint)x * 0x9E3779B9u ^ (uint)z * 0x85EBCA6Bu;
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu; h ^= h >> 16;
                return h / (float)uint.MaxValue * 2f - 1f;
            }
        }
    }
}
