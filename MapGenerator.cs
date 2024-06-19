using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;
using Debug = UnityEngine.Debug;

public class MapGenerator : MonoBehaviour
{
    [Header("Map Settings")]
    [Tooltip("The width of the terrain map.\n" +
             "Higher values result in a wider terrain, while lower values create a narrower terrain.")]
    [Range(1, 10000)]
    public int mapWidth = 100;
    [Tooltip("The height of the terrain map.\n" +
             "Higher values result in a taller terrain, while lower values create a shorter terrain.")]
    [Range(1, 10000)]
    public int mapHeight = 100;
    public int seed = 0;
    public bool autoUpdate = false;
    public bool viewFullMap = false;

    [Header("Chunk Settings")]
    public int chunkSize = 16;

    [Header("Ocean Settings")]
    public int oceanRadius = 5;
    private ConcurrentDictionary<(int, int), float> oceanMap;
    public Queue<(int, int)> terrainGenerationQueue = new Queue<(int, int)>();
    public int maxMapsPerFrame = 8;

    [Header("Noise Settings")]
    public NoiseSettings[] noiseSettings;

    [Header("References")]
    public TerrainGenerator terrainGenerator;
    public TilemapGenerator tilemapGenerator;

    public ConcurrentDictionary<(int, int), ChunkData> chunkGrid;
    private Dictionary<string, float[,]> noiseMaps;


    private void Start()
    {
        if (autoUpdate)
        {
            GenerateMap();
        }
    }

    private void OnValidate()
    {
        mapWidth = Mathf.Max(1, mapWidth);
        mapHeight = Mathf.Max(1, mapHeight);

        if (autoUpdate)
        {
            GenerateMap();
        }
    }

    public async void GenerateMap()
    {
        Stopwatch overallStopwatch = Stopwatch.StartNew();
        Stopwatch overallNoiseMap = Stopwatch.StartNew();
        InitializeGenerators();

        noiseMaps = await GenerateNoiseMaps();

        overallNoiseMap.Stop();
        Debug.Log($"Overall noise map generation time: {overallNoiseMap.ElapsedMilliseconds} ms");

        ProcessNoiseMaps(noiseMaps);

        var OceanStopwatch = Stopwatch.StartNew();
        InitializeTerrainGenerationQueue();
        OceanStopwatch.Stop();
        Debug.Log($"OceanStopwatch Time: {OceanStopwatch.ElapsedMilliseconds} ms");

        await GenerateTerrainAsync();

        overallStopwatch.Stop();
        Debug.Log($"Overall map generation time: {overallStopwatch.ElapsedMilliseconds} ms");

        UpdateTilemapGenerator();
    }

    private void InitializeGenerators()
    {
        terrainGenerator.seed = seed;
        terrainGenerator.mapWidth = mapWidth;
        terrainGenerator.mapHeight = mapHeight;
        tilemapGenerator.chunkSize = chunkSize;
        chunkGrid = new ConcurrentDictionary<(int, int), ChunkData>();
    }

    private async Task<Dictionary<string, float[,]>> GenerateNoiseMaps()
    {
        var noiseMaps = new Dictionary<string, float[,]>();
        var noiseMapTasks = new List<Task<KeyValuePair<string, float[,]>>>();
        int seed_offset = seed;

        foreach (var noiseSetting in noiseSettings)
        {
            int currentSeed = seed_offset;
            if (noiseSetting.name == "Ocean Map" || noiseSetting.name == "River Map")
            {
                noiseMapTasks.Add(Task.Run(() => GenerateNoiseMapAsync(noiseSetting.name, noiseSetting, currentSeed, mapWidth, mapHeight)));
            }
            seed_offset++;
        }

        await Task.WhenAll(noiseMapTasks);

        foreach (var noiseMapTask in noiseMapTasks)
        {
            var result = noiseMapTask.Result;
            noiseMaps[result.Key] = result.Value;
        }

        return noiseMaps;
    }

    private void ProcessNoiseMaps(Dictionary<string, float[,]> noiseMaps)
    {
        if (noiseMaps.ContainsKey("Ocean Map"))
        {
            float[,] oceanMapArray = noiseMaps["Ocean Map"];
            int oceanMapWidth = oceanMapArray.GetLength(0);
            int oceanMapHeight = oceanMapArray.GetLength(1);
            int oceanMapCenterX = oceanMapWidth / 2;
            int oceanMapCenterY = oceanMapHeight / 2;

            oceanMap = new ConcurrentDictionary<(int, int), float>();
            for (int y = 0; y < oceanMapHeight; y++)
            {
                for (int x = 0; x < oceanMapWidth; x++)
                {
                    int relativeX = x - oceanMapCenterX;
                    int relativeY = y - oceanMapCenterY;
                    oceanMap[(relativeX, relativeY)] = oceanMapArray[x, y];
                }
            }
        }
        else
        {
            Debug.LogError("Ocean Map not found in noiseMaps dictionary.");
        }

        foreach (var noiseMap in noiseMaps)
        {
            //DisplayNoiseMap(noiseMap.Value, noiseMap.Key);
        }

        Debug.Log("Available keys in noiseMaps:");
        foreach (var key in noiseMaps.Keys)
        {
            Debug.Log(key);
        }
    }

    private void InitializeTerrainGenerationQueue()
    {
        if (noiseMaps.ContainsKey("Ocean Map"))
        {
            for (int y = -oceanRadius; y <= oceanRadius; y++)
            {
                for (int x = -oceanRadius; x <= oceanRadius; x++)
                {
                    if (oceanMap.ContainsKey((x, y)))
                    {
                        terrainGenerationQueue.Enqueue((x, y));
                    }
                }
            }
        }
        else
        {
            Debug.LogError("Ocean Map not found in noiseMaps dictionary.");
        }
    }

    public async Task GenerateTerrainAsync()
    {
        int mapsProcessedThisFrame = 0;

        while (terrainGenerationQueue.Count > 0)
        {
            if (mapsProcessedThisFrame >= maxMapsPerFrame)
            {
                mapsProcessedThisFrame = 0;
                await Task.Yield();
            }
            else
            {
                try
                {
                    (int x, int y) coordinates = terrainGenerationQueue.Dequeue();
                    //Debug.Log("X: " + coordinates.x + "Y:" + coordinates.y);
                    int[,] terrainGrid = await GenerateOceanChunk(coordinates.x, coordinates.y);
                    SliceTerrainGridIntoChunks(terrainGrid, coordinates.x, coordinates.y);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error generating terrain for chunk: {ex.Message}\n{ex.StackTrace}");
                    return;
                }

                mapsProcessedThisFrame++;
            }
        }
    }

    private async Task<int[,]> GenerateOceanChunk(int x, int y)
    {
        int oceanGridSize = 100;

        if (oceanMap.TryGetValue((x, y), out float oceanValue))
        {
            if (IsDeepOcean(oceanValue))
            {
                return GenerateZerosChunk(oceanGridSize);
            }
            else if (IsShallowOcean(oceanValue))
            {
                return await GenerateNoiseChunk(x, y, oceanGridSize, true);
            }
            else
            {
                return await GenerateNoiseChunk(x, y, oceanGridSize, false);
            }
        }
        else
        {
            Debug.LogError($"Ocean value not found for coordinates ({x}, {y})");
            return GenerateZerosChunk(oceanGridSize);
        }
    }

    private bool IsDeepOcean(float value)
    {
        return value <= 0.01f;
    }

    private bool IsShallowOcean(float value)
    {
        return value > 0.3f && value <= 0.4f;
    }

    private async Task<int[,]> GenerateNoiseChunk(int x, int y, int oceanGridSize, bool isBlend)
    {
        var oceanHeightMapTask = Task.Run(() => GenerateNoiseMapAsync("Ocean Height Map", noiseSettings[0], seed, oceanGridSize, oceanGridSize, x * oceanGridSize, y * oceanGridSize));
        var oceanMoistureMapTask = Task.Run(() => GenerateNoiseMapAsync("Ocean Moisture Map", noiseSettings[1], seed + 1, oceanGridSize, oceanGridSize, x * oceanGridSize, y * oceanGridSize));
        var oceanTemperatureMapTask = Task.Run(() => GenerateNoiseMapAsync("Ocean Temperature Map", noiseSettings[2], seed + 2, oceanGridSize, oceanGridSize, x * oceanGridSize, y * oceanGridSize));

        await Task.WhenAll(oceanHeightMapTask, oceanMoistureMapTask, oceanTemperatureMapTask);

        int[,] terrainGrid = GenerateTerrainGrid(oceanHeightMapTask.Result.Value, oceanMoistureMapTask.Result.Value, oceanTemperatureMapTask.Result.Value, null);

        return terrainGrid;
    }

    private int[,] GenerateZerosChunk(int oceanGridSize)
    {
        return new int[oceanGridSize, oceanGridSize];
    }

    public static void DisplayNoiseMap(float[,] noiseMap, string mapName)
    {
        int mapWidth = noiseMap.GetLength(0);
        int mapHeight = noiseMap.GetLength(1);

        Texture2D texture = new Texture2D(mapWidth, mapHeight);
        Color[] colors = new Color[mapWidth * mapHeight];

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float value = noiseMap[x, y];
                colors[y * mapWidth + x] = new Color(value, value, value);
            }
        }

        texture.SetPixels(colors);
        texture.Apply();

        byte[] bytes = texture.EncodeToPNG();
        System.IO.File.WriteAllBytes(Application.dataPath + "/" + mapName + ".png", bytes);
    }

    private KeyValuePair<string, float[,]> GenerateNoiseMapAsync(string mapName, NoiseSettings settings, int map_seed, int mapWidth, int mapHeight, int offset_x = 0, int offset_y = 0)
    {
        float[,] noiseMap;
        if (mapName == "Blend Map")
        {
            noiseMap = terrainGenerator.GenerateNoiseMap(mapWidth * chunkSize, mapHeight * chunkSize, settings, map_seed);
        }
        else
        {
            noiseMap = terrainGenerator.GenerateNoiseMap(mapWidth, mapHeight, settings, map_seed, offset_x, offset_y);
        }

        return new KeyValuePair<string, float[,]>(mapName, noiseMap);
    }

    private int[,] GenerateTerrainGrid(float[,] heightMap, float[,] moistureMap, float[,] temperatureMap, float[,] riverMap)
    {
        int mapWidth = heightMap.GetLength(0);
        int mapHeight = heightMap.GetLength(1);
        int[,] terrainGrid = new int[mapWidth, mapHeight];

        if (heightMap.GetLength(0) != mapWidth || heightMap.GetLength(1) != mapHeight ||
            moistureMap.GetLength(0) != mapWidth || moistureMap.GetLength(1) != mapHeight ||
            temperatureMap.GetLength(0) != mapWidth || temperatureMap.GetLength(1) != mapHeight)
        {
            Debug.Log($"Height Map Dimensions: {heightMap.GetLength(0)}x{heightMap.GetLength(1)}");
            Debug.Log($"Moisture Map Dimensions: {moistureMap.GetLength(0)}x{moistureMap.GetLength(1)}");
            Debug.Log($"Temperature Map Dimensions: {temperatureMap.GetLength(0)}x{temperatureMap.GetLength(1)}");
            Debug.Log($"River Map Dimensions: {riverMap.GetLength(0)}x{riverMap.GetLength(1)}");
            Debug.LogError("Input arrays have inconsistent dimensions.");
            return null;
        }

        for (int y = 0; y < mapHeight; y++)
        {
            for (int x = 0; x < mapWidth; x++)
            {
                float height = heightMap[x, y];
                float moisture = moistureMap[x, y];
                float temperature = temperatureMap[x, y];

                if (height < 0.3f)
                {
                    if (moisture < 0.4f && temperature > 0.4f)
                        terrainGrid[x, y] = (int)BiomeType.Desert;
                    else if (temperature < 0.2f)
                        terrainGrid[x, y] = (int)BiomeType.Tundra;
                    else
                        terrainGrid[x, y] = (int)BiomeType.Grassland;
                }
                else if (height < 0.6f)
                {
                    if (moisture < 0.6f)
                    {
                        if (temperature < 0.2f)
                            terrainGrid[x, y] = (int)BiomeType.Tundra;
                        else if (temperature > 0.4f && moisture < 0.4f)
                            terrainGrid[x, y] = (int)BiomeType.Desert;
                        else
                            terrainGrid[x, y] = (int)BiomeType.Grassland;
                    }
                    else
                    {
                        terrainGrid[x, y] = (int)BiomeType.Forest;
                    }
                }
                else
                {
                    if (temperature < 0.6f)
                        terrainGrid[x, y] = (int)BiomeType.Tundra;
                    else
                        terrainGrid[x, y] = (int)BiomeType.Forest;
                }
            }
        }
        return terrainGrid;
    }

    private void SliceTerrainGridIntoChunks(int[,] terrainGrid, int offsetX, int offsetY)
    {
        //Debug.LogError("offsetX: " + offsetX + "offsetY: " + offsetY);
        int numChunksX = terrainGrid.GetLength(0) / chunkSize;
        int numChunksY = terrainGrid.GetLength(1) / chunkSize;

        Parallel.For(0, numChunksY, chunkY =>
        {
            for (int chunkX = 0; chunkX < numChunksX; chunkX++)
            {
                int[,] chunkData = new int[chunkSize, chunkSize];
                for (int y = 0; y < chunkSize; y++)
                {
                    for (int x = 0; x < chunkSize; x++)
                    {
                        int terrainX = chunkX * chunkSize + x;
                        int terrainY = chunkY * chunkSize + y;

                        if (terrainX >= 0 && terrainX < terrainGrid.GetLength(0) &&
                            terrainY >= 0 && terrainY < terrainGrid.GetLength(1))
                        {
                            chunkData[x, y] = terrainGrid[terrainX, terrainY];
                        }
                    }
                }

                int relativeChunkX = (chunkSize * offsetX) + chunkX;
                int relativeChunkY = (chunkSize * offsetY) + chunkY;
                chunkGrid[(relativeChunkX, relativeChunkY)] = new ChunkData(chunkData, viewFullMap);
            }
        });
    }

    private void UpdateTilemapGenerator()
    {
        tilemapGenerator.ClearTilemap();
        if (viewFullMap)
            tilemapGenerator.GenerateTilemap(chunkGrid);
        tilemapGenerator.UpdateVisibleChunks(chunkGrid);
        tilemapGenerator.SetCameraToCenter(chunkGrid);
        tilemapGenerator.UpdateVisibleChunks(chunkGrid);
    }
}

public enum BiomeType
{
    Water,
    Desert,
    Grassland,
    Forest,
    Tundra
}

public class NoiseChunk
{
    public bool isBlend = false;
    public float[,] HeightMap { get; set; }
    public float[,] MoistureMap { get; set; }
    public float[,] TemperatureMap { get; set; }
}
public class ChunkData
{
    public int[,] chunkData;
    public bool isVisible;
    public ChunkData(int[,] data, bool visible)
    {
        chunkData = data;
        isVisible = visible;
    }
}
