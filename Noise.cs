using Unity.Mathematics;
using UnityEngine;

public static class Noise
{
    public static float[,] GenerateNoiseMap(int mapWidth, int mapHeight, NoiseSettings settings, int seed, int offsetX = 0, int offsetY = 0)
    {
        float[,] noiseMap = new float[mapWidth, mapHeight];
        //Debug.LogError(offsetX);
        System.Random prng = new System.Random(seed);
        Vector2[] octaveOffsets = new Vector2[settings.octaves];

        for (int i = 0; i < settings.octaves; i++)
        {
            float octaveOffsetX = prng.Next(-100000, 100000);
            float octaveOffsetY = prng.Next(-100000, 100000);
            octaveOffsets[i] = new Vector2(octaveOffsetX, octaveOffsetY);
        }

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        float halfWidth = mapWidth / 2f;
        float halfHeight = mapHeight / 2f;

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float amplitude = 1;
                float frequency = 1f;
                float noiseHeight = 0;

                for (int i = 0; i < settings.octaves; i++)
                {
                    float sampleX = (((x + offsetX) / halfWidth) * settings.scale * frequency) + octaveOffsets[i].x;
                    float sampleY = (((y + offsetY) / halfHeight) * settings.scale * frequency) + octaveOffsets[i].y;

                    float perlinValue = 0;
                    switch (settings.noiseType)
                    {
                        case NoiseType.Perlin:
                            perlinValue = Mathf.PerlinNoise(sampleX, sampleY);
                            
                            break;
                        case NoiseType.FastNoise:
                            // gg
                            break;
                    }

                    noiseHeight += perlinValue * amplitude;

                    frequency *= settings.lacunarity;
                    amplitude *= settings.persistence;
                }

                // Apply the height offset

                if (noiseHeight > maxNoiseHeight)
                {
                    maxNoiseHeight = noiseHeight;
                }
                else if (noiseHeight < minNoiseHeight)
                {
                    minNoiseHeight = noiseHeight;
                }

                noiseMap[x, y] = noiseHeight;
            }
        }
        // normalize
        if (settings.normalize)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    noiseMap[x, y] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y]);
                }
            }
        }


        if (settings.flipValues)
        {
            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    noiseMap[x, y] = 1 - noiseMap[x, y];
                }
            }
        }

        // Apply the height offset after normalization
        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                noiseMap[x, y] += settings.heightOffset;
                noiseMap[x, y] = Mathf.Clamp01(noiseMap[x, y]); // Clamp the values between 0 and 1
            }
        }

        // smooth
        if (settings.smooth)
        {
            float[,] smoothedNoiseMap = new float[mapWidth, mapHeight];
            int smoothingRadius = 2; // Adjust the radius as needed
            for (int y = 0; y < mapHeight; y++)
            {
                for (int x = 0; x < mapWidth; x++)
                {
                    float sum = 0;
                    int count = 0;
                    for (int i = -smoothingRadius; i <= smoothingRadius; i++)
                    {
                        for (int j = -smoothingRadius; j <= smoothingRadius; j++)
                        {
                            int sampleX = x + i;
                            int sampleY = y + j;
                            if (sampleX >= 0 && sampleX < mapWidth && sampleY >= 0 && sampleY < mapHeight)
                            {
                                sum += noiseMap[sampleX, sampleY];
                                count++;
                            }
                        }
                    }
                    smoothedNoiseMap[x, y] = sum / count;
                }
            }

            return smoothedNoiseMap;
        }

        return noiseMap;
    }
}

public enum NoiseType
{
    Perlin,
    FastNoise
}

[System.Serializable]
public class NoiseSettings
{
    public string name;
    public NoiseType noiseType;
    [Range(1, 10)]
    public int octaves = 4;
    [Range(0.001f, 10f)]
    public float scale = 1f;
    [Range(0f, 1f)]
    public float persistence = 0.5f;
    [Range(1f, 5f)]
    public float lacunarity = 2f;
    public bool normalize = true;
    public bool smooth = true;
    [Range(-1f, 1f)]
    public float heightOffset = 0f;
    public bool flipValues = false;
}