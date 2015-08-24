using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibNoise.Generator;
using LibNoise.Operator;
using System.Reflection;
using AssemblyCSharp;

//[ExecuteInEditMode]
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
	
	public bool enableCaching;
	
	public bool stitchTerrain;
	
	public Vector2 startOrigin;
		
	private Vector2 currentCoordinate;
	private float size;
	private Vector3 dataSize;
		
	private int alphaLayers = 4;
	
	public delegate void CoordChange (Vector2 newCoord, TerrainLoader source);
	public static event CoordChange OnCoordChange;
	
	int threadCount = 0;
	
	private float maxSteepness = 0;
	
	private TerrainTile previousTerrain;
	
	private TerrainTile currentTerrain;
	
	LibNoise.ModuleBase module;
	
	public float playerAltitude;
			
	// Use this for initialization
	void Start () {
		
		dataSize = new Vector3 (tileSize, ceilingHeight, tileSize);
		size = dataSize.x;
		
		WorldTerrain.player = gameObject;
		
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
		
		WorldTerrain.resourcesPath = Application.dataPath + @"\Resources";
		
		TerrainTile.enableCaching = enableCaching;
		
		WorldGenerator test = new WorldGenerator();
		
		module = test.Module;
		
		TerrainTile.module = test.Module;
		
		playerAltitude = 2500;
		
		WorldTerrain.origin = startOrigin;
	}	
	// Update is called once per frame
	void Update () 
	{
		float altitude = gameObject.transform.position.y;
		
		
		//We want to populate the surrounding X chunks around the player
		//First, we find the XY coordinate of the player
		Vector2 coordinate = WorldTerrain.getGridCoordinate(gameObject.transform.position);
				
		//WorldTerrain.LocalCoordinate lc = WorldTerrain.WorldToLocalExact(gameObject.transform.position, (int)WorldTerrain.size);
		
		//Debug.Log("Local Coord: [" + lc.localCoordinate.x + "," + lc.localCoordinate.y + "]");
		Debug.Log("NewCoord: [" + coordinate.x + "," + coordinate.y + "]");
		Debug.Log("Origin: [" + WorldTerrain.origin.x + "," + WorldTerrain.origin.y + "]");
		
		if(currentCoordinate != coordinate)
		{											
			WorldTerrain.LocalCoordinate localCoord = WorldTerrain.WorldToLocalExact(gameObject.transform.position, (int)WorldTerrain.size);
						
			WorldTerrain.origin = coordinate;
						
			foreach(TerrainTile tile in WorldTerrain.terrainMap.Values)
			{
				tile.updatePosition();
			}
			

			Vector3 originalCoord = gameObject.transform.position;
		
			Vector2 shiftedPlayerCoord = WorldTerrain.LocalToWorld(localCoord);
		
			gameObject.transform.position = new Vector3(shiftedPlayerCoord.x,gameObject.transform.position.y, shiftedPlayerCoord.y);
			
			currentCoordinate = WorldTerrain.getGridCoordinate(gameObject.transform.position);
			
			CoordChangeEvent(currentCoordinate);
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
					surroundingTerrain.Add(new TerrainTile(terrainCoord, tileName, null, null, null));
				}
			}
		}
		
		//Order list by distance from player
		List<TerrainTile> orderedTerrain = surroundingTerrain.OrderBy(terrain => terrain.playerDistance).ToList();
		
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
					break;
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
	
	private void showTerrainMethods()
	{
		MethodInfo[] terrainMethods = typeof(Terrain).GetMethods();
		
		MethodInfo[] dataMethods = typeof(TerrainData).GetMethods();
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