using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using LibNoise;
using LibNoise.Modifiers;

public class TerrainLoader : MonoBehaviour 
{
	protected class TerrainTile
	{
		public Vector2 position;
		public int dist;
		public GameObject terrain;
		public TerrainTile(Vector2 position, int dist, GameObject terrain)
		{
			this.position = position;
			this.terrain = terrain;
			this.dist = dist;
		}
	}
	public int renderDistance;
	
	public int heightMapResolution;
	
	public int tileSize;
	public int ceilingHeight;
	
	public int noiseScale;
	
	public int seed;
	
	private Vector2 currentCoordinate;
	private float size;
	private Vector3 dataSize;
	
	private int updateCount = 0;
	
	public delegate void CoordChange (Vector2 newCoord, TerrainLoader source);
	public static event CoordChange OnCoordChange;
	
	private Dictionary<string, TerrainTile> terrainMap = new Dictionary<string, TerrainTile>();
	
	IModule module;
	
	// Use this for initialization
	void Start () {
		dataSize = new Vector3 (tileSize, ceilingHeight, tileSize);
		size = dataSize.x;
		
		
		FastNoise fastPlanetContinents = new FastNoise(seed);
		fastPlanetContinents.Frequency = 1.5;
		
		FastBillow fastPlanetLowlands = new FastBillow();
		fastPlanetLowlands.Frequency = 4;
		LibNoise.Modifiers.ScaleBiasOutput fastPlanetLowlandsScaled = new ScaleBiasOutput(fastPlanetLowlands);
		fastPlanetLowlandsScaled.Scale = 0.2;
		fastPlanetLowlandsScaled.Bias = 0.5;
		
		FastRidgedMultifractal fastPlanetMountainsBase = new FastRidgedMultifractal(seed);
		fastPlanetMountainsBase.Frequency = 4;
		
		ScaleBiasOutput fastPlanetMountainsScaled = new ScaleBiasOutput(fastPlanetMountainsBase);
		fastPlanetMountainsScaled.Scale = 0.4;
		fastPlanetMountainsScaled.Bias = 0.85;
		
		FastTurbulence fastPlanetMountains = new FastTurbulence(fastPlanetMountainsScaled);
		fastPlanetMountains.Power = 0.1;
		fastPlanetMountains.Frequency = 50;
		
		FastNoise fastPlanetLandFilter = new FastNoise(seed + 1);
		fastPlanetLandFilter.Frequency = 6;
		
		Select fastPlanetLand = new Select(fastPlanetLandFilter, fastPlanetLowlandsScaled, fastPlanetMountains);
		fastPlanetLand.SetBounds(0, 1000);
		fastPlanetLand.EdgeFalloff = 0.5;
		
		FastBillow fastPlanetOceanBase = new FastBillow(seed);
		fastPlanetOceanBase.Frequency = 15;
		ScaleOutput fastPlanetOcean = new ScaleOutput(fastPlanetOceanBase, 0.1);
		
		Select fastPlanetFinal = new Select(fastPlanetContinents, fastPlanetOcean, fastPlanetLand);
		fastPlanetFinal.SetBounds(0, 1000);
		fastPlanetFinal.EdgeFalloff = 0.5;
		
		module = fastPlanetFinal;
	}
	
	// Update is called once per frame
	void Update () 
	{
		//We want to populate the surrounding X chunks around the player
		//First, we find the XY coordinate of the player
		Vector2 coordinate = getGridCoordinate (gameObject.transform.position);
		updateCount++;
		if(updateCount > 5)
		{
			updateCount = 0;
		}
		Debug.Log("Grid Coordinate: [" + coordinate.x + "," + coordinate.y + "]" );
		currentCoordinate = coordinate;
		CoordChangeEvent(currentCoordinate);
		
		cullTerrain();
		
		List<TerrainTile> surroundingTerrain = new List<TerrainTile>();
		
		for (int x = (int)coordinate.x - renderDistance; x < (int)coordinate.x + renderDistance; x++) 
		{
			for (int y = (int)coordinate.y - renderDistance; y < (int)coordinate.y + renderDistance; y++) 
			{
				Vector2 terrainCoord = new Vector2(x,y);
				
				bool existsInMap = terrainMap.ContainsKey(getTerrainName(x,y));
				bool isNull = true;
				
				if(existsInMap)
				{
					isNull = terrainMap[getTerrainName(x,y)].terrain == null;
				}
				
				if((!existsInMap || isNull) && getGridDistance(terrainCoord,coordinate) <= renderDistance)
				{
					surroundingTerrain.Add(new TerrainTile(terrainCoord, getGridDistance(terrainCoord, currentCoordinate), null));
				}
			}
		}
		
		//Order list by distance from player
		List<TerrainTile> orderedTerrain = surroundingTerrain.OrderBy(terrain => terrain.dist).ToList();
		
		foreach(TerrainTile tile in orderedTerrain)
		{
			if(updateCount == 0)
			{
				//Generate the height map for the terrain in parallel
				float[,] heights = null;
				heights = generateComplexHeightMap((int)tile.position.x,(int)tile.position.y);
				
				GameObject newTile = new GameObject();
				
				newTile.name = getTerrainName((int)tile.position.x,(int)tile.position.y);
				
				if(terrainMap.ContainsKey(newTile.name))
				{
					terrainMap.Remove(newTile.name);
				}
				terrainMap.Add(newTile.name, new TerrainTile(tile.position, tile.dist, newTile));
				
				newTile.transform.position = new Vector3 (((int)tile.position.x - 250) * (size), 250, ((int)tile.position.y - 250) * (size));
				
				newTile.AddComponent(typeof(Terrain));
				Terrain terrain = (Terrain)newTile.GetComponent(typeof(Terrain));
				
				newTile.AddComponent(typeof(TerrainCollider));
				TerrainCollider collider = (TerrainCollider)newTile.GetComponent(typeof(TerrainCollider));
				
				TerrainData terrainData = new TerrainData();
				
				terrain.terrainData = terrainData;
				collider.terrainData = terrainData;
				
				terrainData.heightmapResolution = heightMapResolution;
				terrainData.size = dataSize;
				
				generateTerrainTexture(terrainData);
				
				terrainData.SetHeights(0,0,heights);
				updateCount++;
				break;
			}
		}
	}
	
	private void generateTerrainTexture(TerrainData data)
	{
		SplatPrototype[] texture = new SplatPrototype[1];
		Texture2D load = (Texture2D)Resources.Load("GrassHillAlbedo"); 
		texture[0] = new SplatPrototype();
		texture[0].texture = load;
		data.splatPrototypes = texture;
	}
	
	private float[,] generateComplexHeightMap(int tileX, int tileY)
	{
		int nRows = heightMapResolution;
		int nCols = heightMapResolution;
		float[,] heights = new float[nRows, nCols];
		
		for (int y = 0; y < nCols; y++)
		{
			for (int x = 0; x < nRows; x++)
			{
				heights[x,y] = (float)((module.GetValue(((double)x + (((double)nRows - 1) * ((double)tileY))) / (double)noiseScale,((double)y + (((double)nCols - 1) * ((double)tileX))) / (double)noiseScale, 10)/1.5)/2.0 + 0.5);
			}
		}
		
		return heights;
	}
	
	private void cullTerrain()
	{
		List<TerrainTile> removed = new List<TerrainTile>();
		foreach (TerrainTile tile in terrainMap.Values) 
		{
			if(getGridDistance(tile.position, currentCoordinate) > renderDistance)
			{
				GameObject.DestroyImmediate(tile.terrain);
				Resources.UnloadUnusedAssets();
				removed.Add(tile);
			}
		}
		
		foreach(TerrainTile tile in removed)
		{
			terrainMap.Remove(getTerrainName((int)tile.position.x, (int)tile.position.y));
		}
	}
	
	private string getTerrainName(int x, int y){
		string terrainName = "Terrain:" + x + "," + y;
		return terrainName;
	}
	
	public Vector2 getGridCoordinate(Vector3 position){
		Vector2 coordinate = new Vector2 ();
		
		coordinate.x = Mathf.FloorToInt (position.x / (size)) + 250;
		coordinate.y = Mathf.FloorToInt (position.z / (size)) + 250;
		
		return coordinate;
	}
	
	public int getGridDistance(Vector2 pos1, Vector2 pos2){
		return Mathf.FloorToInt(Mathf.Sqrt ( Mathf.Pow((pos1.x - pos2.x),2) + Mathf.Pow((pos1.y - pos2.y),2)));
	}
	
	void CoordChangeEvent(Vector2 newCoord){
		if(OnCoordChange != null){
			OnCoordChange(newCoord, this);
		}
	}
}