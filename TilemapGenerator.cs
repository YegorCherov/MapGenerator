using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.Tilemaps;
using Debug = UnityEngine.Debug;

public class TilemapGenerator : MonoBehaviour
{
    [Header("Tilemap Settings")]
    public Tilemap groundTilemap;
    public Tilemap pathsTilemap;
    public Tilemap objectsTilemap;
    public Tilemap structuresTilemap;
    public Tilemap baseTilemap;
    public Tilemap showGridTilemap;
    public TileBase[] structureTiles;
    public Tile gridTile;

    [Header("Biome Settings")]
    public BiomeTileset[] biomeTilesets;

    [Header("Tile Placement Settings")]
    public float vegetationDensity = 0.1f;
    public float structureDensity = 0.05f;
    public float structureMinDistance = 5f;
    public float structureMaxDistance = 10f;
    public float structureMinElevation = 0.3f;
    public float structureMaxElevation = 0.7f;
    public float villageDensity = 0.01f;
    public int minimumVillageSize = 5;
    public float waterLevel = 0.2f;

    [Header("Chunk Settings")]
    public int chunkSize;
    public int MaxChunksPerFrame = 5;

    [Header("Camera Settings")]
    public Camera mainCamera;
    public int cameraChunkRadiusX = 2;
    public int cameraChunkRadiusY = 2;

    [Header("Progress Settings")]
    public float generationProgress;
    public int totalChunks;

    [Header("References")]
    public MapGenerator mapGenerator;

    private HashSet<Vector2Int> structurePositions = new HashSet<Vector2Int>();
    private HashSet<Vector2Int> waterPositions = new HashSet<Vector2Int>();
    private Quadtree terrainQuadtree;
    private HashSet<Vector2Int> loadedChunks = new HashSet<Vector2Int>();
    private bool canRun = false;
    private bool force_chunk = false;
    private Queue<KeyValuePair<int, int>> chunkQueue = new Queue<KeyValuePair<int, int>>();
    private Coroutine generateTilemapCoroutine;
    private Vector2Int currentCameraChunk;
    private object tileLock = new object();

    public void ClearTilemap()
    {
        groundTilemap.ClearAllTiles();
        pathsTilemap.ClearAllTiles();
        objectsTilemap.ClearAllTiles();
        structuresTilemap.ClearAllTiles();
        baseTilemap.ClearAllTiles();
        showGridTilemap.ClearAllTiles();
    }

    public void GenerateTilemap(ConcurrentDictionary<(int, int), ChunkData> chunkGrid)
    {
        if (this == null)
        {
            Debug.LogError("TilemapGenerator object is null. Cannot generate tilemap.");
            return;
        }

        chunkQueue.Clear();
        totalChunks = 0;

        foreach (var chunkCoords in chunkGrid.Keys)
        {
            int chunkX = chunkCoords.Item1;
            int chunkY = chunkCoords.Item2;
            if (chunkGrid[(chunkX, chunkY)].isVisible)
            {
                chunkQueue.Enqueue(new KeyValuePair<int, int>(chunkX, chunkY));
                totalChunks++;
            }
        }

        generationProgress = 0f;

        if (generateTilemapCoroutine == null)
        {
            generateTilemapCoroutine = StartCoroutine(LoadChunkCoroutine());
        }
    }

    private IEnumerator LoadChunkCoroutine()
    {
        int chunksGenerated = 0;
        int chunksProcessedThisFrame = 0;

        while (chunkQueue.Count > 0)
        {
            if (chunksProcessedThisFrame >= MaxChunksPerFrame)
            {
                chunksProcessedThisFrame = 0;
                yield return null;
            }
            else
            {
                try
                {
                    KeyValuePair<int, int> chunkCoords = chunkQueue.Dequeue();
                    int chunkX = chunkCoords.Key;
                    int chunkY = chunkCoords.Value;

                    if (mapGenerator.chunkGrid.ContainsKey((chunkX, chunkY)))
                    {
                        ChunkData chunkData = mapGenerator.chunkGrid[(chunkX, chunkY)];
                        if (chunkData.isVisible)
                        {
                            int offsetX = chunkX * chunkSize;
                            int offsetY = chunkY * chunkSize;
                            PlaceTerrainTiles(chunkData.chunkData, offsetX, offsetY);
                            PlaceVegetationTiles(chunkData.chunkData, offsetX, offsetY);
                        }
                    }
                    else
                    {
                        Debug.LogWarning($"Chunk ({chunkX}, {chunkY}) not found in chunkGrid dictionary.");
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Error generating tilemap for chunk: {ex.Message}\n{ex.StackTrace}");
                    StopAllCoroutines();
                    yield break;
                }

                chunksProcessedThisFrame++;
                chunksGenerated++;
                generationProgress = (float)chunksGenerated / totalChunks;
            }
        }

        generateTilemapCoroutine = null;
    }

    private void PlaceTerrainTiles(int[,] chunkData, int offsetX, int offsetY)
    {
        if (chunkData == null || chunkData.GetLength(0) == 0 || chunkData.GetLength(1) == 0)
        {
            Debug.LogError("chunkData array is empty");
            return;
        }

        Stopwatch stopwatch = new Stopwatch();

        int totalTiles = chunkSize * chunkSize;
        TileBase[] groundTileArray = new TileBase[totalTiles];

        Rect chunkBounds = new Rect(offsetX, offsetY, chunkSize, chunkSize);
        terrainQuadtree = new Quadtree(chunkBounds);

        int index = 0;
        for (int y = 0; y < chunkSize; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int terrainIndex = chunkData[x, y];
                BiomeType biomeType = (BiomeType)terrainIndex;
                BiomeTileset biomeTileset = biomeTilesets[(int)biomeType];

                if (terrainIndex >= 0)
                {
                    TileBase tile = biomeTileset.groundTiles[0];
                    groundTileArray[index] = tile;

                    if (terrainIndex == 0)
                    {
                        waterPositions.Add(new Vector2Int(x + offsetX, y + offsetY));
                    }

                    if (terrainIndex != 0)
                    {
                        terrainQuadtree.Insert(new Vector2Int(x + offsetX, y + offsetY));
                    }
                }

                index++;
            }
        }

        stopwatch.Start();
        BoundsInt tilemapChunkBounds = new BoundsInt(offsetX, offsetY, 0, chunkSize, chunkSize, 1);
        SetTiles(groundTilemap, tilemapChunkBounds, groundTileArray, stopwatch);
        SetBaseTiles(chunkData, offsetX, offsetY);
        SetGridTiles(chunkSize, offsetX, offsetY);
    }

    private void SetBaseTiles(int[,] chunkData, int offsetX, int offsetY)
    {
        int totalTiles = chunkSize * chunkSize;
        TileBase[] baseTileArray = new TileBase[totalTiles];

        int index = 0;
        for (int y = 0; y < chunkSize; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int terrainIndex = chunkData[x, y];
                if (terrainIndex != 0)
                {
                    baseTileArray[index] = biomeTilesets[(int)BiomeType.Forest].groundTiles[0];
                }
                index++;
            }
        }

        BoundsInt baseBounds = new BoundsInt(offsetX, offsetY, 0, chunkSize, chunkSize, 1);
        SetTiles(baseTilemap, baseBounds, baseTileArray, null);
    }

    private void SetGridTiles(int chunkSize, int offsetX, int offsetY)
    {
        int totalTiles = chunkSize * chunkSize;
        TileBase[] gridTileArray = new TileBase[totalTiles];

        for (int i = 0; i < totalTiles; i++)
        {
            gridTileArray[i] = gridTile;
        }

        BoundsInt gridBounds = new BoundsInt(offsetX, offsetY, 0, chunkSize, chunkSize, 1);
        SetTiles(showGridTilemap, gridBounds, gridTileArray, null);
    }

    private void SetTiles(Tilemap tilemap, BoundsInt bounds, TileBase[] tileArray, Stopwatch stopwatch)
    {
        lock (tileLock)
        {
            tilemap.SetTilesBlock(bounds, tileArray);
        }

        if (stopwatch != null)
        {
            stopwatch.Stop();
        }
    }

    private void PlaceVegetationTiles(int[,] chunkData, int offsetX, int offsetY)
    {
        if (chunkData == null || chunkData.GetLength(0) == 0 || chunkData.GetLength(1) == 0)
        {
            Debug.LogError("chunkData array is empti");
            return;
        }

        BoundsInt chunkBounds = new BoundsInt(offsetX, offsetY, 0, chunkSize, chunkSize, 1);
        List<TileBase> tileList = new List<TileBase>();
        List<Vector3Int> positionList = new List<Vector3Int>();

        for (int y = 0; y < chunkSize; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int terrainIndex = chunkData[x, y];
                BiomeType biomeType = (BiomeType)terrainIndex;
                BiomeTileset biomeTileset = biomeTilesets[(int)biomeType];

                if (UnityEngine.Random.value < vegetationDensity)
                {
                    if (terrainIndex != (int)BiomeType.Water)
                    {
                        TileBase tile = GetRandomVegetationTile(biomeTileset);
                        if (tile != null)
                        {
                            tileList.Add(tile);
                            positionList.Add(new Vector3Int(x + offsetX, y + offsetY, 0));
                        }
                    }
                }
            }
        }

        if (tileList.Count > 0)
        {
            objectsTilemap.SetTiles(positionList.ToArray(), tileList.ToArray());
        }
    }

    private TileBase GetRandomVegetationTile(BiomeTileset biomeTileset)
    {
        float randomValue = UnityEngine.Random.value;

        if (randomValue < 0.4f && biomeTileset.treeTiles.Length > 0)
        {
            return biomeTileset.treeTiles[UnityEngine.Random.Range(0, biomeTileset.treeTiles.Length)];
        }
        else if (randomValue < 0.7f && biomeTileset.bushTiles.Length > 0)
        {
            return biomeTileset.bushTiles[UnityEngine.Random.Range(0, biomeTileset.bushTiles.Length)];
        }
        else if (biomeTileset.stoneTiles.Length > 0)
        {
            return biomeTileset.stoneTiles[UnityEngine.Random.Range(0, biomeTileset.stoneTiles.Length)];
        }

        return null;
    }
    private void PlaceStructureTiles(int[,] chunkData, int offsetX, int offsetY)
    {
        HashSet<Vector2Int> chunkStructurePositions = new HashSet<Vector2Int>();

        for (int y = 0; y < chunkSize; y++)
        {
            for (int x = 0; x < chunkSize; x++)
            {
                int globalX = offsetX + x;
                int globalY = offsetY + y;

                if (UnityEngine.Random.value < villageDensity && IsValidForVillage(globalX, globalY, chunkData, offsetX, offsetY, chunkStructurePositions))
                {
                    CreateVillage(globalX, globalY, chunkData, offsetX, offsetY, chunkStructurePositions);
                }
            }
        }
    }

    private void CreateVillage(int centerX, int centerY, int[,] chunkData, int offsetX, int offsetY, HashSet<Vector2Int> chunkStructurePositions)
    {
        int structuresPlaced = 0;
        Queue<Vector2Int> positionsToCheck = new Queue<Vector2Int>();
        positionsToCheck.Enqueue(new Vector2Int(centerX, centerY));

        List<Vector3Int> tilePositions = new List<Vector3Int>();
        List<TileBase> tiles = new List<TileBase>();

        while (positionsToCheck.Count > 0 && structuresPlaced < minimumVillageSize)
        {
            Vector2Int pos = positionsToCheck.Dequeue();
            for (int dx = -1; dx <= 1; dx++)
            {
                for (int dy = -1; dy <= 1; dy++)
                {
                    int nx = pos.x + dx;
                    int ny = pos.y + dy;
                    Vector2Int localPosition = new Vector2Int(nx, ny);
                    if (nx >= offsetX && nx < offsetX + chunkSize && ny >= offsetY && ny < offsetY + chunkSize && UnityEngine.Random.value < structureDensity)
                    {
                        if (!chunkStructurePositions.Contains(localPosition) && IsValidForVillage(nx, ny, chunkData, offsetX, offsetY, chunkStructurePositions))
                        {
                            TileBase tile = structureTiles[UnityEngine.Random.Range(0, structureTiles.Length)];
                            tilePositions.Add(new Vector3Int(localPosition.x, localPosition.y, 0));
                            tiles.Add(tile);
                            chunkStructurePositions.Add(localPosition);
                            positionsToCheck.Enqueue(new Vector2Int(nx, ny));
                            structuresPlaced++;
                        }
                    }
                }
            }
        }

        if (tilePositions.Count > 0)
        {
            SetTiles(structuresTilemap, tilePositions.ToArray(), tiles.ToArray());
        }
    }

    private void SetTiles(Tilemap tilemap, Vector3Int[] positions, TileBase[] tiles)
    {
        tilemap.SetTiles(positions, tiles);
    }

    private bool IsValidForVillage(int x, int y, int[,] chunkData, int offsetX, int offsetY, HashSet<Vector2Int> chunkStructurePositions)
    {
        if (x >= 0 && x < chunkSize && y >= 0 && y < chunkSize)
        {
            int terrainIndex = chunkData[x, y];
            if (terrainIndex != 0)
            {
                float elevation = terrainIndex;
                return elevation >= structureMinElevation && elevation <= structureMaxElevation && !IsNearWater(x, y, chunkData, offsetX, offsetY) && !IsNearStructure(x, y, offsetX, offsetY, chunkStructurePositions);
            }
        }
        return false;
    }

    private bool IsNearStructure(int x, int y, int offsetX, int offsetY, HashSet<Vector2Int> chunkStructurePositions)
    {
        int radius = Mathf.RoundToInt(structureMinDistance);
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int sampleX = x + dx;
                int sampleY = y + dy;
                Vector2Int localPosition = new Vector2Int(sampleX - offsetX, sampleY - offsetY);

                if (sampleX >= offsetX && sampleX < offsetX + chunkSize && sampleY >= offsetY && sampleY < offsetY + chunkSize)
                {
                    if (chunkStructurePositions.Contains(localPosition))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    private bool IsNearWater(int x, int y, int[,] chunkData, int offsetX, int offsetY)
    {
        int radius = 1;
        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                int sampleX = x + dx;
                int sampleY = y + dy;

                if (sampleX >= offsetX && sampleX < offsetX + chunkSize && sampleY >= offsetY && sampleY < offsetY + chunkSize)
                {
                    if (waterPositions.Contains(new Vector2Int(sampleX, sampleY)))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }
    public void SetCameraToCenter(ConcurrentDictionary<(int, int), ChunkData> chunkGrid)
    {
        if (chunkGrid.Count == 0)
        {
            Debug.LogError("ChunkGrid is empty. Cannot set camera to center.");
            return;
        }
        int gridWidth = mapGenerator.mapWidth * chunkSize;
        int gridHeight = mapGenerator.mapHeight * chunkSize;
        Vector3 centerPosition = new Vector3(gridWidth / 2f + chunkSize, gridHeight / 2f + chunkSize, mainCamera.transform.position.z);
        mainCamera.transform.position = centerPosition;
        canRun = true;
    }

    public void SetCameraToOffset()
    {
        mainCamera.transform.position = new Vector3(-999, -999, mainCamera.transform.position.z);
    }

    private void Update()
    {
        if (canRun)
        {
            UpdateVisibleChunks(mapGenerator.chunkGrid);
            UpdateCameraChunkRadius();
        }
    }

    private void UpdateCameraChunkRadius()
    {
        float cameraHeight = 2f * mainCamera.orthographicSize;
        float cameraWidth = cameraHeight * mainCamera.aspect;

        int chunkRadiusX = Mathf.CeilToInt(cameraWidth * 1.2f / (2f * chunkSize));
        int chunkRadiusY = Mathf.CeilToInt(cameraHeight * 1.2f / (2f * chunkSize));

        if (chunkRadiusX != cameraChunkRadiusX || chunkRadiusY != cameraChunkRadiusY)
        {
            cameraChunkRadiusX = chunkRadiusX;
            cameraChunkRadiusY = chunkRadiusY;
            force_chunk = true;
        }
    }
    public void UpdateVisibleChunks(ConcurrentDictionary<(int, int), ChunkData> chunkGrid)
    {
        if (chunkGrid == null)
        {
            Debug.LogError("ChunkGrid is null. Cannot update visible chunks.");
            return;
        }

        Vector3 cameraPosition = mainCamera.transform.position;
        int cameraChunkX = Mathf.FloorToInt(cameraPosition.x / chunkSize);
        int cameraChunkY = Mathf.FloorToInt(cameraPosition.y / chunkSize);

        if (cameraChunkX != currentCameraChunk.x || cameraChunkY != currentCameraChunk.y || force_chunk)
        {
            force_chunk = false;
            (int minX, int maxX, int minY, int maxY) = GetVisibleChunkBounds(cameraChunkX, cameraChunkY, cameraChunkRadiusX, cameraChunkRadiusY, chunkGrid);

            HashSet<Vector2Int> loadedChunksCopy = new HashSet<Vector2Int>(loadedChunks);

            foreach (var loadedChunk in loadedChunksCopy)
            {
                if (loadedChunk.x < minX || loadedChunk.x > maxX || loadedChunk.y < minY || loadedChunk.y > maxY)
                {
                    if (chunkGrid.ContainsKey((loadedChunk.x, loadedChunk.y)))
                    {
                        chunkGrid[(loadedChunk.x, loadedChunk.y)].isVisible = false;
                        UnloadChunk(loadedChunk.x, loadedChunk.y);

                        loadedChunks.Remove(loadedChunk);
                    }
                }
            }

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    Vector2Int chunk = new Vector2Int(x, y);
                    if (!loadedChunks.Contains(chunk))
                    {
                        int noiseMapX = x / 10;
                        int noiseMapY = y / 10;
                        if (chunkGrid.ContainsKey((x, y)))
                        {
                            chunkGrid[(x, y)].isVisible = true;
                            loadedChunks.Add(chunk);
                            chunkQueue.Enqueue(new KeyValuePair<int, int>(x, y));
                        }
                        else
                        {
                            ValueTuple<int, int> noiseMapCoords = (noiseMapX, noiseMapY);
                            if (!mapGenerator.terrainGenerationQueue.Contains(noiseMapCoords))
                            {
                                Debug.Log("NoisemapX:" + noiseMapX);
                                mapGenerator.terrainGenerationQueue.Enqueue(noiseMapCoords);
                            }
                        }
                    }
                }
            }

            currentCameraChunk = new Vector2Int(cameraChunkX, cameraChunkY);
            StartCoroutine(CallGenerateTerrainAsync());
            if (generateTilemapCoroutine == null)
            {
                generateTilemapCoroutine = StartCoroutine(LoadChunkCoroutine());
            }
        }
    }

    private IEnumerator CallGenerateTerrainAsync()
    {
        yield return mapGenerator.GenerateTerrainAsync();
    }

    private (int minX, int maxX, int minY, int maxY) GetVisibleChunkBounds(int cameraChunkX, int cameraChunkY, int radiusX, int radiusY, ConcurrentDictionary<(int, int), ChunkData> chunkGrid)
    {
        int maxX = cameraChunkX + radiusX;
        int maxY = cameraChunkY + radiusY;
        int minX = cameraChunkX - radiusX;
        int minY = cameraChunkY - radiusY;

        return (minX, maxX, minY, maxY);
    }

    private void UnloadChunk(int chunkX, int chunkY)
    {
        if (mapGenerator.chunkGrid.ContainsKey((chunkX, chunkY)))
        {
            int offsetX = chunkX * chunkSize;
            int offsetY = chunkY * chunkSize;
            BoundsInt chunkBounds = new BoundsInt(offsetX, offsetY, 0, chunkSize, chunkSize, 1);
            int totalChunkTiles = chunkSize * chunkSize;
            TileBase[] nullTiles = new TileBase[totalChunkTiles];

            groundTilemap.SetTilesBlock(chunkBounds, nullTiles);
            pathsTilemap.SetTilesBlock(chunkBounds, nullTiles);
            objectsTilemap.SetTilesBlock(chunkBounds, nullTiles);
            structuresTilemap.SetTilesBlock(chunkBounds, nullTiles);
            baseTilemap.SetTilesBlock(chunkBounds, nullTiles);
            showGridTilemap.SetTilesBlock(chunkBounds, nullTiles);
        }
    }
}

[System.Serializable]
public struct BiomeTileset
{
    public string name;
    public TileBase[] groundTiles;
    public TileBase[] treeTiles;
    public TileBase[] bushTiles;
    public TileBase[] stoneTiles;
    public TileBase[] villageTiles;
}