using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibNoise.Generator;
using LibNoise.Operator;
using AssemblyCSharp;

public class TerrainLoader : MonoBehaviour 
{
	
	public GameObject waterPrefab;
	
	public int renderDistance;
	
	public int heightMapResolution;
	public int alphaMapResolution;
	
	public int tileSize;
	public int ceilingHeight;
	
	public int noiseScale;
	public int seed;
	
	public int maxRenderThreads;
	
	public float sea_level;
	
	private Vector2 currentCoordinate;
	private float size;
	private Vector3 dataSize;
		
	private int alphaLayers = 4;
	
	public delegate void CoordChange (Vector2 newCoord, TerrainLoader source);
	public static event CoordChange OnCoordChange;
	
	int threadCount = 0;
	
	private float maxHeight = 0;
	private float maxSteepness = 0;
	
	LibNoise.ModuleBase module;
			
	// Use this for initialization
	void Start () {
		
		dataSize = new Vector3 (tileSize, ceilingHeight, tileSize);
		size = dataSize.x;
		
		WorldTerrain.alphaMapResolution = alphaMapResolution;
		WorldTerrain.heightMapResolution = heightMapResolution;
		
		WorldTerrain.dataSize = dataSize;
		WorldTerrain.size = size;
		WorldTerrain.tileSize = tileSize;
		WorldTerrain.ceilingHeight = ceilingHeight;
		
		WorldTerrain.noiseScale = noiseScale;
		WorldTerrain.seed = seed;
		WorldTerrain.waterPrefab = waterPrefab;
		WorldTerrain.sea_level = sea_level;
		
		WorldGenerator test = new WorldGenerator();
		
		module = test.Module;
		
		TerrainTile.module = test.Module;
	}
	
	// Update is called once per frame
	void Update () 
	{
		float altitude = gameObject.transform.position.y;
		
		//Debug.Log("Player Altitude: " + (altitude/ceilingHeight));
		
		//We want to populate the surrounding X chunks around the player
		//First, we find the XY coordinate of the player
		Vector2 coordinate = WorldTerrain.getGridCoordinate(gameObject.transform.position);

		currentCoordinate = coordinate;
		CoordChangeEvent(currentCoordinate);
		
		TerrainTile currentTile;
				
		WorldTerrain.terrainMap.TryGetValue(WorldTerrain.getTerrainName((int)coordinate.x, (int)coordinate.y), out currentTile);
		
		if(currentTile != null)
		{
			WorldTerrain.LocalCoordinate coord = WorldTerrain.WorldToLocal(gameObject.transform.position, WorldTerrain.heightMapResolution);
			
			float playerSteepness = currentTile.getSteepness((int)coord.localCoordinate.x, (int)coord.localCoordinate.y);
			
			Debug.Log("Player Terrain Steepness: " + playerSteepness + ". Max found = " + maxSteepness);
			
			if(maxSteepness < playerSteepness)
			{
				maxSteepness = playerSteepness;
			}
			
			//currentTile.setHeight((int)coord.localCoordinate.x, (int)coord.localCoordinate.y, currentTile.getHeight((int)coord.localCoordinate.x, (int)coord.localCoordinate.y) + 0.01f);
		}
		
		cullTerrain();
		
		List<TerrainTile> surroundingTerrain = new List<TerrainTile>();
		
		for (int x = (int)coordinate.x - renderDistance; x < (int)coordinate.x + renderDistance; x++) 
		{
			for (int y = (int)coordinate.y - renderDistance; y < (int)coordinate.y + renderDistance; y++) 
			{
				Vector2 terrainCoord = new Vector2(x,y);
				
				string tileName = WorldTerrain.getTerrainName(x,y);
				
				bool existsInMap = WorldTerrain.terrainMap.ContainsKey(tileName);
				bool isNull = true;
				
				if(existsInMap)
				{
					isNull = !WorldTerrain.terrainMap[tileName].isLoaded;
					if(isNull)
					{
						//Remove the null terrain from the terrain map
						WorldTerrain.terrainMap.Remove(tileName);
					}
				}
				
				if((!existsInMap || isNull) && !WorldTerrain.pendingTerrain.ContainsKey(tileName) && WorldTerrain.getGridDistance(terrainCoord,coordinate) <= renderDistance)
				{
					surroundingTerrain.Add(new TerrainTile(terrainCoord, tileName, WorldTerrain.getGridDistance(terrainCoord, currentCoordinate), null, null, null));
				}
			}
		}
		
		//Order list by distance from player
		List<TerrainTile> orderedTerrain = surroundingTerrain.OrderBy(terrain => terrain.dist).ToList();
		
		foreach(TerrainTile tile in orderedTerrain)
		{
			if(!WorldTerrain.pendingTerrain.ContainsKey(tile.tileName) && !WorldTerrain.terrainMap.ContainsKey(tile.tileName))
			{				
				if(threadCount < maxRenderThreads)
				{
					WorldTerrain.pendingTerrain.Add(tile.tileName, tile);
					tile.isLoading = true;
					startGenerateThread(tile);
				}
			}
		}
		
		
		List<TerrainTile> loadedTiles = new List<TerrainTile>();
		for(int i = 0; i < WorldTerrain.pendingTerrain.Values.Count; i++)
		//foreach(TerrainTile tile in pendingTerrain.Values)
		{
			TerrainTile tile = WorldTerrain.pendingTerrain.Values.ToArray()[i];
			if(WorldTerrain.getGridDistance(tile.position, currentCoordinate) < renderDistance)
			{
				if(!tile.isLoading)
				{
					if(threadCount < maxRenderThreads)
					{
						tile.isLoading = true;
						startGenerateThread(tile);
					}
				}
				else if(tile.IsGenerated && !tile.isLoaded)
				{
					tile.load();
					loadedTiles.Add(tile);
					WorldTerrain.terrainMap.Add(tile.tileName, tile);
				}
			}
			else
			{
				WorldTerrain.pendingTerrain.Remove(tile.tileName);
			}
		}
		
		foreach(TerrainTile tile in loadedTiles)
		{
			WorldTerrain.pendingTerrain.Remove(tile.tileName);
		}
	}
	
	private void startGenerateThread(TerrainTile tile)
	{
		try
		{
			Interlocked.Add(ref threadCount, 1);
			//pendingTerrain.Add(tile.tileName, tile);
			Thread heightmapThread = new Thread(() =>
			{
				System.Diagnostics.Stopwatch timer = new System.Diagnostics.Stopwatch();
				timer.Start();
				tile.generateComplexHeightMap();
				tile.generateSplatMap();
				Interlocked.Add(ref threadCount, -1);
				timer.Stop();
				//Debug.Log(tile.tileName + " generated in " + (timer.ElapsedMilliseconds/1000f) + " seconds.");
			});
			
			heightmapThread.Start();
		}
		catch(UnityException)
		{
			Interlocked.Add(ref threadCount, -1);
		}
	}
	
	private void cullTerrain()
	{
		List<TerrainTile> removed = new List<TerrainTile>();
		foreach (TerrainTile tile in WorldTerrain.terrainMap.Values) 
		{
			if(WorldTerrain.getGridDistance(tile.position, currentCoordinate) > renderDistance)
			{
				tile.unload();
				Resources.UnloadUnusedAssets();
				removed.Add(tile);
			}
		}
		
		foreach(TerrainTile tile in removed)
		{
			WorldTerrain.terrainMap.Remove(WorldTerrain.getTerrainName((int)tile.position.x, (int)tile.position.y));
		}
	}
	
	void CoordChangeEvent(Vector2 newCoord){
		if(OnCoordChange != null){
			OnCoordChange(newCoord, this);
		}
	}
}