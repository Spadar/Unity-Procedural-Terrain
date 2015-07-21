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
	
	private Dictionary<string, TerrainTile> terrainMap = new Dictionary<string, TerrainTile>();
	
	private Dictionary<string, TerrainTile> pendingTerrain = new Dictionary<string, TerrainTile>();
	
	int threadCount = 0;
	
	private float maxHeight = 0;
	private float maxSteepness = 0;
	
	LibNoise.ModuleBase module;
			
	// Use this for initialization
	void Start () {
		
		dataSize = new Vector3 (tileSize, ceilingHeight, tileSize);
		size = dataSize.x;
		
		TerrainTile.alphaMapResolution = alphaMapResolution;
		TerrainTile.heightMapResolution = heightMapResolution;
		
		TerrainTile.dataSize = dataSize;
		TerrainTile.size = size;
		TerrainTile.tileSize = tileSize;
		TerrainTile.ceilingHeight = ceilingHeight;
		
		TerrainTile.noiseScale = noiseScale;
		TerrainTile.seed = seed;
		TerrainTile.waterPrefab = waterPrefab;
		TerrainTile.sea_level = sea_level;
		
		WorldGenerator test = new WorldGenerator();
		
		module = test.Module;
		
		TerrainTile.module = test.Module;
	}
	
	// Update is called once per frame
	void Update () 
	{
		float altitude = gameObject.transform.position.y;
		
		Debug.Log("Player Altitude: " + (altitude/ceilingHeight));
		
		//We want to populate the surrounding X chunks around the player
		//First, we find the XY coordinate of the player
		Vector2 coordinate = TerrainTile.getGridCoordinate(gameObject.transform.position);

		currentCoordinate = coordinate;
		CoordChangeEvent(currentCoordinate);
		
		TerrainTile currentTile;
		
		terrainMap.TryGetValue(TerrainTile.getTerrainName((int)coordinate.x, (int)coordinate.y), out currentTile);
		
		cullTerrain();
		
		List<TerrainTile> surroundingTerrain = new List<TerrainTile>();
		
		for (int x = (int)coordinate.x - renderDistance; x < (int)coordinate.x + renderDistance; x++) 
		{
			for (int y = (int)coordinate.y - renderDistance; y < (int)coordinate.y + renderDistance; y++) 
			{
				Vector2 terrainCoord = new Vector2(x,y);
				
				string tileName = TerrainTile.getTerrainName(x,y);
				
				bool existsInMap = terrainMap.ContainsKey(tileName);
				bool isNull = true;
				
				if(existsInMap)
				{
					isNull = !terrainMap[tileName].isLoaded;
					if(isNull)
					{
						//Remove the null terrain from the terrain map
						terrainMap.Remove(tileName);
					}
				}
				
				if((!existsInMap || isNull) && !pendingTerrain.ContainsKey(tileName) && TerrainTile.getGridDistance(terrainCoord,coordinate) <= renderDistance)
				{
					surroundingTerrain.Add(new TerrainTile(terrainCoord, tileName, TerrainTile.getGridDistance(terrainCoord, currentCoordinate), null, null, null));
				}
			}
		}
		
		//Order list by distance from player
		List<TerrainTile> orderedTerrain = surroundingTerrain.OrderBy(terrain => terrain.dist).ToList();
		
		foreach(TerrainTile tile in orderedTerrain)
		{
			if(!pendingTerrain.ContainsKey(tile.tileName) && !terrainMap.ContainsKey(tile.tileName))
			{				
				if(threadCount < maxRenderThreads)
				{
					pendingTerrain.Add(tile.tileName, tile);
					tile.isLoading = true;
					startGenerateThread(tile);
				}
			}
		}
		
		
		List<TerrainTile> loadedTiles = new List<TerrainTile>();
		for(int i = 0; i < pendingTerrain.Values.Count; i++)
		//foreach(TerrainTile tile in pendingTerrain.Values)
		{
			TerrainTile tile = pendingTerrain.Values.ToArray()[i];
			if(TerrainTile.getGridDistance(tile.position, currentCoordinate) < renderDistance)
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
					terrainMap.Add(tile.tileName, tile);
				}
			}
			else
			{
				pendingTerrain.Remove(tile.tileName);
			}
		}
		
		foreach(TerrainTile tile in loadedTiles)
		{
			pendingTerrain.Remove(tile.tileName);
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
		foreach (TerrainTile tile in terrainMap.Values) 
		{
			if(TerrainTile.getGridDistance(tile.position, currentCoordinate) > renderDistance)
			{
				tile.unload();
				Resources.UnloadUnusedAssets();
				removed.Add(tile);
			}
		}
		
		foreach(TerrainTile tile in removed)
		{
			terrainMap.Remove(TerrainTile.getTerrainName((int)tile.position.x, (int)tile.position.y));
		}
	}
	
	void CoordChangeEvent(Vector2 newCoord){
		if(OnCoordChange != null){
			OnCoordChange(newCoord, this);
		}
	}
}