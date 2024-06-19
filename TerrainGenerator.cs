using System;
using System.Linq;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

public class TerrainGenerator : MonoBehaviour
{
    public int mapWidth = 1;
    public int mapHeight = 1;

    [Header("Terrain Settings")]
    [Tooltip("The shape of the terrain mask.\n" +
             "Different shapes can be selected to control the overall shape of the generated terrain.")]
    public MaskShape maskShape;
    [Tooltip("Controls the radial gradient for island shaping.\n" +
             "Higher values result in a more pronounced island shape, while lower values create a flatter terrain.")]
    [Range(0f, 10f)]
    public float islandFactor = 2.0f;
    [Tooltip("Controls how much the selected shape affects the terrain.\n" +
             "Higher values result in a stronger influence of the shape, while lower values create a more subtle effect.")]
    [Range(0f, 1f)]
    public float shapeInfluence = 0.5f;
    [Tooltip("The seed value used for generating random terrain.\n" +
             "Different seed values will produce different terrain layouts.")]
    public int seed;

    [Header("Land-Ocean Distribution")]
    [Tooltip("Controls the steepness of the transition between land and ocean.\n" +
             "Higher values result in a sharper transition, while lower values create a more gradual transition.")]
    [Range(0f, 10f)]
    public float TransitionSteepness = 2.0f;
    [Tooltip("Determines the center point of the land-ocean transition as a percentage of the shape.\n" +
             "Values range from 0% to 100%, where 0% represents the center and 100% represents the edge of the shape.\n" +
             "Higher values result in a larger central landmass, while lower values create a smaller central landmass.")]
    [Range(0f, 100f)]
    public float CenterSize = 50f;

    public enum MaskShape // Different available shapes for the terrain mask
    {
        Circle,
        Square,
        Diamond,
        Star
    }
    // General noise function to be used for both height and moisture
    // Generate a height map based on Perlin noise
    // Generate a height map based on Perlin noise. Include moisture option.
    public float[,] GenerateNoiseMap(int mapWidth, int mapHeight, NoiseSettings settings, int seed, int offset_x = 0, int offset_y = 0)
    {

        float[,] noiseMap = Noise.GenerateNoiseMap(mapWidth, mapHeight, settings, seed, offset_x, offset_y);

        /*
        if (!isMoisture)
        {
            ApplyRadialMask(noiseMap);
        }
        */

        return noiseMap;
    }



    /// <summary>
    /// Applies a radial mask to the terrain based on the selected shape and land-ocean distribution.
    /// </summary>
    /// <param name="map">The noise map to apply the radial mask to.</param>
    private void ApplyRadialMask(float[,] map)
    {
        Vector2 center = new Vector2(mapWidth / 2, mapHeight / 2);
        float maxDistance = Mathf.Max(center.x, center.y);

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                Vector2 point = new Vector2(x, y);
                float distance = Vector2.Distance(point, center) / maxDistance;
                float maskEffect = CalculateShapeEffect(point, distance, maxDistance, center);

                // Adjust the islandFactor value to control the center fill.
                // Higher values result in more land in the center, while lower values create less land in the center.
                float islandFactorAdjusted = islandFactor * (1.0f - Mathf.Pow(1.0f - distance, 2.0f));

                // Adjust the land-ocean distribution based on distance from the center.
                // Higher values of landOceanTransitionSteepness result in a sharper transition between land and ocean.
                // The landOceanCenter value is normalized from the range [0, 100] to [0, 1].
                float normalizedLandOceanCenter = CenterSize / 100f;
                float landOceanFactor = Mathf.Clamp01(TransitionSteepness * (normalizedLandOceanCenter - distance));

                float finalMask = Mathf.Lerp(1.0f - distance * islandFactorAdjusted, maskEffect, shapeInfluence);
                map[x, y] = Mathf.Lerp(map[x, y] * finalMask, Mathf.Clamp01(map[x, y] * finalMask + landOceanFactor), landOceanFactor);
            }
        }
    }

    private float CalculateShapeEffect(Vector2 point, float distance, float maxDistance, Vector2 center)
    {
        float shapeEffect;
        switch (maskShape)
        {
            case MaskShape.Circle:
                shapeEffect = (distance <= 1.0f) ? 1.0f : 0.0f;
                break;
            case MaskShape.Square:
                float maxDistanceFromCenter = Mathf.Max(Mathf.Abs(point.x - center.x), Mathf.Abs(point.y - center.y)) / maxDistance;
                shapeEffect = (maxDistanceFromCenter <= 1.0f) ? 1.0f : 0.0f;
                break;
            case MaskShape.Diamond:
                float distanceFromCenter = (Mathf.Abs(point.x - center.x) + Mathf.Abs(point.y - center.y)) / maxDistance;
                shapeEffect = (distanceFromCenter <= 1.0f) ? 1.0f : 0.0f;
                break;
            case MaskShape.Star:
                float angle = Mathf.Atan2(point.y - center.y, point.x - center.x) * Mathf.Rad2Deg;
                float starEffect = (distance + 0.5f * Mathf.Cos(4 * angle) * 0.5f);
                shapeEffect = (starEffect <= 1.0f) ? 1.0f : 0.0f;
                break;
            default:
                shapeEffect = 1.0f;
                break;
        }

        return shapeEffect;
    }


    // Determine biome based on height and moisture
    /*
    public int DetermineBiome(float height, float moisture)
    {
        if (height < 0.1f)
            return 0; // Water
        else if (height < 0.3f)
            return 1; // Desert
        else if (height < 0.5f)
            return 2; // Land
        else if (height > 0.7f)
            return 3; // Forest
        else
            return 4; // Mountain
    }
    */
}